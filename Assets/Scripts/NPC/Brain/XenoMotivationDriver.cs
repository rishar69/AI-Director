using UnityEngine;

/// <summary>
/// Raises and lowers motivations from what the creature can sense.
///
/// THIS IS THE MISSING LINK. The behaviour tree arbitrates on
/// motivations but nothing in the exported graphs sets them - that
/// happens outside the tree, in the systems that feed it. Without
/// something filling this role the creature sits in IDLE forever,
/// because every branch above IDLE is gated on a motivation nobody
/// raised.
///
/// The Director takes this over later, and when it does it will do
/// exactly what this does: write motivations onto the blackboard. The
/// tree cannot tell the difference, which is the point.
/// </summary>
[RequireComponent(typeof(XenoBlackboard))]
[RequireComponent(typeof(XenoSenses))]
[RequireComponent(typeof(PerceptionSystem))]
public class XenoMotivationDriver : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Enable")]
    /// <summary>
    /// Turn off once the Director drives motivations, or to test the
    /// tree with the debug keys alone.
    /// </summary>
    [SerializeField]
    private bool driveFromSenses = true;


    [Header("Memory")]
    /// <summary>
    /// How long a contact keeps the creature motivated after the senses
    /// have gone quiet. Without this, motivations would flicker off the
    /// instant you broke line of sight.
    /// </summary>
    [SerializeField, Min(0f)]
    private float contactMemory = 12f;


    [Header("Debug")]
    [SerializeField]
    private bool logChanges = false;


    // =========================================================
    // STATE
    // =========================================================

    private XenoBlackboard blackboard;

    private XenoSenses senses;

    private PerceptionSystem perception;

    private XenoSuspiciousItems suspiciousItems;

    private float lastContactTime = -999f;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        blackboard = GetComponent<XenoBlackboard>();

        senses = GetComponent<XenoSenses>();

        perception = GetComponent<PerceptionSystem>();

        suspiciousItems = GetComponent<XenoSuspiciousItems>();
    }


    private void Update()
    {
        if (!driveFromSenses)
            return;

        bool anythingAtAll =
            senses.IsAnySenseActivationAbove(SenseThreshold.Lower);

        if (anythingAtAll)
        {
            lastContactTime = Time.time;
        }

        bool remembered =
            Time.time - lastContactTime <= contactMemory;

        bool hasTarget = perception.HasLastKnownPosition;

        Drive(hasTarget, remembered);
    }


    // =========================================================
    // DRIVING
    // =========================================================

    private void Drive(
        bool hasTarget,
        bool remembered)
    {
        /*
         * ATTACK when something is registering strongly right now. The
         * tree still applies its own preconditions on top - this only
         * decides that attacking is on the table at all.
         */

        bool attack =
            hasTarget &&
            senses.IsAnySenseActivationAbove(SenseThreshold.Activated);

        Set(Motivation.Attack, attack);

        /*
         * STALK in the gap: something is there, but not clearly enough
         * to commit. This is the band the creature spends most of its
         * time in, and it is where the circling comes from.
         */

        Set(
            Motivation.Stalk,
            hasTarget && !attack && remembered
        );

        /*
         * SEARCH is raised as PERMISSION, not as a decision.
         *
         * It deliberately does not test "has the trail gone cold" -
         * the branch itself does that, with the exported precondition
         * (something registered at ACTIVATED in the last 20 seconds, or
         * a sweep is already under way). Duplicating that test here
         * with a second, invented timeout coupled the two: a player who
         * lingered just above LOWER for ten seconds aged out the
         * ACTIVATED stamp before this ever raised the motivation, and
         * the creature searched nothing.
         *
         * Letting the export's own gate decide is both more faithful
         * and more robust. SEARCH sits above AREA_SWEEP in the tree, so
         * when the precondition fails the creature simply falls through
         * to stalking.
         */

        Set(
            Motivation.SearchSystematic,
            hasTarget && !attack
        );

        /*
         * PLAYER_HIDE whenever the creature has somewhere to attach the
         * belief to. The branch itself re-tests the flag, and the three
         * vetoes inside it decide whether something more recent
         * outranks the locker - so like SEARCH, this is permission
         * rather than a decision.
         */

        Set(
            Motivation.PlayerHide,
            hasTarget &&
            !attack &&
            blackboard.GetTargetFlag(TargetLogicFlag.PlayerHiding)
        );

        /*
         * SUSPECT_TARGET_RESPONSE on a LIVE stimulus only - no memory
         * term, unlike STALK.
         *
         * That is the distinction that makes the branch feel right.
         * Stalking runs on the memory of a contact; the double take is
         * a reaction to something happening now. Both are usually
         * raised at once and the branch sits above STALK, so the
         * creature stops, looks for a beat, and then goes back to
         * circling - and its own thirty second cooldown stops it doing
         * that at every footstep.
         */

        Set(
            Motivation.SuspectTargetResponse,
            !attack &&
            senses.IsAnySenseActivationAbove(SenseThreshold.Lower)
        );

        /*
         * SUSPICIOUS_ITEM whenever there is one being processed. Which
         * of the three branches takes it is decided by the item's own
         * priority, and where those branches sit relative to the
         * search - the motivation itself says nothing about that.
         */

        Set(
            Motivation.SuspiciousItem,
            !attack &&
            suspiciousItems != null &&
            suspiciousItems.InProgress
        );

        /*
         * BACKSTAGE_STALK is the creature's DEFAULT, raised precisely
         * when it knows nothing.
         *
         * Every other motivation needs a last known position, so
         * without this the tree falls to IDLE and the creature stands
         * still until something walks into its senses. In the real game
         * that gap is filled by the alien roaming the service ducts;
         * the branch is the same one, run front stage for want of
         * backstage markup.
         */

        /*
         * Also raised while a withdrawal is outstanding, because the
         * scripted withdrawal branch is guarded by this motivation.
         * The Director expresses the request as WithdrawState and this
         * is what turns it into something the tree can arbitrate on -
         * keeping a single writer for the motivation itself.
         */

        bool withdrawing =
            blackboard.Withdraw != WithdrawState.NotWithdrawing;

        Set(
            Motivation.BackstageStalk,
            withdrawing ||
            (!hasTarget &&
             (suspiciousItems == null ||
              !suspiciousItems.InProgress))
        );

        /*
         * THREAT_AWARE while being burned. The flamethrower already
         * reports onto AFFECTED_BY_FLAME_THROWER, so this needs nothing
         * new from the player side.
         */

        Set(
            Motivation.ThreatAware,
            senses.IsSenseActivationAbove(
                SenseChannelId.AffectedByFlameThrower,
                SenseThreshold.Lower)
        );
    }


    private void Set(
        Motivation motivation,
        bool value)
    {
        if (blackboard.HasMotivation(motivation) == value)
            return;

        blackboard.SetMotivation(motivation, value);

        if (logChanges)
        {
            Debug.Log(
                "[Motivation] " + motivation + " = " + value,
                this
            );
        }
    }


    /// <summary>
    /// Seconds since anything registered, for the debug overlay.
    /// </summary>
    public float SecondsSinceContact =>
        lastContactTime < 0f
            ? -1f
            : Time.time - lastContactTime;
}

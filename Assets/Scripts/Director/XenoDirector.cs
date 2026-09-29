using UnityEngine;

/// <summary>
/// The second brain, speaking the blackboard's vocabulary.
///
/// Replaces AIDirector, which was written against the superseded hint
/// design (IXenoDirectorHints, zone hints, XenoStateId). The pacing
/// logic is the same idea; the output is not. This one writes only what
/// the CATHODE blackboard actually exposes.
///
/// WHAT IT MAY WRITE, AND NOTHING ELSE:
///   - GaugeId.Retreat        how much the player is being oppressed
///   - WithdrawState          "back off" / "you are backing off"
///   - AlienPermission.*      whole classes of behaviour switched off
///
/// WHAT IT MUST NEVER DO. It may not set a destination, write a
/// perception, raise a sense, force a branch, or hand the creature the
/// player's position. It knows exactly where the player is; the
/// creature must never learn that from here. Every lever above is
/// something the TREE then interprets - which is the entire point of
/// the split, and the reason the creature still has to find you with
/// its own senses even while the Director is steering the pacing.
///
/// Its job is pacing, not difficulty. A horror game that never relents
/// is exhausting; one that never re-engages is boring.
/// </summary>
public class XenoDirector : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("References")]
    [SerializeField]
    private Transform player;

    /// <summary>
    /// Being watched is a large part of feeling oppressed, so the
    /// Director needs to know roughly what is on screen.
    /// </summary>
    [SerializeField]
    private Camera playerCamera;


    [Header("Oppression")]
    /// <summary>
    /// Distance at which the creature stops contributing pressure at
    /// all.
    /// </summary>
    [SerializeField, Min(5f)]
    private float pressureRange = 25f;

    /// <summary>
    /// How fast the retreat gauge fills while the player is being
    /// crowded, in gauge units per second.
    ///
    /// DO THE ARITHMETIC BEFORE CHANGING THIS. The rate is multiplied
    /// by proximity and then by the visible and attack weights, so the
    /// worst case is buildRate x 1 x 1.4 x 1.8 = buildRate x 2.52.
    /// At 0.02 that is 0.05/sec, so being mauled at point blank takes
    /// about 16 seconds to earn a withdrawal, and being stalked at ten
    /// metres in the open takes nearer fifty.
    ///
    /// It was 0.11 to begin with, which meant three seconds - the
    /// creature walked up to the player and immediately turned round.
    /// Oppression is supposed to accumulate over a scene, not a breath.
    /// </summary>
    [SerializeField, Min(0.001f)]
    private float buildRate = 0.02f;

    [SerializeField, Min(0.001f)]
    private float decayRate = 0.05f;

    /// <summary>
    /// Extra weight while the creature is actually visible.
    /// </summary>
    [SerializeField, Min(0f)]
    private float visibleWeight = 1.4f;

    /// <summary>
    /// Extra weight while it is committed to attacking.
    /// </summary>
    [SerializeField, Min(0f)]
    private float attackWeight = 1.8f;


    [Header("Withdrawal")]
    /// <summary>
    /// OFF BY DEFAULT, deliberately.
    ///
    /// With this off the Director still measures oppression - the gauge
    /// is useful to watch - but it never asks for a withdrawal, so
    /// ScriptedWithdrawRequested stays false and the withdrawal branch
    /// fails through as though it were still a stub. The creature hunts
    /// without ever being called off.
    ///
    /// That is the right default for now. Pacing only reads as mercy
    /// once the hunt underneath it is worth being rescued FROM, and
    /// tuning the two at the same time means never knowing which one
    /// you are feeling. Turn it on when the hunt is settled.
    /// </summary>
    [SerializeField]
    private bool requestWithdrawals = false;

    [SerializeField, Range(0f, 1f)]
    private float withdrawAbove = 0.80f;

    /// <summary>
    /// The gauge has to fall this far before the creature is allowed
    /// back. The gap to withdrawAbove is what stops it yo-yoing on the
    /// threshold, and it is why retreat feels like a decision rather
    /// than a flicker.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float releaseBelow = 0.40f;

    /// <summary>
    /// How far out the creature must get before a withdrawal is
    /// considered served.
    /// </summary>
    [SerializeField, Min(5f)]
    private float withdrawClearDistance = 30f;

    /// <summary>
    /// Hard cap on a withdrawal, whatever the gauge and distance say.
    ///
    /// A SAFETY VALVE, NOT A TUNING KNOB. On a level smaller than
    /// withdrawClearDistance across, the creature can never get clear,
    /// the release condition can never be met, and it would spend the
    /// rest of the session backing away from a wall. Nothing else in
    /// the loop can break that deadlock, because the Director stops
    /// pushing only when the creature succeeds.
    /// </summary>
    [SerializeField, Min(5f)]
    private float maximumWithdrawTime = 45f;


    [Header("Permissions")]
    /// <summary>
    /// Traps and ambushes are revoked while withdrawing. Setting one up
    /// in the middle of backing off would undo the relief the
    /// withdrawal exists to give.
    /// </summary>
    [SerializeField]
    private bool revokeTrapsWhileWithdrawing = true;


    [Header("Visibility Test")]
    [SerializeField]
    private LayerMask occlusionMask = ~0;


    [Header("Debug")]
    [SerializeField]
    private bool logDecisions = true;


    // =========================================================
    // STATE
    // =========================================================

    private XenoBlackboard blackboard;

    private Transform creature;

    private float gauge;

    private float withdrawStartedAt = -1f;


    public float Retreat => gauge;

    public WithdrawState Withdraw =>
        blackboard != null
            ? blackboard.Withdraw
            : WithdrawState.NotWithdrawing;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        DisableLegacyDirector();

        if (player == null)
        {
            PlayerController found =
                FindFirstObjectByType<PlayerController>();

            if (found != null)
            {
                player = found.transform;
            }
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }


    /// <summary>
    /// The Phase 5 Director and this one would both be pacing the same
    /// creature through different vocabularies. Same arrangement as
    /// XenoBehaviourTree switching off XenoBrain.
    /// </summary>
    private void DisableLegacyDirector()
    {
        AIDirector legacy = GetComponent<AIDirector>();

        if (legacy == null || !legacy.enabled)
            return;

        legacy.enabled = false;

        Debug.LogWarning(
            "[XenoDirector] Disabled AIDirector - it speaks the " +
            "superseded hint vocabulary.",
            this
        );
    }


    private void Update()
    {
        if (!Acquire())
            return;

        float distance = FlatDistance();

        UpdateGauge(distance);

        UpdateWithdrawal(distance);

        UpdatePermissions();
    }


    private bool Acquire()
    {
        if (blackboard == null)
        {
            blackboard = FindFirstObjectByType<XenoBlackboard>();

            if (blackboard == null)
                return false;

            creature = blackboard.transform;
        }

        return player != null && creature != null;
    }


    private float FlatDistance()
    {
        Vector3 delta = creature.position - player.position;

        delta.y = 0f;

        return delta.magnitude;
    }


    // =========================================================
    // OPPRESSION
    // =========================================================

    /// <summary>
    /// Fills while the creature is close, seen, or hunting; drains
    /// otherwise.
    ///
    /// Deliberately continuous rather than event driven. Oppression is
    /// not a thing that happens, it is a thing that accumulates - two
    /// minutes of being circled at ten metres should cost more than one
    /// jump scare, and only an integrating measure gets that right.
    /// </summary>
    private void UpdateGauge(float distance)
    {
        float proximity =
            distance >= pressureRange
                ? 0f
                : 1f - (distance / pressureRange);

        float weight = proximity;

        if (proximity > 0f)
        {
            if (IsVisibleToPlayer())
            {
                weight *= visibleWeight;
            }

            if (blackboard.HasMotivation(Motivation.Attack))
            {
                weight *= attackWeight;
            }
        }

        gauge =
            weight > 0f
                ? Mathf.Clamp01(
                    gauge + buildRate * weight * Time.deltaTime)
                : Mathf.Clamp01(
                    gauge - decayRate * Time.deltaTime);

        blackboard.SetGaugeAmount(GaugeId.Retreat, gauge);
    }


    /// <summary>
    /// Rough on-screen test. Precision is not the point - the gauge
    /// only needs to know roughly whether the thing is being looked at.
    /// </summary>
    private bool IsVisibleToPlayer()
    {
        if (playerCamera == null)
            return false;

        Vector3 eye = playerCamera.transform.position;

        Vector3 target = creature.position + Vector3.up;

        Vector3 delta = target - eye;

        float distance = delta.magnitude;

        if (distance < 0.01f)
            return false;

        float angle =
            Vector3.Angle(playerCamera.transform.forward, delta);

        if (angle > playerCamera.fieldOfView * 0.6f)
            return false;

        return !Physics.Raycast(
            eye,
            delta / distance,
            distance - 0.3f,
            occlusionMask,
            QueryTriggerInteraction.Ignore
        );
    }


    // =========================================================
    // WITHDRAWAL
    // =========================================================

    private void UpdateWithdrawal(float distance)
    {
        if (!requestWithdrawals)
        {
            /*
             * Clear any request left over from the toggle being
             * switched off mid-session, or the creature would stay
             * withdrawing forever with nothing left to release it.
             */

            if (blackboard.Withdraw != WithdrawState.NotWithdrawing)
            {
                blackboard.SetWithdrawState(
                    WithdrawState.NotWithdrawing);

                withdrawStartedAt = -1f;
            }

            return;
        }

        WithdrawState state = blackboard.Withdraw;

        if (state == WithdrawState.NotWithdrawing)
        {
            if (gauge >= withdrawAbove)
            {
                blackboard.SetWithdrawState(
                    WithdrawState.NeedsToWithdraw);

                withdrawStartedAt = Time.time;

                Log("requesting withdrawal");
            }

            return;
        }

        bool timedOut =
            withdrawStartedAt >= 0f &&
            Time.time - withdrawStartedAt >= maximumWithdrawTime;

        if (timedOut)
        {
            /*
             * Bleed the gauge off as well, or the next frame simply
             * asks for another withdrawal and nothing has changed.
             */

            gauge = Mathf.Min(gauge, releaseBelow);

            blackboard.SetGaugeAmount(GaugeId.Retreat, gauge);

            blackboard.SetWithdrawState(
                WithdrawState.NotWithdrawing);

            withdrawStartedAt = -1f;

            Log("withdrawal timed out - releasing");

            return;
        }

        /*
         * Served only when BOTH conditions hold - the pressure has
         * genuinely come off AND the creature is actually somewhere
         * else. Releasing on the gauge alone would let it turn round
         * while still standing in the doorway.
         */

        bool relieved =
            gauge <= releaseBelow &&
            distance >= withdrawClearDistance;

        if (relieved)
        {
            blackboard.SetWithdrawState(
                WithdrawState.NotWithdrawing);

            withdrawStartedAt = -1f;

            Log("withdrawal served");
        }
    }


    private void UpdatePermissions()
    {
        bool withdrawing =
            blackboard.Withdraw != WithdrawState.NotWithdrawing;

        bool allow =
            !withdrawing || !revokeTrapsWhileWithdrawing;

        blackboard.SetAllowed(AlienPermission.WillKilltrap, allow);

        blackboard.SetAllowed(AlienPermission.WillAmbush, allow);

        blackboard.SetAllowed(AlienPermission.WillFlank, allow);
    }


    // =========================================================
    // API
    // =========================================================

    public void ResetPacing()
    {
        gauge = 0f;

        withdrawStartedAt = -1f;

        if (blackboard != null)
        {
            blackboard.SetGaugeAmount(GaugeId.Retreat, 0f);

            blackboard.SetWithdrawState(
                WithdrawState.NotWithdrawing);
        }
    }


    private void Log(string what)
    {
        if (!logDecisions)
            return;

        Debug.Log(
            "[XenoDirector] " + what +
            " - retreat " + gauge.ToString("F2"),
            this
        );
    }


    /// <summary>For the debug overlay.</summary>
    public string Describe()
    {
        return
            "retreat " + gauge.ToString("F2") +
            " / " + Withdraw;
    }
}

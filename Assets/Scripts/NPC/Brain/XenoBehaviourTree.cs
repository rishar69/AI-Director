using System.Text;
using UnityEngine;

/// <summary>
/// The creature's behaviour tree, built to match alien_behave.
///
/// The root is a reactive Selector whose children are guarded
/// sequences of the form [guard] -> Branch(NAME), which is exactly the
/// shape the exported graph has: a guard condition at one column and
/// the branch body at the next.
///
/// Branches are being ported bottom up, in priority order from the
/// cheapest. Anything not ported yet is a BTNotImplemented that fails,
/// so the selector simply falls through to the next priority and the
/// tree keeps working while it is incomplete.
///
/// Replaces XenoBrain. The two must not both run.
/// </summary>
[RequireComponent(typeof(XenoBlackboard))]
[RequireComponent(typeof(XenoLocomotion))]
[RequireComponent(typeof(PerceptionSystem))]
public class XenoBehaviourTree : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Stalk Role")]
    [SerializeField, Min(1f)]
    private float stalkRingDistance = 9f;

    [SerializeField, Min(0.5f)]
    private float stalkRepositionInterval = 4f;


    [Header("Attack")]
    [SerializeField, Min(0.2f)]
    private float meleeRange = 2f;

    [SerializeField, Min(0.1f)]
    private float meleeDuration = 1.2f;

    /// <summary>
    /// ATTACK_BAN_TIMER length after a strike. Every attack branch
    /// opens with HasTimerExpired(ATTACK_BAN_TIMER), so this is what
    /// stops it chain-attacking.
    /// </summary>
    [SerializeField, Min(0f)]
    private float attackBanSeconds = 3f;


    [Header("Threat Aware")]
    /// <summary>
    /// Distance the creature keeps from an armed, aimed target.
    /// </summary>
    [SerializeField, Min(1f)]
    private float threatAwareStandoff = 6f;

    /// <summary>
    /// TargetIsWithinDistance in the export is 1.5, which is close
    /// enough that the creature is practically on top of you before it
    /// turns wary. Raised here because our flamethrower reaches 6m -
    /// at 1.5 the branch would almost never fire.
    /// </summary>
    [SerializeField, Min(0.5f)]
    private float threatAwareTriggerRange = 7f;


    [Header("Suspect Response")]
    /// <summary>
    /// How long the creature holds still staring at whatever
    /// registered. Short - it is a double take, not a search.
    /// </summary>
    [SerializeField, Min(0.2f)]
    private float suspectResponseDuration = 1.6f;

    /// <summary>
    /// Timer(30, SUSPECT_TARGET_RESPONSE_DELAY_TIMER) from the export.
    /// Armed when the branch stops, so the creature cannot keep
    /// double-taking at the same noise.
    /// </summary>
    [SerializeField, Min(0f)]
    private float suspectResponseCooldown = 30f;


    /// <summary>
    /// SEQUENCE_93, the move-to publisher.
    ///
    /// OFF, because the export says so. MainBehave carries a designer
    /// comment at y=4196, sitting between the response entry and this
    /// one, reading "disabled supect target response move to". The
    /// subtree is ported and wired at its real priority so the shape is
    /// right, but shipped inert exactly as Creative Assembly shipped
    /// it. Turn on once the search branch exists to read the flag.
    /// </summary>
    [SerializeField]
    private bool enableSuspectResponseMoveTo = false;


    [Header("Systematic Search")]
    /// <summary>
    /// How near counts as "already searched there".
    /// </summary>
    [SerializeField, Min(0.5f)]
    private float searchedTolerance = 3f;

    /// <summary>
    /// Points swept around the contact before the search gives up.
    /// </summary>
    [SerializeField, Min(0)]
    private int searchSweepPoints = 3;

    [SerializeField, Min(1f)]
    private float searchSweepRadius = 8f;

    /// <summary>
    /// Time spent looking around at each point.
    /// </summary>
    [SerializeField, Min(0.1f)]
    private float searchLookDuration = 2.5f;


    [Header("Hiding Player")]
    /// <summary>
    /// How close the creature comes to the spot it thinks you are
    /// hiding in. Close enough to be unbearable, not so close it is
    /// standing inside the locker.
    /// </summary>
    [SerializeField, Min(0.5f)]
    private float hidingApproachDistance = 2.5f;

    /// <summary>
    /// How long it prowls the spot before losing interest.
    /// </summary>
    [SerializeField, Min(1f)]
    private float hidingLingerDuration = 12f;

    /// <summary>
    /// How often it shifts position while lingering.
    /// </summary>
    [SerializeField, Min(0.5f)]
    private float hidingRepositionInterval = 3f;


    [Header("Area Sweep (no contact)")]
    /// <summary>
    /// Stands in for backstage markup the level does not have.
    ///
    /// alien_backstage_stalk gates its sweep on CanBreakout and, with
    /// no backstage, fires Assert(Error - level has no backstage.). The
    /// creature would otherwise be inert until it senses something,
    /// which for a stalker is a dead opening. With this on, the sweep
    /// runs across the ordinary NavMesh instead. Turn it off once the
    /// map has real backstage volumes.
    /// </summary>
    [SerializeField]
    private bool allowFrontstageSweep = true;

    [SerializeField, Min(5f)]
    private float sweepRadius = 40f;

    /// <summary>
    /// Minimum distance a sweep point must be from the creature, so it
    /// commits to going somewhere rather than shuffling on the spot.
    /// </summary>
    [SerializeField, Min(1f)]
    private float sweepMinimumTravel = 12f;

    [SerializeField, Min(0f)]
    private float sweepPauseDuration = 2f;


    [Header("Suspicious Items")]
    [SerializeField, Min(0.1f)]
    private float suspiciousInitialReactionTime = 1.2f;

    [SerializeField, Min(0.1f)]
    private float suspiciousCloseReactionTime = 2.5f;

    /// <summary>
    /// Radius swept around an item once the creature reaches it.
    /// </summary>
    [SerializeField, Min(1f)]
    private float suspiciousSearchRadius = 7f;

    [SerializeField, Min(0)]
    private int suspiciousSearchPoints = 2;


    [Header("Withdrawal")]
    /// <summary>
    /// How far from the last known contact counts as clear.
    /// </summary>
    [SerializeField, Min(5f)]
    private float withdrawClearDistance = 30f;


    [Header("Debug")]
    [SerializeField]
    private bool logBranchChanges = true;


    // =========================================================
    // STATE
    // =========================================================

    private XenoBlackboard blackboard;

    private PerceptionSystem perception;

    private XenoSenses senses;

    private XenoLocomotion locomotion;

    private XenoAnimatorDriver animatorDriver;

    private XenoSuspiciousItems suspiciousItems;

    private XenoEventLog eventLog;

    private BTContext context;

    private BTSelector root;

    private string lastTraceTop;

    private readonly StringBuilder traceBuilder = new StringBuilder(256);


    public BTNode Root => root;

    public XenoBlackboard Blackboard => blackboard;

    /// <summary>
    /// Raised when the creature strikes, carrying the point struck and
    /// the reach.
    ///
    /// Same contract the state machine had: the tree announces an
    /// attack at a position and never learns whether it connected.
    /// Deciding that is the job of whatever can legitimately know where
    /// the player is.
    /// </summary>
    public event System.Action<Vector3, float> OnAttack;


    public void RaiseAttack(Vector3 position)
    {
        OnAttack?.Invoke(position, meleeRange);
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        blackboard = GetComponent<XenoBlackboard>();

        perception = GetComponent<PerceptionSystem>();

        senses = GetComponent<XenoSenses>();

        if (senses == null)
        {
            senses = gameObject.AddComponent<XenoSenses>();
        }

        locomotion = GetComponent<XenoLocomotion>();

        animatorDriver = GetComponent<XenoAnimatorDriver>();

        suspiciousItems = GetComponent<XenoSuspiciousItems>();

        if (suspiciousItems == null)
        {
            suspiciousItems =
                gameObject.AddComponent<XenoSuspiciousItems>();
        }

        eventLog = GetComponent<XenoEventLog>();

        if (eventLog == null)
        {
            eventLog = gameObject.AddComponent<XenoEventLog>();
        }

        DisableLegacyBrain();

        context =
            new BTContext(
                blackboard,
                perception,
                senses,
                locomotion,
                animatorDriver,
                transform
            );

        context.SuspiciousItems = suspiciousItems;

        context.Events = eventLog;

        root = BuildRoot();
    }


    /// <summary>
    /// The Phase 3 state machine and this tree both drive locomotion.
    /// Running them together produces a creature that fights itself, so
    /// the older one is switched off rather than left to argue.
    /// </summary>
    private void DisableLegacyBrain()
    {
        XenoBrain legacy = GetComponent<XenoBrain>();

        if (legacy == null || !legacy.enabled)
            return;

        legacy.enabled = false;

        Debug.LogWarning(
            "[XenoBehaviourTree] Disabled XenoBrain on " + name +
            " - the behaviour tree replaces it.",
            this
        );
    }


    private void Update()
    {
        if (root == null)
            return;

        blackboard.BeginTick();

        root.Tick(context);

        ReportBranch();
    }


    // =========================================================
    // TREE
    // =========================================================

    /// <summary>
    /// alien_behave / SEL_1.
    ///
    /// Children are listed in the exported priority order. Everything
    /// above the three ported branches is stubbed for now, which is
    /// safe because a stub fails and the selector moves on.
    /// </summary>
    private BTSelector BuildRoot()
    {
        BTSelector selector = new BTSelector("SEL_1");

        /*
         * ORDER IS TAKEN FROM THE EXPORT'S GEOMETRY, top to bottom, and
         * it is the single most important thing in this file. Priority
         * IS the design here - every branch's meaning depends on what
         * sits above it.
         *
         * Two orderings are easy to get backwards and both matter:
         *
         *  - THREAT_AWARE (y=3262) sits ABOVE ATTACK (y=3847). When the
         *    player is armed and aiming, both motivations are usually
         *    live at once, and threat awareness has to win or the
         *    creature charges through a flamethrower.
         *
         *  - The SHOULD_ATTACK and SHOULD_HIT_AND_RUN entries (y=1372,
         *    y=1754) are gated on FLAGS, not motivations, and sit far
         *    higher than either. That is the "already committed" path.
         */

        selector.AddRange(
            Stub("REPEATED_PATHFIND_FAILS_BRANCH"),
            Stub("PRIMARY_CONTROL_RESPONSE_BRANCH"),
            Stub("SUSPEND_BRANCH"),
            Stub("SHOULD_RESET -> SEQ_1"),
            Stub("DEAD_BRANCH"),
            Stub("DESPAWN_BRANCH"),
            Stub("DebugMenuLinkTest(Disable All Aliens)"),
            Stub("DebugMenuLinkTest(Alien Gamepad control)"),
            Stub("CINEMATIC_BRANCH")
        );

        selector.Add(BuildCommittedAttackBranch());    /* y=1372 */

        selector.AddRange(
            Stub("SHOULD_HIT_AND_RUN -> ATTACK_CORE_BRANCH"),
            Stub("STUN_DAMAGE_BRANCH"),
            Stub("BREAKOUT_BRANCH"),
            Stub("SHOT_BRANCH")
        );

        selector.Add(
            BuildScriptedWithdrawalBranch());          /* y=2564 */

        selector.AddRange(
            Stub("TARGET_IS_NPC_BRANCH"),
            Stub("alien_in_vent -> in_vent_threat_aware"),
            Stub("AMBUSH_MOTIVATION")
        );

        selector.Add(BuildThreatAwareBranch());        /* y=3262 */

        selector.Add(BuildAttackCoreBranch());         /* y=3847 */

        selector.AddRange(
            Stub("BREAKOUT -> alien_flamed_unaware"),
            Stub("KILLTRAP_BRANCH")
        );

        selector.Add(
            BuildSuspectTargetResponseBranch());       /* y=4162 */

        selector.Add(
            BuildSuspectResponseMoveToPublisher());    /* y=4402 */

        selector.Add(BuildPlayerHideBranch());         /* y=4657 */

        selector.Add(
            BuildSuspiciousItemBranch(
                SuspiciousItemPriority.High));         /* y=4702 */

        selector.Add(BuildSearchSystematicBranch());   /* y=4747 */

        selector.Add(
            BuildSuspiciousItemBranch(
                SuspiciousItemPriority.Medium));       /* y=4882 */

        selector.Add(Stub("SCRIPT_BRANCH"));

        selector.Add(
            BuildSuspiciousItemBranch(
                SuspiciousItemPriority.Low));          /* y=4972 */

        selector.Add(
            Stub("BACKSTAGE_STALK -> alien_backstage_stalk"));

        selector.Add(BuildAreaSweepBranch());          /* y=5062 */

        selector.Add(BuildBackstageAreaSweepBranch()); /* y=5107 */

        selector.Add(BuildIdleBranch());               /* y=5152 */

        return selector;
    }


    private BTNode Stub(string branchName)
    {
        return new BTNotImplemented(branchName);
    }


    // =========================================================
    // COMMITTED ATTACK
    // =========================================================

    /// <summary>
    /// LogicCharacterFlags(SHOULD_ATTACK) -> Branch(ATTACK_CORE_BRANCH)
    /// at MainBehave y=1372.
    ///
    /// THIS IS WHAT MAKES A COMMITTED ATTACK UNINTERRUPTIBLE, and
    /// leaving it stubbed was a real bug rather than a missing feature.
    /// ATTACK_CORE latches SHOULD_ATTACK the moment it commits; this
    /// entry sits at y=1372, far above the scripted withdrawal at
    /// y=2564, so once the creature has committed, the Director asking
    /// it to back off no longer reaches it. Without this the withdrawal
    /// outranked the attack and the creature would turn and walk away
    /// with the player in front of it.
    ///
    /// The flag is released in two places, and it needs both:
    ///   - MeleeAttackTarget clears it once the strike lands, which is
    ///     the attack being served.
    ///   - the fall-through below clears it when the chase stops being
    ///     viable, which is the attack being abandoned.
    ///
    /// Miss either and the creature is pinned in this entry forever,
    /// unable to be called off and unable to do anything else.
    /// </summary>
    private BTNode BuildCommittedAttackBranch()
    {
        BTNode giveUp =
            new BTAlwaysFail(
                XenoLeaf.SetLogicCharacterFlags(
                    LogicFlag.ShouldAttack, false));

        BTSelector body = new BTSelector("SEL_committed_attack");

        body.AddRange(
            BuildAttackLoop(),
            giveUp
        );

        BTSequenceStateless spine =
            new BTSequenceStateless("COMMITTED_ATTACK");

        spine.AddRange(
            XenoLeaf.LogicCharacterFlags(LogicFlag.ShouldAttack),

            /*
             * Still banned from attacking means the commitment is
             * stale - clear it rather than sitting on it.
             */

            XenoLeaf.HasTimerExpired(TimerId.AttackBan),

            body
        );

        return new BTBranch(
            "Branch(ATTACK_CORE_BRANCH) [SHOULD_ATTACK]",
            spine
        );
    }


    // =========================================================
    // SCRIPTED WITHDRAWAL
    // =========================================================

    /// <summary>
    /// alien_scripted_withdrawl / SELECTOR_8, at MainBehave y=2564.
    ///
    /// THE HEIGHT IS THE WHOLE POINT. This sits above THREAT_AWARE
    /// (3262) and ATTACK (3847), so when the Director asks the creature
    /// to back off, it genuinely stops hunting - rather than politely
    /// agreeing and then charging you anyway. It is the only lever in
    /// the tree that can call off a hunt in progress, which is exactly
    /// what a pacing system needs and nothing less would do.
    ///
    /// Two paths in the export, and a third of mine:
    ///
    ///   SEQUENCE_12  "If we want to withdraw - and the alien is close
    ///                by and could attack - then do!" Takes the kill on
    ///                the way out. Gated on Alien_HitAndRun, its own
    ///                unported graph, so it fails through.
    ///
    ///   SEQUENCE_87  "Wait until the alien is far enough out, then go
    ///                backstage."
    ///
    ///   move away    Mine. See WithdrawMoveAway for why the export
    ///                needs no such node and we do.
    /// </summary>
    private BTNode BuildScriptedWithdrawalBranch()
    {
        /* SEQUENCE_12 - kill on the way out. */

        BTSequence killFirst = new BTSequence("SEQUENCE_12");

        killFirst.AddRange(
            new BTInverter(XenoLeaf.IsBackstage()),
            XenoLeaf.TargetIsPlayer(),
            XenoLeaf.TargetIsWithinDistanceUnobscured(5f, ~0),
            XenoLeaf.AlienHitAndRun()
        );

        /* SEQUENCE_87 - far enough out, so go to ground. */

        BTSequence goBackstage = new BTSequence("SEQUENCE_87");

        goBackstage.AddRange(
            XenoLeaf.RangeTestForScriptedWithdrawal(
                withdrawClearDistance),

            new BTInverter(XenoLeaf.IsBackstage()),

            XenoLeaf.StartTimer(-1f, TimerId.BackstageStalkTimeout),

            XenoLeaf.SetWithdrawState(WithdrawState.Withdrawing),

            BuildAreaSweepRole()
        );

        BTSelector selector8 = new BTSelector("SELECTOR_8");

        selector8.AddRange(
            killFirst,
            goBackstage,

            /*
             * Still too close, so put some distance in first. Marking
             * the state here as well means the Director can see the
             * request was picked up rather than ignored.
             */

            BuildWithdrawRetreat()
        );

        BTSequence spine = new BTSequence("SCRIPTED_WITHDRAWAL");

        spine.AddRange(
            XenoLeaf.ScriptedWithdrawRequested(),

            /*
             * Verbatim from the export, where SetWithdrawState(
             * NEEDS_TO_WITHDRAW) is followed immediately by clearing
             * both attack commitments. A withdrawal that left them set
             * would be overruled next tick by the committed-attack
             * entry above.
             */

            XenoLeaf.SetLogicCharacterFlags(
                LogicFlag.ShouldAttack, false),

            XenoLeaf.SetLogicCharacterFlags(
                LogicFlag.ShouldHitAndRun, false),

            selector8
        );

        return Guarded(
            Motivation.BackstageStalk,
            new BTBranch("Branch(SCRIPTED_WITHDRAWAL)", spine)
        );
    }


    private BTNode BuildWithdrawRetreat()
    {
        BTSequence retreat = new BTSequence("WITHDRAW_RETREAT");

        retreat.AddRange(
            XenoLeaf.SetWithdrawState(WithdrawState.Withdrawing),
            XenoLeaf.WithdrawMoveAway(
                withdrawClearDistance, Sst.Graceful)
        );

        return retreat;
    }


    // =========================================================
    // THREAT_AWARE_BRANCH
    // =========================================================

    /// <summary>
    /// Branch(THREAT_AWARE_BRANCH).
    ///
    /// PORTED FROM THE IN-VENT VARIANT. alien_threat_aware itself is
    /// not among the exported graphs - only alien_in_vent_threat_aware
    /// is - so the vent specific action is stubbed and the rest is
    /// taken as written.
    ///
    /// The condition chain is the whole character of this behaviour:
    /// the creature only becomes wary when the target is close, armed,
    /// still has fuel, is being faced, AND is pointing the weapon back.
    /// Any one of those failing and it goes back to hunting normally.
    /// </summary>
    private BTNode BuildThreatAwareBranch()
    {
        /* SELECTOR_49 - flinch in a vent, otherwise close slowly. */

        BTSequence ventFlinch = new BTSequence("SEQUENCE_87");

        ventFlinch.AddRange(
            XenoLeaf.IsGaugeAmountAbove(
                GaugeId.Retreat, GaugeLevel.Trace),
            XenoLeaf.AffectedByFlameThrowerInVent(Sst.Graceful)
        );

        BTSelector approach = new BTSelector("SELECTOR_49");

        approach.AddRange(
            ventFlinch,
            XenoLeaf.MoveThroughTarget(Gait.Prowl, Sst.Graceful)
        );

        /* SELECTOR_50 - hold wary, arming a long cooldown once. */

        BTSequence firstTime = new BTSequence("SEQUENCE_88");

        firstTime.AddRange(
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Visual, SenseThreshold.Activated),
            XenoLeaf.HasTimerExpired(TimerId.ThreatAwareTimeout),
            XenoLeaf.StartTimer(120f, TimerId.ThreatAwareTimeout),
            XenoLeaf.TargetIsWithinRoutingDistance(5f),
            XenoLeaf.ThreatAware(threatAwareStandoff, Sst.Graceful)
        );

        BTSelector hold = new BTSelector("SELECTOR_50");

        hold.AddRange(
            firstTime,
            XenoLeaf.ThreatAware(threatAwareStandoff, Sst.Graceful)
        );

        BTSequence body = new BTSequence("SEQ_26");

        body.AddRange(
            XenoLeaf.SetWithdrawState(WithdrawState.NotWithdrawing),
            approach,
            hold
        );

        BTSequence spine = new BTSequence("THREAT_AWARE_SPINE");

        spine.AddRange(
            XenoLeaf.HaveTarget(),
            XenoLeaf.IsEnemyOfTarget(),
            XenoLeaf.HasTimerExpired(TimerId.AttackBan),
            XenoLeaf.TargetsWeaponHasProperty("ALIEN_THREAT_AWARE_OF"),
            XenoLeaf.TargetIsWithinRoutingDistance(12f),
            XenoLeaf.TargetIsWithinDistance(threatAwareTriggerRange),
            XenoLeaf.TargetsWeaponHasAmmo(),
            XenoLeaf.AngleToTargetLessThan(20f),
            XenoLeaf.AngleNPCToTargetsAimLessThan(30f),
            body
        );

        BTNode timed =
            new BTTimerScope(
                30f,
                TimerId.SuspectTargetResponseDelay,
                new BTTimerScope(
                    25f,
                    TimerId.HeightenedSenses,
                    spine
                )
            );

        return Guarded(
            Motivation.ThreatAware,
            new BTBranch("Branch(THREAT_AWARE_BRANCH)", timed)
        );
    }


    // =========================================================
    // ATTACK_CORE_BRANCH
    // =========================================================

    /// <summary>
    /// alien_attack_core / Branch(ATTACK_BRANCH).
    ///
    /// The two nested Timer scopes come straight from the export and
    /// are the reason attacking suppresses the suspect response and
    /// heightens the senses for a while afterwards.
    /// </summary>
    private BTNode BuildAttackCoreBranch()
    {
        BTSequence spine = new BTSequence("SEQ_41");

        spine.AddRange(
            XenoLeaf.HasTimerExpired(TimerId.AttackBan),
            XenoLeaf.HaveTarget(),
            XenoLeaf.IsEnemyOfTarget(),
            XenoLeaf.HasValidRouteToTarget(),

            /*
             * The export wires the FALSE output of PLAYER_HIDING
             * onward, so a hidden player blocks the attack outright.
             */

            new BTInverter(
                XenoLeaf.TargetLogicCharacterFlags(
                    TargetLogicFlag.PlayerHiding)),

            BuildAttackPreconditions(),

            XenoLeaf.SetLogicCharacterFlags(
                LogicFlag.ShouldAttack, true),

            BuildAttackLoop()
        );

        BTNode timed =
            new BTTimerScope(
                30f,
                TimerId.SuspectTargetResponseDelay,
                new BTTimerScope(
                    25f,
                    TimerId.HeightenedSenses,
                    spine
                )
            );

        return Guarded(
            Motivation.Attack,
            new BTBranch("Branch(ATTACK_CORE_BRANCH)", timed)
        );
    }


    /// <summary>
    /// alien_attack_precondition / ATTACK_Preconds.
    ///
    /// Any one of these is enough to justify starting an attack.
    /// </summary>
    private BTNode BuildAttackPreconditions()
    {
        BTSelector preconds = new BTSelector("ATTACK_Preconds");

        BTSequence crawlspace =
            new BTSequence("crawlspace_preconds");

        BTSelector crawlSenses = new BTSelector("SEL_crawl");

        crawlSenses.AddRange(
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.SeeFlashLight, SenseThreshold.Activated),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.HeardCombat, SenseThreshold.Activated),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Combined, SenseThreshold.Activated),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Visual, SenseThreshold.Lower),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Touched, SenseThreshold.Activated)
        );

        crawlspace.AddRange(
            XenoLeaf.IsInCrawlSpace(Subject.Target),
            XenoLeaf.TargetIsWithinDistance(5f),
            crawlSenses
        );

        preconds.AddRange(
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Visual, SenseThreshold.Activated),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Touched, SenseThreshold.Activated),
            crawlspace
        );

        return preconds;
    }


    /// <summary>
    /// alien_attack_core_checks / SEQ_43.
    ///
    /// Re-run every tick while chasing, because the loop around it is a
    /// SequenceStateless. This is what decides whether the creature
    /// keeps coming - and it is far more forgiving than the sense
    /// thresholds that started the attack, which is why breaking line
    /// of sight mid-chase does not save you.
    /// </summary>
    private BTNode BuildAttackCoreChecks()
    {
        BTSelector recent = new BTSelector("SELECTOR_2");

        recent.AddRange(
            XenoLeaf.LastTimeSensed(SenseChannelId.HeardCombat,
                SenseThreshold.Activated, SenseTimeWindow.Tm2),
            XenoLeaf.LastTimeSensed(SenseChannelId.Touched,
                SenseThreshold.Activated, SenseTimeWindow.Tm1),
            XenoLeaf.LastTimeSensed(SenseChannelId.Combined,
                SenseThreshold.Activated, SenseTimeWindow.Tm2),
            XenoLeaf.LastTimeSensed(SenseChannelId.SeeFlashLight,
                SenseThreshold.Activated, SenseTimeWindow.Tm2)
        );

        BTSelector keepGoing = new BTSelector("SEL_19");

        keepGoing.AddRange(
            XenoLeaf.LastTimeSensed(SenseChannelId.Visual,
                SenseThreshold.Activated, SenseTimeWindow.Tm1),
            XenoLeaf.LastTimeSensed(SenseChannelId.Combined,
                SenseThreshold.Activated, SenseTimeWindow.Tm1),
            recent,

            /*
             * "If backstage don't stop attack behaviour until we come
             * front stage again" - verbatim from the export.
             */

            XenoLeaf.IsBackstage(),
            XenoLeaf.IsInVent(Subject.Target),
            XenoLeaf.IsInCrawlSpace(Subject.Target)
        );

        BTSequence checks = new BTSequence("SEQ_43");

        checks.AddRange(
            XenoLeaf.HasValidRouteToTarget(),
            keepGoing
        );

        return checks;
    }


    /// <summary>
    /// STATELESS_2 - the chase loop.
    ///
    /// Stateless on purpose: the checks in front of the movement are
    /// re-evaluated every tick, so the moment the creature stops having
    /// a reason to chase, the chase ends mid-stride.
    /// </summary>
    private BTNode BuildAttackLoop()
    {
        BTSequence strike = new BTSequence("SEQ_strike");

        strike.AddRange(
            XenoLeaf.TargetIsWithinDistance(meleeRange),
            XenoLeaf.HasMeleeAttackAvailable(),
            XenoLeaf.SetWithdrawState(WithdrawState.NotWithdrawing),
            XenoLeaf.MeleeAttackTarget(
                meleeDuration, attackBanSeconds, Sst.Graceful)
        );

        BTSequence charge = new BTSequence("SEQ_charge");

        charge.AddRange(
            XenoLeaf.TriggerSound("ALIEN_CHARGE_TO_ATTACK"),
            XenoLeaf.SetWithdrawState(WithdrawState.NotWithdrawing),
            XenoLeaf.MoveThroughTarget(Gait.Run, Sst.Graceful)
        );

        BTSelector choice = new BTSelector("SELECTOR_45");

        choice.AddRange(strike, charge);

        BTSequenceStateless loop =
            new BTSequenceStateless("STATELESS_2");

        loop.AddRange(
            BuildAttackCoreChecks(),
            choice
        );

        return loop;
    }


    // =========================================================
    // SUSPECT_TARGET_RESPONSE_BRANCH
    // =========================================================

    /// <summary>
    /// alien_suspect_target_response, plus the SEQUENCE_93 wrapper
    /// MainBehave puts around it at this slot.
    ///
    /// This is the band between stalking and attacking: something
    /// registered at LOWER threshold on some channel - a footstep, a
    /// door, a glimpse - which is not enough to commit to, and too much
    /// to walk past. The creature stops and looks.
    ///
    /// SEL_33 asks IsSenseActivationAbove(..., LOWER) - "is anything
    /// registering right now". That is the trigger, and LOWER is the
    /// point: at ACTIVATED the attack branch above would have taken
    /// over already.
    ///
    /// The companion query - "was the last thing that CHANGED a big
    /// one" - lives in the publisher below, not here.
    ///
    /// The walk over to look is NOT part of this branch. That is
    /// SEQUENCE_93, a separate root child immediately below this one -
    /// see BuildSuspectResponseMoveToPublisher - and the export ships
    /// it disabled. So this is the whole behaviour: stop, look, and go
    /// back to what you were doing.
    /// </summary>
    private BTNode BuildSuspectTargetResponseBranch()
    {
        /* SEL_33 - has anything registered at all. */

        BTSelector registering = new BTSelector("SEL_33");

        registering.AddRange(
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Visual, SenseThreshold.Lower),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.HeardCombat, SenseThreshold.Lower),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.HeardMovement, SenseThreshold.Lower),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Damaged, SenseThreshold.Lower),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Touched, SenseThreshold.Lower),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.SeeFlashLight, SenseThreshold.Lower),
            XenoLeaf.IsSenseActivationAbove(
                SenseChannelId.Combined, SenseThreshold.Lower)
        );

        BTSequence spine = new BTSequence("SEQ_61");

        spine.AddRange(
            registering,

            /*
             * IsInVent(OWNER) with its FALSE output wired on - taken
             * positively it would mean the creature only ever double
             * takes while inside a vent, which is backwards. It cannot
             * stop and stare from inside a duct.
             */

            new BTInverter(XenoLeaf.IsInVent(Subject.Owner)),

            XenoLeaf.HasTimerExpired(
                TimerId.SuspectTargetResponseDelay),

            XenoLeaf.HasTimerExpired(TimerId.AttackBan),

            XenoLeaf.SuspectTargetResponse(
                suspectResponseDuration, Sst.Graceful)
        );

        /*
         * The Timer scope arms the thirty second cooldown when the
         * branch stops - and SEQ_61 reads that same timer above. The
         * attack branch wraps itself in this timer too, which is why
         * the creature does not stop to double take right after a
         * chase.
         */

        BTNode timed =
            new BTTimerScope(
                suspectResponseCooldown,
                TimerId.SuspectTargetResponseDelay,
                spine
            );

        return Guarded(
            Motivation.SuspectTargetResponse,
            new BTBranch(
                "Branch(SUSPECT_TARGET_RESPONSE_BRANCH)",
                timed
            )
        );
    }


    /// <summary>
    /// SEQUENCE_93 - the move-to publisher, a SEPARATE root child.
    ///
    /// It sits directly below the response branch (y=4402 against
    /// y=4162) and shares its motivation guard, which is why it reads
    /// at first glance like part of the same slot. It is not, and the
    /// distinction matters: the response returns Running while the
    /// creature holds its stare, so anything sequenced behind it inside
    /// the same branch could never be reached.
    ///
    /// It performs no action. Every path through it ends at
    /// SetFrameFlag(COULD_DO_SUSPECT_TARGET_RESPONSE_MOVE_TO), which
    /// publishes "a walk over to look would be justified right now" for
    /// the lower priority search and hiding branches to read on the
    /// same tick. Hence BTAlwaysFail - a publisher that succeeded would
    /// halt the selector above the very branches meant to read it, and
    /// the flag is cleared again next tick.
    ///
    /// DISABLED IN THE SHIPPED GAME. See enableSuspectResponseMoveTo.
    /// </summary>
    private BTNode BuildSuspectResponseMoveToPublisher()
    {
        if (!enableSuspectResponseMoveTo)
        {
            return new BTNotImplemented(
                "SEQUENCE_93 (disabled suspect response move to)");
        }

        /*
         * SEQUENCE_89_CLONE_CLONE_CLONE - was the last CHANGE a loud
         * one, rather than "is anything registering now". A channel
         * coasting down from a bang answers yes here and no to SEL_33
         * in the branch above, and only a loud event earns a walk over.
         */

        BTSelector lastChangeWasLoud =
            new BTSelector("SEQUENCE_89_CLONE_CLONE_CLONE");

        lastChangeWasLoud.AddRange(
            Loud(SenseChannelId.Visual),
            Loud(SenseChannelId.HeardCombat),
            Loud(SenseChannelId.HeardMovement),
            Loud(SenseChannelId.Damaged),
            Loud(SenseChannelId.Touched),
            Loud(SenseChannelId.SeeFlashLight),
            Loud(SenseChannelId.Combined)
        );

        BTSequence publish = new BTSequence("SEQUENCE_93");

        publish.AddRange(
            Condition(
                "HasMotivation(SuspectTargetResponse)",
                c => c.Blackboard.HasMotivation(
                    Motivation.SuspectTargetResponse)),

            /*
             * Both of these have their FALSE outputs wired onward: the
             * creature must NOT already have walked to a suspect
             * response, and not have done one in the last ten seconds.
             */

            new BTInverter(XenoLeaf.HasDoneSuspectResponseMoveTo()),

            new BTInverter(
                XenoLeaf.HasDoneSuspectResponseWithinTime(10f)),

            XenoLeaf.IsPerformingRoleOrCouldPerformRole(
                Role.SuspectResponseMoveTo),

            lastChangeWasLoud,

            XenoLeaf.SetFrameFlag(
                FrameFlag.CouldDoSuspectTargetResponseMoveTo)
        );

        return new BTAlwaysFail(publish);
    }


    /// <summary>
    /// BackstageAreaSweep(BACKSTAGE_ONLY, SST_GRACEFULL), run front
    /// stage.
    ///
    /// The creature's default life: pick somewhere else on the level,
    /// walk there at a prowl, pause and look around, pick again. This
    /// is what it does when it knows nothing about you, and it is the
    /// only branch above IDLE that needs no contact - so it is what
    /// stops the opening of the game being a statue in a corridor.
    ///
    /// Deliberately never returns Success. A sweep is a role: it runs
    /// until a real motivation outranks it, and the reactive selector
    /// is what ends it, mid-stride, the moment the creature senses
    /// anything at all.
    /// </summary>
    private BTNode BuildAreaSweepRole()
    {
        float pauseUntil = -1f;

        bool moving = false;

        return new BTAction(
            "BackstageAreaSweep(BACKSTAGE_ONLY, SST_GRACEFULL)",

            c =>
            {
                if (pauseUntil > 0f)
                {
                    c.Animation?.RequestLookAround(
                        Mathf.Max(0.05f, pauseUntil - Time.time));

                    if (Time.time < pauseUntil)
                        return BTStatus.Running;

                    pauseUntil = -1f;
                }

                if (moving &&
                    !c.Locomotion.HasArrived &&
                    !c.Locomotion.PathFailed)
                {
                    return BTStatus.Running;
                }

                if (moving)
                {
                    moving = false;

                    c.Locomotion.Stop();

                    if (sweepPauseDuration > 0f)
                    {
                        pauseUntil =
                            Time.time + sweepPauseDuration;

                        return BTStatus.Running;
                    }
                }

                if (PickSweepPoint(c))
                {
                    moving = true;
                }

                return BTStatus.Running;
            },

            Sst.Graceful,

            (c, urgency) =>
            {
                pauseUntil = -1f;

                moving = false;

                c.Animation?.CancelLookAround();

                c.Locomotion.ClearFacing();

                if (urgency == Sst.Critical)
                {
                    c.Locomotion.Stop();
                }
            }
        );
    }


    private bool PickSweepPoint(BTContext c)
    {
        Vector3 from = c.Owner.position;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            if (!c.Locomotion.TryGetRandomPoint(
                    from, sweepRadius, out Vector3 point))
            {
                continue;
            }

            /*
             * Rejecting near points matters more than it looks. An
             * unfiltered random point is usually close, so the creature
             * spends the whole sweep taking two steps and pausing
             * instead of covering the level.
             */

            if (FlatDistance(from, point) < sweepMinimumTravel)
                continue;

            if (c.Locomotion.MoveTo(point, Gait.Prowl))
                return true;
        }

        /*
         * Nowhere far enough to go - take anything rather than stall.
         */

        if (c.Locomotion.TryGetRandomPoint(
                from, sweepRadius, out Vector3 fallback))
        {
            return c.Locomotion.MoveTo(fallback, Gait.Prowl);
        }

        return false;
    }


    // =========================================================
    // SUSPICIOUS_ITEM_BRANCH (HIGH / MEDIUM / LOW)
    // =========================================================

    /// <summary>
    /// alien_suspicious_item_high / _medium / _low, all three of which
    /// are the same subtree wrapped in a different priority test and
    /// dropped into the root selector at a different height:
    /// HIGH y=4702 ABOVE the systematic search, MEDIUM y=4882 and LOW
    /// y=4972 below it.
    ///
    /// That placement is the entire design of decoys. A thrown flare
    /// outranks looking for you; a door swinging in the next room does
    /// not. Same behaviour, three answers to "is this worth abandoning
    /// the hunt for".
    /// </summary>
    private BTNode BuildSuspiciousItemBranch(
        SuspiciousItemPriority priority)
    {
        FrameFlag published =
            priority == SuspiciousItemPriority.High
                ? FrameFlag.SuspiciousItemHighPriority
                : priority == SuspiciousItemPriority.Medium
                    ? FrameFlag.SuspiciousItemMediumPriority
                    : FrameFlag.SuspiciousItemLowPriority;

        BTSequence body = new BTSequence("SEQ_88");

        body.AddRange(
            BuildSuspiciousItemCommon(),
            XenoLeaf.SuspiciousItemDoneStage(
                SuspiciousItemStage.SearchArea)
        );

        /*
         * HIGH additionally publishes COULD_DO_SUSPICIOUS_ITEM_HIGH_
         * PRIORITY. That is the flag the hiding branch's first veto
         * reads - so from now on, a high priority item genuinely does
         * outrank standing over a locker, and one of the three vetoes
         * I wired blind has a real writer.
         */

        BTSequenceStateless spine =
            new BTSequenceStateless("STATELESS_17_CLONE");

        spine.Add(XenoLeaf.SuspiciousItemBTPriority(priority));

        spine.Add(XenoLeaf.ShouldProcessSuspiciousItem());

        /*
         * SEL_52 gates the HIGH branch on the item being more recent
         * than a sighting or a suspect response. Only HIGH - medium
         * and low sit below the search in the root order and are not
         * competing for the same slot.
         */

        if (priority == SuspiciousItemPriority.High)
        {
            spine.Add(
                MoreRecentThan(
                    XenoEvent.SensedSuspiciousItem,
                    XenoEvent.SensedTarget));

            spine.Add(
                MoreRecentThan(
                    XenoEvent.SensedSuspiciousItem,
                    XenoEvent.SuspectTargetResponse));
        }

        spine.Add(XenoLeaf.SetFrameFlag(published));

        if (priority == SuspiciousItemPriority.High)
        {
            spine.Add(
                XenoLeaf.SetFrameFlag(
                    FrameFlag.CouldDoSuspiciousItemHighPriority));
        }

        spine.Add(body);

        return Guarded(
            Motivation.SuspiciousItem,
            new BTBranch(
                "Branch(SUSPICIOUS_ITEM_BRANCH_" +
                priority.ToString().ToUpperInvariant() + ")",
                spine
            )
        );
    }


    /// <summary>
    /// alien_suspicious_item_common / SEQUENCE_8.
    ///
    /// A five stage march, each stage guarded by
    /// SuspiciousItemShouldDoStage and closed by
    /// SuspiciousItemDoneStage: react where you stand, come over, react
    /// up close, wait for the others, search around it.
    ///
    /// IGNORE_CHILD_FAIL ON THE OUTER SEQUENCE IS LORE-CRITICAL. Only
    /// one stage is live at a time, so every other stage in the list
    /// fails its guard on every tick. Under CHILD_DEFAULT the first
    /// failed guard would kill the whole branch and nothing past stage
    /// one would ever run - which is exactly what the export avoids by
    /// labelling STATELESS_7 as SequenceStateless(IGNORE_CHILD_FAIL).
    /// </summary>
    private BTNode BuildSuspiciousItemCommon()
    {
        BTSequenceStateless stages =
            new BTSequenceStateless(
                "STATELESS_7",
                BTSequence.FailPolicy.IgnoreChildFail);

        stages.AddRange(
            Stage(
                SuspiciousItemStage.InitialReaction,
                XenoLeaf.SuspiciousItemReaction(
                    "INITIAL_REACTION",
                    suspiciousInitialReactionTime,
                    Sst.Graceful)),

            Stage(
                SuspiciousItemStage.MoveCloseTo,
                BuildSuspiciousItemApproach()),

            Stage(
                SuspiciousItemStage.CloseToReaction,
                BuildSuspiciousItemCloseReaction()),

            /*
             * IsPartOfNPCGroup is false with one creature, so the wait
             * completes immediately rather than idling forever facing
             * the item. The structure is kept for a second creature.
             */

            Stage(
                SuspiciousItemStage.CloseToWaitForGroupMembers,
                BuildSuspiciousItemGroupWait()),

            Stage(
                SuspiciousItemStage.SearchArea,
                BuildSuspiciousItemSearch())
        );

        BTSequence sequence8 = new BTSequence("SEQUENCE_8");

        sequence8.AddRange(
            XenoLeaf.SuspiciousItemInProgress(),
            XenoLeaf.SetWithdrawState(WithdrawState.NotWithdrawing),
            stages
        );

        /*
         * Ten and five again, as the search branch uses - investigating
         * a decoy suppresses the double take briefly and sharpens the
         * senses while it happens.
         */

        return new BTTimerScope(
            10f,
            TimerId.SuspectTargetResponseDelay,
            new BTTimerScope(
                5f,
                TimerId.HeightenedSenses,
                sequence8
            )
        );
    }


    /// <summary>
    /// One stage: run it only while it is the live one, and mark it
    /// done the moment its action succeeds.
    /// </summary>
    private BTNode Stage(
        SuspiciousItemStage stage,
        BTNode action)
    {
        BTSequence sequence =
            new BTSequence("stage:" + stage);

        sequence.AddRange(
            XenoLeaf.SuspiciousItemShouldDoStage(stage),
            action,
            XenoLeaf.SuspiciousItemDoneStage(stage)
        );

        return sequence;
    }


    /// <summary>
    /// SuspiciousItemShouldDoStage(MOVE_CLOSE_TO).
    ///
    /// Gait falls out of distance: beyond 15m it comes at a run, inside
    /// 15m a walk, inside 8m a prowl. Re-picked every tick because the
    /// sequence is stateless, so the creature genuinely decelerates as
    /// it closes rather than choosing once and committing.
    /// </summary>
    private BTNode BuildSuspiciousItemApproach()
    {
        BTSequence slow = new BTSequence("approach_slow");

        slow.AddRange(
            XenoLeaf.SuspiciousItemIsWithinDistance(8f),
            XenoLeaf.SuspiciousItemMoveTo(Gait.Prowl, Sst.Graceful)
        );

        BTSequence fast = new BTSequence("approach_fast");

        fast.AddRange(
            XenoLeaf.SuspiciousItemIsWithinDistance(15f),
            XenoLeaf.SuspiciousItemMoveTo(Gait.Walk, Sst.Graceful)
        );

        BTSelector approach = new BTSelector("approach");

        approach.AddRange(
            slow,
            fast,
            XenoLeaf.SuspiciousItemMoveTo(Gait.Run, Sst.Graceful)
        );

        return approach;
    }


    private BTNode BuildSuspiciousItemCloseReaction()
    {
        BTSequence first = new BTSequence("close_first_member");

        first.AddRange(
            XenoLeaf.SuspiciousItemFirstGroupMember(),
            XenoLeaf.SuspiciousItemReaction(
                "CLOSE_TO_FIRST_GROUP_MEMBER_REACTION",
                suspiciousCloseReactionTime,
                Sst.Graceful)
        );

        BTSelector reaction = new BTSelector("SEL_close_reaction");

        reaction.AddRange(
            first,
            XenoLeaf.SuspiciousItemReaction(
                "CLOSE_TO_SUBSEQUENT_GROUP_MEMBER_REACTION",
                suspiciousCloseReactionTime,
                Sst.Graceful)
        );

        return reaction;
    }


    private BTNode BuildSuspiciousItemGroupWait()
    {
        BTSequence grouped = new BTSequence("SEQ_63");

        grouped.AddRange(
            XenoLeaf.IsPartOfNPCGroup(),
            XenoLeaf.IdleTimeFacingSuspiciousItem(2f, Sst.Graceful)
        );

        BTSelector wait = new BTSelector("SEL_34");

        wait.AddRange(
            grouped,

            /*
             * Alone, so there is nobody to wait for. Succeeding
             * immediately is what moves the march on to SEARCH_AREA.
             */

            new BTCondition("NotInGroup", c => true)
        );

        return wait;
    }


    /// <summary>
    /// PerformRole(SYSTEMATIC_SEARCH_SUSPICIOUS_ITEM, SST_GRACEFULL).
    ///
    /// A tighter version of the systematic search, anchored on the item
    /// rather than on a contact - the creature circles what it found,
    /// looking, before losing interest.
    /// </summary>
    private BTNode BuildSuspiciousItemSearch()
    {
        int swept = 0;

        float lookUntil = -1f;

        bool claimed = false;

        return new BTAction(
            "PerformRole(SYSTEMATIC_SEARCH_SUSPICIOUS_ITEM, " +
            "SST_GRACEFULL)",

            c =>
            {
                if (c.SuspiciousItems == null ||
                    !c.SuspiciousItems.InProgress)
                {
                    return BTStatus.Failure;
                }

                Vector3 anchor = c.SuspiciousItems.Position;

                if (!claimed)
                {
                    claimed = true;

                    swept = 0;

                    lookUntil = Time.time + searchLookDuration;
                }

                if (lookUntil > 0f)
                {
                    c.Animation?.RequestLookAround(
                        Mathf.Max(0.05f, lookUntil - Time.time));

                    if (Time.time < lookUntil)
                        return BTStatus.Running;

                    lookUntil = -1f;

                    c.Senses.MarkSearched();

                    if (swept >= suspiciousSearchPoints)
                    {
                        claimed = false;

                        c.Senses.MarkSearched(anchor);

                        return BTStatus.Success;
                    }

                    if (!MoveNearItem(c, anchor))
                    {
                        claimed = false;

                        c.Senses.MarkSearched(anchor);

                        return BTStatus.Success;
                    }

                    swept++;

                    return BTStatus.Running;
                }

                if (c.Locomotion.PathFailed ||
                    c.Locomotion.HasArrived)
                {
                    c.Locomotion.Stop();

                    lookUntil = Time.time + searchLookDuration;
                }

                return BTStatus.Running;
            },

            Sst.Graceful,

            (c, urgency) =>
            {
                claimed = false;

                lookUntil = -1f;

                c.Animation?.CancelLookAround();

                c.Locomotion.ClearFacing();

                if (urgency == Sst.Critical)
                {
                    c.Locomotion.Stop();
                }
            }
        );
    }


    private bool MoveNearItem(
        BTContext c,
        Vector3 anchor)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 offset =
                Quaternion.Euler(
                    0f,
                    Random.Range(0f, 360f),
                    0f) *
                Vector3.forward *
                Random.Range(
                    suspiciousSearchRadius * 0.4f,
                    suspiciousSearchRadius);

            if (!c.Locomotion.TrySnapToNavMesh(
                    anchor + offset,
                    out Vector3 snapped))
            {
                continue;
            }

            if (c.Locomotion.MoveTo(snapped, Gait.Prowl))
                return true;
        }

        return false;
    }


    // =========================================================
    // PLAYER_HIDING_BRANCH
    // =========================================================

    /// <summary>
    /// MainBehave y=4657 - HasMotivation(PLAYER_HIDE_MOTIVATION) ->
    /// AlienVersionPlayerIsHiding - with the body taken from
    /// Branch(PLAYER_HIDING_BRANCH), the first child of SEL_52 in
    /// alien_all_search_variants.
    ///
    /// This is the locker moment: the creature believes you went to
    /// ground somewhere it can still point at, and it comes to stand
    /// over the spot.
    ///
    /// STATELESS_HIDING IS THREE VETOES, NOT THREE STEPS. Each pair
    /// reads "did the target hide AFTER this other thing happened, and
    /// is that other thing actionable right now" - a suspicious item, a
    /// fresh sighting, a suspect response. The export wires their False
    /// outputs onward, so any one of them being true means something
    /// more recent outranks the locker and the creature goes and deals
    /// with that instead.
    ///
    /// All three are gated on EventAOccuredAfterB, which is a failing
    /// stub for want of an event log. Inverted, a failing veto passes -
    /// so the current behaviour is the permissive one: nothing outranks
    /// the locker yet. That is the right default to be wrong in, and it
    /// becomes correct for free once the event log exists.
    /// </summary>
    private BTNode BuildPlayerHideBranch()
    {
        BTSequenceStateless vetoes =
            new BTSequenceStateless("STATELESS_HIDING");

        vetoes.AddRange(
            MoreRecentThan(
                XenoEvent.TargetHiding,
                XenoEvent.SensedSuspiciousItem),

            MoreRecentThan(
                XenoEvent.TargetHiding,
                XenoEvent.SensedTarget),

            MoreRecentThan(
                XenoEvent.TargetHiding,
                XenoEvent.SuspectTargetResponse)
        );

        /* SEQUENCE_80_HIDING. */

        BTSequence body = new BTSequence("SEQUENCE_80_HIDING");

        body.AddRange(
            XenoLeaf.SetWithdrawState(WithdrawState.NotWithdrawing),
            XenoLeaf.TargetLogicCharacterFlags(
                TargetLogicFlag.PlayerHiding),
            BuildPerformHidingPlayerRole()
        );

        BTNode timed =
            new BTTimerScope(
                10f,
                TimerId.SuspectTargetResponseDelay,
                new BTTimerScope(
                    5f,
                    TimerId.HeightenedSenses,
                    body
                )
            );

        vetoes.Add(timed);

        BTSequence sequence92 = new BTSequence("SEQUENCE_92");

        sequence92.AddRange(
            /*
             * COULD_RESPOND_TO_HIDING_PLAYER has no publisher in any of
             * the twenty-three exports - it belongs to a hiding-player
             * manager that is not among them. The root slot gates on
             * AlienVersionPlayerIsHiding instead, so that is what
             * publishes the flag here, one node before the branch reads
             * it. Same shape as the search precondition publishing
             * COULD_SEARCH for SEARCH_BRANCH in the same tick.
             */

            XenoLeaf.AlienVersionPlayerIsHiding(),

            XenoLeaf.SetFrameFlag(
                FrameFlag.CouldRespondToHidingPlayer),

            XenoLeaf.IsFrameFlagSet(
                FrameFlag.CouldRespondToHidingPlayer),

            vetoes
        );

        return Guarded(
            Motivation.PlayerHide,
            new BTBranch("Branch(PLAYER_HIDING_BRANCH)", sequence92)
        );
    }


    /// <summary>
    /// One SEL_52 recency gate: this behaviour's own event has to be
    /// more recent than a competing one.
    ///
    /// THE FRAME FLAG HALF OF THE EXPORT IS DELIBERATELY OMITTED, and
    /// it is worth saying why. Each gate in the graph pairs the recency
    /// test with an IsFrameFlagSet on the competitor, reading "or that
    /// competitor cannot act anyway". Those flags are published by
    /// branches that sit BELOW their readers in the root order, and
    /// frame flags are cleared at the top of every tick - so the flag
    /// clause would be false on essentially every tick, and since the
    /// pair is an OR, the gate would pass unconditionally and sort
    /// nothing at all.
    ///
    /// Recency alone is the half that works, and it is the half the
    /// discrimination is actually about. Restore the flag clause if the
    /// publishers are ever hoisted above their readers.
    /// </summary>
    private BTNode MoreRecentThan(
        XenoEvent mine,
        XenoEvent competitor)
    {
        return XenoLeaf.EventAOccuredAfterB(mine, competitor);
    }


    /// <summary>
    /// PerformRole(HIDING_PLAYER, SST_GRACEFULL).
    ///
    /// Comes to the spot and will not leave. It closes to just outside
    /// the hiding place, then keeps shifting around it, facing it the
    /// whole time - the creature knows roughly where you are and is
    /// waiting for you to give it a reason.
    ///
    /// It gives up on a timer rather than on evidence, because there is
    /// no evidence to be had: a hidden player emits nothing. Without
    /// the timeout the creature would stand there forever, which is not
    /// tension, just a softlock.
    /// </summary>
    private BTNode BuildPerformHidingPlayerRole()
    {
        bool claimed = false;

        float until = 0f;

        float repositionAt = 0f;

        return new BTAction(
            "PerformRole(HIDING_PLAYER, SST_GRACEFULL)",

            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return BTStatus.Failure;

                Vector3 spot = c.Perception.LastKnownPosition;

                if (!claimed)
                {
                    /*
                     * Already staked this one out. Without this the
                     * branch re-arms the timer every tick it is
                     * re-entered, and since PLAYER_HIDE outranks
                     * search and stalk the creature would stand over
                     * the locker forever.
                     */

                    if (c.Senses.HasSearchedPosition(
                            spot, searchedTolerance))
                    {
                        return BTStatus.Failure;
                    }

                    c.Blackboard.SetRole(Role.HidingPlayer);

                    claimed = true;

                    until = Time.time + hidingLingerDuration;

                    repositionAt = 0f;
                }

                if (Time.time >= until)
                {
                    /*
                     * FAILURE, not success - giving up has to let the
                     * selector reach the branches below. Marking the
                     * spot searched stops the systematic search sending
                     * it straight back here.
                     */

                    c.Senses.MarkSearched(spot);

                    claimed = false;

                    c.Blackboard.SetRole(Role.None);

                    c.Locomotion.ClearFacing();

                    return BTStatus.Failure;
                }

                float distance = FlatDistance(c.Owner.position, spot);

                bool needsPoint =
                    Time.time >= repositionAt ||
                    c.Locomotion.PathFailed;

                if (distance > hidingApproachDistance && needsPoint)
                {
                    repositionAt =
                        Time.time + hidingRepositionInterval;

                    c.Locomotion.MoveTo(spot, Gait.Prowl);
                }
                else if (needsPoint)
                {
                    repositionAt =
                        Time.time + hidingRepositionInterval;

                    MoveAroundSpot(c, spot);
                }

                if (!c.Locomotion.IsMoving)
                {
                    c.Locomotion.FaceTowards(spot);
                }

                return BTStatus.Running;
            },

            Sst.Graceful,

            (c, urgency) =>
            {
                claimed = false;

                c.Blackboard.SetRole(Role.None);

                c.Locomotion.ClearFacing();

                if (urgency == Sst.Critical)
                {
                    c.Locomotion.Stop();
                }
            }
        );
    }


    private void MoveAroundSpot(
        BTContext c,
        Vector3 spot)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 offset =
                Quaternion.Euler(
                    0f,
                    Random.Range(0f, 360f),
                    0f) *
                Vector3.forward *
                hidingApproachDistance;

            if (!c.Locomotion.TrySnapToNavMesh(
                    spot + offset,
                    out Vector3 snapped))
            {
                continue;
            }

            if (c.Locomotion.MoveTo(snapped, Gait.Prowl))
                return;
        }

        c.Locomotion.Stop();
    }


    private static float FlatDistance(
        Vector3 a,
        Vector3 b)
    {
        Vector3 delta = a - b;

        delta.y = 0f;

        return delta.magnitude;
    }


    // =========================================================
    // SEARCH_SYSTEMATIC
    // =========================================================

    /// <summary>
    /// HasMotivation(SEARCH_SYSTEMATIC_MOTIVATION) at MainBehave
    /// y=4747, built from three exported graphs that make one slot:
    /// alien_systematic_search_precondition, alien_all_search_variants
    /// (SEL_52) and alien_systematic_search.
    ///
    /// This is what the creature does when the trail goes cold. Before
    /// it existed the tree fell straight through to IDLE and the
    /// creature stood still, which is the hole this closes.
    ///
    /// THE PRECONDITION IS A PUBLISHER, like SEQUENCE_93 - it ends at
    /// SetFrameFlag(COULD_SEARCH) and performs nothing. The difference
    /// is where its reader sits. SEQUENCE_93's readers are lower
    /// priority root children, so it has to fail to let the selector
    /// reach them. COULD_SEARCH is read by SEARCH_BRANCH inside this
    /// same slot, sequenced directly behind it, so here Success is
    /// exactly right and no BTAlwaysFail is wanted.
    /// </summary>
    private BTNode BuildSearchSystematicBranch()
    {
        /*
         * SELECTOR_44_CLONE - either something registered strongly in
         * the last twenty seconds, or the creature is already mid
         * search. The second keeps a sweep alive once it has begun,
         * because every point searched restamps it.
         */

        BTSelector stillWorthSearching =
            new BTSelector("SELECTOR_44_CLONE");

        stillWorthSearching.AddRange(
            XenoLeaf.HasAnySenseBeenAboveWithinTime(
                20f, SenseThreshold.Activated),
            XenoLeaf.LastTimeSearchedWithinTime(20f)
        );

        BTSequence precondition = new BTSequence("SEQ_65_CLONE");

        precondition.AddRange(
            XenoLeaf.HaveTarget(),

            /*
             * False output wired onward: keep searching only while
             * there is somewhere unvisited to go.
             */

            new BTInverter(
                XenoLeaf.HasSearchedMostRecentSensedPosition(
                    searchedTolerance)),

            stillWorthSearching,

            XenoLeaf.IsPerformingRoleOrCouldPerformRole(
                Role.SystematicSearch),

            XenoLeaf.SetFrameFlag(FrameFlag.CouldSearch)
        );

        /* alien_systematic_search / SEQ_66. */

        BTSequence searchBody = new BTSequence("SEQ_66");

        searchBody.AddRange(
            XenoLeaf.SetWithdrawState(WithdrawState.NotWithdrawing),
            XenoLeaf.TriggerSound("ALIEN_STARTS_SEARCHING"),
            XenoLeaf.HaveTarget(),
            BuildPerformSystematicSearchRole()
        );

        /*
         * Ten and five, not the thirty and twenty-five the attack
         * branch uses. Searching suppresses the double take and
         * heightens the senses far more briefly than a chase does.
         */

        BTNode timedSearch =
            new BTTimerScope(
                10f,
                TimerId.SuspectTargetResponseDelay,
                new BTTimerScope(
                    5f,
                    TimerId.HeightenedSenses,
                    searchBody
                )
            );

        /*
         * Branch(SEARCH_BRANCH), the third child of SEL_52. The
         * EventAOccuredAfterB gates that choose between search variants
         * are stubs, so this is wired IGNORE_CHILD_FAIL - an
         * unimplemented discriminator must not block the search itself.
         */

        BTSequence searchBranch =
            new BTSequence(
                "SEQUENCE_94",
                BTSequence.FailPolicy.IgnoreChildFail);

        searchBranch.AddRange(
            XenoLeaf.IsFrameFlagSet(FrameFlag.CouldSearch),

            /*
             * SEL_52 ALSO gates this on SENSED_TARGET being more recent
             * than SUSPECT_TARGET_RESPONSE, and that one is left out on
             * purpose.
             *
             * The gate exists to hand off to SEL_52's fourth child, the
             * suspect response MOVE TO - "don't search, go look at the
             * thing instead". That child is disabled in the shipped
             * game, so honouring the gate here would defer to nothing:
             * the creature would double take at a noise, stamp
             * SUSPECT_TARGET_RESPONSE, and then be permanently unable
             * to search until it sensed the player again. Which is the
             * opposite of what a search is for.
             *
             * Restore it alongside enableSuspectResponseMoveTo.
             */

            timedSearch
        );

        BTSequence slot = new BTSequence("ALL_SEARCH_VARIANTS");

        slot.AddRange(
            precondition,
            new BTBranch("Branch(SEARCH_BRANCH)", searchBranch)
        );

        return Guarded(Motivation.SearchSystematic, slot);
    }


    /// <summary>
    /// PerformRole(SYSTEMATIC_SEARCH, SST_GRACEFULL).
    ///
    /// Walks to the place something was last sensed, looks around, then
    /// sweeps a few points nearby before giving up.
    ///
    /// THE SWEEP IS WHY THIS TERMINATES CLEANLY. Marking the contact
    /// searched the instant the creature arrives would fail the
    /// precondition on the next tick and cut the behaviour off at the
    /// door. Treating "search the most recent sensed position" as the
    /// whole activity - arrive, look, check the surroundings, then
    /// declare it searched - is both what the name means and what keeps
    /// LastTimeSearchedWithinTime(20) restamped while it runs.
    /// </summary>
    private BTNode BuildPerformSystematicSearchRole()
    {
        bool claimed = false;

        int swept = 0;

        float lookUntil = -1f;

        Vector3 contact = Vector3.zero;

        return new BTAction(
            "PerformRole(SYSTEMATIC_SEARCH, SST_GRACEFULL)",

            c =>
            {
                if (!c.Perception.HasLastKnownPosition)
                    return BTStatus.Failure;

                if (!claimed)
                {
                    c.Blackboard.SetRole(Role.SystematicSearch);

                    claimed = true;

                    swept = 0;

                    lookUntil = -1f;

                    contact = c.Perception.LastKnownPosition;

                    if (!c.Locomotion.MoveTo(contact, Gait.Walk))
                        return BTStatus.Failure;

                    return BTStatus.Running;
                }

                /* Looking around at a point. */

                if (lookUntil > 0f)
                {
                    c.Animation?.RequestLookAround(
                        Mathf.Max(0.05f, lookUntil - Time.time));

                    if (Time.time < lookUntil)
                        return BTStatus.Running;

                    lookUntil = -1f;

                    c.Senses.MarkSearched();

                    if (swept >= searchSweepPoints)
                    {
                        /*
                         * Done. Recording the CONTACT - not the last
                         * sweep point - is what makes the precondition
                         * fail and ends the search.
                         */

                        c.Senses.MarkSearched(contact);

                        claimed = false;

                        return BTStatus.Success;
                    }

                    if (!MoveToSweepPoint(c))
                    {
                        c.Senses.MarkSearched(contact);

                        claimed = false;

                        return BTStatus.Success;
                    }

                    swept++;

                    return BTStatus.Running;
                }

                /* Travelling. */

                if (c.Locomotion.PathFailed)
                {
                    c.Senses.MarkSearched(contact);

                    claimed = false;

                    return BTStatus.Success;
                }

                if (c.Locomotion.HasArrived)
                {
                    c.Locomotion.Stop();

                    lookUntil = Time.time + searchLookDuration;
                }

                return BTStatus.Running;
            },

            Sst.Graceful,

            (c, urgency) =>
            {
                claimed = false;

                lookUntil = -1f;

                c.Blackboard.SetRole(Role.None);

                c.Animation?.CancelLookAround();

                c.Locomotion.ClearFacing();

                if (urgency == Sst.Critical)
                {
                    c.Locomotion.Stop();
                }
            }
        );
    }


    private bool MoveToSweepPoint(BTContext c)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 offset =
                Quaternion.Euler(
                    0f,
                    Random.Range(0f, 360f),
                    0f) *
                Vector3.forward *
                Random.Range(
                    searchSweepRadius * 0.4f,
                    searchSweepRadius);

            if (!c.Locomotion.TrySnapToNavMesh(
                    c.Perception.LastKnownPosition + offset,
                    out Vector3 snapped))
            {
                continue;
            }

            if (c.Locomotion.MoveTo(snapped, Gait.Walk))
                return true;
        }

        return false;
    }


    private BTNode Loud(SenseChannelId channel)
    {
        return XenoLeaf
            .WasSenseThresholdLastIncreaseActivationAbove(
                channel, SenseThreshold.Activated);
    }


    /// <summary>
    /// HasMotivation(STALK_MOTIVATION)
    ///   -> Branch(AREA_SWEEP_BRANCH)
    ///        HaveTarget
    ///        IsEnemyOfTarget
    ///        TargetIsPlayer
    ///        IsPerformingRoleOrCouldPerformRole(STALK)
    ///        PerformRole(STALK, SST_GRACEFULL)
    /// </summary>
    private BTNode BuildAreaSweepBranch()
    {
        BTSequence body =
            new BTSequence("AREA_SWEEP_BODY");

        body.AddRange(
            Condition("HaveTarget",
                c => c.Perception.HasLastKnownPosition),

            Condition("IsEnemyOfTarget",
                c => true),

            Condition("TargetIsPlayer",
                c => true),

            Condition("IsPerformingRoleOrCouldPerformRole(STALK)",
                c => c.Blackboard
                      .IsPerformingRoleOrCouldPerformRole(Role.Stalk)),

            BuildPerformStalkRole()
        );

        return Guarded(
            Motivation.Stalk,
            new BTBranch("Branch(AREA_SWEEP_BRANCH)", body)
        );
    }


    /// <summary>
    /// HasMotivation(BACKSTAGE_STALK_MOTIVATION)
    ///   -> Branch(BACKSTAGE_AREA_SWEEP_BRANCH)
    ///        CanBreakout
    ///        BackstageAreaSweep(BACKSTAGE_ONLY, SST_GRACEFULL)
    ///
    /// CanBreakout is hard false until the level has backstage markup,
    /// so this branch is structurally present but never taken.
    /// </summary>
    private BTNode BuildBackstageAreaSweepBranch()
    {
        BTSequence body =
            new BTSequence("BACKSTAGE_AREA_SWEEP_BODY");

        body.AddRange(
            /*
             * CanBreakout means "this level has backstage the creature
             * can get into". Ours has none, and the export's own answer
             * to that is an assert. allowFrontstageSweep substitutes a
             * NavMesh sweep instead of asserting - the same behaviour,
             * run somewhere the creature can actually stand.
             */

            Condition("CanBreakout",
                c => allowFrontstageSweep),

            BuildAreaSweepRole()
        );

        return Guarded(
            Motivation.BackstageStalk,
            new BTBranch(
                "Branch(BACKSTAGE_AREA_SWEEP_BRANCH)",
                body
            )
        );
    }


    /// <summary>
    /// Branch(IDLE_BRANCH) -> Idle(SST_GRACEFULL)
    ///
    /// No motivation guard in the export - it is the unconditional
    /// fallback, which is why the tree never returns Failure at the
    /// root.
    ///
    /// Faithfully, Idle really does mean stand still. A creature with
    /// no motivations raised is meant to be inert; movement comes from
    /// AREA_SWEEP and the backstage branches, not from here.
    /// </summary>
    private BTNode BuildIdleBranch()
    {
        BTAction idle =
            new BTAction(
                "Idle(SST_GRACEFULL)",
                c =>
                {
                    c.Locomotion.Stop();

                    return BTStatus.Running;
                },
                Sst.Graceful
            );

        return new BTBranch("Branch(IDLE_BRANCH)", idle);
    }


    // =========================================================
    // ROLE: STALK
    // =========================================================

    /// <summary>
    /// PerformRole(STALK, SST_GRACEFULL).
    ///
    /// Circles the last known position on a ring rather than closing.
    /// Never returns Success - a role runs until something outranks it,
    /// which is what makes the reactive selector do the real work.
    /// </summary>
    private BTNode BuildPerformStalkRole()
    {
        float repositionAt = 0f;

        bool claimed = false;

        return new BTAction(
            "PerformRole(STALK, SST_GRACEFULL)",

            c =>
            {
                if (!claimed)
                {
                    c.Blackboard.SetRole(Role.Stalk);

                    claimed = true;

                    repositionAt = 0f;
                }

                if (!c.Perception.HasLastKnownPosition)
                    return BTStatus.Failure;

                Vector3 contact = c.Perception.LastKnownPosition;

                bool needsPoint =
                    Time.time >= repositionAt ||
                    c.Locomotion.HasArrived ||
                    c.Locomotion.PathFailed;

                if (needsPoint)
                {
                    repositionAt =
                        Time.time + stalkRepositionInterval;

                    MoveOntoRing(c, contact);
                }

                if (!c.Locomotion.IsMoving)
                {
                    c.Locomotion.FaceTowards(contact);
                }

                return BTStatus.Running;
            },

            Sst.Graceful,

            (c, urgency) =>
            {
                claimed = false;

                c.Blackboard.SetRole(Role.None);

                c.Locomotion.ClearFacing();

                if (urgency == Sst.Critical)
                {
                    c.Locomotion.Stop();
                }
            }
        );
    }


    private void MoveOntoRing(
        BTContext c,
        Vector3 contact)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 offset =
                Quaternion.Euler(
                    0f,
                    Random.Range(0f, 360f),
                    0f) *
                Vector3.forward *
                stalkRingDistance;

            if (!c.Locomotion.TrySnapToNavMesh(
                    contact + offset,
                    out Vector3 snapped))
            {
                continue;
            }

            if (c.Locomotion.MoveTo(snapped, Gait.Prowl))
                return;
        }

        c.Locomotion.Stop();
    }


    // =========================================================
    // HELPERS
    // =========================================================

    /// <summary>
    /// The shape every top level child of SEL_1 has: a motivation gate
    /// in front of a branch.
    /// </summary>
    private BTNode Guarded(
        Motivation motivation,
        BTNode branch)
    {
        /*
         * STATELESS, and it has to be.
         *
         * A stateful sequence remembers which child was running and
         * resumes there, which would skip the motivation check for as
         * long as the branch kept running - so lowering a motivation
         * could never stop a branch already under way, and the whole
         * arbitration system would be one-way.
         */

        BTSequenceStateless gate =
            new BTSequenceStateless("guard:" + motivation);

        gate.Add(
            Condition(
                "HasMotivation(" + motivation + ")",
                c => c.Blackboard.HasMotivation(motivation)
            )
        );

        gate.Add(branch);

        return gate;
    }


    private BTCondition Condition(
        string name,
        System.Func<BTContext, bool> test)
    {
        return new BTCondition(name, test);
    }


    // =========================================================
    // DEBUG
    // =========================================================

    /// <summary>
    /// The live path through the tree, for the overlay.
    /// </summary>
    public string BuildTrace()
    {
        traceBuilder.Clear();

        BTTrace.Describe(root, traceBuilder);

        return traceBuilder.ToString();
    }


    private void ReportBranch()
    {
        if (!logBranchChanges)
            return;

        string top = ActiveBranchName();

        if (top == lastTraceTop)
            return;

        lastTraceTop = top;

        Debug.Log("[BT] " + name + " -> " + top, this);
    }


    /// <summary>
    /// Name of the branch currently running, for logs and the overlay.
    /// </summary>
    public string ActiveBranchName()
    {
        if (root == null)
            return "none";

        foreach (BTNode child in root.Children)
        {
            if (child.LastStatus != BTStatus.Running &&
                child.LastStatus != BTStatus.Success)
            {
                continue;
            }

            if (child is BTBranch direct)
                return direct.Name;

            if (child is BTComposite gate)
            {
                foreach (BTNode inner in gate.Children)
                {
                    if (inner is BTBranch branch &&
                        (branch.LastStatus == BTStatus.Running ||
                         branch.LastStatus == BTStatus.Success))
                    {
                        return branch.Name;
                    }
                }
            }

            return child.Name;
        }

        return "none";
    }
}

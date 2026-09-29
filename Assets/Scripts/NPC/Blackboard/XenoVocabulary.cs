/// <summary>
/// What the creature currently wants to be doing.
///
/// This is the arbitration input for the root selector: every top level
/// branch of alien_behave is guarded by HasMotivation(...), and the
/// selector runs the first branch whose motivation is live.
///
/// It is also the Director's entire vocabulary. The Director knows
/// where the player is; the creature does not. All the Director can do
/// is raise and lower motivations, and the tree decides what that
/// means.
///
/// Order matters only for readability - the root selector's own child
/// order is what sets priority.
/// </summary>
public enum Motivation
{
    Despawn = 0,
    Cinematic = 1,
    StunDamage = 2,
    Breakout = 3,
    Shot = 4,
    BackstageStalk = 5,
    Ambush = 6,
    ThreatAware = 7,
    Attack = 8,
    SuspectTargetResponse = 9,
    PlayerHide = 10,
    SuspiciousItem = 11,
    SearchSystematic = 12,
    Stalk = 13
}


/// <summary>
/// Persistent per-creature booleans, read as
/// LogicCharacterFlags(NAME) and written as SetLogicCharacterFlags.
///
/// These outlive a single branch. SHOULD_ATTACK being raised in one
/// branch is what lets a completely different branch higher up the
/// tree take over on a later tick.
/// </summary>
public enum LogicFlag
{
    ShouldAttack = 0,
    ShouldBreakout = 1,
    ShouldAmbush = 2,
    ShouldHitAndRun = 3,
    ShouldDespawn = 4,
    ShouldReset = 5,

    DoneBreakout = 6,
    DoneHitAndRun = 7,

    LockBackstageStalk = 8,
    HasRepeatedPathfindFailures = 9,
    IsInExploitableArea = 10,

    /// <summary>
    /// Latched once the creature has closed to routing range during an
    /// attack. Guards the sense cheat - the exports note that the alien
    /// cheats its senses until within twenty metres, and this is the
    /// flag that records having got there.
    /// </summary>
    AttackHasGotWithinRoutingThreshold = 11,

    AllowFlankedVentAttack = 12
}


/// <summary>
/// Per-creature permissions, read as AlienIsAllowed(X).
///
/// The Director's other lever: it can forbid a whole class of behaviour
/// without touching the tree.
/// </summary>
public enum AlienPermission
{
    WillKilltrap = 0,
    WillAmbush = 1,
    WillFlank = 2
}


/// <summary>
/// Which character a condition is asking about - IsInVent(OWNER)
/// versus IsInVent(TARGET).
/// </summary>
public enum Subject
{
    Owner = 0,
    Target = 1
}


/// <summary>
/// Flags read off the TARGET rather than the creature, via
/// TargetLogicCharacterFlags(NAME).
/// </summary>
public enum TargetLogicFlag
{
    PlayerHiding = 0,
    PreventGrapples = 1
}


/// <summary>
/// Transient flags, cleared at the top of every tick.
///
/// Used to publish "I could have done this" from deep inside one
/// branch so a different branch can read it on the same frame without
/// the value leaking into the next.
/// </summary>
public enum FrameFlag
{
    CouldSearch = 0,
    CouldRespondToHidingPlayer = 6,
    CouldDoSuspectTargetResponseMoveTo = 1,
    CouldDoSuspiciousItemHighPriority = 2,
    SuspiciousItemHighPriority = 3,
    SuspiciousItemMediumPriority = 4,
    SuspiciousItemLowPriority = 5
}


/// <summary>
/// Accumulating pressure values, read at coarse levels rather than as
/// raw numbers.
///
/// This is the shape the real thing uses instead of a single menace
/// float, and the levels are the whole point: branches gate on
/// IsGaugeAmountAbove(RETREAT_GAUGE, GAUGE_ACTIVATED), never on an
/// exact amount.
/// </summary>
public enum GaugeId
{
    Retreat = 0,
    StunDamage = 1
}


/// <summary>
/// Thresholds a gauge is compared against. Ordered, so a simple
/// greater-than-or-equal works.
/// </summary>
public enum GaugeLevel
{
    None = 0,
    Trace = 1,
    Lower = 2,
    Activated = 3
}


/// <summary>
/// Named countdowns. StartTimer / HasTimerExpired / ExpireTimer.
/// </summary>
public enum TimerId
{
    AttackBan = 0,
    VentBan = 1,
    SuspectTargetResponseDelay = 2,
    HeightenedSenses = 3,
    ThreatAwareTimeout = 4,
    BackstageStalkTimeout = 5,
    BackstageStalkPickKilltrap = 6,
    AmbushTimeout = 7,
    VentAttract = 8,
    FlankedVentAttack = 9,
    RepeatedPathfindFailures = 10,
    AttackKeepChasing = 11
}


/// <summary>
/// Long-running jobs a creature can claim.
///
/// IsPerformingRoleOrCouldPerformRole gates a branch on whether this
/// creature is allowed to take the job, which is how several aliens
/// would avoid all doing the same thing. With one creature it is a
/// formality, but the branches read it, so it exists.
/// </summary>
public enum Role
{
    None = 0,
    Stalk = 1,
    SystematicSearch = 2,
    SuspectResponseMoveTo = 3,
    HidingPlayer = 4
}


/// <summary>
/// Three state machine driving disengagement, written with
/// SetWithdrawState and read with WithdrawState.
/// </summary>
public enum WithdrawState
{
    NotWithdrawing = 0,
    NeedsToWithdraw = 1,
    Withdrawing = 2
}

/// <summary>
/// The stimulus channels the behaviour tree queries.
///
/// Distinct from SenseChannel, which identifies a SENSOR component
/// (VisionSense, HearingSense, ProximitySense). These are the KINDS OF
/// STIMULUS those sensors produce, and the difference matters: one
/// HearingSense feeds two channels here, because HEARD_MOVEMENT and
/// HEARD_COMBAT gate completely different branches.
///
/// Names mirror the CATHODE labels exactly.
/// </summary>
public enum SenseChannelId
{
    Visual = 0,
    HeardCombat = 1,
    HeardMovement = 2,
    Damaged = 3,
    Touched = 4,
    SeeFlashLight = 5,
    AffectedByFlameThrower = 6,

    /// <summary>
    /// Aggregate of every other channel. Queried in its own right, so
    /// it is a real channel with its own history rather than something
    /// computed at the call site.
    /// </summary>
    Combined = 7
}


/// <summary>
/// The three levels every channel is compared against.
///
/// Ordered, so a greater-than-or-equal comparison works.
/// </summary>
public enum SenseThreshold
{
    Lower = 0,
    Activated = 1,
    Upper = 2
}


/// <summary>
/// The trailing time window used by LastTimeSensed.
///
/// INFERENCE. The exports pass TM_1 or TM_2 as a third argument and do
/// not say what either means. Read here as two named lookback windows -
/// a short one and a long one - because LastTimeSensed is used as a
/// condition, so it has to resolve to a yes or no, and a window is the
/// only third argument that would make it do so.
/// </summary>
public enum SenseTimeWindow
{
    Tm1 = 0,
    Tm2 = 1
}

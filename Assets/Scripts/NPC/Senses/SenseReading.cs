using UnityEngine;

/// <summary>
/// Which sense produced a reading.
///
/// Kept as a flat enum rather than a type check so the perception
/// system can weight and display channels without knowing the concrete
/// sense classes.
/// </summary>
public enum SenseChannel
{
    None = 0,
    Vision = 1,
    Hearing = 2,
    Proximity = 3
}


/// <summary>
/// One sense's report for one frame.
///
/// IMPORTANT:
///
/// A reading carries a POSITION, never a Transform. This is the seam
/// that keeps the two-brain design honest - the xenomorph's brain is
/// built on top of readings, so it is structurally incapable of
/// following a target it cannot currently sense.
/// </summary>
public readonly struct SenseReading
{
    public readonly bool HasContact;

    /// <summary>
    /// Where the sense believes the target is. For vision this is the
    /// visible point on the target; for hearing it is where the sound
    /// was made, which may be nowhere near the target by the time it
    /// is acted on. That inaccuracy is the point.
    /// </summary>
    public readonly Vector3 Position;

    /// <summary>How convincing the contact is, 0 to 1.</summary>
    public readonly float Strength;

    public readonly SenseChannel Channel;

    /// <summary>
    /// True for one-off events such as a sound, false for sustained
    /// contact such as seeing something.
    ///
    /// The perception system integrates continuous readings over time
    /// but applies impulses as an instant bump, so a single loud bang
    /// registers even though it only exists for one frame.
    /// </summary>
    public readonly bool IsImpulse;


    private SenseReading(
        Vector3 position,
        float strength,
        SenseChannel channel,
        bool isImpulse)
    {
        HasContact = true;
        Position = position;
        Strength = Mathf.Clamp01(strength);
        Channel = channel;
        IsImpulse = isImpulse;
    }


    /// <summary>Nothing sensed this frame.</summary>
    public static SenseReading None => default;


    public static SenseReading Continuous(
        SenseChannel channel,
        Vector3 position,
        float strength)
    {
        return new SenseReading(
            position,
            strength,
            channel,
            false
        );
    }


    public static SenseReading Impulse(
        SenseChannel channel,
        Vector3 position,
        float strength)
    {
        return new SenseReading(
            position,
            strength,
            channel,
            true
        );
    }
}

using UnityEngine;

/// <summary>
/// What kind of sound was made.
///
/// The xenomorph's hearing sense reacts differently per type:
/// a thrown distraction is worth walking to, a gunshot is worth
/// sprinting to.
/// </summary>
public enum NoiseType
{
    Footstep,
    Landing,
    Door,
    Object,
    Distraction,
    Gunshot,
    Device,
    Voice
}


/// <summary>
/// A single sound made in the world at a single instant.
///
/// IMPORTANT:
///
/// A NoiseEvent carries a POSITION, never a Transform. Sound happens
/// at the place it was made and then it is over. If a listener held a
/// Transform it could follow the emitter around, which is exactly the
/// kind of accidental cheating the two-brain design exists to prevent.
/// </summary>
public readonly struct NoiseEvent
{
    /// <summary>
    /// Where the sound was made, in world space.
    /// </summary>
    public readonly Vector3 Position;

    /// <summary>
    /// How far away the sound can be heard, in metres.
    ///
    /// This is the single loudness knob. A crouched footstep might
    /// carry 1.5m, a sprint 16m, a gunshot 60m. Listeners derive
    /// their own falloff from the distance to this radius.
    /// </summary>
    public readonly float Radius;

    public readonly NoiseType Type;

    /// <summary>
    /// Who made it. May be null. Only ever used for debug display and
    /// for letting an emitter ignore its own noise - never for tracking.
    /// </summary>
    public readonly GameObject Source;

    /// <summary>
    /// Time.time at the moment of emission.
    /// </summary>
    public readonly float Time;


    public NoiseEvent(
        Vector3 position,
        float radius,
        NoiseType type,
        GameObject source,
        float time)
    {
        Position = position;
        Radius = Mathf.Max(0f, radius);
        Type = type;
        Source = source;
        Time = time;
    }


    /// <summary>
    /// How strongly this noise registers at a listening position,
    /// from 1 at the source down to 0 at the edge of its radius.
    ///
    /// Returns 0 when out of range entirely.
    /// </summary>
    public float StrengthAt(Vector3 listenerPosition)
    {
        if (Radius <= 0f)
            return 0f;

        float distance =
            Vector3.Distance(
                listenerPosition,
                Position
            );

        if (distance >= Radius)
            return 0f;

        return 1f - (distance / Radius);
    }


    public float Age =>
        UnityEngine.Time.time - Time;


    public override string ToString()
    {
        string sourceName =
            Source != null
                ? Source.name
                : "world";

        return $"{Type} from {sourceName} r={Radius:F1}m";
    }
}

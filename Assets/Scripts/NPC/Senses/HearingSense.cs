using UnityEngine;

/// <summary>
/// Reacts to NoiseEvents on the NoiseBus.
///
/// This is the sense the whole stealth loop is built around. Sight can
/// be broken by a corner; sound cannot, and the player controls how
/// much of it they make.
///
/// Hearing reports IMPULSES. A sound exists for a single instant, so it
/// gives awareness one shove of a size proportional to how loud it was
/// where the alien was standing. It does not hold awareness up the way
/// sustained line of sight does.
/// </summary>
public class HearingSense : Sense
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Hearing")]
    [SerializeField, Min(0f)]
    private float sensitivity = 1f;

    /// <summary>
    /// Sounds weaker than this are ignored entirely. Without a floor,
    /// distant footsteps trickle awareness upward forever.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float minimumStrength = 0.06f;


    [Header("Occlusion")]
    /*
     * Walls muffle sound rather than stopping it, which is why hearing
     * damps instead of failing the way vision does.
     */

    [SerializeField]
    private bool muffleThroughWalls = true;

    [SerializeField, Range(0f, 1f)]
    private float occlusionDamping = 0.45f;


    [Header("Debug")]
    [SerializeField]
    private bool logHeardNoises = false;


    // =========================================================
    // STATE
    // =========================================================

    /*
     * The loudest noise heard since the last Poll. Several noises can
     * land in a single frame and only the most alarming should count -
     * a gunshot and a footstep together is still just a gunshot.
     */

    private bool hasPending;

    private Vector3 pendingPosition;

    private float pendingStrength;

    private NoiseType pendingType;


    private float lastHeardTime = -999f;

    private Vector3 lastHeardPosition;

    private float lastHeardStrength;

    private NoiseType lastHeardType;


    public override SenseChannel Channel => SenseChannel.Hearing;

    public float Sensitivity => sensitivity;

    /// <summary>Written by XenoProgression as hearing sharpens.</summary>
    public void SetSensitivity(float value)
    {
        sensitivity = Mathf.Max(0f, value);
    }

    public float LastHeardStrength => lastHeardStrength;

    public Vector3 LastHeardPosition => lastHeardPosition;

    /// <summary>
    /// What kind of noise the last consumed reading came from.
    ///
    /// Needed because the behaviour tree does not have one hearing
    /// channel - it has HEARD_MOVEMENT and HEARD_COMBAT, and they gate
    /// completely different branches. Without the type, everything
    /// would land on one of them.
    /// </summary>
    public NoiseType LastHeardType => lastHeardType;


    // =========================================================
    // UNITY
    // =========================================================

    private void OnEnable()
    {
        NoiseBus.OnNoise += HandleNoise;
    }


    private void OnDisable()
    {
        NoiseBus.OnNoise -= HandleNoise;

        hasPending = false;
    }


    // =========================================================
    // LISTENING
    // =========================================================

    private void HandleNoise(NoiseEvent noise)
    {
        if (!SenseEnabled || !isActiveAndEnabled)
            return;

        if (IsOwnNoise(noise))
            return;

        float strength = noise.StrengthAt(SensorPosition);

        if (strength <= 0f)
            return;

        strength *= WeightFor(noise.Type) * sensitivity;

        if (muffleThroughWalls &&
            IsOccluded(SensorPosition, noise.Position))
        {
            strength *= occlusionDamping;
        }

        strength = Mathf.Clamp01(strength);

        if (strength < minimumStrength)
            return;

        if (hasPending && strength <= pendingStrength)
            return;

        hasPending = true;

        pendingPosition = noise.Position;

        pendingStrength = strength;

        pendingType = noise.Type;

        if (logHeardNoises)
        {
            Debug.Log(
                "[HearingSense] " + name + " heard " + noise.Type +
                " at strength " + strength.ToString("F2"),
                this
            );
        }
    }


    /// <summary>
    /// The alien's own footsteps must not startle it.
    /// </summary>
    private bool IsOwnNoise(NoiseEvent noise)
    {
        if (noise.Source == null)
            return false;

        return noise.Source.transform.IsChildOf(transform.root);
    }


    /// <summary>
    /// How alarming each kind of sound is, relative to a plain
    /// footstep. Loudness is already encoded in the noise radius, so
    /// this is purely about character.
    /// </summary>
    private static float WeightFor(NoiseType type)
    {
        switch (type)
        {
            case NoiseType.Footstep:
                return 0.8f;

            case NoiseType.Landing:
                return 1.0f;

            case NoiseType.Door:
                return 0.9f;

            case NoiseType.Object:
                return 1.0f;

            case NoiseType.Distraction:
                return 1.2f;

            case NoiseType.Gunshot:
                return 1.5f;

            case NoiseType.Device:
                return 0.7f;

            case NoiseType.Voice:
                return 1.1f;

            default:
                return 1.0f;
        }
    }


    // =========================================================
    // SAMPLING
    // =========================================================

    protected override SenseReading Sample()
    {
        if (!hasPending)
            return SenseReading.None;

        /*
         * Consumed on read. An impulse must fire exactly once.
         */

        hasPending = false;

        lastHeardTime = Time.time;

        lastHeardPosition = pendingPosition;

        lastHeardStrength = pendingStrength;

        lastHeardType = pendingType;

        return SenseReading.Impulse(
            SenseChannel.Hearing,
            pendingPosition,
            pendingStrength
        );
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying)
            return;

        float age = Time.time - lastHeardTime;

        if (age > 2f)
            return;

        float fade = 1f - (age / 2f);

        Gizmos.color =
            new Color(0.4f, 1f, 0.6f, fade);

        Gizmos.DrawLine(SensorPosition, lastHeardPosition);

        Gizmos.DrawWireSphere(
            lastHeardPosition,
            0.2f + lastHeardStrength
        );
    }
}

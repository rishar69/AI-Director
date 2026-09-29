using UnityEngine;

/// <summary>
/// One frame's worth of everything the menace gauge cares about.
/// </summary>
public readonly struct MenaceSample
{
    /// <summary>True when there is an alien in the level at all.</summary>
    public readonly bool HasCreature;

    /// <summary>Metres between the player and the nearest creature.</summary>
    public readonly float Distance;

    /// <summary>
    /// Whether the player can currently see it. Being watched is far
    /// more stressful than being hunted from off screen, and the gauge
    /// has to reflect that or it will happily park the alien in plain
    /// sight for a minute.
    /// </summary>
    public readonly bool VisibleToPlayer;

    public readonly XenoStateId CreatureState;


    public MenaceSample(
        bool hasCreature,
        float distance,
        bool visibleToPlayer,
        XenoStateId creatureState)
    {
        HasCreature = hasCreature;
        Distance = distance;
        VisibleToPlayer = visibleToPlayer;
        CreatureState = creatureState;
    }


    public static MenaceSample Empty =>
        new MenaceSample(false, float.MaxValue, false, XenoStateId.Patrol);
}


/// <summary>
/// How much pressure the player is currently under.
///
/// This is the pacing instrument at the heart of the Director. It is
/// NOT a difficulty setting and NOT a measure of danger - it measures
/// oppression. A creature that has been breathing down the player's
/// neck for ninety seconds is unbearable regardless of whether it has
/// actually managed to kill them, and the Director's job is to notice
/// that and pull it off.
///
/// A plain serializable class rather than a MonoBehaviour, so its
/// tuning appears inline in the Director's inspector.
/// </summary>
[System.Serializable]
public class MenaceGauge
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Sources")]
    /// <summary>
    /// Distance beyond which the creature contributes no pressure.
    /// </summary>
    [SerializeField, Min(1f)]
    private float proximityRange = 20f;

    [SerializeField, Range(0f, 2f)]
    private float proximityWeight = 0.7f;

    [SerializeField, Range(0f, 2f)]
    private float visibleWeight = 1f;

    [SerializeField, Range(0f, 2f)]
    private float pursuitWeight = 1.1f;


    [Header("Rates")]
    /*
     * Pressure is normalised to 0..1, so these are read directly as
     * "menace per second at full pressure". At 0.05 a creature applying
     * maximum pressure takes 16 seconds to trip the retreat threshold,
     * and a typical stalk - around 0.55 pressure - takes closer to 30.
     *
     * These were originally 0.4 and 0.16, tuned as though pressure were
     * already 0..1 when it actually summed to as much as 2.8. That made
     * the Director order a push-away about a second after the creature
     * found the player.
     */

    [SerializeField, Min(0.001f)]
    private float riseRate = 0.05f;

    [SerializeField, Min(0.001f)]
    private float decayRate = 0.035f;


    // =========================================================
    // STATE
    // =========================================================

    /// <summary>Accumulated oppression, 0 to 1.</summary>
    public float Value { get; private set; }

    /// <summary>
    /// This frame's raw pressure before integration. Useful for
    /// telling "nothing is happening" apart from "a lot is happening
    /// but the gauge has not caught up yet".
    /// </summary>
    public float Pressure { get; private set; }


    // =========================================================
    // TICK
    // =========================================================

    public void Tick(
        in MenaceSample sample,
        float deltaTime)
    {
        Pressure = CalculatePressure(sample);

        if (Pressure > 0.01f)
        {
            Value += Pressure * riseRate * deltaTime;
        }
        else
        {
            Value -= decayRate * deltaTime;
        }

        Value = Mathf.Clamp01(Value);
    }


    private float CalculatePressure(in MenaceSample sample)
    {
        if (!sample.HasCreature)
            return 0f;

        float proximity =
            1f - Mathf.Clamp01(sample.Distance / proximityRange);

        if (proximity <= 0f && !sample.VisibleToPlayer)
            return 0f;

        float pressure = proximity * proximityWeight;

        if (sample.VisibleToPlayer)
        {
            /*
             * Seeing it at all is stressful even at range, so this term
             * never falls to nothing while it is on screen.
             */

            pressure +=
                visibleWeight * Mathf.Max(0.3f, proximity);
        }

        pressure +=
            PursuitTerm(sample.CreatureState) * pursuitWeight;

        /*
         * Normalised against the weights, so Pressure is always 0..1
         * regardless of how the weights are tuned. Without this,
         * changing a weight silently rescales how fast menace builds.
         */

        float total =
            proximityWeight + visibleWeight + pursuitWeight;

        return total > 0.001f
            ? Mathf.Clamp01(pressure / total)
            : 0f;
    }


    /// <summary>
    /// How threatening the creature's current behaviour is.
    ///
    /// Stalking scores high on purpose. It is the least dangerous of
    /// the aggressive states and by far the most exhausting to be on
    /// the receiving end of.
    /// </summary>
    private static float PursuitTerm(XenoStateId state)
    {
        switch (state)
        {
            case XenoStateId.Attack:
                return 1f;

            case XenoStateId.Chase:
                return 0.85f;

            case XenoStateId.Stalk:
                return 0.6f;

            case XenoStateId.Search:
                return 0.3f;

            case XenoStateId.Investigate:
                return 0.2f;

            default:
                return 0f;
        }
    }


    public void Reset()
    {
        Value = 0f;

        Pressure = 0f;
    }


    /// <summary>
    /// Drops the gauge without zeroing it, for after the Director has
    /// acted on a high reading and should not immediately act again.
    /// </summary>
    public void Relieve(float amount)
    {
        Value = Mathf.Clamp01(Value - Mathf.Abs(amount));
    }
}

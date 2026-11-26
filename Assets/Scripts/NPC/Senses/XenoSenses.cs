using System;
using UnityEngine;

/// <summary>
/// Per-channel stimulus levels and their history.
///
/// This is what the behaviour tree actually asks about. PerceptionSystem
/// still exists underneath and still produces the single awareness
/// value the Phase 3 state machine used, but the tree never reads it -
/// it reads eight channels, each with three thresholds and a memory of
/// when it was last above each one.
///
/// The sensors are not replaced. VisionSense, HearingSense and
/// ProximitySense keep doing the physics; this converts what they found
/// into channel activation. One HearingSense feeds two channels, split
/// on the noise type.
///
/// Runs in LateUpdate on purpose: PerceptionSystem polls the sensors in
/// Update, and a sense reading must be consumed exactly once. Reading
/// the cached LastReading afterwards keeps that guarantee.
/// </summary>
[RequireComponent(typeof(PerceptionSystem))]
public class XenoSenses : MonoBehaviour
{
    private const int ChannelCount = 8;

    private const int ThresholdCount = 3;


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Thresholds")]
    [SerializeField, Range(0f, 1f)]
    private float lowerThreshold = 0.20f;

    [SerializeField, Range(0f, 1f)]
    private float activatedThreshold = 0.55f;

    [SerializeField, Range(0f, 1f)]
    private float upperThreshold = 0.85f;


    [Header("Rates")]
    /// <summary>
    /// Activation gained per second by a channel receiving full
    /// strength continuous stimulus.
    /// </summary>
    [SerializeField, Min(0.01f)]
    private float riseRate = 1.6f;

    /// <summary>
    /// Scales a one-off stimulus - a noise - into an instant bump.
    /// </summary>
    [SerializeField, Min(0f)]
    private float impulseScale = 0.45f;

    [SerializeField, Min(0.01f)]
    private float decayRate = 0.30f;

    [SerializeField, Min(0f)]
    private float decayDelay = 1.5f;


    [Header("Time Windows")]
    /// <summary>Short lookback for LastTimeSensed(..., TM_1).</summary>
    [SerializeField, Min(0f)]
    private float tm1Seconds = 5f;

    /// <summary>Long lookback for LastTimeSensed(..., TM_2).</summary>
    [SerializeField, Min(0f)]
    private float tm2Seconds = 20f;


    [Header("Debug")]
    [SerializeField]
    private bool logThresholdCrossings = false;


    // =========================================================
    // STATE
    // =========================================================

    private PerceptionSystem perception;

    private VisionSense vision;

    private HearingSense hearing;

    private ProximitySense proximity;

    private readonly float[] activation = new float[ChannelCount];

    private readonly float[] quietFor = new float[ChannelCount];

    /*
     * lastAbove[channel, threshold] = Time.time when that channel was
     * last at or above that threshold. Negative means never.
     */

    private readonly float[,] lastAbove =
        new float[ChannelCount, ThresholdCount];

    /*
     * The level a channel reached on its most recent INCREASE, which is
     * a different question from what it is at now - a channel can be
     * decaying through Activated without its last increase having
     * reached it.
     */

    private readonly int[] lastIncreaseLevel = new int[ChannelCount];

    private float lastSearchTime = -999f;

    private Vector3 lastSearchPosition;

    private bool hasSearchPosition;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        perception = GetComponent<PerceptionSystem>();

        vision = GetComponentInChildren<VisionSense>();

        hearing = GetComponentInChildren<HearingSense>();

        proximity = GetComponentInChildren<ProximitySense>();

        for (int c = 0; c < ChannelCount; c++)
        {
            lastIncreaseLevel[c] = -1;

            for (int t = 0; t < ThresholdCount; t++)
            {
                lastAbove[c, t] = -1f;
            }
        }
    }


    private void OnValidate()
    {
        activatedThreshold =
            Mathf.Max(activatedThreshold, lowerThreshold);

        upperThreshold =
            Mathf.Max(upperThreshold, activatedThreshold);

        tm2Seconds = Mathf.Max(tm2Seconds, tm1Seconds);
    }


    private void LateUpdate()
    {
        GatherFromSensors();

        Decay();

        UpdateCombined();

        RecordHistory();
    }


    // =========================================================
    // GATHERING
    // =========================================================

    private void GatherFromSensors()
    {
        if (vision != null)
        {
            SenseReading reading = vision.LastReading;

            /*
             * One eye, two CATHODE channels: a body seen is VISUAL, a
             * torch beam seen (its glare or its pool of light) is
             * SEE_FLASH_LIGHT, which the tree weighs separately.
             */

            if (reading.HasContact)
            {
                if (vision.LastBodyStrength > 0f)
                {
                    AddContinuous(SenseChannelId.Visual, vision.LastBodyStrength);
                }

                if (vision.LastBeamStrength > 0f)
                {
                    AddContinuous(SenseChannelId.SeeFlashLight, vision.LastBeamStrength);
                }
            }
        }

        if (proximity != null)
        {
            SenseReading reading = proximity.LastReading;

            if (reading.HasContact)
            {
                AddContinuous(SenseChannelId.Touched, reading.Strength);
            }
        }

        if (hearing != null)
        {
            SenseReading reading = hearing.LastReading;

            if (reading.HasContact)
            {
                AddImpulse(
                    ChannelForNoise(hearing.LastHeardType),
                    reading.Strength
                );
            }
        }
    }


    /// <summary>
    /// Splits one hearing sensor across the two hearing channels.
    ///
    /// Anything a person makes by moving is HEARD_MOVEMENT; anything
    /// loud and deliberate is HEARD_COMBAT. The tree treats them very
    /// differently, so this mapping is a real design decision rather
    /// than bookkeeping.
    /// </summary>
    private static SenseChannelId ChannelForNoise(NoiseType type)
    {
        switch (type)
        {
            case NoiseType.Gunshot:
            case NoiseType.Distraction:
            case NoiseType.Device:
            case NoiseType.Voice:
                return SenseChannelId.HeardCombat;

            default:
                return SenseChannelId.HeardMovement;
        }
    }


    // =========================================================
    // WRITING
    // =========================================================

    public void AddContinuous(
        SenseChannelId channel,
        float strength)
    {
        Raise(channel, strength * riseRate * Time.deltaTime);
    }


    public void AddImpulse(
        SenseChannelId channel,
        float strength)
    {
        Raise(channel, strength * impulseScale);
    }


    /// <summary>
    /// Reported by whatever hurts the creature. The flamethrower drives
    /// both DAMAGED and AFFECTED_BY_FLAME_THROWER.
    /// </summary>
    public void ReportDamage(
        float strength,
        bool fromFlameThrower)
    {
        AddImpulse(SenseChannelId.Damaged, strength);

        if (fromFlameThrower)
        {
            AddImpulse(
                SenseChannelId.AffectedByFlameThrower,
                strength
            );
        }
    }


    public void ReportFlashLight(float strength)
    {
        AddImpulse(SenseChannelId.SeeFlashLight, strength);
    }


    /// <summary>
    /// Stamped when the creature finishes searching somewhere, so
    /// LastTimeSearchedWithinTime can answer.
    /// </summary>
    public void MarkSearched()
    {
        lastSearchTime = Time.time;
    }


    /// <summary>
    /// The same stamp, but remembering WHERE.
    ///
    /// HasSearchedMostRecentSensedPosition needs a position, not just a
    /// time - it is the node that ends a systematic search, by noticing
    /// the creature has already been to the place it last sensed
    /// something. A bare timestamp cannot answer that.
    /// </summary>
    public void MarkSearched(Vector3 where)
    {
        lastSearchTime = Time.time;

        lastSearchPosition = where;

        hasSearchPosition = true;
    }


    /// <summary>
    /// Whether the last place searched was near enough to this one to
    /// count as the same place.
    /// </summary>
    public bool HasSearchedPosition(
        Vector3 target,
        float tolerance)
    {
        if (!hasSearchPosition)
            return false;

        Vector3 delta = lastSearchPosition - target;

        delta.y = 0f;

        return delta.magnitude <= tolerance;
    }


    private void Raise(
        SenseChannelId channel,
        float amount)
    {
        if (amount <= 0f)
            return;

        int c = (int)channel;

        activation[c] = Mathf.Clamp01(activation[c] + amount);

        quietFor[c] = 0f;

        int level = LevelIndexOf(activation[c]);

        if (level > lastIncreaseLevel[c])
        {
            lastIncreaseLevel[c] = level;

            if (logThresholdCrossings && level >= 0)
            {
                Debug.Log(
                    "[XenoSenses] " + channel + " rose to " +
                    (SenseThreshold)level,
                    this
                );
            }
        }
    }


    // =========================================================
    // DECAY & AGGREGATE
    // =========================================================

    private void Decay()
    {
        for (int c = 0; c < ChannelCount; c++)
        {
            if (c == (int)SenseChannelId.Combined)
                continue;

            quietFor[c] += Time.deltaTime;

            if (quietFor[c] < decayDelay)
                continue;

            if (activation[c] <= 0f)
                continue;

            activation[c] =
                Mathf.Clamp01(
                    activation[c] - decayRate * Time.deltaTime
                );

            /*
             * A channel that has fallen back below where its last
             * increase reached should stop claiming that increase.
             */

            int level = LevelIndexOf(activation[c]);

            if (level < lastIncreaseLevel[c])
            {
                lastIncreaseLevel[c] = level;
            }
        }
    }


    private void UpdateCombined()
    {
        float best = 0f;

        for (int c = 0; c < ChannelCount; c++)
        {
            if (c == (int)SenseChannelId.Combined)
                continue;

            if (activation[c] > best)
            {
                best = activation[c];
            }
        }

        int combined = (int)SenseChannelId.Combined;

        if (best > activation[combined])
        {
            activation[combined] = best;

            lastIncreaseLevel[combined] = LevelIndexOf(best);

            quietFor[combined] = 0f;
        }
        else
        {
            activation[combined] = best;
        }
    }


    private void RecordHistory()
    {
        float now = Time.time;

        for (int c = 0; c < ChannelCount; c++)
        {
            for (int t = 0; t < ThresholdCount; t++)
            {
                if (activation[c] >= ThresholdValue((SenseThreshold)t))
                {
                    lastAbove[c, t] = now;
                }
            }
        }
    }


    // =========================================================
    // QUERIES
    // =========================================================

    public float LowerThreshold => lowerThreshold;

    public float ActivatedThreshold => activatedThreshold;

    public float UpperThreshold => upperThreshold;

    /// <summary>Where the last search stamp was taken, if any. Debug view only.</summary>
    public bool HasLastSearchPosition => hasSearchPosition;

    public Vector3 LastSearchPosition => lastSearchPosition;


    public float GetActivation(SenseChannelId channel)
    {
        return activation[(int)channel];
    }


    public bool IsSenseActivationAbove(
        SenseChannelId channel,
        SenseThreshold threshold)
    {
        return activation[(int)channel] >= ThresholdValue(threshold);
    }


    public bool IsAnySenseActivationAbove(SenseThreshold threshold)
    {
        float value = ThresholdValue(threshold);

        for (int c = 0; c < ChannelCount; c++)
        {
            if (c == (int)SenseChannelId.Combined)
                continue;

            if (activation[c] >= value)
                return true;
        }

        return false;
    }


    /// <summary>
    /// Whether the channel was above the threshold inside the named
    /// lookback window.
    /// </summary>
    public bool LastTimeSensed(
        SenseChannelId channel,
        SenseThreshold threshold,
        SenseTimeWindow window)
    {
        float stamp = lastAbove[(int)channel, (int)threshold];

        if (stamp < 0f)
            return false;

        float limit =
            window == SenseTimeWindow.Tm1
                ? tm1Seconds
                : tm2Seconds;

        return Time.time - stamp <= limit;
    }


    /// <summary>Seconds since the channel was last above. -1 if never.</summary>
    public float SecondsSinceAbove(
        SenseChannelId channel,
        SenseThreshold threshold)
    {
        float stamp = lastAbove[(int)channel, (int)threshold];

        return stamp < 0f ? -1f : Time.time - stamp;
    }


    public bool HasAnySenseBeenAboveWithinTime(
        float seconds,
        SenseThreshold threshold)
    {
        float now = Time.time;

        for (int c = 0; c < ChannelCount; c++)
        {
            if (c == (int)SenseChannelId.Combined)
                continue;

            float stamp = lastAbove[c, (int)threshold];

            if (stamp >= 0f && now - stamp <= seconds)
                return true;
        }

        return false;
    }


    /// <summary>
    /// Whether the channel's most recent INCREASE took it to or past
    /// the threshold.
    ///
    /// Different from IsSenseActivationAbove: this answers "did
    /// something just happen that was big enough", not "is the level
    /// currently high". A channel coasting downward through Activated
    /// answers yes to one and no to the other, which is exactly the
    /// distinction the suspect-response branch needs.
    /// </summary>
    public bool WasSenseThresholdLastIncreaseActivationAbove(
        SenseChannelId channel,
        SenseThreshold threshold)
    {
        return lastIncreaseLevel[(int)channel] >= (int)threshold;
    }


    public bool LastTimeSearchedWithinTime(float seconds)
    {
        if (lastSearchTime < 0f)
            return false;

        return Time.time - lastSearchTime <= seconds;
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private float ThresholdValue(SenseThreshold threshold)
    {
        switch (threshold)
        {
            case SenseThreshold.Upper: return upperThreshold;

            case SenseThreshold.Activated: return activatedThreshold;

            default: return lowerThreshold;
        }
    }


    /// <summary>
    /// Highest threshold index an amount satisfies, or -1 for none.
    /// </summary>
    private int LevelIndexOf(float amount)
    {
        if (amount >= upperThreshold) return (int)SenseThreshold.Upper;

        if (amount >= activatedThreshold)
            return (int)SenseThreshold.Activated;

        if (amount >= lowerThreshold) return (int)SenseThreshold.Lower;

        return -1;
    }


    /// <summary>
    /// One line per active channel, for the debug overlay.
    /// </summary>
    public string Describe()
    {
        string result = null;

        for (int c = 0; c < ChannelCount; c++)
        {
            if (activation[c] < 0.01f)
                continue;

            int level = LevelIndexOf(activation[c]);

            string entry =
                (SenseChannelId)c + " " +
                activation[c].ToString("F2") +
                (level >= 0
                    ? " [" + (SenseThreshold)level + "]"
                    : "");

            result = result == null ? entry : result + "\n  " + entry;
        }

        return result ?? "all quiet";
    }


    public void ResetAll()
    {
        Array.Clear(activation, 0, activation.Length);

        Array.Clear(quietFor, 0, quietFor.Length);

        for (int c = 0; c < ChannelCount; c++)
        {
            lastIncreaseLevel[c] = -1;

            for (int t = 0; t < ThresholdCount; t++)
            {
                lastAbove[c, t] = -1f;
            }
        }

        lastSearchTime = -999f;

        hasSearchPosition = false;
    }
}

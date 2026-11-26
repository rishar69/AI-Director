using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How convinced the xenomorph is that there is prey nearby.
/// </summary>
public enum AwarenessLevel
{
    /// <summary>Nothing registered. Free to patrol.</summary>
    Unaware = 0,

    /// <summary>Something happened. Worth a look.</summary>
    Suspicious = 1,

    /// <summary>Certain something is here, unsure exactly where.</summary>
    Alerted = 2,

    /// <summary>Direct contact right now.</summary>
    Confirmed = 3
}


/// <summary>
/// Aggregates every Sense on this creature into one awareness value
/// plus a memory of where contact last happened.
///
/// THIS IS THE ALIEN'S ONLY WINDOW ONTO THE WORLD.
///
/// The public surface of this class exposes positions and confidences,
/// never a Transform and never a reference to the player. Phase 3's
/// brain is built strictly on top of what is exposed here, which makes
/// it structurally unable to cheat. The Director, which is allowed to
/// know where the player is, must never write into this class - it
/// influences the alien through hints, not through forged perception.
/// </summary>
[DisallowMultipleComponent]
public class PerceptionSystem : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Senses")]
    /*
     * Left empty, the senses on this object and its children are
     * collected automatically at Awake.
     */

    [SerializeField]
    private List<Sense> senses = new List<Sense>();


    [Header("Awareness")]
    /// <summary>
    /// Awareness gained per second while a continuous sense reports
    /// full strength contact.
    /// </summary>
    [SerializeField, Min(0.01f)]
    private float riseRate = 1.5f;

    /// <summary>
    /// Awareness lost per second once decay begins.
    /// </summary>
    [SerializeField, Min(0.01f)]
    private float decayRate = 0.22f;

    /// <summary>
    /// Quiet time before awareness starts falling. Prevents a target
    /// who ducks behind a pillar for half a second from resetting the
    /// alien's suspicion.
    /// </summary>
    [SerializeField, Min(0f)]
    private float decayDelay = 2f;

    /// <summary>
    /// Scales how much a single impulse - a sound - adds. At 0.35 a
    /// point-blank gunshot is most of the way to Alerted on its own,
    /// while a distant footstep barely moves the needle.
    /// </summary>
    [SerializeField, Min(0f)]
    private float impulseScale = 0.35f;


    [Header("Channel Weights")]
    [SerializeField, Min(0f)]
    private float visionWeight = 1f;

    [SerializeField, Min(0f)]
    private float hearingWeight = 1f;

    [SerializeField, Min(0f)]
    private float proximityWeight = 1.4f;


    [Header("Thresholds")]
    /*
     * Separate enter and exit values give each level hysteresis, so
     * awareness hovering exactly on a boundary does not flicker the
     * state machine dozens of times a second.
     */

    [SerializeField, Range(0f, 1f)]
    private float suspiciousEnter = 0.18f;

    [SerializeField, Range(0f, 1f)]
    private float suspiciousExit = 0.10f;

    [SerializeField, Range(0f, 1f)]
    private float alertedEnter = 0.50f;

    [SerializeField, Range(0f, 1f)]
    private float alertedExit = 0.40f;

    [SerializeField, Range(0f, 1f)]
    private float confirmedEnter = 0.88f;

    [SerializeField, Range(0f, 1f)]
    private float confirmedExit = 0.72f;


    [Header("Memory")]
    /// <summary>
    /// Contact weaker than this does not move the last known position.
    /// A faint noise should raise suspicion without pinpointing anyone.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float memoryMinStrength = 0.12f;

    /// <summary>
    /// How long a last known position stays meaningful. After this it
    /// is forgotten and the alien has nowhere in particular to search.
    /// </summary>
    [SerializeField, Min(1f)]
    private float memoryDuration = 45f;


    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;

    [SerializeField]
    private bool logLevelChanges = true;


    // =========================================================
    // STATE
    // =========================================================

    private float awareness;

    private AwarenessLevel level = AwarenessLevel.Unaware;

    private float timeSinceContact = float.MaxValue;

    private bool hasLastKnownPosition;

    private Vector3 lastKnownPosition;

    private float lastKnownPositionTime = -999f;

    private SenseChannel strongestChannel = SenseChannel.None;

    private float strongestStrength;

    /*
     * Indexed by (int)SenseChannel. Debug display only.
     */

    private readonly float[] channelStrength = new float[4];


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    /// <summary>Current conviction, 0 to 1.</summary>
    public float Awareness => awareness;

    public AwarenessLevel Level => level;

    /// <summary>Seconds since any sense last reported contact.</summary>
    public float TimeSinceContact => timeSinceContact;

    /// <summary>
    /// True while a remembered contact position is still fresh enough
    /// to be worth searching.
    /// </summary>
    public bool HasLastKnownPosition =>
        hasLastKnownPosition &&
        Time.time - lastKnownPositionTime <= memoryDuration;

    /// <summary>
    /// Where contact last happened. Meaningless unless
    /// HasLastKnownPosition is true.
    /// </summary>
    public Vector3 LastKnownPosition => lastKnownPosition;

    public float TimeSinceLastKnownPosition =>
        Time.time - lastKnownPositionTime;

    /// <summary>Which sense contributed most this frame.</summary>
    public SenseChannel StrongestChannel => strongestChannel;

    public float StrongestStrength => strongestStrength;

    public IReadOnlyList<Sense> Senses => senses;


    public event Action<AwarenessLevel, AwarenessLevel> OnLevelChanged;

    public event Action<Vector3> OnLastKnownPositionUpdated;


    /// <summary>
    /// Strength contributed by one channel on the last frame. Debug
    /// display only.
    /// </summary>
    public float StrengthOf(SenseChannel channel)
    {
        int index = (int)channel;

        return index >= 0 && index < channelStrength.Length
            ? channelStrength[index]
            : 0f;
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        if (senses == null || senses.Count == 0)
        {
            senses =
                new List<Sense>(
                    GetComponentsInChildren<Sense>(true)
                );
        }

        if (senses.Count == 0)
        {
            Debug.LogWarning(
                "[PerceptionSystem] " + name +
                " has no senses. It will never notice anything.",
                this
            );
        }
    }


    private void OnValidate()
    {
        suspiciousExit = Mathf.Min(suspiciousExit, suspiciousEnter);

        alertedExit = Mathf.Min(alertedExit, alertedEnter);

        confirmedExit = Mathf.Min(confirmedExit, confirmedEnter);
    }


    private void Update()
    {
        PollSenses(out bool hadContact);

        IntegrateDecay(hadContact);

        UpdateLevel();
    }


    // =========================================================
    // POLLING
    // =========================================================

    private void PollSenses(out bool hadContact)
    {
        hadContact = false;

        strongestChannel = SenseChannel.None;

        strongestStrength = 0f;

        Array.Clear(channelStrength, 0, channelStrength.Length);

        float bestMemoryStrength = 0f;

        Vector3 bestMemoryPosition = Vector3.zero;

        for (int i = 0; i < senses.Count; i++)
        {
            Sense sense = senses[i];

            if (sense == null)
                continue;

            SenseReading reading = sense.Poll();

            if (!reading.HasContact)
                continue;

            hadContact = true;

            float weight = WeightFor(reading.Channel);

            float weighted =
                Mathf.Clamp01(reading.Strength * weight);

            RecordChannel(reading.Channel, weighted);

            if (reading.IsImpulse)
            {
                /*
                 * A sound is a shove, not a pressure. It has to land in
                 * one frame or it may as well not have happened.
                 */

                awareness += weighted * impulseScale;
            }
            else
            {
                awareness += weighted * riseRate * Time.deltaTime;
            }

            if (weighted > strongestStrength)
            {
                strongestStrength = weighted;

                strongestChannel = reading.Channel;
            }

            if (reading.Strength >= memoryMinStrength &&
                weighted > bestMemoryStrength)
            {
                bestMemoryStrength = weighted;

                bestMemoryPosition = reading.Position;
            }
        }

        awareness = Mathf.Clamp01(awareness);

        if (bestMemoryStrength > 0f)
        {
            SetLastKnownPosition(bestMemoryPosition);
        }
    }


    private void RecordChannel(
        SenseChannel channel,
        float strength)
    {
        int index = (int)channel;

        if (index < 0 || index >= channelStrength.Length)
            return;

        if (strength > channelStrength[index])
        {
            channelStrength[index] = strength;
        }
    }


    private float WeightFor(SenseChannel channel)
    {
        switch (channel)
        {
            case SenseChannel.Vision:
                return visionWeight;

            case SenseChannel.Hearing:
                return hearingWeight;

            case SenseChannel.Proximity:
                return proximityWeight;

            default:
                return 1f;
        }
    }


    // =========================================================
    // DECAY
    // =========================================================

    private void IntegrateDecay(bool hadContact)
    {
        if (hadContact)
        {
            timeSinceContact = 0f;

            return;
        }

        timeSinceContact += Time.deltaTime;

        if (timeSinceContact < decayDelay)
            return;

        awareness =
            Mathf.Clamp01(
                awareness - decayRate * Time.deltaTime
            );
    }


    // =========================================================
    // LEVEL
    // =========================================================

    private void UpdateLevel()
    {
        AwarenessLevel next = EvaluateLevel(awareness, level);

        if (next == level)
            return;

        AwarenessLevel previous = level;

        level = next;

        if (logLevelChanges)
        {
            Debug.Log(
                "[Perception] " + name + "  " + previous +
                " -> " + next +
                "  (" + awareness.ToString("F2") + ")",
                this
            );
        }

        OnLevelChanged?.Invoke(previous, next);
    }


    /// <summary>
    /// Applies the enter thresholds going up and the exit thresholds
    /// coming down, so each level has a dead band around it.
    /// </summary>
    private AwarenessLevel EvaluateLevel(
        float value,
        AwarenessLevel current)
    {
        AwarenessLevel result = current;

        if (value >= confirmedEnter)
        {
            result = AwarenessLevel.Confirmed;
        }
        else if (value >= alertedEnter &&
                 result < AwarenessLevel.Alerted)
        {
            result = AwarenessLevel.Alerted;
        }
        else if (value >= suspiciousEnter &&
                 result < AwarenessLevel.Suspicious)
        {
            result = AwarenessLevel.Suspicious;
        }

        if (result == AwarenessLevel.Confirmed &&
            value < confirmedExit)
        {
            result = AwarenessLevel.Alerted;
        }

        if (result == AwarenessLevel.Alerted &&
            value < alertedExit)
        {
            result = AwarenessLevel.Suspicious;
        }

        if (result == AwarenessLevel.Suspicious &&
            value < suspiciousExit)
        {
            result = AwarenessLevel.Unaware;
        }

        return result;
    }


    // =========================================================
    // MEMORY
    // =========================================================

    private void SetLastKnownPosition(Vector3 position)
    {
        hasLastKnownPosition = true;

        lastKnownPosition = position;

        lastKnownPositionTime = Time.time;

        OnLastKnownPositionUpdated?.Invoke(position);
    }


    /// <summary>
    /// Wipes awareness and memory. Used when the Director pulls the
    /// alien out of the level, and by Phase 3 when a search gives up.
    /// </summary>
    public void ResetPerception()
    {
        awareness = 0f;

        timeSinceContact = float.MaxValue;

        hasLastKnownPosition = false;

        Array.Clear(channelStrength, 0, channelStrength.Length);

        strongestChannel = SenseChannel.None;

        strongestStrength = 0f;

        UpdateLevel();
    }


    /// <summary>
    /// Lets the alien discard a search location it has already checked
    /// without wiping how alert it is.
    /// </summary>
    public void NoteContact(Vector3 position)
    {
        /*
         * A contact the creature made itself rather than through a
         * Sense poll - looking under a desk and finding someone there.
         * Treated exactly like a sighting at that spot.
         */
        awareness = Mathf.Max(awareness, alertedEnter);

        timeSinceContact = 0f;

        SetLastKnownPosition(position);

        UpdateLevel();
    }


    public void ForgetLastKnownPosition()
    {
        hasLastKnownPosition = false;
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying)
            return;

        if (!HasLastKnownPosition)
            return;

        Gizmos.color = new Color(1f, 0.25f, 0.9f, 0.9f);

        Gizmos.DrawWireSphere(lastKnownPosition, 0.4f);

        Gizmos.DrawLine(
            transform.position + Vector3.up,
            lastKnownPosition
        );
    }
}

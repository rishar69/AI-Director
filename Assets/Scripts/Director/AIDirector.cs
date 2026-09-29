using UnityEngine;

/// <summary>
/// The second brain. The one the project is named after.
///
/// The Director knows exactly where the player is at all times and is
/// allowed to. What it CANNOT do is drive the creature. It has no
/// reference to XenoBrain's states, cannot set a destination, and
/// cannot write a perception. Its entire vocabulary is the three things
/// on IXenoDirectorHints: an area worth looking at, "back off", and
/// "stop toying".
///
/// Its job is pacing, not difficulty. It watches how oppressed the
/// player is and pushes the creature away when that gets unbearable,
/// then quietly steers it back when nothing has happened for too long.
/// Both halves matter - a horror game that never relents is exhausting,
/// and one that never re-engages is boring.
/// </summary>
public class AIDirector : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("References")]
    [SerializeField]
    private PlayerController player;

    /// <summary>
    /// Used to work out whether the creature is on screen. Being
    /// watched is a large part of feeling oppressed.
    /// </summary>
    [SerializeField]
    private Camera playerCamera;

    [SerializeField]
    private ZoneGraph zones;


    [Header("Menace")]
    [SerializeField]
    private MenaceGauge menace = new MenaceGauge();

    [SerializeField, Range(0f, 1f)]
    private float retreatAbove = 0.80f;

    /// <summary>
    /// Menace must fall this far before the creature is allowed back.
    /// The gap between this and retreatAbove is what stops it
    /// yo-yoing on the threshold.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float releaseBelow = 0.45f;


    [Header("Visibility Test")]
    [SerializeField]
    private LayerMask occlusionMask = ~0;

    [SerializeField, Min(1f)]
    private float maxVisibleDistance = 40f;


    [Header("Pull In")]
    /// <summary>
    /// How long the creature may wander obliviously before the Director
    /// starts steering it back toward the player's part of the level.
    /// </summary>
    [SerializeField, Min(0f)]
    private float boredomTime = 25f;

    /*
     * The pull-in hint is the one place this class could accidentally
     * become a cheat. It is a ZONE, and never closer than
     * pullInMinDistance, so the creature arrives in the neighbourhood
     * and then has to find the player using its own senses.
     *
     * Lowering pullInMinDistance toward zero turns the Director into a
     * tracker. Do not.
     */

    [SerializeField, Min(8f)]
    private float pullInMinDistance = 15f;

    [SerializeField, Min(10f)]
    private float pullInMaxDistance = 34f;


    [Header("Push Away")]
    [SerializeField, Min(10f)]
    private float pushAwayMinDistance = 40f;


    [Header("Commit")]
    /// <summary>
    /// After this long with nothing happening, the Director stops
    /// letting the creature stalk and tells it to commit.
    /// </summary>
    [SerializeField, Min(10f)]
    private float commitAfterQuiet = 90f;


    [Header("Relocation")]
    [SerializeField]
    private bool allowRelocation = true;

    /// <summary>
    /// Menace so high that walking away is not enough - the creature is
    /// moved bodily to a vent or distant zone.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float relocateAbove = 0.97f;

    [SerializeField, Min(10f)]
    private float relocateMinDistance = 45f;

    [SerializeField, Min(0f)]
    private float relocateCooldown = 60f;


    [Header("Timing")]
    [SerializeField, Min(0.5f)]
    private float hintInterval = 8f;


    [Header("Debug")]
    [SerializeField]
    private bool logDecisions = true;

    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    private bool retreating;

    private float unawareTimer;

    private float quietTimer;

    private float nextHintTime;

    private float lastRelocateTime = -999f;

    private MenaceSample lastSample;


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    public float Menace => menace.Value;

    public float Pressure => menace.Pressure;

    public bool IsPushingAway => retreating;

    public bool IsCommitting { get; private set; }

    public float QuietTime => quietTimer;

    public float UnawareTime => unawareTimer;

    public MenaceSample LastSample => lastSample;

    public bool HasZones => zones != null && zones.IsBuilt;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
        }

        if (playerCamera == null && player != null)
        {
            playerCamera = player.GetComponentInChildren<Camera>();
        }

        if (zones == null)
        {
            zones = FindFirstObjectByType<ZoneGraph>();
        }
    }


    private void Update()
    {
        if (player == null)
            return;

        DirectorHintReceiver nearest = FindNearestCreature();

        lastSample = BuildSample(nearest);

        menace.Tick(lastSample, Time.deltaTime);

        UpdateTimers(nearest);

        UpdateRetreatDecision();

        ApplyDirectives(nearest);
    }


    // =========================================================
    // SAMPLING
    // =========================================================

    private DirectorHintReceiver FindNearestCreature()
    {
        var receivers = DirectorHintReceiver.Active;

        DirectorHintReceiver best = null;

        float bestSqr = float.MaxValue;

        Vector3 origin = player.transform.position;

        for (int i = 0; i < receivers.Count; i++)
        {
            DirectorHintReceiver receiver = receivers[i];

            if (receiver == null)
                continue;

            float sqr =
                (receiver.transform.position - origin).sqrMagnitude;

            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;

            best = receiver;
        }

        return best;
    }


    private MenaceSample BuildSample(DirectorHintReceiver creature)
    {
        if (creature == null)
            return MenaceSample.Empty;

        Vector3 delta =
            creature.transform.position - player.transform.position;

        delta.y = 0f;

        XenoStateId state =
            creature.Brain != null
                ? creature.Brain.CurrentState
                : XenoStateId.Patrol;

        return new MenaceSample(
            true,
            delta.magnitude,
            IsVisibleToPlayer(creature.transform),
            state
        );
    }


    /// <summary>
    /// Rough on-screen test.
    ///
    /// Uses a widened slice of the vertical field of view as a stand-in
    /// for the real frustum. Precision is not the point - the gauge
    /// only needs to know roughly whether the thing is being looked at.
    /// </summary>
    private bool IsVisibleToPlayer(Transform creature)
    {
        if (playerCamera == null)
            return false;

        Vector3 eye = playerCamera.transform.position;

        Vector3 target = creature.position + Vector3.up;

        Vector3 delta = target - eye;

        float distance = delta.magnitude;

        if (distance > maxVisibleDistance || distance < 0.01f)
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
    // TIMERS
    // =========================================================

    private void UpdateTimers(DirectorHintReceiver creature)
    {
        bool oblivious =
            creature == null ||
            creature.Brain == null ||
            creature.Brain.CurrentState == XenoStateId.Patrol;

        if (oblivious)
        {
            unawareTimer += Time.deltaTime;
        }
        else
        {
            unawareTimer = 0f;
        }

        /*
         * Quiet means nothing frightening has happened lately - not
         * merely that the creature is elsewhere.
         */

        bool quiet =
            menace.Pressure < 0.15f &&
            (creature == null ||
             creature.Brain == null ||
             creature.Brain.CurrentState <= XenoStateId.Search);

        if (quiet)
        {
            quietTimer += Time.deltaTime;
        }
        else
        {
            quietTimer = 0f;
        }
    }


    // =========================================================
    // DECISIONS
    // =========================================================

    private void UpdateRetreatDecision()
    {
        bool previous = retreating;

        if (menace.Value >= retreatAbove)
        {
            retreating = true;
        }
        else if (menace.Value <= releaseBelow)
        {
            retreating = false;
        }

        if (previous == retreating || !logDecisions)
            return;

        Debug.Log(
            "[AIDirector] " +
            (retreating ? "PUSHING AWAY" : "releasing") +
            " - menace " + menace.Value.ToString("F2"),
            this
        );
    }


    private void ApplyDirectives(DirectorHintReceiver creature)
    {
        IsCommitting =
            !retreating &&
            quietTimer >= commitAfterQuiet;

        var receivers = DirectorHintReceiver.Active;

        for (int i = 0; i < receivers.Count; i++)
        {
            DirectorHintReceiver receiver = receivers[i];

            if (receiver == null)
                continue;

            receiver.SetRetreat(retreating);

            receiver.SetCommit(IsCommitting);
        }

        if (creature == null)
            return;

        if (retreating && TryRelocate(creature))
            return;

        if (Time.time < nextHintTime)
            return;

        nextHintTime = Time.time + hintInterval;

        IssueHint(creature);
    }


    // =========================================================
    // HINTS
    // =========================================================

    private void IssueHint(DirectorHintReceiver creature)
    {
        if (zones == null || !zones.IsBuilt)
            return;

        Vector3 playerPosition = player.transform.position;

        if (retreating)
        {
            if (zones.TryGetZoneFarFrom(
                    playerPosition,
                    pushAwayMinDistance,
                    out ZoneGraph.Zone away))
            {
                creature.SetHint(away.Center, away.Radius);

                Log("push-away hint", away.Center);
            }

            return;
        }

        /*
         * Only steer when the creature has genuinely lost the plot. If
         * it is already investigating or hunting, it is doing better
         * than any hint could, and interfering would flatten exactly
         * the emergent behaviour this design exists for.
         */

        if (unawareTimer < boredomTime)
            return;

        if (zones.TryGetZoneNear(
                playerPosition,
                pullInMinDistance,
                pullInMaxDistance,
                out ZoneGraph.Zone near))
        {
            creature.SetHint(near.Center, near.Radius);

            unawareTimer = 0f;

            Log("pull-in hint", near.Center);
        }
    }


    // =========================================================
    // RELOCATION
    // =========================================================

    /// <summary>
    /// Moves the creature bodily out of the player's world when menace
    /// is critical and simply walking away would take too long.
    /// </summary>
    private bool TryRelocate(DirectorHintReceiver creature)
    {
        if (!allowRelocation)
            return false;

        if (menace.Value < relocateAbove)
            return false;

        if (Time.time - lastRelocateTime < relocateCooldown)
            return false;

        if (creature.Locomotion == null)
            return false;

        Vector3 playerPosition = player.transform.position;

        Vector3 destination;

        if (VentPoint.TryGetFurthestExit(
                playerPosition,
                relocateMinDistance,
                out VentPoint vent))
        {
            destination = vent.Position;
        }
        else if (zones != null &&
                 zones.TryGetZoneFarFrom(
                     playerPosition,
                     relocateMinDistance,
                     out ZoneGraph.Zone zone))
        {
            destination = zone.Center;
        }
        else
        {
            return false;
        }

        /*
         * Never relocate onto something the player is looking at.
         * Vanishing is fine; blinking across the room in full view is
         * not.
         */

        if (IsVisibleToPlayer(creature.transform))
            return false;

        lastRelocateTime = Time.time;

        creature.Locomotion.Warp(destination);

        creature.Perception?.ResetPerception();

        creature.Brain?.ClearSearchMemory();

        creature.ClearHint();

        /*
         * Bleed the gauge so the Director does not immediately decide
         * it needs to act again.
         */

        menace.Relieve(0.5f);

        Log("RELOCATED creature", destination);

        return true;
    }


    // =========================================================
    // API
    // =========================================================

    /// <summary>
    /// Wipes the pacing state. For after a player death or a
    /// checkpoint reload.
    /// </summary>
    public void ResetPacing()
    {
        menace.Reset();

        retreating = false;

        unawareTimer = 0f;

        quietTimer = 0f;
    }


    private void Log(string what, Vector3 where)
    {
        if (!logDecisions)
            return;

        Debug.Log(
            "[AIDirector] " + what + " at " + where +
            "  (menace " + menace.Value.ToString("F2") + ")",
            this
        );
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || player == null)
            return;

        Vector3 origin = player.transform.position;

        /*
         * The band pull-in hints are drawn from. Nothing inside the
         * inner ring is ever suggested.
         */

        Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.8f);

        DrawRing(origin, pullInMinDistance);

        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.6f);

        DrawRing(origin, pullInMaxDistance);

        Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.5f);

        DrawRing(origin, pushAwayMinDistance);
    }


    private static void DrawRing(
        Vector3 center,
        float radius)
    {
        const int segments = 48;

        Vector3 previous =
            center + new Vector3(radius, 0.1f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;

            Vector3 point =
                center +
                new Vector3(
                    Mathf.Cos(angle) * radius,
                    0.1f,
                    Mathf.Sin(angle) * radius
                );

            Gizmos.DrawLine(previous, point);

            previous = point;
        }
    }
}

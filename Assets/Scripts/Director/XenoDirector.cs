using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The second brain: the Macro AI.
///
/// Modelled on the director in Alien: Isolation. It always knows where
/// the player is, and it uses that for one thing only - deciding where
/// the creature should wander while the creature has no trace of the
/// player. It never hands the creature the player's position.
///
/// It works in two modes:
///
///   PRESSURE   the creature is nudged to wander the player's general
///              area (never closer than minimumPullDistance), so the
///              player keeps hearing it nearby.
///
///   EASE OFF   the menace gauge has filled: the player has been under
///              pressure long enough. The creature is sent to wander a
///              far part of the museum for a while, so the player can
///              breathe and make progress. Then back to PRESSURE.
///
/// MENACE GAUGE - how much pressure the player is under right now. It
/// rises while the creature is close BY WALKING PATH (a creature behind
/// a wall in another room counts for less) and faster while the player
/// can see it. Attacking does not add to it, as in Isolation. It falls
/// while the creature is far away.
///
/// NEVER BREAKS OFF A LIVE HUNT. The Director's area is only used by
/// the tree's lowest roaming branch. While the creature has a trace of
/// the player it follows its own senses, and a full gauge waits until
/// that hunt is over before the creature is sent away.
/// </summary>
public class XenoDirector : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("References")]
    [SerializeField]
    private Transform player;

    /// <summary>Used to tell whether the creature is on the player's screen.</summary>
    [SerializeField]
    private Camera playerCamera;


    [Header("Menace gauge")]
    /// <summary>Walking-path distance at which the creature stops adding menace.</summary>
    [SerializeField, Min(5f)]
    private float menaceRange = 20f;

    /// <summary>
    /// Menace gained per second at point blank, before the visible
    /// weight. With visibleWeight 2 the worst case is 0.05/s, so being
    /// close and in view fills the gauge in about 20 seconds; the
    /// creature lurking 10 m away unseen takes about 80.
    /// </summary>
    [SerializeField, Min(0.001f)]
    private float buildRate = 0.025f;

    /// <summary>Multiplier while the player can actually see the creature.</summary>
    [SerializeField, Min(1f)]
    private float visibleWeight = 2f;

    /// <summary>Menace lost per second while the creature is out of range.</summary>
    [SerializeField, Min(0.001f)]
    private float decayRate = 0.02f;

    /// <summary>How often the walking-path distance is recalculated.</summary>
    [SerializeField, Min(0.1f)]
    private float pathCheckSeconds = 0.5f;


    [Header("Ease off")]
    /// <summary>How long the creature is kept away once the gauge has filled.</summary>
    [SerializeField, Min(5f)]
    private float easeOffSeconds = 40f;

    /// <summary>Menace lost per second during an ease off.</summary>
    [SerializeField, Min(0.001f)]
    private float easeOffDrainRate = 0.05f;

    /// <summary>During an ease off the area goes at least this far from the player.</summary>
    [SerializeField, Min(5f)]
    private float easeOffDistance = 22f;


    [Header("Pick area")]
    /// <summary>Off, the creature roams wherever it has not looked.</summary>
    [SerializeField]
    private bool steerRoaming = true;

    /// <summary>
    /// THE TWO-BRAIN GUARD. The area's centre is never closer to the
    /// player than this, so the area points at a part of the map, not
    /// at the player.
    /// </summary>
    [SerializeField, Min(4f)]
    private float minimumPullDistance = 9f;

    /// <summary>In PRESSURE the area sits between minimumPullDistance and this.</summary>
    [SerializeField, Min(5f)]
    private float pressureMaxDistance = 18f;

    [SerializeField, Min(2f)]
    private float areaRadius = 8f;

    /// <summary>The area is re-picked this often...</summary>
    [SerializeField, Min(2f)]
    private float areaRefreshSeconds = 15f;

    /// <summary>...or when the player has moved this far from where it was picked.</summary>
    [SerializeField, Min(2f)]
    private float areaRefreshPlayerMoved = 12f;


    [Header("Caught (respawn mode only)")]
    /// <summary>
    /// Only used when PlayerDeathSequence respawns the player instead
    /// of restarting the level: an ease off of this length, and the
    /// creature moved far away and out of sight.
    /// </summary>
    [SerializeField, Min(0f)]
    private float easeOffAfterCaught = 35f;

    [SerializeField, Min(5f)]
    private float relocateMinimumDistance = 20f;


    [Header("Visibility test")]
    [SerializeField]
    private LayerMask occlusionMask = ~0;


    [Header("Debug")]
    [SerializeField]
    private bool logDecisions = true;


    // =========================================================
    // STATE
    // =========================================================

    public enum DirectorMode
    {
        /// <summary>Keep the creature wandering the player's general area.</summary>
        Pressure = 0,

        /// <summary>Send the creature far away for a while.</summary>
        EaseOff = 1
    }


    private XenoBlackboard blackboard;

    private PerceptionSystem creaturePerception;

    private Transform creature;

    private float menace;

    private DirectorMode mode = DirectorMode.Pressure;

    private float easeOffUntil;

    private bool easeOffPending;

    private float pathDistance = float.MaxValue;

    private float nextPathCheck;

    private NavMeshPath path;

    private float nextAreaAt;

    private Vector3 areaAnchor;

    private bool hasArea;


    /// <summary>0..1: how much pressure the player is under.</summary>
    public float Menace => menace;

    public DirectorMode Mode => mode;

    public float EaseOffRemaining => mode == DirectorMode.EaseOff ? Mathf.Max(0f, easeOffUntil - Time.time) : 0f;

    /// <summary>The gauge is full but the creature is mid-hunt; the ease off waits for it to end.</summary>
    public bool EaseOffPending => easeOffPending;

    /// <summary>True while the creature has its own trace of the player.</summary>
    public bool CreatureHunting =>
        creaturePerception != null && creaturePerception.HasLastKnownPosition;

    /// <summary>Walking-path distance from the creature to the player, as last measured.</summary>
    public float PathDistance => pathDistance;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        if (enabled)
        {
            DisableLegacyDirector();
        }

        if (player == null)
        {
            PlayerController found = FindFirstObjectByType<PlayerController>();

            if (found != null)
            {
                player = found.transform;
            }
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        path = new NavMeshPath();
    }


    /// <summary>The old Director speaks a superseded vocabulary; only one may pace the creature.</summary>
    private void DisableLegacyDirector()
    {
        AIDirector legacy = GetComponent<AIDirector>();

        if (legacy == null || !legacy.enabled)
            return;

        legacy.enabled = false;

        Debug.LogWarning("[XenoDirector] Disabled AIDirector - it speaks the superseded hint vocabulary.", this);
    }


    private void Update()
    {
        if (!Acquire())
            return;

        UpdatePathDistance();

        /* Mode first: a gauge that filled last frame is acted on before it can drift. */
        UpdateMode();

        UpdateMenace();

        UpdateArea();
    }


    private bool Acquire()
    {
        if (blackboard == null)
        {
            blackboard = FindFirstObjectByType<XenoBlackboard>();

            if (blackboard == null)
                return false;

            creature = blackboard.transform;
        }

        if (creaturePerception == null && creature != null)
        {
            creaturePerception = creature.GetComponent<PerceptionSystem>();
        }

        return player != null && creature != null;
    }


    // =========================================================
    // MENACE GAUGE
    // =========================================================

    /// <summary>
    /// Walking distance, not straight-line distance: a creature on the
    /// other side of a wall is far less of a threat than one in the
    /// same room. A path the NavMesh cannot complete counts as far.
    /// </summary>
    private void UpdatePathDistance()
    {
        if (Time.time < nextPathCheck)
            return;

        nextPathCheck = Time.time + pathCheckSeconds;

        float flat = FlatDistance(creature.position, player.position);

        if (flat > menaceRange * 2f)
        {
            pathDistance = flat;
            return;
        }

        if (NavMesh.CalculatePath(creature.position, player.position, NavMesh.AllAreas, path) &&
            path.status == NavMeshPathStatus.PathComplete)
        {
            float length = 0f;

            for (int i = 1; i < path.corners.Length; i++)
            {
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            }

            pathDistance = length;
        }
        else
        {
            pathDistance = flat * 1.5f;
        }
    }


    /// <summary>
    /// Continuous, not event driven: two minutes of being circled at
    /// ten metres should weigh more than one close call.
    /// </summary>
    private void UpdateMenace()
    {
        if (mode == DirectorMode.EaseOff)
        {
            menace = Mathf.Clamp01(menace - easeOffDrainRate * Time.deltaTime);
            return;
        }

        /* A break is already owed; hold the gauge full until it is given. */
        if (easeOffPending)
            return;

        float closeness = pathDistance >= menaceRange ? 0f : 1f - pathDistance / menaceRange;

        if (closeness <= 0f)
        {
            menace = Mathf.Clamp01(menace - decayRate * Time.deltaTime);
            return;
        }

        float weight = closeness * (IsVisibleToPlayer() ? visibleWeight : 1f);

        menace = Mathf.Clamp01(menace + buildRate * weight * Time.deltaTime);
    }


    /// <summary>Rough on-screen test: in the camera's view and not behind a wall.</summary>
    private bool IsVisibleToPlayer()
    {
        if (playerCamera == null)
            return false;

        Vector3 eye = playerCamera.transform.position;

        Vector3 target = creature.position + Vector3.up;

        Vector3 delta = target - eye;

        float distance = delta.magnitude;

        if (distance < 0.01f)
            return false;

        if (Vector3.Angle(playerCamera.transform.forward, delta) > playerCamera.fieldOfView * 0.6f)
            return false;

        return !Physics.Raycast(eye, delta / distance, distance - 0.3f, occlusionMask, QueryTriggerInteraction.Ignore);
    }


    // =========================================================
    // MODE
    // =========================================================

    private void UpdateMode()
    {
        if (mode == DirectorMode.Pressure)
        {
            /*
             * Once the player has been under full pressure the break is
             * owed: it is given the moment the hunt ends, even if the
             * gauge has drifted down meanwhile.
             */
            if (easeOffPending)
            {
                if (!CreatureHunting) EnterEaseOff(easeOffSeconds, "menace was full, hunt over");

                return;
            }

            if (menace < 1f)
                return;

            if (CreatureHunting)
            {
                /* Never break off a live hunt: wait for it to end. */
                if (!easeOffPending) Log("menace full - waiting for the hunt to end before easing off");

                easeOffPending = true;
                return;
            }

            EnterEaseOff(easeOffSeconds, "menace full");
            return;
        }

        if (Time.time >= easeOffUntil)
        {
            mode = DirectorMode.Pressure;
            hasArea = false;

            Log("break over - back to PRESSURE");
        }
    }


    private void EnterEaseOff(float seconds, string why)
    {
        mode = DirectorMode.EaseOff;
        easeOffUntil = Time.time + seconds;
        easeOffPending = false;
        hasArea = false;

        Log("EASE OFF for " + seconds.ToString("F0") + "s (" + why + ")");
    }


    // =========================================================
    // PICK AREA
    // =========================================================

    private void UpdateArea()
    {
        if (!steerRoaming)
        {
            if (blackboard.HasDirectorArea) blackboard.ClearDirectorArea();

            hasArea = false;
            return;
        }

        bool playerMoved = hasArea && FlatDistance(player.position, areaAnchor) > areaRefreshPlayerMoved;

        if (hasArea && Time.time < nextAreaAt && !playerMoved)
            return;

        nextAreaAt = Time.time + areaRefreshSeconds;

        if (!TryPickArea(out Vector3 center))
            return;

        areaAnchor = player.position;
        hasArea = true;

        blackboard.SetDirectorArea(
            center,
            areaRadius,
            mode == DirectorMode.EaseOff ? DirectorIntent.EaseOff : DirectorIntent.Pressure);
    }


    /// <summary>
    /// PRESSURE: a point on the NavMesh between minimumPullDistance and
    /// pressureMaxDistance from the player. EASE OFF: the farthest of a
    /// handful of samples, at least easeOffDistance away.
    /// </summary>
    private bool TryPickArea(out Vector3 center)
    {
        center = player.position;

        bool away = mode == DirectorMode.EaseOff;

        float best = float.NegativeInfinity;

        bool found = false;

        for (int i = 0; i < 16; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);

            float dist = away
                ? Random.Range(easeOffDistance, easeOffDistance + 20f)
                : Random.Range(minimumPullDistance, pressureMaxDistance);

            Vector3 candidate = player.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                continue;

            float d = FlatDistance(hit.position, player.position);

            /* The guard: never centre the area on the player. */
            if (d < minimumPullDistance)
                continue;

            float score = away
                ? d
                : -Mathf.Max(0f, d - pressureMaxDistance) + Random.value;

            if (score > best)
            {
                best = score;
                center = hit.position;
                found = true;
            }
        }

        return found;
    }


    // =========================================================
    // CAUGHT (respawn mode only)
    // =========================================================

    /// <summary>
    /// Called by PlayerDeathSequence when it respawns the player in
    /// place instead of restarting the level.
    /// </summary>
    public void NotifyPlayerCaught()
    {
        if (!Acquire())
            return;

        menace = 0f;

        EnterEaseOff(easeOffAfterCaught, "player caught");

        Relocate();
    }


    /// <summary>Somewhere far from the player that the player cannot see. Farthest wins.</summary>
    private void Relocate()
    {
        XenoLocomotion loco = creature.GetComponent<XenoLocomotion>();

        if (loco == null)
            return;

        Vector3 eye = playerCamera != null ? playerCamera.transform.position : player.position + Vector3.up * 1.6f;

        Vector3 best = creature.position;
        float bestD = -1f;

        Vector3 fallback = creature.position;
        float fallbackD = -1f;

        for (int i = 0; i < 80; i++)
        {
            Vector2 flat = Random.insideUnitCircle * 45f;

            Vector3 candidate = player.position + new Vector3(flat.x, 0f, flat.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                continue;

            float d = FlatDistance(hit.position, player.position);

            if (d > fallbackD)
            {
                fallbackD = d;
                fallback = hit.position;
            }

            if (d < relocateMinimumDistance || d <= bestD)
                continue;

            if (!Physics.Linecast(eye, hit.position + Vector3.up * 1.2f, occlusionMask, QueryTriggerInteraction.Ignore))
                continue;

            best = hit.position;
            bestD = d;
        }

        if (bestD < 0f)
        {
            if (fallbackD < 0f)
                return;

            best = fallback;
            bestD = fallbackD;
        }

        loco.Stop();
        loco.Warp(best);

        Log("relocated the creature " + bestD.ToString("F0") + " m from the respawn, out of sight");
    }


    // =========================================================
    // API
    // =========================================================

    /// <summary>Back to a calm start: empty gauge, PRESSURE.</summary>
    public void ResetPacing()
    {
        menace = 0f;
        mode = DirectorMode.Pressure;
        easeOffPending = false;
        hasArea = false;
    }


    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }


    private void Log(string what)
    {
        if (!logDecisions)
            return;

        Debug.Log("[XenoDirector] " + what + " - menace " + menace.ToString("F2"), this);
    }


    /// <summary>For the debug overlay.</summary>
    public string Describe()
    {
        return "menace " + menace.ToString("F2") + " / " + mode + (easeOffPending ? " (pending)" : "");
    }
}

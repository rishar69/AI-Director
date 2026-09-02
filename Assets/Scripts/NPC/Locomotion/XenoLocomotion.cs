using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// How the creature is currently moving.
///
/// Gaits are intent, not speed. The brain in Phase 3 picks a gait to
/// express what it is doing - prowling while stalking, running while
/// chasing - and locomotion turns that into agent settings.
/// </summary>
public enum Gait
{
    Idle = 0,
    Prowl = 1,
    Walk = 2,
    Run = 3
}


/// <summary>
/// Everything the xenomorph needs to get from A to B.
///
/// Wraps NavMeshAgent so no other system has to know about pathfinding,
/// and so every destination is validated against the NavMesh before it
/// is used. That validation matters more than it looks: a last known
/// position derived from a SOUND can easily land inside a wall or out
/// in the void, and NavMeshAgent handles that badly.
///
/// Agent-driven, not root motion. Root motion fights the agent and the
/// only clip available is authored for a fixed speed anyway.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class XenoLocomotion : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Gait Speeds (m/s)")]
    [SerializeField, Min(0f)]
    private float prowlSpeed = 1.2f;

    [SerializeField, Min(0f)]
    private float walkSpeed = 2.4f;

    /// <summary>
    /// Slightly faster than the player's 5.0 sprint, so a straight line
    /// chase is always lost eventually and the player has to route
    /// rather than outrun.
    /// </summary>
    [SerializeField, Min(0f)]
    private float runSpeed = 5.2f;


    [Header("Turning (deg/s)")]
    [SerializeField, Min(0f)]
    private float prowlTurnSpeed = 200f;

    [SerializeField, Min(0f)]
    private float walkTurnSpeed = 320f;

    [SerializeField, Min(0f)]
    private float runTurnSpeed = 480f;


    [Header("Agent")]
    [SerializeField, Min(0f)]
    private float acceleration = 14f;

    [SerializeField, Min(0.05f)]
    private float stoppingDistance = 0.6f;

    /// <summary>
    /// How far from a requested point the NavMesh may be sampled before
    /// the request is rejected as unreachable.
    /// </summary>
    [SerializeField, Min(0.5f)]
    private float destinationSnapRadius = 4f;


    [Header("Rotation")]
    /*
     * Rotation is driven here rather than by NavMeshAgent.
     *
     * The agent turns toward its next steering corner, which is not
     * always the direction it is actually travelling - and it goes on
     * doing so while anything else also writes to the transform. Owning
     * rotation in one place removes a whole class of "facing the wrong
     * way" bugs.
     */

    [SerializeField]
    private bool manageRotation = true;

    [SerializeField, Min(0f)]
    private float facingTurnSpeed = 260f;


    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    private NavMeshAgent agent;

    private Gait gait = Gait.Idle;

    private bool hasDestination;

    private Vector3 destination;

    private bool hasFacingTarget;

    private Vector3 facingTarget;


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    public Gait CurrentGait => gait;

    /// <summary>Actual ground speed in metres per second.</summary>
    public float CurrentSpeed =>
        agent != null
            ? new Vector3(
                agent.velocity.x,
                0f,
                agent.velocity.z).magnitude
            : 0f;

    public bool IsMoving => CurrentSpeed > 0.15f;

    /// <summary>Speed the current gait is aiming for.</summary>
    public float TargetSpeed => SpeedFor(gait);

    public bool HasDestination => hasDestination;

    public Vector3 Destination => destination;

    public bool IsOnNavMesh =>
        agent != null && agent.isOnNavMesh;

    public float RemainingDistance =>
        agent != null && agent.isOnNavMesh && !agent.pathPending
            ? agent.remainingDistance
            : float.PositiveInfinity;

    /// <summary>
    /// True once the agent has reached its destination and settled.
    ///
    /// Checks pathPending as well, because remainingDistance reads as
    /// zero for the frame or two before a freshly requested path has
    /// been computed - without that check every new order looks like it
    /// has already been completed.
    /// </summary>
    public bool HasArrived
    {
        get
        {
            if (!hasDestination || agent == null || !agent.isOnNavMesh)
                return false;

            if (agent.pathPending)
                return false;

            if (agent.remainingDistance > agent.stoppingDistance)
                return false;

            return !agent.hasPath ||
                   agent.velocity.sqrMagnitude < 0.04f;
        }
    }

    /// <summary>
    /// True when the last path request could not reach the target.
    /// Phase 3 uses this to give up on an unreachable search point
    /// instead of standing still forever.
    /// </summary>
    public bool PathFailed =>
        agent != null &&
        agent.isOnNavMesh &&
        !agent.pathPending &&
        agent.pathStatus != NavMeshPathStatus.PathComplete;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        agent.updatePosition = true;

        agent.updateRotation = !manageRotation;

        agent.acceleration = acceleration;

        agent.stoppingDistance = stoppingDistance;

        ApplyGait(Gait.Idle);
    }


    private void Update()
    {
        if (agent == null || !agent.isOnNavMesh)
            return;

        ApplyRotation();
    }


    // =========================================================
    // ORDERS
    // =========================================================

    /// <summary>
    /// Sends the creature to a point, snapping it onto the NavMesh
    /// first.
    ///
    /// Returns false when nothing walkable was found nearby, which the
    /// caller should treat as "that place cannot be searched" rather
    /// than retrying.
    /// </summary>
    public bool MoveTo(
        Vector3 target,
        Gait desiredGait = Gait.Walk)
    {
        if (agent == null || !agent.isOnNavMesh)
            return false;

        if (!TrySnapToNavMesh(target, out Vector3 snapped))
            return false;

        SetGait(desiredGait);

        hasDestination = true;

        destination = snapped;

        hasFacingTarget = false;

        agent.isStopped = false;

        return agent.SetDestination(snapped);
    }


    /// <summary>
    /// Halts movement but keeps the creature where it is.
    /// </summary>
    public void Stop()
    {
        hasDestination = false;

        if (agent == null || !agent.isOnNavMesh)
            return;

        agent.isStopped = true;

        agent.ResetPath();

        SetGait(Gait.Idle);
    }


    public void SetGait(Gait next)
    {
        if (gait == next)
            return;

        ApplyGait(next);
    }


    private void ApplyGait(Gait next)
    {
        gait = next;

        if (agent == null)
            return;

        agent.speed = SpeedFor(next);

        agent.angularSpeed = TurnSpeedFor(next);
    }


    private float SpeedFor(Gait value)
    {
        switch (value)
        {
            case Gait.Prowl:
                return prowlSpeed;

            case Gait.Walk:
                return walkSpeed;

            case Gait.Run:
                return runSpeed;

            default:
                return 0f;
        }
    }


    private float TurnSpeedFor(Gait value)
    {
        switch (value)
        {
            case Gait.Prowl:
                return prowlTurnSpeed;

            case Gait.Walk:
                return walkTurnSpeed;

            case Gait.Run:
                return runTurnSpeed;

            default:
                return walkTurnSpeed;
        }
    }


    // =========================================================
    // FACING
    // =========================================================

    /// <summary>
    /// Turns to face a point while stationary.
    ///
    /// The agent only steers while it has somewhere to go, so scanning
    /// a room from a standstill needs its own rotation path.
    /// </summary>
    public void FaceTowards(Vector3 point)
    {
        hasFacingTarget = true;

        facingTarget = point;
    }


    public void ClearFacing()
    {
        hasFacingTarget = false;
    }


    /// <summary>
    /// The single place this creature's rotation is decided.
    ///
    /// Travel direction always wins over a facing target. Anything else
    /// produces a creature that walks one way while looking another,
    /// which is what happens when the agent's own rotation and a manual
    /// facing request are both live at once.
    /// </summary>
    private void ApplyRotation()
    {
        if (!manageRotation)
            return;

        if (TryGetTravelDirection(out Vector3 travel))
        {
            RotateTowards(travel, TurnSpeedFor(gait));

            return;
        }

        if (!hasFacingTarget)
            return;

        Vector3 delta = facingTarget - transform.position;

        delta.y = 0f;

        if (delta.sqrMagnitude < 0.01f)
            return;

        RotateTowards(delta.normalized, facingTurnSpeed);
    }


    /// <summary>
    /// Which way the creature is actually going, if it is going
    /// anywhere.
    /// </summary>
    private bool TryGetTravelDirection(out Vector3 direction)
    {
        direction = Vector3.zero;

        if (agent == null || !agent.isOnNavMesh)
            return false;

        Vector3 velocity = agent.velocity;

        velocity.y = 0f;

        if (velocity.sqrMagnitude > 0.02f)
        {
            direction = velocity.normalized;

            return true;
        }

        /*
         * Accelerating from a standstill the real velocity is still
         * near zero, but the agent already knows which way it intends
         * to set off. Using it here stops the creature spending the
         * first moments of every move pointing the wrong way.
         */

        if (!hasDestination)
            return false;

        Vector3 desired = agent.desiredVelocity;

        desired.y = 0f;

        if (desired.sqrMagnitude > 0.02f)
        {
            direction = desired.normalized;

            return true;
        }

        return false;
    }


    private void RotateTowards(
        Vector3 direction,
        float degreesPerSecond)
    {
        Quaternion target =
            Quaternion.LookRotation(direction, Vector3.up);

        transform.rotation =
            Quaternion.RotateTowards(
                transform.rotation,
                target,
                degreesPerSecond * Time.deltaTime
            );
    }


    // =========================================================
    // NAVMESH HELPERS
    // =========================================================

    /// <summary>
    /// Finds the nearest walkable point to a raw world position.
    /// </summary>
    public bool TrySnapToNavMesh(
        Vector3 target,
        out Vector3 snapped)
    {
        if (NavMesh.SamplePosition(
                target,
                out NavMeshHit hit,
                destinationSnapRadius,
                NavMesh.AllAreas))
        {
            snapped = hit.position;

            return true;
        }

        snapped = target;

        return false;
    }


    /// <summary>
    /// Picks a random reachable point within a radius. Used for
    /// wandering and, later, for search sweeps around a hint zone.
    /// </summary>
    public bool TryGetRandomPoint(
        Vector3 origin,
        float radius,
        out Vector3 point)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 candidate =
                origin +
                Random.insideUnitSphere * radius;

            candidate.y = origin.y;

            if (NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit hit,
                    radius * 0.5f,
                    NavMesh.AllAreas))
            {
                point = hit.position;

                return true;
            }
        }

        point = origin;

        return false;
    }


    /// <summary>
    /// Teleports the creature. For Director-driven vent entrances and
    /// exits in Phase 4 - never for ordinary movement.
    /// </summary>
    public void Warp(Vector3 position)
    {
        if (agent == null)
            return;

        hasDestination = false;

        hasFacingTarget = false;

        agent.Warp(position);
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying)
            return;

        /*
         * Blue is where it is pointing, green is where it is going.
         * When those two disagree, this shows it immediately.
         */

        Vector3 eye = transform.position + Vector3.up * 1.2f;

        Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.95f);

        Gizmos.DrawRay(eye, transform.forward * 2f);

        Vector3 flatVelocity = agent != null
            ? new Vector3(agent.velocity.x, 0f, agent.velocity.z)
            : Vector3.zero;

        if (flatVelocity.sqrMagnitude > 0.02f)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.95f);

            Gizmos.DrawRay(eye, flatVelocity.normalized * 2f);
        }

        if (!hasDestination)
            return;

        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);

        Gizmos.DrawWireCube(
            destination + Vector3.up * 0.1f,
            new Vector3(0.4f, 0.2f, 0.4f)
        );

        if (agent != null && agent.hasPath)
        {
            Vector3[] corners = agent.path.corners;

            for (int i = 1; i < corners.Length; i++)
            {
                Gizmos.DrawLine(corners[i - 1], corners[i]);
            }
        }
    }
}

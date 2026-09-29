using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The xenomorph's mind.
///
/// Owns the state machine, all behaviour tuning, and the small amount
/// of memory that has to outlive a single state - chiefly which places
/// have already been searched.
///
/// THE RULE THIS CLASS EXISTS TO ENFORCE:
///
/// There is no player reference here, and there must never be one. The
/// brain's entire picture of the world comes from PerceptionSystem,
/// which reports positions and confidences and nothing else. The
/// Director, added in Phase 4, may know where the player is, but it can
/// only reach the alien through IXenoDirectorHints - a rough area and
/// two coarse moods. If a future change needs the player's transform
/// in here, that change is wrong.
/// </summary>
[RequireComponent(typeof(XenoLocomotion))]
[RequireComponent(typeof(PerceptionSystem))]
public class XenoBrain : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Contact")]
    /// <summary>
    /// How recently a sense must have fired for contact to count as
    /// live. A small window stops vision blinking out behind a railing
    /// for one frame from reading as a lost target.
    /// </summary>
    [SerializeField, Min(0f)]
    private float contactGrace = 0.6f;


    [Header("Patrol")]
    [SerializeField, Min(1f)]
    private float patrolRadius = 25f;

    [SerializeField, Min(0f)]
    private float patrolPauseMin = 1.5f;

    [SerializeField, Min(0f)]
    private float patrolPauseMax = 4.5f;


    [Header("Investigate")]
    [SerializeField]
    private AwarenessLevel investigateFrom = AwarenessLevel.Suspicious;


    [Header("Search")]
    [SerializeField, Min(1)]
    private int searchPoints = 4;

    [SerializeField, Min(1f)]
    private float searchRadius = 9f;

    [SerializeField, Min(0f)]
    private float searchLookDuration = 2.5f;

    /// <summary>
    /// Whether a sweep prioritises known hiding places.
    ///
    /// This is what stops a locker being a pause button. It is also the
    /// most natural thing in the project to stage as a Phase 6
    /// behaviour unlock - the creature that has not yet learned to
    /// check lockers is meaningfully less frightening than the one
    /// that has.
    /// </summary>
    [SerializeField]
    private bool checkHidingSpots = true;

    /*
     * PerceptionSystem rewrites the last known position every frame a
     * sense has contact, so forgetting it is not enough on its own.
     * Without a record of where it has already looked, the creature
     * re-investigates the spot it is standing on indefinitely.
     */

    [SerializeField, Min(0f)]
    private float searchedMemoryRadius = 4f;

    [SerializeField, Min(0f)]
    private float searchedMemoryDuration = 20f;


    [Header("Stalk")]
    [SerializeField]
    private bool enableStalking = true;

    /// <summary>
    /// Awareness at which it starts circling instead of charging.
    ///
    /// Stalking is the UNCERTAIN state: it knows something is here but
    /// has not got a clean fix on it. Reached from a noise, a glimpse
    /// through cover, or contact at the edge of its vision.
    /// </summary>
    [SerializeField]
    private AwarenessLevel stalkFrom = AwarenessLevel.Alerted;

    /// <summary>
    /// Awareness at which circling stops and it simply comes for you.
    ///
    /// Once it has the player dead to rights, orbiting at nine metres
    /// reads as broken rather than menacing - the fear in a stalk comes
    /// from the creature being unsure, not from it politely declining a
    /// clear opportunity.
    /// </summary>
    [SerializeField]
    private AwarenessLevel chaseFrom = AwarenessLevel.Confirmed;

    /// <summary>Radius of the ring it circles the contact on.</summary>
    [SerializeField, Min(1f)]
    private float stalkDistance = 9f;

    [SerializeField, Min(0.5f)]
    private float stalkRepositionInterval = 4f;

    /// <summary>
    /// How long it will toy before committing. This single number is
    /// most of the difference between frightening and tedious.
    /// </summary>
    [SerializeField, Min(0f)]
    private float stalkDuration = 14f;

    /// <summary>
    /// Contact drifting beyond this means the ring is pointless and it
    /// should just close.
    /// </summary>
    [SerializeField, Min(1f)]
    private float stalkBreakDistance = 22f;


    [Header("Chase")]
    [SerializeField, Min(0.5f)]
    private float loseContactTime = 5f;


    [Header("Attack")]
    [SerializeField, Min(0.2f)]
    private float attackRange = 2.2f;

    [SerializeField, Min(0.1f)]
    private float attackDuration = 1.2f;

    /// <summary>
    /// How far from the strike point the lunge stops.
    ///
    /// The creature charges, then plants at this distance rather than
    /// running through the player. Driving all the way in puts it
    /// inside the camera's near plane, where the death shot has nothing
    /// it can actually frame.
    ///
    /// Must stay below attackRange or there is no room to lunge at all.
    /// </summary>
    [SerializeField, Min(0.2f)]
    private float attackStandoff = 1.6f;

    [SerializeField, Min(0f)]
    private float attackCooldown = 3f;


    [Header("Retreat")]
    [SerializeField, Min(1f)]
    private float retreatDistance = 30f;

    [SerializeField, Min(0f)]
    private float retreatDuration = 12f;


    [Header("Safety")]
    /// <summary>
    /// Longest any single movement order may take before it is
    /// abandoned. Stops the creature freezing on an unreachable point.
    /// </summary>
    [SerializeField, Min(1f)]
    private float orderTimeout = 25f;


    [Header("Debug")]
    [SerializeField]
    private bool logTransitions = true;

    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    private readonly Dictionary<XenoStateId, XenoState> states =
        new Dictionary<XenoStateId, XenoState>();

    private XenoState current;

    private Vector3 patrolOrigin;

    private bool hasSearchedPosition;

    private Vector3 searchedPosition;

    private float searchedUntil;

    private float deterredUntil;

    private float lastAttackTime = -999f;


    // =========================================================
    // COMPONENTS
    // =========================================================

    public PerceptionSystem Perception { get; private set; }

    public XenoLocomotion Locomotion { get; private set; }

    public XenoAnimatorDriver Animation { get; private set; }

    /// <summary>
    /// Null until Phase 4 adds a Director. Every state must cope with
    /// that, so the alien still works standalone.
    /// </summary>
    public IXenoDirectorHints Hints { get; private set; }


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    public XenoStateId CurrentState =>
        current != null ? current.Id : XenoStateId.Patrol;

    public float TimeInCurrentState { get; private set; }

    /// <summary>Raised on entering a new state: previous, next.</summary>
    public event Action<XenoStateId, XenoStateId> OnStateChanged;

    /// <summary>
    /// Raised when it strikes, carrying the point struck and the reach.
    ///
    /// The brain does not decide whether this connects. It announces an
    /// attack at a position; anything that can be hurt checks for
    /// itself. That keeps the player out of the brain entirely.
    /// </summary>
    public event Action<Vector3, float> OnAttack;


    // =========================================================
    // TUNING ACCESS
    // =========================================================

    public float PatrolRadius => patrolRadius;
    public float PatrolPauseMin => patrolPauseMin;
    public float PatrolPauseMax => patrolPauseMax;
    public Vector3 PatrolOrigin => patrolOrigin;

    public AwarenessLevel InvestigateFrom => investigateFrom;

    public int SearchPoints => searchPoints;
    public float SearchRadius => searchRadius;
    public float SearchLookDuration => searchLookDuration;
    public bool CheckHidingSpots => checkHidingSpots;

    public bool EnableStalking => enableStalking;
    public AwarenessLevel StalkFrom => stalkFrom;
    public AwarenessLevel ChaseFrom => chaseFrom;
    public float StalkDistance => stalkDistance;
    public float StalkRepositionInterval => stalkRepositionInterval;
    public float StalkDuration => stalkDuration;
    public float StalkBreakDistance => stalkBreakDistance;

    public float LoseContactTime => loseContactTime;

    public float AttackRange => attackRange;
    public float AttackDuration => attackDuration;
    public float AttackStandoff => attackStandoff;

    public float RetreatDistance => retreatDistance;
    public float RetreatDuration => retreatDuration;

    public float OrderTimeout => orderTimeout;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        Perception = GetComponent<PerceptionSystem>();

        Locomotion = GetComponent<XenoLocomotion>();

        Animation = GetComponent<XenoAnimatorDriver>();

        Hints = GetComponent<IXenoDirectorHints>();

        patrolOrigin = transform.position;

        BuildStates();
    }


    private void Start()
    {
        ChangeState(XenoStateId.Patrol);
    }


    private void Update()
    {
        if (current == null)
            return;

        TimeInCurrentState += Time.deltaTime;

        /*
         * The one genuinely global rule. Being driven off overrides
         * whatever the creature thought it was doing.
         */

        if (ShouldRetreatNow() && current.Id != XenoStateId.Retreat)
        {
            ChangeState(XenoStateId.Retreat);

            return;
        }

        XenoStateId? next = current.Tick();

        if (next.HasValue)
        {
            ChangeState(next.Value);
        }
    }


    private void OnValidate()
    {
        patrolPauseMax =
            Mathf.Max(patrolPauseMax, patrolPauseMin);

        /*
         * A standoff at or beyond the attack range leaves no room to
         * lunge, and the creature simply plants where it already was.
         */

        attackStandoff =
            Mathf.Min(attackStandoff, attackRange - 0.1f);
    }


    // =========================================================
    // STATE MACHINE
    // =========================================================

    private void BuildStates()
    {
        Register(new PatrolState());
        Register(new InvestigateState());
        Register(new SearchState());
        Register(new StalkState());
        Register(new ChaseState());
        Register(new AttackState());
        Register(new RetreatState());
    }


    private void Register(XenoState state)
    {
        state.Bind(this);

        states[state.Id] = state;
    }


    /// <summary>
    /// Transitions, including to the state already running - re-entry
    /// is how a state restarts itself, such as a search beginning a
    /// fresh sweep somewhere else.
    /// </summary>
    public void ChangeState(XenoStateId next)
    {
        if (!states.TryGetValue(next, out XenoState target))
        {
            Debug.LogError(
                "[XenoBrain] No state registered for " + next,
                this
            );

            return;
        }

        XenoStateId previous = CurrentState;

        current?.Exit();

        current = target;

        TimeInCurrentState = 0f;

        current.Enter();

        if (logTransitions && previous != next)
        {
            Debug.Log(
                "[XenoBrain] " + name + "  " +
                previous + " -> " + next +
                "  (awareness " +
                Perception.Awareness.ToString("F2") + ")",
                this
            );
        }

        if (previous != next)
        {
            OnStateChanged?.Invoke(previous, next);
        }
    }


    // =========================================================
    // SHARED QUERIES
    // =========================================================

    /// <summary>
    /// True while some sense reported contact within the grace window.
    /// </summary>
    public bool HasLiveContact =>
        Perception != null &&
        Perception.TimeSinceContact <= contactGrace;


    /// <summary>
    /// Whether there is a contact worth walking to that has not just
    /// been checked.
    /// </summary>
    public bool ShouldInvestigate()
    {
        if (Perception == null)
            return false;

        if (!Perception.HasLastKnownPosition)
            return false;

        if (Perception.Level < investigateFrom)
            return false;

        /*
         * Search memory suppresses STALE leads, never live ones.
         *
         * Without these two exits the creature goes blind to a player
         * standing in front of it for the whole memory duration, purely
         * because it happened to sweep that spot a moment earlier -
         * which reads as it finding you and then losing interest.
         */

        if (HasLiveContact)
            return true;

        if (Perception.Level >= stalkFrom)
            return true;

        return !IsRecentlySearched(Perception.LastKnownPosition);
    }


    /// <summary>
    /// Whether engaging right now means stalking or committing.
    ///
    /// One place, so Investigate, Search and Stalk cannot drift apart
    /// on the question of when circling is still appropriate.
    /// </summary>
    public XenoStateId ChooseEngagement()
    {
        if (!enableStalking)
            return XenoStateId.Chase;

        if (Perception != null && Perception.Level >= chaseFrom)
            return XenoStateId.Chase;

        return XenoStateId.Stalk;
    }


    public float DistanceTo(Vector3 point)
    {
        Vector3 delta = point - transform.position;

        delta.y = 0f;

        return delta.magnitude;
    }


    // =========================================================
    // SEARCH MEMORY
    // =========================================================

    public void MarkSearched(Vector3 position)
    {
        hasSearchedPosition = true;

        searchedPosition = position;

        searchedUntil = Time.time + searchedMemoryDuration;
    }


    public bool IsRecentlySearched(Vector3 position)
    {
        if (!hasSearchedPosition)
            return false;

        if (Time.time > searchedUntil)
        {
            hasSearchedPosition = false;

            return false;
        }

        return Vector3.Distance(position, searchedPosition) <=
               searchedMemoryRadius;
    }


    public void ClearSearchMemory()
    {
        hasSearchedPosition = false;
    }


    // =========================================================
    // ATTACK
    // =========================================================

    public bool CanAttack =>
        Time.time - lastAttackTime >= attackCooldown;


    public void RaiseAttack(Vector3 position)
    {
        lastAttackTime = Time.time;

        OnAttack?.Invoke(position, attackRange);
    }


    // =========================================================
    // PROGRESSION HOOKS
    // =========================================================

    /*
     * Written only by XenoProgression. Everything staged as a
     * behaviour unlock lives behind one of these, so the set of things
     * the creature can "learn" is enumerable rather than scattered.
     */

    public void SetCheckHidingSpots(bool value)
    {
        checkHidingSpots = value;
    }


    public void SetStalkDuration(float seconds)
    {
        stalkDuration = Mathf.Max(0f, seconds);
    }


    public void SetLoseContactTime(float seconds)
    {
        loseContactTime = Mathf.Max(0.5f, seconds);
    }


    // =========================================================
    // RETREAT
    // =========================================================

    /// <summary>
    /// Drives the creature off. Phase 5's flamethrower calls this; the
    /// Director calls it when menace runs too high.
    /// </summary>
    public void Deter(float duration)
    {
        deterredUntil =
            Mathf.Max(
                deterredUntil,
                Time.time + duration
            );
    }


    public bool IsDeterred => Time.time < deterredUntil;


    private bool ShouldRetreatNow()
    {
        if (IsDeterred)
            return true;

        return Hints != null && Hints.ShouldRetreat;
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.25f);

        Gizmos.DrawWireSphere(
            Application.isPlaying ? patrolOrigin : transform.position,
            patrolRadius
        );

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);

        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (!Application.isPlaying)
            return;

        if (hasSearchedPosition && Time.time <= searchedUntil)
        {
            Gizmos.color = new Color(0.55f, 0.55f, 0.6f, 0.7f);

            Gizmos.DrawWireSphere(
                searchedPosition,
                searchedMemoryRadius
            );
        }

        if (current != null &&
            current.Id == XenoStateId.Stalk &&
            Perception.HasLastKnownPosition)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);

            Gizmos.DrawWireSphere(
                Perception.LastKnownPosition,
                stalkDistance
            );
        }
    }
}

using UnityEngine;

/// <summary>
/// A throwaway stand-in for the brain, so Phase 2 locomotion can be
/// tested before Phase 3 exists.
///
/// DELETE THIS IN PHASE 3. It is not a design for the xenomorph's
/// behaviour - there is no stalking, no patience and no Director here.
/// It exists only to prove that pathing, gaits, arrival detection and
/// the animation hookup all work when driven by real perception data.
///
/// It does obey the one rule that matters: it reads nothing but
/// PerceptionSystem. There is no player reference, and Phase 3 inherits
/// that constraint rather than having to be retrofitted with it.
/// </summary>
[RequireComponent(typeof(XenoLocomotion))]
public class LocomotionTestDriver : MonoBehaviour
{
    private enum Mode
    {
        Wandering,
        Pausing,
        Investigating,

        /// <summary>
        /// Arrived while contact is still live. Holds position and
        /// faces the contact instead of pretending to have lost it.
        /// </summary>
        Watching,

        /// <summary>
        /// Arrived after contact was lost. Looks around, then gives up
        /// on this spot and remembers having checked it.
        /// </summary>
        Scanning
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("References")]
    [SerializeField]
    private PerceptionSystem perception;

    [SerializeField]
    private XenoAnimatorDriver animatorDriver;


    [Header("Wander")]
    [SerializeField, Min(1f)]
    private float wanderRadius = 18f;

    [SerializeField, Min(0f)]
    private float minPause = 1.5f;

    [SerializeField, Min(0f)]
    private float maxPause = 4f;


    [Header("Investigate")]
    /// <summary>
    /// Awareness level at which the last known position becomes worth
    /// walking to.
    /// </summary>
    [SerializeField]
    private AwarenessLevel investigateFrom = AwarenessLevel.Suspicious;

    [SerializeField, Min(0f)]
    private float scanDuration = 3f;

    /// <summary>
    /// While watching, the contact drifting further than this makes the
    /// creature close the distance again.
    /// </summary>
    [SerializeField, Min(0.5f)]
    private float repositionDistance = 4f;


    [Header("Contact")]
    /// <summary>
    /// How recently a sense must have reported contact for it to count
    /// as still live.
    ///
    /// Without a small grace window, a single frame where vision blinks
    /// out behind a railing would be read as having lost the target.
    /// </summary>
    [SerializeField, Min(0f)]
    private float contactGrace = 0.6f;


    [Header("Search Memory")]
    /*
     * PerceptionSystem re-establishes the last known position every
     * frame a sense has contact, so ForgetLastKnownPosition alone is
     * undone immediately. Without a record of where it has already
     * looked, the creature re-investigates the spot it is standing on
     * forever.
     */

    [SerializeField, Min(0f)]
    private float searchedRadius = 3f;

    [SerializeField, Min(0f)]
    private float searchedCooldown = 8f;


    [Header("Safety")]
    /// <summary>
    /// Longest a single order may take before it is abandoned. Stops
    /// the rig freezing forever on an unreachable point.
    /// </summary>
    [SerializeField, Min(1f)]
    private float orderTimeout = 20f;


    [Header("Debug")]
    [SerializeField]
    private bool logTransitions = true;


    // =========================================================
    // STATE
    // =========================================================

    private XenoLocomotion locomotion;

    private Mode mode = Mode.Pausing;

    private Vector3 home;

    private float modeExpiry;

    private float orderExpiry;

    private Vector3 scanPosition;

    private bool hasSearchedPosition;

    private Vector3 searchedPosition;

    private float searchedUntil;


    public string DebugMode => mode.ToString();


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        locomotion = GetComponent<XenoLocomotion>();

        if (perception == null)
        {
            perception = GetComponent<PerceptionSystem>();
        }

        if (animatorDriver == null)
        {
            animatorDriver = GetComponent<XenoAnimatorDriver>();
        }

        home = transform.position;
    }


    private void Start()
    {
        EnterPause();
    }


    private void Update()
    {
        if (ShouldInvestigate() && CanInterruptForContact())
        {
            EnterInvestigate();
        }

        switch (mode)
        {
            case Mode.Wandering:
                TickWandering();
                break;

            case Mode.Pausing:
                TickPausing();
                break;

            case Mode.Investigating:
                TickInvestigating();
                break;

            case Mode.Watching:
                TickWatching();
                break;

            case Mode.Scanning:
                TickScanning();
                break;
        }
    }


    // =========================================================
    // CONDITIONS
    // =========================================================

    /// <summary>
    /// True while some sense reported contact within the grace window.
    /// </summary>
    private bool HasLiveContact =>
        perception != null &&
        perception.TimeSinceContact <= contactGrace;


    private bool ShouldInvestigate()
    {
        if (perception == null)
            return false;

        if (!perception.HasLastKnownPosition)
            return false;

        if (perception.Level < investigateFrom)
            return false;

        return !IsRecentlySearched(perception.LastKnownPosition);
    }


    /// <summary>
    /// Decides whether new contact may override what the creature is
    /// already doing.
    ///
    /// A scan may only be broken off for something meaningfully
    /// elsewhere. Letting any contact interrupt would restart the
    /// investigate cycle every frame the target stayed visible.
    /// </summary>
    private bool CanInterruptForContact()
    {
        switch (mode)
        {
            case Mode.Investigating:
            case Mode.Watching:
                return false;

            case Mode.Scanning:
                return Vector3.Distance(
                    perception.LastKnownPosition,
                    scanPosition) > searchedRadius;

            default:
                return true;
        }
    }


    private bool IsRecentlySearched(Vector3 position)
    {
        if (!hasSearchedPosition)
            return false;

        if (Time.time > searchedUntil)
        {
            hasSearchedPosition = false;

            return false;
        }

        return Vector3.Distance(position, searchedPosition) <=
               searchedRadius;
    }


    private void MarkSearched(Vector3 position)
    {
        hasSearchedPosition = true;

        searchedPosition = position;

        searchedUntil = Time.time + searchedCooldown;
    }


    private bool OrderTimedOut => Time.time > orderExpiry;


    // =========================================================
    // WANDER
    // =========================================================

    private void EnterWander()
    {
        locomotion.ClearFacing();

        if (!locomotion.TryGetRandomPoint(
                home,
                wanderRadius,
                out Vector3 point))
        {
            EnterPause();

            return;
        }

        if (!locomotion.MoveTo(point, Gait.Prowl))
        {
            EnterPause();

            return;
        }

        SetMode(Mode.Wandering);

        orderExpiry = Time.time + orderTimeout;
    }


    private void TickWandering()
    {
        if (locomotion.HasArrived ||
            locomotion.PathFailed ||
            OrderTimedOut)
        {
            EnterPause();
        }
    }


    // =========================================================
    // PAUSE
    // =========================================================

    private void EnterPause()
    {
        locomotion.Stop();

        locomotion.ClearFacing();

        SetMode(Mode.Pausing);

        modeExpiry =
            Time.time +
            Random.Range(minPause, maxPause);
    }


    private void TickPausing()
    {
        if (Time.time >= modeExpiry)
        {
            EnterWander();
        }
    }


    // =========================================================
    // INVESTIGATE
    // =========================================================

    private void EnterInvestigate()
    {
        Vector3 target = perception.LastKnownPosition;

        locomotion.ClearFacing();

        /*
         * Run when it is fairly sure, walk when merely curious. A
         * creature that sprints at every distant footstep reads as
         * twitchy rather than dangerous.
         */

        Gait gait =
            perception.Level >= AwarenessLevel.Alerted
                ? Gait.Run
                : Gait.Walk;

        if (!locomotion.MoveTo(target, gait))
        {
            /*
             * Nothing walkable near the contact. Mark it searched so
             * the same impossible point is not retried every frame.
             */

            MarkSearched(target);

            perception.ForgetLastKnownPosition();

            EnterPause();

            return;
        }

        SetMode(Mode.Investigating);

        orderExpiry = Time.time + orderTimeout;
    }


    private void TickInvestigating()
    {
        /*
         * Re-issue the order if perception has since placed the contact
         * somewhere meaningfully different.
         */

        if (perception.HasLastKnownPosition &&
            Vector3.Distance(
                locomotion.Destination,
                perception.LastKnownPosition) > 1.5f)
        {
            EnterInvestigate();

            return;
        }

        if (locomotion.HasArrived)
        {
            /*
             * The important distinction. Arriving while the target is
             * still being sensed is not the same as arriving at a spot
             * it has already left.
             */

            if (HasLiveContact)
            {
                EnterWatch();
            }
            else
            {
                EnterScan();
            }

            return;
        }

        if (locomotion.PathFailed || OrderTimedOut)
        {
            MarkSearched(locomotion.Destination);

            perception.ForgetLastKnownPosition();

            EnterPause();
        }
    }


    // =========================================================
    // WATCH
    // =========================================================

    private void EnterWatch()
    {
        locomotion.Stop();

        SetMode(Mode.Watching);
    }


    private void TickWatching()
    {
        if (perception.HasLastKnownPosition)
        {
            locomotion.FaceTowards(perception.LastKnownPosition);
        }

        if (!HasLiveContact)
        {
            /*
             * Contact lost while watching - now a scan is the honest
             * response.
             */

            EnterScan();

            return;
        }

        /*
         * Target drifting away, so close the distance again. Phase 3
         * replaces this with a real chase.
         */

        if (perception.HasLastKnownPosition &&
            Vector3.Distance(
                transform.position,
                perception.LastKnownPosition) > repositionDistance)
        {
            EnterInvestigate();
        }
    }


    // =========================================================
    // SCAN
    // =========================================================

    private void EnterScan()
    {
        locomotion.Stop();

        locomotion.ClearFacing();

        SetMode(Mode.Scanning);

        scanPosition =
            perception != null && perception.HasLastKnownPosition
                ? perception.LastKnownPosition
                : transform.position;

        modeExpiry = Time.time + scanDuration;

        animatorDriver?.RequestLookAround(scanDuration);
    }


    private void TickScanning()
    {
        if (HasLiveContact)
        {
            /*
             * Re-acquired mid-scan. Stop pretending to search for
             * something that is standing in front of it.
             */

            EnterWatch();

            return;
        }

        if (Time.time < modeExpiry)
            return;

        /*
         * Remember this spot as checked, so re-detection of a stale
         * position does not send it straight back here. Phase 3
         * replaces this with a proper multi-point search sweep.
         */

        MarkSearched(scanPosition);

        perception.ForgetLastKnownPosition();

        EnterPause();
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private void SetMode(Mode next)
    {
        if (mode == next)
            return;

        if (logTransitions)
        {
            Debug.Log(
                "[LocomotionTest] " + name + "  " +
                mode + " -> " + next,
                this
            );
        }

        mode = next;
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        if (hasSearchedPosition && Time.time <= searchedUntil)
        {
            Gizmos.color = new Color(0.5f, 0.5f, 0.55f, 0.6f);

            Gizmos.DrawWireSphere(searchedPosition, searchedRadius);
        }

        if (mode == Mode.Scanning)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);

            Gizmos.DrawWireSphere(scanPosition, 0.5f);
        }
    }
}

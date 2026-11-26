using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What the creature remembers about the hunt, built only from its own
/// senses and its own movement.
///
///   TRAIL      every place it sensed the target, with when and how.
///              Two sightings close together in time give a heading -
///              "it was running that way" - which is what lets the hunt
///              follow you instead of standing on the spot you vanished.
///
///   HIDING     which hiding spots it has already checked and when, so
///              a search moves on instead of re-checking the same desk.
///              Hiding spots are furniture: the creature knows where the
///              desks are the same way it knows where the walls are.
///
///   COVERAGE   a coarse grid over the NavMesh, stamped with when the
///              creature last had eyes on each cell. Roaming goes to the
///              cells it has not seen for longest, so it patrols the map
///              like it is checking rooms rather than drifting at random.
///
/// TWO-BRAIN RULE: nothing here touches the player. The trail is fed
/// from PerceptionSystem's last-known-position updates, the coverage
/// from the creature's own position and line of sight.
/// </summary>
[DisallowMultipleComponent]
public class XenoHuntMemory : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Trail")]
    [SerializeField, Min(4)]
    private int trailLength = 24;

    /// <summary>Sightings further apart than this are not used for a heading.</summary>
    [SerializeField, Min(0.2f)]
    private float headingWindow = 1.6f;

    [SerializeField, Min(0.1f)]
    private float minimumHeadingSpeed = 0.6f;

    [Header("Coverage")]
    [SerializeField, Min(1f)]
    private float cellSize = 4f;

    /// <summary>How far the creature counts as having looked at a cell.</summary>
    [SerializeField, Min(1f)]
    private float coverageSightRange = 7f;

    [SerializeField, Min(0.05f)]
    private float coverageInterval = 0.4f;

    [Header("Hiding Spots")]
    /// <summary>A spot checked within this long is skipped by the next search.</summary>
    [SerializeField, Min(1f)]
    private float spotRecheckSeconds = 60f;

    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // TYPES
    // =========================================================

    public struct Sighting
    {
        public Vector3 Position;
        public float Time;
        public SenseChannel Channel;
    }


    private class Cell
    {
        public Vector3 Center;
        public float LastSeen = -999f;
    }


    // =========================================================
    // STATE
    // =========================================================

    private PerceptionSystem perception;

    private VisionSense vision;

    private XenoSenses senses;

    private XenoBlackboard blackboard;

    private XenoLearning learning;

    private bool wasSeeing;

    private Vector3 lastSeenAt;

    private readonly List<Sighting> trail = new List<Sighting>();

    private readonly Dictionary<HidingSpot, float> checkedSpots = new Dictionary<HidingSpot, float>();

    private readonly Dictionary<HidingSpot, int> foundIn = new Dictionary<HidingSpot, int>();

    private readonly List<Cell> cells = new List<Cell>();

    private float nextCoverage;

    private float huntStartedAt = -1f;


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    public IReadOnlyList<Sighting> Trail => trail;

    public bool HasHeading { get; private set; }

    /// <summary>Unit direction the target was last seen moving in.</summary>
    public Vector3 Heading { get; private set; }

    public float HeadingSpeed { get; private set; }

    /// <summary>Time.time of the most recent sighting of any kind, or -999.</summary>
    public float LastSightingTime => trail.Count > 0 ? trail[trail.Count - 1].Time : -999f;

    public float SecondsSinceSighting => Time.time - LastSightingTime;

    /// <summary>Time.time it last actually SAW the target (vision only), or -999.</summary>
    public float LastVisualSightingTime { get; private set; } = -999f;

    public int CellCount => cells.Count;

    /// <summary>When the current hunt began, -1 when there is none.</summary>
    public float HuntStartedAt => huntStartedAt;

    /// <summary>The hunt's current best guess, for the debug view. Set by the tree.</summary>
    public Vector3 Estimate { get; set; }

    public bool HasEstimate { get; set; }

    /// <summary>What the hunt is doing right now, for the debug view. Set by the tree.</summary>
    public string HuntStep { get; set; } = "";


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        perception = GetComponent<PerceptionSystem>();

        vision = GetComponentInChildren<VisionSense>();

        senses = GetComponent<XenoSenses>();

        blackboard = GetComponent<XenoBlackboard>();

        learning = GetComponent<XenoLearning>();
    }


    private void OnEnable()
    {
        if (perception != null)
        {
            perception.OnLastKnownPositionUpdated += HandleContact;
        }
    }


    private void OnDisable()
    {
        if (perception != null)
        {
            perception.OnLastKnownPositionUpdated -= HandleContact;
        }
    }


    private void Start()
    {
        BuildCells();
    }


    private void Update()
    {
        if (Time.time >= nextCoverage)
        {
            nextCoverage = Time.time + coverageInterval;

            StampCoverage();
        }

        UpdateHidingBelief();
    }


    // =========================================================
    // HIDING BELIEF
    // =========================================================

    [Header("Hiding Belief")]
    /// <summary>Losing sight of the target this close to a hiding spot makes it suspect that spot.</summary>
    [SerializeField, Min(0.5f)]
    private float vanishNearSpotRadius = 2.6f;

    /// <summary>A sound this close to a hiding spot makes it suspect that spot.</summary>
    [SerializeField, Min(0.5f)]
    private float soundNearSpotRadius = 2.2f;


    /// <summary>
    /// The hiding spot the creature believes the target is in, or null.
    /// Formed only from its own senses, never from the player.
    /// </summary>
    public HidingSpot SuspectedSpot { get; private set; }


    private void UpdateHidingBelief()
    {
        if (senses == null) senses = GetComponent<XenoSenses>();

        bool seeing = senses != null && senses.IsSenseActivationAbove(SenseChannelId.Visual, SenseThreshold.Lower);

        /* The moment sight is lost: did it lose you beside a hiding spot? */
        if (wasSeeing && !seeing)
        {
            HidingSpot near = NearestSpot(lastSeenAt, vanishNearSpotRadius);

            if (near != null) Suspect(near, "vanished beside it");
        }

        wasSeeing = seeing;

        if (perception != null && !perception.HasLastKnownPosition && SuspectedSpot != null)
        {
            SuspectedSpot = null;
        }

        if (blackboard == null) blackboard = GetComponent<XenoBlackboard>();

        if (blackboard != null && blackboard.GetTargetFlag(TargetLogicFlag.PlayerHiding) != (SuspectedSpot != null))
        {
            blackboard.SetTargetFlag(TargetLogicFlag.PlayerHiding, SuspectedSpot != null);
        }
    }


    private void Suspect(HidingSpot spot, string why)
    {
        if (spot == SuspectedSpot)
            return;

        SuspectedSpot = spot;

        if (learning == null) learning = GetComponent<XenoLearning>();

        learning?.NoteSuspectedHide(spot);

        Debug.Log("[Hunt] " + name + " suspects " + spot.name + " (" + why + ")", this);
    }


    /// <summary>It looked into a spot and it was empty.</summary>
    public void ClearSuspicion(HidingSpot spot)
    {
        if (spot == SuspectedSpot) SuspectedSpot = null;
    }


    private static HidingSpot NearestSpot(Vector3 p, float radius)
    {
        HidingSpot best = null;

        float bestD = radius * radius;

        IReadOnlyList<HidingSpot> all = HidingSpot.Active;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] == null) continue;

            Vector3 d = all[i].transform.position - p;

            d.y = 0f;

            if (d.sqrMagnitude <= bestD)
            {
                bestD = d.sqrMagnitude;
                best = all[i];
            }
        }

        return best;
    }


    // =========================================================
    // TRAIL
    // =========================================================

    private void HandleContact(Vector3 position)
    {
        Sighting s = new Sighting
        {
            Position = position,
            Time = Time.time,
            Channel = perception.StrongestChannel
        };

        /*
         * Perception reports every frame of contact. Keep one entry per
         * 0.2 s: inside that, refresh the newest entry's position but NOT
         * its time - refreshing the time as well would keep pushing it
         * forward and the trail would never grow during a chase.
         */
        if (trail.Count > 0)
        {
            Sighting last = trail[trail.Count - 1];

            if (Time.time - last.Time < 0.2f)
            {
                NoteBelief(s);

                last.Position = position;

                if (s.Channel == SenseChannel.Vision) last.Channel = SenseChannel.Vision;

                trail[trail.Count - 1] = last;

                UpdateHeading();
                return;
            }
        }

        trail.Add(s);

        NoteBelief(s);

        if (trail.Count > trailLength)
        {
            trail.RemoveAt(0);
        }

        UpdateHeading();
    }


    /// <summary>
    /// A sound by a hiding spot raises suspicion of that spot; seeing the
    /// target out in the open clears it.
    /// </summary>
    private void NoteBelief(Sighting s)
    {
        if (s.Channel == SenseChannel.Vision)
        {
            lastSeenAt = s.Position;

            LastVisualSightingTime = Time.time;

            if (SuspectedSpot != null && NearestSpot(s.Position, vanishNearSpotRadius) == null)
            {
                SuspectedSpot = null;
            }

            return;
        }

        if (s.Channel == SenseChannel.Hearing)
        {
            HidingSpot near = NearestSpot(s.Position, soundNearSpotRadius);

            if (near != null) Suspect(near, "heard it");
        }
    }


    /// <summary>
    /// Heading from the earliest and latest sightings inside the window.
    /// Only sightings count - a sound's position is too coarse to give
    /// a direction, and a heading invented from noise sends the creature
    /// off confidently the wrong way.
    /// </summary>
    private void UpdateHeading()
    {
        Sighting newest = trail[trail.Count - 1];

        if (newest.Channel != SenseChannel.Vision)
            return;

        for (int i = trail.Count - 2; i >= 0; i--)
        {
            Sighting older = trail[i];

            float dt = newest.Time - older.Time;

            if (dt > headingWindow)
                break;

            if (older.Channel != SenseChannel.Vision || dt < 0.3f)
                continue;

            Vector3 delta = newest.Position - older.Position;

            delta.y = 0f;

            float speed = delta.magnitude / dt;

            if (speed < minimumHeadingSpeed)
            {
                /* Standing still: a heading from before that would be stale. */
                HasHeading = false;
                return;
            }

            Heading = delta / delta.magnitude;

            HeadingSpeed = speed;

            HasHeading = true;

            return;
        }
    }


    // =========================================================
    // HUNT LIFETIME
    // =========================================================

    public void BeginHunt()
    {
        if (huntStartedAt < 0f)
        {
            huntStartedAt = Time.time;
        }
    }


    public void EndHunt()
    {
        huntStartedAt = -1f;

        HasHeading = false;

        HasEstimate = false;

        HuntStep = "";
    }


    // =========================================================
    // HIDING SPOTS
    // =========================================================

    public void MarkChecked(HidingSpot spot)
    {
        if (spot != null) checkedSpots[spot] = Time.time;
    }


    public bool WasCheckedRecently(HidingSpot spot)
    {
        return checkedSpots.TryGetValue(spot, out float t) && Time.time - t < spotRecheckSeconds;
    }


    public float SecondsSinceChecked(HidingSpot spot)
    {
        return checkedSpots.TryGetValue(spot, out float t) ? Time.time - t : float.PositiveInfinity;
    }


    /// <summary>Remembered for Phase C: spots it has pulled the target out of.</summary>
    public void NoteFoundIn(HidingSpot spot)
    {
        if (spot == null) return;

        foundIn.TryGetValue(spot, out int n);

        foundIn[spot] = n + 1;

        /* It is looking straight at you in there: belief is now certainty. */
        SuspectedSpot = spot;

        if (learning == null) learning = GetComponent<XenoLearning>();

        learning?.NoteCaughtInHiding(spot);
    }


    public int TimesFoundIn(HidingSpot spot)
    {
        return spot != null && foundIn.TryGetValue(spot, out int n) ? n : 0;
    }


    // =========================================================
    // COVERAGE
    // =========================================================

    private void BuildCells()
    {
        cells.Clear();

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();

        if (tri.vertices == null || tri.vertices.Length == 0)
            return;

        Bounds b = new Bounds(tri.vertices[0], Vector3.zero);

        foreach (Vector3 v in tri.vertices) b.Encapsulate(v);

        for (float x = b.min.x + cellSize * 0.5f; x < b.max.x; x += cellSize)
        {
            for (float z = b.min.z + cellSize * 0.5f; z < b.max.z; z += cellSize)
            {
                /*
                 * Sample at every height band the mesh spans, so a
                 * second floor gets its own cells.
                 */
                for (float y = b.min.y; y <= b.max.y + 0.01f; y += 2.5f)
                {
                    if (NavMesh.SamplePosition(new Vector3(x, y, z), out NavMeshHit hit, cellSize * 0.45f, NavMesh.AllAreas) &&
                        Mathf.Abs(hit.position.y - y) < 1.5f)
                    {
                        bool duplicate = false;

                        foreach (Cell c in cells)
                        {
                            if ((c.Center - hit.position).sqrMagnitude < 1f)
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (!duplicate) cells.Add(new Cell { Center = hit.position });
                    }
                }
            }
        }
    }


    private void StampCoverage()
    {
        if (cells.Count == 0)
            return;

        Vector3 eye = vision != null ? vision.transform.position : transform.position + Vector3.up * 1.6f;

        float range2 = coverageSightRange * coverageSightRange;

        foreach (Cell c in cells)
        {
            Vector3 d = c.Center - transform.position;

            if (d.sqrMagnitude > range2 || Mathf.Abs(d.y) > 2f)
                continue;

            Vector3 target = c.Center + Vector3.up * 0.8f;

            if (Physics.Linecast(eye, target, GameLayers.SightBlockers, QueryTriggerInteraction.Ignore))
                continue;

            c.LastSeen = Time.time;
        }
    }


    /// <summary>
    /// The cell most worth checking: longest unseen, discounted by how
    /// far away it is, optionally pulled toward a focus point (the place
    /// a hunt went cold). Returns false when the grid is empty.
    /// </summary>
    public bool TryPickStaleCell(
        Vector3 from,
        Vector3? focus,
        float focusRadius,
        float minimumTravel,
        out Vector3 point)
    {
        point = from;

        float best = float.NegativeInfinity;

        bool found = false;

        foreach (Cell c in cells)
        {
            float dist = Vector3.Distance(from, c.Center);

            if (dist < minimumTravel)
                continue;

            float age = Mathf.Min(Time.time - c.LastSeen, 180f);

            float score = age - dist * 0.6f + Random.Range(0f, 6f);

            if (focus.HasValue)
            {
                float fd = Vector3.Distance(focus.Value, c.Center);

                if (fd > focusRadius)
                    continue;

                score -= fd * 1.5f;
            }

            if (score > best)
            {
                best = score;
                point = c.Center;
                found = true;
            }
        }

        return found;
    }


    public float SecondsSinceSeen(Vector3 position)
    {
        Cell nearest = null;

        float bestD = float.PositiveInfinity;

        foreach (Cell c in cells)
        {
            float d = (c.Center - position).sqrMagnitude;

            if (d < bestD)
            {
                bestD = d;
                nearest = c;
            }
        }

        return nearest == null ? float.PositiveInfinity : Time.time - nearest.LastSeen;
    }


    /// <summary>For the debug view: each cell and how stale it is (0 fresh .. 1 unseen for 60 s+).</summary>
    public void ForEachCell(System.Action<Vector3, float> visit)
    {
        foreach (Cell c in cells)
        {
            visit(c.Center, Mathf.Clamp01((Time.time - c.LastSeen) / 60f));
        }
    }


    public void ResetAll()
    {
        trail.Clear();
        checkedSpots.Clear();
        SuspectedSpot = null;
        EndHunt();

        foreach (Cell c in cells) c.LastSeen = -999f;
    }


    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying)
            return;

        foreach (Cell c in cells)
        {
            float stale = Mathf.Clamp01((Time.time - c.LastSeen) / 60f);

            Gizmos.color = Color.Lerp(new Color(0.3f, 1f, 0.4f, 0.4f), new Color(1f, 0.3f, 0.3f, 0.4f), stale);

            Gizmos.DrawWireCube(c.Center + Vector3.up * 0.05f, new Vector3(cellSize * 0.9f, 0.05f, cellSize * 0.9f));
        }
    }
}

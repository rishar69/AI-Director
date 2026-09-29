using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chops the walkable world into coarse zones.
///
/// The Director thinks in zones rather than points, and that is not a
/// convenience - it is the mechanism that stops hints from being
/// cheating. A hint that named a position would be the player's
/// location by another name. A hint that names a twelve metre area the
/// creature then has to search itself is a nudge.
///
/// Zones are derived from the baked NavMesh, so no authoring is needed
/// and they automatically match wherever the creature can actually go.
/// </summary>
public class ZoneGraph : MonoBehaviour
{
    public readonly struct Zone
    {
        public readonly Vector3 Center;
        public readonly float Radius;

        public Zone(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Build")]
    /// <summary>
    /// Width of one zone in metres. Wants to be large enough that
    /// searching one takes real time - around a room's worth.
    /// </summary>
    [SerializeField, Min(2f)]
    private float cellSize = 12f;

    [SerializeField]
    private bool rebuildOnAwake = true;


    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    private readonly List<Zone> zones = new List<Zone>();

    public IReadOnlyList<Zone> Zones => zones;

    public bool IsBuilt => zones.Count > 0;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        if (rebuildOnAwake)
        {
            Build();
        }
    }


    // =========================================================
    // BUILD
    // =========================================================

    /// <summary>
    /// Buckets the NavMesh triangulation into a grid and keeps one zone
    /// per occupied cell.
    /// </summary>
    public void Build()
    {
        zones.Clear();

        NavMeshTriangulation triangulation =
            NavMesh.CalculateTriangulation();

        if (triangulation.vertices == null ||
            triangulation.vertices.Length == 0)
        {
            Debug.LogWarning(
                "[ZoneGraph] No NavMesh found. Bake one before the " +
                "Director can hint anywhere.",
                this
            );

            return;
        }

        Dictionary<Vector2Int, Vector3> sums =
            new Dictionary<Vector2Int, Vector3>();

        Dictionary<Vector2Int, int> counts =
            new Dictionary<Vector2Int, int>();

        foreach (Vector3 vertex in triangulation.vertices)
        {
            Vector2Int key =
                new Vector2Int(
                    Mathf.FloorToInt(vertex.x / cellSize),
                    Mathf.FloorToInt(vertex.z / cellSize)
                );

            if (sums.ContainsKey(key))
            {
                sums[key] += vertex;

                counts[key]++;
            }
            else
            {
                sums[key] = vertex;

                counts[key] = 1;
            }
        }

        foreach (KeyValuePair<Vector2Int, Vector3> entry in sums)
        {
            Vector3 average =
                entry.Value / counts[entry.Key];

            /*
             * The average of a cell's vertices can land in a hole - off
             * a ledge, or inside a pillar - so it has to be pulled back
             * onto walkable ground before it is usable as a hint.
             */

            if (!NavMesh.SamplePosition(
                    average,
                    out NavMeshHit hit,
                    cellSize,
                    NavMesh.AllAreas))
            {
                continue;
            }

            zones.Add(
                new Zone(hit.position, cellSize * 0.5f)
            );
        }

        Debug.Log(
            "[ZoneGraph] Built " + zones.Count +
            " zones at " + cellSize + "m.",
            this
        );
    }


    // =========================================================
    // QUERIES
    // =========================================================

    /// <summary>
    /// A zone within a distance band of a point.
    ///
    /// The minimum distance is the important half. Hinting the zone the
    /// player is standing in would put the creature on top of them and
    /// make the Director indistinguishable from a tracker.
    /// </summary>
    public bool TryGetZoneNear(
        Vector3 origin,
        float minDistance,
        float maxDistance,
        out Zone zone)
    {
        return TryPickWeighted(
            origin,
            minDistance,
            maxDistance,
            out zone
        );
    }


    /// <summary>
    /// A zone at least minDistance from a point - used to push the
    /// creature away when menace runs too high.
    /// </summary>
    public bool TryGetZoneFarFrom(
        Vector3 origin,
        float minDistance,
        out Zone zone)
    {
        return TryPickWeighted(
            origin,
            minDistance,
            float.MaxValue,
            out zone
        );
    }


    private bool TryPickWeighted(
        Vector3 origin,
        float minDistance,
        float maxDistance,
        out Zone zone)
    {
        zone = default;

        if (zones.Count == 0)
            return false;

        List<Zone> candidates = new List<Zone>();

        float minSqr = minDistance * minDistance;

        bool boundedAbove = maxDistance < float.MaxValue;

        float maxSqr =
            boundedAbove ? maxDistance * maxDistance : 0f;

        for (int i = 0; i < zones.Count; i++)
        {
            Vector3 delta = zones[i].Center - origin;

            delta.y = 0f;

            float sqr = delta.sqrMagnitude;

            if (sqr < minSqr)
                continue;

            if (boundedAbove && sqr > maxSqr)
                continue;

            candidates.Add(zones[i]);
        }

        if (candidates.Count == 0)
            return false;

        zone = candidates[Random.Range(0, candidates.Count)];

        return true;
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || zones.Count == 0)
            return;

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);

        for (int i = 0; i < zones.Count; i++)
        {
            Gizmos.DrawWireCube(
                zones[i].Center + Vector3.up * 0.1f,
                new Vector3(cellSize, 0.2f, cellSize)
            );
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A locker, a gap under a desk, a maintenance cupboard.
///
/// Registers itself so two very different things can find it: the
/// player, to get in, and the creature's SearchState, to check it.
/// That second half is what stops hiding from being a guaranteed win -
/// a hiding place nothing ever looks in is just a pause button.
/// </summary>
public class HidingSpot : MonoBehaviour
{
    // =========================================================
    // REGISTRY
    // =========================================================

    private static readonly List<HidingSpot> active =
        new List<HidingSpot>();

    public static IReadOnlyList<HidingSpot> Active => active;


    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Placement")]
    /// <summary>
    /// Where the player stands while hidden. Falls back to this
    /// object's own transform.
    /// </summary>
    [SerializeField]
    private Transform hidePoint;

    [SerializeField, Min(0.2f)]
    private float interactRadius = 1.8f;


    [Header("Concealment")]
    [SerializeField, Range(0f, 1f)]
    private float visibilityWhileHidden = 0.05f;

    /// <summary>
    /// Leaning out to look is the whole risk-reward of hiding. It has
    /// to be genuinely dangerous or nobody will ever stay still.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float visibilityWhilePeeking = 0.55f;

    [SerializeField, Range(0f, 1f)]
    private float noiseWhileHidden = 0.3f;


    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    public bool Occupied { get; set; }

    public Vector3 HidePosition =>
        hidePoint != null
            ? hidePoint.position
            : transform.position;

    public Quaternion HideRotation =>
        hidePoint != null
            ? hidePoint.rotation
            : transform.rotation;

    public float InteractRadius => interactRadius;

    public float VisibilityWhileHidden => visibilityWhileHidden;

    public float VisibilityWhilePeeking => visibilityWhilePeeking;

    public float NoiseWhileHidden => noiseWhileHidden;


    // =========================================================
    // UNITY
    // =========================================================

    private void OnEnable()
    {
        if (!active.Contains(this))
        {
            active.Add(this);
        }
    }


    private void OnDisable()
    {
        active.Remove(this);

        Occupied = false;
    }


    // =========================================================
    // QUERIES
    // =========================================================

    /// <summary>
    /// The closest spot the player could step into right now.
    /// </summary>
    public static bool TryGetInteractable(
        Vector3 from,
        out HidingSpot result)
    {
        result = null;

        float best = float.MaxValue;

        for (int i = 0; i < active.Count; i++)
        {
            HidingSpot spot = active[i];

            if (spot == null || spot.Occupied)
                continue;

            float distance =
                Vector3.Distance(from, spot.HidePosition);

            if (distance > spot.interactRadius || distance >= best)
                continue;

            best = distance;

            result = spot;
        }

        return result != null;
    }


    /// <summary>
    /// Every spot within a radius, nearest first. Used by SearchState
    /// to decide what is worth checking.
    /// </summary>
    public static void CollectNear(
        Vector3 origin,
        float radius,
        List<HidingSpot> results)
    {
        results.Clear();

        float radiusSqr = radius * radius;

        for (int i = 0; i < active.Count; i++)
        {
            HidingSpot spot = active[i];

            if (spot == null)
                continue;

            if ((spot.HidePosition - origin).sqrMagnitude > radiusSqr)
                continue;

            results.Add(spot);
        }

        results.Sort((a, b) =>
            (a.HidePosition - origin).sqrMagnitude
                .CompareTo(
                    (b.HidePosition - origin).sqrMagnitude));
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        Gizmos.color =
            Occupied
                ? new Color(1f, 0.5f, 0.1f, 0.9f)
                : new Color(0.3f, 0.9f, 0.6f, 0.6f);

        Gizmos.DrawWireSphere(HidePosition, interactRadius);

        Gizmos.DrawRay(HidePosition, Vector3.up * 1.5f);
    }
}

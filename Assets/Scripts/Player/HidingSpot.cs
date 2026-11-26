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


    [Header("Pose")]
    /// <summary>
    /// Eye height above the floor while hidden. Under a desk the player
    /// is crouched with their head just below the tabletop. Below zero
    /// keeps the player's own eye height.
    /// </summary>
    [SerializeField]
    private float eyeHeight = -1f;

    /// <summary>How far the player may turn either side of the hide point's facing.</summary>
    [SerializeField, Range(0f, 180f)]
    private float lookYawRange = 180f;

    [SerializeField, Range(-89f, 0f)]
    private float lookPitchUp = -85f;

    [SerializeField, Range(0f, 89f)]
    private float lookPitchDown = 85f;

    /// <summary>Seconds to duck in or climb out.</summary>
    [SerializeField, Min(0.05f)]
    private float transitionTime = 0.45f;


    [Header("Concealment")]
    [SerializeField, Range(0f, 1f)]
    private float visibilityWhileHidden = 0.05f;

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

    public float EyeHeight => eyeHeight;

    public float LookYawRange => lookYawRange;

    public float LookPitchUp => lookPitchUp;

    public float LookPitchDown => lookPitchDown;

    public float TransitionTime => transitionTime;


    // =========================================================
    // DESK
    // =========================================================

    /*
     * The desk pose, tuned in the hiding test scene: crouched with the
     * eyes just under the tabletop, tucked against the far legs so the
     * top's edge crosses the top of the view and the leg frames stand
     * at either side, looking out of one long side.
     */

    public const float DeskEyeHeight = 0.48f;

    /// <summary>How far back from the desk's centre the player crouches.</summary>
    public const float DeskTuckBack = 0.36f;


    /// <summary>Applies the tuned under-the-desk pose.</summary>
    public void ConfigureDesk(float reach = 1.6f)
    {
        ConfigurePose(null, DeskEyeHeight, 60f, -8f, 25f, 0.45f, reach);
    }


    /// <summary>Sets the hidden pose - used by the scene builders.</summary>
    public void ConfigurePose(
        Transform point, float eye, float yawRange,
        float pitchUp, float pitchDown, float transition, float radius)
    {
        hidePoint = point;
        eyeHeight = eye;
        lookYawRange = yawRange;
        lookPitchUp = pitchUp;
        lookPitchDown = pitchDown;
        transitionTime = transition;
        interactRadius = radius;
    }

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

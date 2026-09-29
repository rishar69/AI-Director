using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Joins two vents into a route the creature can actually use.
///
/// This is what turns vents from a teleport convenience for the
/// Director into part of the level's geometry. Once linked, the
/// creature will path through a duct of its own accord because the
/// NavMesh says it is a shorter way round - which is exactly how it
/// should end up behind you.
/// </summary>
[RequireComponent(typeof(VentPoint))]
public class VentLink : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Route")]
    [SerializeField]
    private VentPoint destination;

    [SerializeField]
    private bool bidirectional = true;

    [SerializeField, Min(0.1f)]
    private float width = 0.9f;

    /// <summary>
    /// Extra path cost for using the duct.
    ///
    /// Above 1 the creature prefers walking and only ducts through when
    /// it is a genuine shortcut, which keeps vent use feeling like a
    /// decision rather than a habit.
    /// </summary>
    [SerializeField, Min(0.1f)]
    private float costModifier = 1.4f;


    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    private NavMeshLink link;


    public bool IsLinked => link != null && destination != null;


    // =========================================================
    // UNITY
    // =========================================================

    private void OnEnable()
    {
        Rebuild();
    }


    private void OnValidate()
    {
        if (isActiveAndEnabled)
        {
            Rebuild();
        }
    }


    // =========================================================
    // BUILD
    // =========================================================

    public void Rebuild()
    {
        if (destination == null)
            return;

        if (link == null)
        {
            link = GetComponent<NavMeshLink>();
        }

        if (link == null)
        {
            link = gameObject.AddComponent<NavMeshLink>();
        }

        /*
         * NavMeshLink endpoints are in the link's own local space, so
         * the destination has to be converted rather than assigned
         * straight from world coordinates.
         */

        link.startPoint = Vector3.zero;

        link.endPoint =
            transform.InverseTransformPoint(destination.Position);

        link.bidirectional = bidirectional;

        link.width = width;

        link.costModifier = costModifier;

        link.UpdateLink();
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawGizmos || destination == null)
            return;

        Gizmos.color = new Color(0.9f, 0.3f, 1f, 0.9f);

        Vector3 from = transform.position + Vector3.up * 0.3f;

        Vector3 to = destination.Position + Vector3.up * 0.3f;

        Gizmos.DrawLine(from, to);

        /*
         * A marker at the midpoint, lifted, so overlapping links in a
         * dense level stay tellable apart.
         */

        Vector3 mid = (from + to) * 0.5f + Vector3.up * 1.2f;

        Gizmos.DrawLine(from, mid);

        Gizmos.DrawLine(mid, to);

        Gizmos.DrawWireSphere(mid, 0.25f);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A named region of the level - a gallery, the hall, the staff
/// corridors - described as a set of boxes.
///
/// This is the backstage markup the behaviour tree has been missing.
/// The alien's BACKSTAGE_* branches ask "am I backstage?" and "where is
/// backstage?", and until now the only honest answer was "the level has
/// none". Nothing reads it yet; wiring XenoLeaf.IsBackstage to
/// <see cref="IsBackstage"/> is its own phase.
///
/// No colliders on purpose: a trigger volume would sit in the way of
/// every vision raycast that does not explicitly ignore triggers.
/// </summary>
public class MapArea : MonoBehaviour
{
    // =========================================================
    // REGISTRY
    // =========================================================

    private static readonly List<MapArea> active = new List<MapArea>();

    public static IReadOnlyList<MapArea> Active => active;


    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private string displayName = "Area";

    /// <summary>
    /// Staff-only space: service corridors, storage, the vault.
    /// </summary>
    [SerializeField]
    private bool backstage;

    /// <summary>
    /// World-space boxes. Several, because few rooms are one rectangle.
    /// </summary>
    [SerializeField]
    private List<Bounds> boxes = new List<Bounds>();


    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    public string DisplayName => displayName;

    public bool Backstage => backstage;

    public IReadOnlyList<Bounds> Boxes => boxes;


    public void Configure(
        string name,
        bool isBackstage,
        List<Bounds> areaBoxes)
    {
        displayName = name;
        backstage = isBackstage;
        boxes = areaBoxes;
    }


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
    }


    // =========================================================
    // QUERIES
    // =========================================================

    public bool Contains(Vector3 point)
    {
        foreach (Bounds box in boxes)
        {
            if (box.Contains(point))
                return true;
        }

        return false;
    }


    /// <summary>
    /// The area containing <paramref name="point"/>, or null.
    /// </summary>
    public static MapArea Find(Vector3 point)
    {
        foreach (MapArea area in active)
        {
            if (area.Contains(point))
                return area;
        }

        return null;
    }


    public static bool IsBackstage(Vector3 point)
    {
        MapArea area = Find(point);

        return area != null && area.backstage;
    }


    // =========================================================
    // DEBUG
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = backstage
            ? new Color(1f, 0.4f, 0.1f, 0.6f)
            : new Color(0.2f, 0.8f, 1f, 0.6f);

        foreach (Bounds box in boxes)
        {
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}

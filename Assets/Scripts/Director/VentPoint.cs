using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A place the creature can vanish into or emerge from.
///
/// Purely a marker - drop these around the level wherever a duct,
/// grille or shaft would plausibly be. The Director uses them when it
/// needs the creature genuinely gone rather than merely far away.
///
/// Entirely optional. With no vents placed, the Director relocates to a
/// distant zone instead, which works but is less convincing because the
/// creature has to walk there.
/// </summary>
public class VentPoint : MonoBehaviour
{
    // =========================================================
    // REGISTRY
    // =========================================================

    private static readonly List<VentPoint> active =
        new List<VentPoint>();

    public static IReadOnlyList<VentPoint> Active => active;


    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Vent")]
    /// <summary>
    /// Whether the creature may emerge here. Some vents make sense as
    /// exits only - a grille the player can see is a bad place to have
    /// something appear from unannounced.
    /// </summary>
    [SerializeField]
    private bool allowEntry = true;

    [SerializeField]
    private bool allowExit = true;


    public bool AllowEntry => allowEntry;

    public bool AllowExit => allowExit;

    public Vector3 Position => transform.position;


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

    /// <summary>
    /// The exit vent furthest from a point - where to put a creature
    /// that needs to stop being the player's problem.
    /// </summary>
    public static bool TryGetFurthestExit(
        Vector3 from,
        float minDistance,
        out VentPoint result)
    {
        result = null;

        float best = minDistance * minDistance;

        for (int i = 0; i < active.Count; i++)
        {
            VentPoint vent = active[i];

            if (vent == null || !vent.allowExit)
                continue;

            float sqr =
                (vent.Position - from).sqrMagnitude;

            if (sqr < best)
                continue;

            best = sqr;

            result = vent;
        }

        return result != null;
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.9f, 0.3f, 1f, 0.8f);

        Gizmos.DrawWireCube(
            transform.position + Vector3.up * 0.5f,
            new Vector3(1f, 1f, 1f)
        );

        Gizmos.DrawRay(
            transform.position,
            Vector3.up * 1.5f
        );
    }
}

using UnityEngine;

/// <summary>
/// Turns loud noises into suspicious items.
///
/// The player already has the tools - Noisemaker emits
/// NoiseType.Distraction, PlayerThrower throws things, doors emit
/// NoiseType.Door. Rather than teach each of those about suspicious
/// items, this listens to NoiseBus and leaves an item behind wherever
/// something worth investigating happened.
///
/// WHY A SEPARATE THING FROM HEARING. A noise is instantaneous - the
/// hearing sense spikes and decays and the moment is gone. An item
/// persists, which is what lets the creature walk somewhere, arrive
/// twenty seconds later and still find a reason to be there. One is a
/// stimulus, the other is evidence.
/// </summary>
public class SuspiciousItemSpawner : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("What Leaves Evidence")]
    /// <summary>
    /// Thrown decoys - the loudest signal the player can deliberately
    /// send, and the reason the high priority branch outranks search.
    /// </summary>
    [SerializeField]
    private bool distractionsAreHigh = true;

    [SerializeField]
    private bool objectsAreMedium = true;

    [SerializeField]
    private bool doorsAreLow = true;


    [Header("Item")]
    [SerializeField, Min(1f)]
    private float lifetime = 45f;

    /// <summary>
    /// Stops one rattling noisemaker carpeting the level in items.
    /// </summary>
    [SerializeField, Min(0f)]
    private float minimumSpacing = 4f;

    [SerializeField, Min(0f)]
    private float minimumInterval = 1f;


    [Header("Debug")]
    [SerializeField]
    private bool logSpawns = false;


    private float nextAllowedTime;


    // =========================================================
    // UNITY
    // =========================================================

    private void OnEnable()
    {
        NoiseBus.OnNoise += HandleNoise;
    }


    private void OnDisable()
    {
        NoiseBus.OnNoise -= HandleNoise;
    }


    // =========================================================
    // SPAWNING
    // =========================================================

    private void HandleNoise(NoiseEvent noise)
    {
        if (!TryClassify(noise.Type, out SuspiciousItemPriority p))
            return;

        if (Time.time < nextAllowedTime)
            return;

        if (IsCrowded(noise.Position))
            return;

        nextAllowedTime = Time.time + minimumInterval;

        GameObject host =
            new GameObject("SuspiciousItem (" + noise.Type + ")");

        host.transform.SetParent(transform, false);

        host.transform.position = noise.Position;

        SuspiciousItem item =
            host.AddComponent<SuspiciousItem>();

        /*
         * The notice radius IS the noise radius. An item nobody could
         * have heard is not evidence, it is telepathy.
         */

        item.Configure(p, noise.Radius, lifetime);

        if (logSpawns)
        {
            Debug.Log(
                "[SuspiciousItem] " + p + " at " + noise.Position +
                " from " + noise.Type,
                host
            );
        }
    }


    private bool TryClassify(
        NoiseType type,
        out SuspiciousItemPriority priority)
    {
        switch (type)
        {
            case NoiseType.Distraction:

                priority = SuspiciousItemPriority.High;

                return distractionsAreHigh;

            case NoiseType.Object:

                priority = SuspiciousItemPriority.Medium;

                return objectsAreMedium;

            case NoiseType.Door:

                priority = SuspiciousItemPriority.Low;

                return doorsAreLow;

            default:

                /*
                 * Footsteps, gunshots and the flamethrower are the
                 * PLAYER making noise, not an object worth visiting.
                 * Those belong to the hearing sense, and turning them
                 * into items here would have the creature investigate
                 * a spot it should be attacking.
                 */

                priority = SuspiciousItemPriority.Low;

                return false;
        }
    }


    private bool IsCrowded(Vector3 position)
    {
        if (minimumSpacing <= 0f)
            return false;

        var items = SuspiciousItemBus.Items;

        for (int i = 0; i < items.Count; i++)
        {
            SuspiciousItem existing = items[i];

            if (existing == null || existing.Consumed)
                continue;

            Vector3 delta = existing.Position - position;

            delta.y = 0f;

            if (delta.magnitude < minimumSpacing)
                return true;
        }

        return false;
    }
}

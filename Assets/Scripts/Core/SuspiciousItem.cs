using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How much a suspicious item is worth dropping everything for.
///
/// The exports run three separate branches off this, at three
/// different priorities in the root selector - HIGH above the
/// systematic search, MEDIUM and LOW below it. So the priority is not
/// a weighting inside one behaviour, it decides whether investigating
/// beats searching for you or loses to it.
/// </summary>
public enum SuspiciousItemPriority
{
    Low = 0,
    Medium = 1,
    High = 2
}


/// <summary>
/// Something in the world the creature might come and look at.
///
/// A flare that landed, a door left swinging, a noisemaker still
/// rattling. Not the player - the whole point of an item is that it is
/// a decoy, so investigating one is time spent NOT looking for you.
///
/// NOTICE RADIUS IS THE HONEST PART. An item is only ever acquired by a
/// creature standing inside that radius, because the radius is the one
/// the noise actually carried. Nothing here reaches out to a creature;
/// creatures come and ask.
/// </summary>
[DisallowMultipleComponent]
public class SuspiciousItem : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private SuspiciousItemPriority priority =
        SuspiciousItemPriority.Medium;

    /// <summary>
    /// How far away it can be noticed from.
    /// </summary>
    [SerializeField, Min(1f)]
    private float noticeRadius = 20f;

    /// <summary>
    /// Seconds before it stops being interesting. Negative never
    /// expires, for placed scenery rather than thrown objects.
    /// </summary>
    [SerializeField]
    private float lifetime = 45f;


    // =========================================================
    // STATE
    // =========================================================

    private float spawnTime;

    private bool consumed;


    public SuspiciousItemPriority Priority => priority;

    public float NoticeRadius => noticeRadius;

    public Vector3 Position => transform.position;

    /// <summary>
    /// Whether a creature has already been over and finished with it.
    /// A consumed item stays in the scene but stops attracting
    /// anything, so the creature does not loop back to the same flare.
    /// </summary>
    public bool Consumed => consumed;


    public bool IsExpired =>
        lifetime >= 0f && Time.time - spawnTime > lifetime;


    // =========================================================
    // UNITY
    // =========================================================

    private void OnEnable()
    {
        spawnTime = Time.time;

        SuspiciousItemBus.Register(this);
    }


    private void OnDisable()
    {
        SuspiciousItemBus.Unregister(this);
    }


    // =========================================================
    // API
    // =========================================================

    public void Configure(
        SuspiciousItemPriority value,
        float radius,
        float life)
    {
        priority = value;

        noticeRadius = Mathf.Max(1f, radius);

        lifetime = life;

        spawnTime = Time.time;

        consumed = false;
    }


    public void MarkConsumed()
    {
        consumed = true;
    }


    private void OnDrawGizmosSelected()
    {
        Gizmos.color =
            consumed
                ? new Color(0.4f, 0.4f, 0.4f, 0.4f)
                : new Color(1f, 0.85f, 0.2f, 0.7f);

        Gizmos.DrawWireSphere(transform.position, noticeRadius);
    }
}


/// <summary>
/// Every live suspicious item in the scene.
///
/// A plain static registry for the same reason NoiseBus is one: the
/// creature has to be able to ask "is there anything worth looking at
/// near me" without holding references to things that may be destroyed
/// underneath it.
/// </summary>
public static class SuspiciousItemBus
{
    private static readonly List<SuspiciousItem> items =
        new List<SuspiciousItem>();


    public static IReadOnlyList<SuspiciousItem> Items => items;


#if UNITY_EDITOR
    [UnityEngine.RuntimeInitializeOnLoadMethod(
        UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        items.Clear();
    }
#endif


    public static void Register(SuspiciousItem item)
    {
        if (item != null && !items.Contains(item))
        {
            items.Add(item);
        }
    }


    public static void Unregister(SuspiciousItem item)
    {
        items.Remove(item);
    }


    /// <summary>
    /// The best item to go and look at from here, or null.
    ///
    /// Highest priority first, nearest as the tie break - not nearest
    /// outright. A flare across the room beats a rattle underfoot,
    /// which is what makes a high priority decoy worth throwing.
    /// </summary>
    public static SuspiciousItem FindBest(Vector3 from)
    {
        SuspiciousItem best = null;

        float bestDistance = 0f;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            SuspiciousItem item = items[i];

            if (item == null)
            {
                items.RemoveAt(i);

                continue;
            }

            if (item.Consumed || item.IsExpired)
                continue;

            Vector3 delta = item.Position - from;

            delta.y = 0f;

            float distance = delta.magnitude;

            if (distance > item.NoticeRadius)
                continue;

            if (best != null)
            {
                if (item.Priority < best.Priority)
                    continue;

                if (item.Priority == best.Priority &&
                    distance >= bestDistance)
                {
                    continue;
                }
            }

            best = item;

            bestDistance = distance;
        }

        return best;
    }
}

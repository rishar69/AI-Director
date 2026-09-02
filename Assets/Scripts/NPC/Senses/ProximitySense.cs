using UnityEngine;

/// <summary>
/// Short range unconditional detection - the "it can smell you" sense.
///
/// Exists to close the hole the other two senses leave. Without it, a
/// player who stands perfectly still directly behind the alien, outside
/// its vision cone, is completely safe, which reads as the AI being
/// broken rather than as clever play.
///
/// Its radius should stay small. This is a last resort, not a second
/// pair of eyes.
/// </summary>
public class ProximitySense : Sense
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Proximity")]
    [SerializeField, Min(0.1f)]
    private float radius = 2.5f;

    [SerializeField, Range(0f, 1f)]
    private float maximumStrength = 0.85f;

    /// <summary>
    /// When true, a wall between the alien and the target defeats this
    /// sense. Turning it off makes the alien able to sense through thin
    /// cover, which is a good late-game behaviour unlock.
    /// </summary>
    [SerializeField]
    private bool respectObstacles = true;


    [Header("Query")]
    [SerializeField, Min(1)]
    private int maxTargets = 8;


    // =========================================================
    // STATE
    // =========================================================

    private Collider[] overlapResults;

    private readonly Vector3[] samplePoints = new Vector3[3];

    private bool hadContactLastPoll;


    public override SenseChannel Channel => SenseChannel.Proximity;

    public float Radius => radius;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        overlapResults = new Collider[maxTargets];
    }


    private void OnValidate()
    {
        radius = Mathf.Max(0.1f, radius);

        maxTargets = Mathf.Max(1, maxTargets);

        if (overlapResults == null ||
            overlapResults.Length != maxTargets)
        {
            overlapResults = new Collider[maxTargets];
        }
    }


    // =========================================================
    // SAMPLING
    // =========================================================

    protected override SenseReading Sample()
    {
        if (overlapResults == null)
        {
            overlapResults = new Collider[maxTargets];
        }

        Vector3 origin = SensorPosition;

        int count =
            Physics.OverlapSphereNonAlloc(
                origin,
                radius,
                overlapResults,
                targetMask,
                QueryTriggerInteraction.Ignore
            );

        float bestStrength = 0f;

        Vector3 bestPoint = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Collider collider = overlapResults[i];

            if (collider == null)
                continue;

            /*
             * Measure to the closest point on the collider rather than
             * its centre, so a large target counts as near when any
             * part of it is near.
             */

            Vector3 closest =
                collider.ClosestPoint(origin);

            float distance =
                Vector3.Distance(origin, closest);

            if (distance > radius)
                continue;

            if (respectObstacles &&
                IsOccludedToAnySample(collider, origin))
            {
                continue;
            }

            float strength =
                Mathf.Clamp01(1f - (distance / radius)) *
                maximumStrength;

            if (strength > bestStrength)
            {
                bestStrength = strength;

                bestPoint = closest;
            }
        }

        hadContactLastPoll = bestStrength > 0f;

        if (!hadContactLastPoll)
            return SenseReading.None;

        return SenseReading.Continuous(
            SenseChannel.Proximity,
            bestPoint,
            bestStrength
        );
    }


    /// <summary>
    /// True only when every sample point on the target is blocked.
    ///
    /// One clear line is enough - crouching behind a low crate should
    /// not make someone standing at arm's length undetectable.
    /// </summary>
    private bool IsOccludedToAnySample(
        Collider collider,
        Vector3 origin)
    {
        GetSamplePoints(collider, samplePoints);

        for (int i = 0; i < samplePoints.Length; i++)
        {
            if (!IsOccluded(origin, samplePoints[i]))
                return false;
        }

        return true;
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color =
            hadContactLastPoll
                ? new Color(1f, 0.4f, 0.1f, 0.9f)
                : new Color(1f, 0.8f, 0.2f, 0.35f);

        Gizmos.DrawWireSphere(SensorPosition, radius);
    }
}

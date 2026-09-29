using UnityEngine;

/// <summary>
/// Line of sight detection.
///
/// Canonically the xenomorph has no eyes, but a player needs a legible
/// threat model, so it gets sight like everything else in the genre.
/// What keeps it from feeling like a security camera is the falloff:
/// contact at the edge of the cone or the edge of the range is weak,
/// and weak contact only creeps awareness upward instead of snapping
/// straight to certainty.
/// </summary>
public class VisionSense : Sense
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Cone")]
    [SerializeField, Min(0.1f)]
    private float range = 18f;

    [SerializeField, Range(1f, 360f)]
    private float angle = 110f;


    [Header("Falloff")]
    /*
     * Both curves are sampled on 0..1 and return a 0..1 multiplier.
     * They multiply together, so a target at the far edge of the
     * periphery is very weak contact indeed.
     */

    [SerializeField]
    private AnimationCurve distanceFalloff =
        AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f);

    [SerializeField]
    private AnimationCurve peripheralFalloff =
        AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f);

    /// <summary>
    /// Contact below this is discarded rather than reported, so a
    /// barely-glimpsed target does not keep awareness permanently
    /// simmering.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float minimumStrength = 0.05f;


    [Header("Query")]
    [SerializeField, Min(1)]
    private int maxTargets = 16;


    // =========================================================
    // STATE
    // =========================================================

    private Collider[] overlapResults;

    private readonly Vector3[] samplePoints = new Vector3[3];

    /// <summary>
    /// The point that produced the best reading. Gizmos only.
    /// </summary>
    private Vector3 lastVisiblePoint;

    private bool hadContactLastPoll;


    public override SenseChannel Channel => SenseChannel.Vision;

    public float Range => range;

    public float Angle => angle;

    public void SetRange(float value)
    {
        range = Mathf.Max(0.1f, value);
    }

    public void SetAngle(float value)
    {
        angle = Mathf.Clamp(value, 1f, 360f);
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        overlapResults = new Collider[maxTargets];
    }


    private void OnValidate()
    {
        range = Mathf.Max(0.1f, range);

        angle = Mathf.Clamp(angle, 1f, 360f);

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

        Vector3 eye = SensorPosition;

        int count =
            Physics.OverlapSphereNonAlloc(
                eye,
                range,
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

            float strength =
                EvaluateCollider(
                    collider,
                    eye,
                    out Vector3 point
                );

            if (strength > bestStrength)
            {
                bestStrength = strength;

                bestPoint = point;
            }
        }

        hadContactLastPoll = bestStrength >= minimumStrength;

        if (!hadContactLastPoll)
            return SenseReading.None;

        lastVisiblePoint = bestPoint;

        return SenseReading.Continuous(
            SenseChannel.Vision,
            bestPoint,
            bestStrength
        );
    }


    /// <summary>
    /// Scores the most visible point on one collider.
    /// </summary>
    private float EvaluateCollider(
        Collider collider,
        Vector3 eye,
        out Vector3 bestPoint)
    {
        bestPoint = Vector3.zero;

        /*
         * A target inside a locker is still physically there and still
         * inside the cone. Concealment is what makes hiding work, and
         * it is applied here rather than by excluding the collider so
         * that partial cover stays partial.
         */

        float concealment = Concealment.VisibilityOf(collider);

        if (concealment <= 0f)
            return 0f;

        GetSamplePoints(collider, samplePoints);

        float halfAngle = angle * 0.5f;

        float best = 0f;

        for (int i = 0; i < samplePoints.Length; i++)
        {
            Vector3 point = samplePoints[i];

            Vector3 delta = point - eye;

            float distance = delta.magnitude;

            if (distance > range)
                continue;

            float pointAngle =
                Vector3.Angle(SensorForward, delta);

            if (pointAngle > halfAngle)
                continue;

            if (IsOccluded(eye, point))
                continue;

            float distanceTerm =
                distanceFalloff.Evaluate(
                    Mathf.Clamp01(distance / range)
                );

            /*
             * At 360 degrees every direction is dead centre, so the
             * peripheral term has to collapse to 1 rather than divide
             * by a half-angle of 180 and wash everything out.
             */

            float peripheralTerm =
                halfAngle > 0.01f
                    ? peripheralFalloff.Evaluate(
                        Mathf.Clamp01(pointAngle / halfAngle))
                    : 1f;

            float strength =
                Mathf.Clamp01(
                    distanceTerm * peripheralTerm * concealment
                );

            if (strength > best)
            {
                best = strength;

                bestPoint = point;
            }
        }

        return best;
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Vector3 eye = SensorPosition;

        Gizmos.color =
            hadContactLastPoll
                ? new Color(1f, 0.3f, 0.2f, 0.9f)
                : new Color(0.4f, 0.8f, 1f, 0.5f);

        float halfAngle = angle * 0.5f;

        int segments = 32;

        Vector3 previous = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;

            float a = Mathf.Lerp(-halfAngle, halfAngle, t);

            Vector3 direction =
                Quaternion.AngleAxis(a, Vector3.up) * SensorForward;

            Vector3 point = eye + direction * range;

            if (i == 0 || i == segments)
            {
                Gizmos.DrawLine(eye, point);
            }

            if (i > 0)
            {
                Gizmos.DrawLine(previous, point);
            }

            previous = point;
        }

        if (hadContactLastPoll && Application.isPlaying)
        {
            Gizmos.color = Color.red;

            Gizmos.DrawLine(eye, lastVisiblePoint);

            Gizmos.DrawWireSphere(lastVisiblePoint, 0.12f);
        }
    }
}

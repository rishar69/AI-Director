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


    [Header("Flashlight Beam")]
    /*
     * A lit torch is the brightest thing in a night museum. Two ways to
     * catch it, both from the caretaker's own eyes and line of sight:
     * the lens itself pointed his way (glare - seen from much further
     * than a body), and the pool of light it throws on a wall or floor
     * (he sees the spot and reads roughly where it comes from). Turning
     * the torch off removes both - that is the player's trade-off.
     */

    [SerializeField]
    private bool seesFlashlights = true;

    /// <summary>How much further than a body the glare of a lens reaches.</summary>
    [SerializeField, Min(1f)]
    private float glareRangeMultiplier = 1.6f;

    /// <summary>Strength of looking straight into the lens at close range.</summary>
    [SerializeField, Range(0f, 1f)]
    private float glareStrength = 0.9f;

    /// <summary>Strength of seeing the lit spot at close range - creeps awareness up.</summary>
    [SerializeField, Range(0f, 1f)]
    private float spotStrength = 0.35f;


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

    /// <summary>Strongest sighting of a body in the last poll (VISUAL).</summary>
    public float LastBodyStrength { get; private set; }

    /// <summary>Strongest sighting of a torch beam in the last poll (SEE_FLASH_LIGHT).</summary>
    public float LastBeamStrength { get; private set; }

    public bool SeesFlashlights
    {
        get => seesFlashlights;
        set => seesFlashlights = value;
    }

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

        LastBodyStrength = bestStrength >= minimumStrength ? bestStrength : 0f;

        LastBeamStrength = 0f;

        if (seesFlashlights)
        {
            float beamStrength = EvaluateBeams(eye, out Vector3 beamPoint);

            if (beamStrength >= minimumStrength)
            {
                LastBeamStrength = beamStrength;
            }

            if (beamStrength > bestStrength)
            {
                bestStrength = beamStrength;

                bestPoint = beamPoint;
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
    // FLASHLIGHT BEAMS
    // =========================================================

    /// <summary>
    /// The strongest sighting of any lit flashlight. The reported point
    /// is the lens - what a man following a beam back would walk to.
    /// </summary>
    private float EvaluateBeams(Vector3 eye, out Vector3 bestPoint)
    {
        bestPoint = Vector3.zero;

        float best = 0f;

        float halfAngle = angle * 0.5f;

        foreach (Flashlight torch in Flashlight.Lit)
        {
            if (torch == null || !torch.IsBeaming)
                continue;

            Vector3 lens = torch.BeamOrigin;

            Vector3 beamDir = torch.BeamDirection;

            /* 1. Glare: the lens is in his view and points at him. */

            Vector3 toLens = lens - eye;

            float lensDistance = toLens.magnitude;

            float glareRange = range * glareRangeMultiplier;

            if (lensDistance <= glareRange &&
                Vector3.Angle(SensorForward, toLens) <= halfAngle &&
                Vector3.Angle(beamDir, -toLens) <= torch.BeamAngle * 0.5f &&
                !IsOccluded(eye, lens))
            {
                float s = glareStrength *
                          distanceFalloff.Evaluate(Mathf.Clamp01(lensDistance / glareRange));

                if (s > best)
                {
                    best = s;

                    bestPoint = lens;
                }
            }

            /* 2. The pool of light where the beam lands. */

            if (Physics.Raycast(lens, beamDir, out RaycastHit hit, torch.BeamRange,
                    obstacleMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 spot = hit.point + hit.normal * 0.05f;

                Vector3 toSpot = spot - eye;

                float spotDistance = toSpot.magnitude;

                float spotAngle = Vector3.Angle(SensorForward, toSpot);

                if (spotDistance <= range &&
                    spotAngle <= halfAngle &&
                    !IsOccluded(eye, spot))
                {
                    /* Light falls off with throw: a far wall shows a dim spot. */

                    float throwTerm = 1f - Mathf.Clamp01(hit.distance / torch.BeamRange) * 0.6f;

                    float s = spotStrength * throwTerm *
                              distanceFalloff.Evaluate(Mathf.Clamp01(spotDistance / range)) *
                              (halfAngle > 0.01f
                                  ? peripheralFalloff.Evaluate(Mathf.Clamp01(spotAngle / halfAngle))
                                  : 1f);

                    if (s > best)
                    {
                        best = s;

                        bestPoint = lens;
                    }
                }
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

using UnityEngine;

/// <summary>
/// Base class for everything the xenomorph can perceive with.
///
/// A sense knows how to look for a target and how confident it is. It
/// deliberately knows nothing about awareness, memory, states or the
/// Director - PerceptionSystem owns all of that. Adding a fourth sense
/// later (heat, vibration) means subclassing this and nothing else.
/// </summary>
/*
 * Deliberately NOT marked DisallowMultipleComponent. Unity applies that
 * attribute to subtypes as well, which would make vision, hearing and
 * proximity mutually exclusive on a single creature.
 */

public abstract class Sense : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Sense")]
    [SerializeField]
    private bool senseEnabled = true;

    [SerializeField]
    protected LayerMask targetMask;

    [SerializeField]
    protected LayerMask obstacleMask;


    [Header("Sensor Origin")]
    /*
     * Where on the body this sense measures from. Leaving it empty
     * falls back to the transform raised by sensorHeight, so a bare
     * GameObject still senses from roughly head height instead of
     * from the floor.
     */

    [SerializeField]
    private Transform sensorOrigin;

    [SerializeField]
    private float sensorHeight = 1.6f;


    [Header("Debug")]
    [SerializeField]
    protected bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    /// <summary>
    /// The reading produced by the most recent Poll. Debug display
    /// only - consumers should use the value Poll returned them.
    /// </summary>
    public SenseReading LastReading { get; private set; }


    public abstract SenseChannel Channel { get; }


    /// <summary>
    /// Runtime switch used by the Director and by behaviour unlocks -
    /// blinding or deafening the alien without disabling the component
    /// and losing its configuration.
    /// </summary>
    public bool SenseEnabled
    {
        get => senseEnabled;
        set => senseEnabled = value;
    }


    public Vector3 SensorPosition =>
        sensorOrigin != null
            ? sensorOrigin.position
            : transform.position + Vector3.up * sensorHeight;


    public Vector3 SensorForward =>
        sensorOrigin != null
            ? sensorOrigin.forward
            : transform.forward;


    // =========================================================
    // POLLING
    // =========================================================

    /// <summary>
    /// Asks the sense what it perceives right now.
    ///
    /// Called once per frame by PerceptionSystem. Nothing else should
    /// call this - a second caller would consume impulse readings that
    /// the perception system never gets to see.
    /// </summary>
    public SenseReading Poll()
    {
        if (!senseEnabled || !isActiveAndEnabled)
        {
            LastReading = SenseReading.None;

            return LastReading;
        }

        LastReading = Sample();

        return LastReading;
    }


    /// <summary>
    /// Where a subclass does its actual work.
    /// </summary>
    protected abstract SenseReading Sample();


    // =========================================================
    // SHARED HELPERS
    // =========================================================

    /// <summary>
    /// True when geometry on obstacleMask stands between two points.
    ///
    /// Stops slightly short of the destination so the target's own
    /// collider cannot register as the thing blocking the view of it.
    /// </summary>
    protected bool IsOccluded(
        Vector3 from,
        Vector3 to)
    {
        Vector3 delta = to - from;

        float distance = delta.magnitude;

        if (distance < 0.02f)
            return false;

        return Physics.Raycast(
            from,
            delta / distance,
            distance - 0.02f,
            obstacleMask,
            QueryTriggerInteraction.Ignore
        );
    }


    /// <summary>
    /// Picks sample points spread up the target's bounds rather than
    /// testing its pivot alone.
    ///
    /// Testing only the pivot means a target whose feet are hidden
    /// behind a crate but whose head is in plain view reads as fully
    /// concealed, and a target whose pivot pokes past a corner reads as
    /// fully visible. Both are very noticeable in play.
    /// </summary>
    protected static void GetSamplePoints(
        Collider collider,
        Vector3[] buffer)
    {
        Bounds bounds = collider.bounds;

        Vector3 centre = bounds.center;

        float low = Mathf.Lerp(bounds.min.y, bounds.max.y, 0.15f);

        float mid = Mathf.Lerp(bounds.min.y, bounds.max.y, 0.55f);

        float high = Mathf.Lerp(bounds.min.y, bounds.max.y, 0.9f);

        buffer[0] = new Vector3(centre.x, mid, centre.z);
        buffer[1] = new Vector3(centre.x, high, centre.z);
        buffer[2] = new Vector3(centre.x, low, centre.z);
    }
}

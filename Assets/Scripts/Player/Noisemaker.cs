using UnityEngine;

/// <summary>
/// A thrown object that makes a loud noise where it lands.
///
/// The point of a distraction is that it is a real noise at a real
/// place - it goes onto the same NoiseBus as your footsteps, and the
/// creature's hearing cannot tell the two apart. Nothing here talks to
/// the AI directly, which is why the trick works and why the AI would
/// also fall for a crate genuinely falling over.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Noisemaker : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Noise")]
    [SerializeField, Min(0f)]
    private float impactNoiseRadius = 22f;

    /// <summary>
    /// Number of separate clatters it makes after landing. A single
    /// ping is easy to dismiss; a bouncing object rattling three times
    /// pulls the creature all the way over.
    /// </summary>
    [SerializeField, Min(1)]
    private int clatterCount = 3;

    [SerializeField, Min(0.05f)]
    private float clatterInterval = 0.45f;

    /// <summary>
    /// Each clatter is quieter than the last.
    /// </summary>
    [SerializeField, Range(0.1f, 1f)]
    private float clatterFalloff = 0.6f;


    [Header("Lifetime")]
    [SerializeField, Min(1f)]
    private float lifetime = 20f;

    /// <summary>
    /// Impacts below this speed are ignored, so it does not fire while
    /// settling on the floor.
    /// </summary>
    [SerializeField, Min(0f)]
    private float minimumImpactSpeed = 1.5f;


    // =========================================================
    // STATE
    // =========================================================

    private int clattersRemaining;

    private float nextClatterTime;

    private float currentRadius;

    private bool landed;


    private void Awake()
    {
        clattersRemaining = clatterCount;

        currentRadius = impactNoiseRadius;

        Destroy(gameObject, lifetime);
    }


    private void Update()
    {
        if (!landed)
            return;

        if (clattersRemaining <= 0)
            return;

        if (Time.time < nextClatterTime)
            return;

        Clatter();
    }


    private void OnCollisionEnter(Collision collision)
    {
        if (landed)
            return;

        if (collision.relativeVelocity.magnitude < minimumImpactSpeed)
            return;

        landed = true;

        Clatter();
    }


    private void Clatter()
    {
        NoiseBus.Emit(
            transform.position,
            currentRadius,
            NoiseType.Distraction,

            /*
             * Source is left null on purpose. A thrown object is not
             * the player, and tagging it as such would let a listener
             * that filters its own noise wrongly ignore it.
             */

            null
        );

        clattersRemaining--;

        currentRadius *= clatterFalloff;

        nextClatterTime = Time.time + clatterInterval;
    }
}

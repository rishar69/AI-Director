using UnityEngine;

/// <summary>
/// Turns player movement into NoiseEvents.
///
/// This is the component that gives crouching a meaning. In Phase 1 the
/// xenomorph's HearingSense subscribes to the same bus, and the gap
/// between crouchRadius and sprintRadius becomes the core stealth
/// tension of the whole game.
///
/// Footsteps are emitted per distance travelled rather than per second,
/// so stride spacing stays consistent no matter what speed the player
/// is moving at.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerNoiseEmitter : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Footstep Loudness (metres)")]
    [SerializeField, Min(0f)]
    private float crouchNoiseRadius = 1.5f;

    [SerializeField, Min(0f)]
    private float walkNoiseRadius = 7f;

    [SerializeField, Min(0f)]
    private float sprintNoiseRadius = 16f;


    [Header("Stride Length (metres)")]
    [SerializeField, Min(0.05f)]
    private float crouchStride = 1.0f;

    [SerializeField, Min(0.05f)]
    private float walkStride = 1.4f;

    [SerializeField, Min(0.05f)]
    private float sprintStride = 1.9f;


    [Header("Landing")]
    [SerializeField]
    private bool emitLandingNoise = true;

    [SerializeField, Min(0f)]
    private float landingNoiseRadius = 12f;

    /// <summary>
    /// Impact speeds below this are treated as stepping off a kerb
    /// rather than landing, and stay silent.
    /// </summary>
    [SerializeField, Min(0f)]
    private float landingSpeedThreshold = 4f;


    [Header("Variation")]

    /*
     * Per-step random scaling, so the alien's hearing does not receive
     * a perfectly metronomic signal.
     */

    [SerializeField, Range(0f, 0.5f)]
    private float radiusJitter = 0.12f;


    [Header("Debug")]
    [SerializeField]
    private bool logSteps = false;

    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    private PlayerController player;

    private Concealment concealment;

    private float distanceSinceLastStep;

    private Vector3 lastPosition;

    private float lastStepRadius;

    private float lastStepTime = -999f;


    /// <summary>
    /// Audible radius of the most recent footstep. Debug readout only.
    /// </summary>
    public float LastStepRadius => lastStepRadius;

    /// <summary>
    /// Audible radius the player's next footstep will have, given their
    /// current stance. This is the number a HUD noise meter would show.
    /// </summary>
    public float CurrentNoiseRadius =>
        player != null && player.IsMoving
            ? RadiusForCurrentStance()
            : 0f;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        player = GetComponent<PlayerController>();

        concealment = GetComponent<Concealment>();

        lastPosition = transform.position;
    }


    private void OnEnable()
    {
        if (player != null)
        {
            player.OnLanded += HandleLanded;
        }

        lastPosition = transform.position;

        distanceSinceLastStep = 0f;
    }


    private void OnDisable()
    {
        if (player != null)
        {
            player.OnLanded -= HandleLanded;
        }
    }


    private void Update()
    {
        AccumulateStepDistance();
    }


    // =========================================================
    // FOOTSTEPS
    // =========================================================

    private void AccumulateStepDistance()
    {
        Vector3 current = transform.position;

        /*
         * Horizontal only. Riding a lift or sliding down a ramp should
         * not manufacture footsteps out of vertical motion.
         */

        Vector3 delta = current - lastPosition;

        delta.y = 0f;

        lastPosition = current;

        if (!player.IsGrounded || !player.IsMoving)
        {
            /*
             * Coming to a stop leaves the accumulator part-full so the
             * next step lands naturally rather than instantly.
             */

            return;
        }

        distanceSinceLastStep += delta.magnitude;

        float stride = StrideForCurrentStance();

        if (distanceSinceLastStep < stride)
            return;

        distanceSinceLastStep -= stride;

        EmitFootstep();
    }


    private void EmitFootstep()
    {
        float radius =
            RadiusForCurrentStance() *
            Random.Range(1f - radiusJitter, 1f + radiusJitter);

        if (radius <= 0f)
            return;

        lastStepRadius = radius;

        lastStepTime = Time.time;

        NoiseBus.Emit(
            transform.position,
            radius,
            NoiseType.Footstep,
            gameObject
        );

        if (logSteps)
        {
            Debug.Log(
                "[PlayerNoiseEmitter] Footstep " +
                player.CurrentStance +
                " radius " + radius.ToString("F1") + "m",
                this
            );
        }
    }


    private float RadiusForCurrentStance()
    {
        float radius;

        if (player.IsCrouching)
        {
            radius = crouchNoiseRadius;
        }
        else
        {
            radius = player.IsSprinting
                ? sprintNoiseRadius
                : walkNoiseRadius;
        }

        /*
         * Concealment muffles as well as hides - shifting your weight
         * inside a locker carries less than the same movement in the
         * open.
         */

        if (concealment != null)
        {
            radius *= concealment.Noise;
        }

        return radius;
    }


    private float StrideForCurrentStance()
    {
        if (player.IsCrouching)
            return crouchStride;

        return player.IsSprinting
            ? sprintStride
            : walkStride;
    }


    // =========================================================
    // LANDING
    // =========================================================

    private void HandleLanded(float impactSpeed)
    {
        if (!emitLandingNoise)
            return;

        if (impactSpeed < landingSpeedThreshold)
            return;

        /*
         * Scale with impact so a small drop is quieter than a big one,
         * but never quieter than a walking footstep.
         */

        float scale =
            Mathf.Clamp(
                impactSpeed / (landingSpeedThreshold * 2f),
                0.5f,
                1.5f
            );

        float radius = landingNoiseRadius * scale;

        lastStepRadius = radius;

        lastStepTime = Time.time;

        NoiseBus.Emit(
            transform.position,
            radius,
            NoiseType.Landing,
            gameObject
        );
    }


    // =========================================================
    // PUBLIC API
    // =========================================================

    /// <summary>
    /// Makes a one-off noise at the player's position.
    ///
    /// Used later by doors, dropped items, the motion tracker and the
    /// flamethrower. Anything that is not the player should call
    /// NoiseBus.Emit directly instead of routing through here.
    /// </summary>
    public void EmitOneShot(
        float radius,
        NoiseType type)
    {
        lastStepRadius = radius;

        lastStepTime = Time.time;

        NoiseBus.Emit(
            transform.position,
            radius,
            type,
            gameObject
        );
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        if (!Application.isPlaying)
            return;

        /*
         * A ring that flashes outward on each step, showing exactly how
         * far the sound just carried.
         */

        float age = Time.time - lastStepTime;

        if (age > 0.5f)
            return;

        float fade = 1f - (age / 0.5f);

        Gizmos.color =
            new Color(1f, 0.85f, 0.2f, fade * 0.8f);

        Gizmos.DrawWireSphere(
            transform.position,
            lastStepRadius
        );
    }
}

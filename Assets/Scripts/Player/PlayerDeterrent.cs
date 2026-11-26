using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The flamethrower. Drives the creature off; never kills it.
///
/// DIMINISHING RETURNS ARE THE ENTIRE DESIGN.
///
/// A deterrent that always works turns the game into a shooter with one
/// weapon. Every second of use builds resistance, so the second time
/// buys you less than the first and the fifth barely buys you anything.
/// Resistance only decays while you leave it alone, which means the
/// correct play is to save it - and being forced to decide whether this
/// is the moment worth spending it on is where the tension lives.
///
/// It is also loud. Driving it off announces exactly where you are.
/// </summary>
public class PlayerDeterrent : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Input")]
    /// <summary>Q, not F: F is the flashlight, and sharing it fired the flash every time the torch was toggled.</summary>
    [SerializeField]
    private Key fireKey = Key.Q;


    [Header("Cone")]
    [SerializeField, Min(1f)]
    private float range = 6f;

    [SerializeField, Range(5f, 180f)]
    private float coneAngle = 45f;

    [SerializeField]
    private LayerMask creatureMask = ~0;


    [Header("Effect")]
    /// <summary>
    /// Seconds of retreat granted per second of fully effective use.
    /// </summary>
    [SerializeField, Min(0.1f)]
    private float deterPerSecond = 6f;

    /// <summary>
    /// Refreshed continuously while firing, so the creature keeps
    /// running for this long after the last burst.
    /// </summary>
    [SerializeField, Min(0.5f)]
    private float minimumDeter = 3f;


    [Header("Diminishing Returns")]
    [SerializeField, Min(0.01f)]
    private float resistanceGainPerSecond = 0.16f;

    [SerializeField, Min(0.001f)]
    private float resistanceDecayPerSecond = 0.02f;

    /// <summary>
    /// Even a fully resistant creature flinches slightly, so the tool
    /// never becomes literally useless.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float minimumEffectiveness = 0.15f;


    [Header("Fuel")]
    [SerializeField, Min(0f)]
    private float maxFuel = 10f;

    [SerializeField, Min(0f)]
    private float refuelPerSecond = 0.35f;

    [SerializeField, Min(0f)]
    private float refuelDelay = 4f;


    [Header("Noise")]
    [SerializeField, Min(0f)]
    private float noiseRadius = 26f;

    [SerializeField, Min(0.05f)]
    private float noiseInterval = 0.4f;


    [Header("Threat")]
    /// <summary>
    /// Whether the creature's behaviour tree treats this as a weapon
    /// worth respecting - ALIEN_THREAT_AWARE_OF in the exports.
    /// </summary>
    [SerializeField]
    private bool alienIsThreatAwareOf = true;


    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    private Transform origin;

    private PlayerHiding hiding;

    private float resistance;

    private float fuel;

    private float lastFiredTime = -999f;

    private float nextNoiseTime;

    private readonly Collider[] overlapResults = new Collider[16];

    private XenoBlackboard board;


    public bool IsFiring { get; private set; }

    public float Fuel => fuel;

    public float FuelFraction =>
        maxFuel > 0f ? fuel / maxFuel : 0f;

    /// <summary>
    /// How well it is currently working, 1 down to
    /// minimumEffectiveness.
    /// </summary>
    public float Effectiveness =>
        Mathf.Max(minimumEffectiveness, 1f - resistance);

    public float Resistance => resistance;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        fuel = maxFuel;

        hiding = GetComponent<PlayerHiding>();

        Camera camera = GetComponentInChildren<Camera>();

        origin = camera != null ? camera.transform : transform;
    }


    private void Update()
    {
        PublishWeaponState();

        Keyboard keyboard = Keyboard.current;

        bool wantsFire =
            keyboard != null &&
            keyboard[fireKey].isPressed &&
            fuel > 0f &&
            (hiding == null || !hiding.IsHiding);

        IsFiring = wantsFire;

        if (wantsFire)
        {
            Fire(Time.deltaTime);
        }
        else
        {
            Recover(Time.deltaTime);
        }
    }


    // =========================================================
    // FIRING
    // =========================================================

    /// <summary>
    /// Tells the creature what the player is holding and where it is
    /// pointed.
    ///
    /// The threat-aware branch gates on all three of these: that the
    /// weapon is something the alien has learned to respect, that it
    /// still has fuel, and that it is currently aimed at the creature.
    /// An empty flamethrower stops being frightening, which is a nice
    /// consequence of reading the ammo rather than the weapon alone.
    /// </summary>
    private void PublishWeaponState()
    {
        if (board == null)
        {
            board = FindFirstObjectByType<XenoBlackboard>();

            if (board == null)
                return;
        }

        Transform from = origin != null ? origin : transform;

        board.SetTargetWeapon(
            alienIsThreatAwareOf,
            fuel > 0f,
            from.forward
        );
    }


    private void Fire(float deltaTime)
    {
        lastFiredTime = Time.time;

        fuel = Mathf.Max(0f, fuel - deltaTime);

        EmitNoise();

        int count =
            Physics.OverlapSphereNonAlloc(
                origin.position,
                range,
                overlapResults,
                creatureMask,
                QueryTriggerInteraction.Ignore
            );

        bool hitSomething = false;

        for (int i = 0; i < count; i++)
        {
            Collider collider = overlapResults[i];

            if (collider == null)
                continue;

            if (!IsInCone(collider.bounds.center))
                continue;

            XenoBrain brain =
                collider.GetComponentInParent<XenoBrain>();

            if (brain == null)
                continue;

            /*
             * Refreshed every frame rather than applied as a lump, so
             * releasing the trigger lets the effect lapse naturally.
             */

            float duration =
                Mathf.Max(
                    minimumDeter,
                    deterPerSecond * Effectiveness
                );

            brain.Deter(duration);

            /*
             * Burning the creature is a stimulus, not just an effect.
             * The behaviour tree gates whole branches on DAMAGED and
             * AFFECTED_BY_FLAME_THROWER, so the fire has to register on
             * those channels or those branches can never fire.
             */

            XenoSenses senses =
                collider.GetComponentInParent<XenoSenses>();

            if (senses != null)
            {
                senses.ReportDamage(
                    Effectiveness * deltaTime * 4f,
                    fromFlameThrower: true
                );
            }

            hitSomething = true;
        }

        if (hitSomething)
        {
            /*
             * Resistance only builds while it is actually being burned.
             * Firing at empty air costs fuel but teaches it nothing.
             */

            resistance =
                Mathf.Clamp01(
                    resistance +
                    resistanceGainPerSecond * deltaTime
                );
        }
    }


    private bool IsInCone(Vector3 point)
    {
        Vector3 delta = point - origin.position;

        if (delta.sqrMagnitude > range * range)
            return false;

        return Vector3.Angle(origin.forward, delta) <=
               coneAngle * 0.5f;
    }


    private void EmitNoise()
    {
        if (Time.time < nextNoiseTime)
            return;

        nextNoiseTime = Time.time + noiseInterval;

        NoiseBus.Emit(
            transform.position,
            noiseRadius,
            NoiseType.Device,
            gameObject
        );
    }


    // =========================================================
    // RECOVERY
    // =========================================================

    private void Recover(float deltaTime)
    {
        resistance =
            Mathf.Clamp01(
                resistance -
                resistanceDecayPerSecond * deltaTime
            );

        if (Time.time - lastFiredTime < refuelDelay)
            return;

        fuel =
            Mathf.Min(
                maxFuel,
                fuel + refuelPerSecond * deltaTime
            );
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Transform from = origin != null ? origin : transform;

        Gizmos.color =
            IsFiring
                ? new Color(1f, 0.45f, 0.1f, 0.9f)
                : new Color(1f, 0.7f, 0.3f, 0.35f);

        float half = coneAngle * 0.5f;

        for (int i = -1; i <= 1; i += 2)
        {
            Vector3 edge =
                Quaternion.AngleAxis(half * i, Vector3.up) *
                from.forward;

            Gizmos.DrawRay(from.position, edge * range);
        }

        Gizmos.DrawRay(from.position, from.forward * range);
    }
}

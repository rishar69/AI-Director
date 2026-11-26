using UnityEngine;

/// <summary>
/// Everything the player hears from themselves and their tools.
///
///   - Footsteps, one per stride, on the same step the creature hears
///     (PlayerNoiseEmitter raises it), so being loud sounds loud.
///   - A heartbeat that climbs as the creature closes in. The player is
///     allowed to know where it is; the creature is never told anything
///     from here.
///   - Held breathing inside a hiding place.
///   - The pocket watch ticking faster the closer the contact.
///   - The camera flash, the death sting, and the museum's hum.
///
/// Purely presentation - every gameplay noise already goes through
/// NoiseBus in the components that make it.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerAudio : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Footsteps")]
    [SerializeField, Range(0f, 1f)]
    private float walkVolume = 0.5f;

    [SerializeField, Range(0f, 1f)]
    private float sprintVolume = 0.8f;

    [SerializeField, Range(0f, 1f)]
    private float crouchVolume = 0.25f;


    [Header("Heartbeat")]
    /// <summary>Within this distance the heart starts to race.</summary>
    [SerializeField, Min(1f)]
    private float fearDistance = 18f;

    [SerializeField, Range(0f, 1f)]
    private float heartbeatMaxVolume = 0.85f;


    [Header("Pocket Watch")]
    /// <summary>Contact distance at which the ticking is slowest.</summary>
    [SerializeField, Min(1f)]
    private float watchFarDistance = 25f;

    [SerializeField, Min(0.05f)]
    private float watchSlowInterval = 1.0f;

    [SerializeField, Min(0.05f)]
    private float watchFastInterval = 0.18f;


    [Header("Ambience")]
    [SerializeField, Range(0f, 1f)]
    private float ambienceVolume = 0.35f;

    [SerializeField, Range(0f, 1f)]
    private float hidingBreathVolume = 0.7f;


    // =========================================================
    // STATE
    // =========================================================

    private PlayerController player;

    private PlayerNoiseEmitter steps;

    private PlayerHiding hiding;

    private PlayerDeathSequence death;

    private PlayerDeterrent deterrent;

    private MotionTracker watch;

    private PerceptionSystem creature;

    private AudioSource heartbeat;

    private AudioSource breath;

    private AudioSource ambience;

    private bool wasDying;

    private bool wasFiring;

    private bool wasHiding;

    private float nextWatchTick;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        steps = GetComponent<PlayerNoiseEmitter>();
        hiding = GetComponent<PlayerHiding>();
        death = GetComponent<PlayerDeathSequence>();
        deterrent = GetComponent<PlayerDeterrent>();
        watch = GetComponent<MotionTracker>();
    }


    private void Start()
    {
        heartbeat = GameSound.Loop(gameObject, "Heartbeat", GameSound.Player, 0f);
        breath = GameSound.Loop(gameObject, "HidingBreath", GameSound.Player, 0f);
        ambience = GameSound.Loop(gameObject, "AmbienceHum", GameSound.Ambience, 0f);

        creature = FindFirstObjectByType<PerceptionSystem>();
    }


    private void OnEnable()
    {
        if (steps != null)
        {
            steps.OnFootstep += HandleFootstep;
        }
    }


    private void OnDisable()
    {
        if (steps != null)
        {
            steps.OnFootstep -= HandleFootstep;
        }
    }


    private void Update()
    {
        TickHeartbeat();
        TickHiding();
        TickWatch();
        TickEvents();

        if (ambience != null)
        {
            ambience.volume = Mathf.MoveTowards(ambience.volume, ambienceVolume, Time.deltaTime * 0.2f);
        }
    }


    // =========================================================
    // FOOTSTEPS
    // =========================================================

    private void HandleFootstep()
    {
        bool sprinting = player.IsSprinting && !player.IsCrouching;

        AudioClip clip = GameSound.Any(GameSound.Set(sprinting ? "Steps/Run" : "Steps/Walk"));

        float volume = player.IsCrouching ? crouchVolume : sprinting ? sprintVolume : walkVolume;

        float pitch = Random.Range(0.94f, 1.06f) * (player.IsCrouching ? 1.05f : 1f);

        AudioSource source = GameSound.Play(clip, transform.position, volume, GameSound.Player, pitch, 10f);

        if (source != null)
        {
            source.spatialBlend = 0f;
        }
    }


    // =========================================================
    // FEAR
    // =========================================================

    /// <summary>
    /// 0 when the creature is far or unaware, 1 when it is on top of
    /// the player. Hiding adds a little: holding still with it nearby is
    /// the most frightening moment the game has.
    /// </summary>
    private float Fear()
    {
        if (creature == null)
            return 0f;

        float distance = Vector3.Distance(transform.position, creature.transform.position);

        float near = 1f - Mathf.Clamp01(distance / fearDistance);

        float hunt = creature.Level >= AwarenessLevel.Alerted && distance < fearDistance * 1.6f
            ? 0.45f
            : 0f;

        float fear = Mathf.Max(near, hunt);

        if (hiding != null && hiding.IsHiding)
        {
            fear = Mathf.Clamp01(fear + 0.2f * near);
        }

        return fear;
    }


    private void TickHeartbeat()
    {
        if (heartbeat == null)
            return;

        float fear = (death != null && death.IsDying) ? 0f : Fear();

        heartbeat.volume = Mathf.MoveTowards(heartbeat.volume, fear * heartbeatMaxVolume, Time.deltaTime * 0.8f);

        heartbeat.pitch = Mathf.MoveTowards(heartbeat.pitch, 0.9f + fear * 0.55f, Time.deltaTime * 0.3f);
    }


    private void TickHiding()
    {
        bool hidden = hiding != null && hiding.IsHiding;

        if (breath != null)
        {
            breath.volume = Mathf.MoveTowards(breath.volume, hidden ? hidingBreathVolume : 0f, Time.deltaTime * 1.5f);
        }

        if (hidden != wasHiding)
        {
            wasHiding = hidden;

            /* Muffles the world inside a hiding place, if the mixer has the snapshot. */

            GameSound.Snapshot(hidden ? "Hiding" : "Normal", 0.4f);
        }
    }


    // =========================================================
    // TOOLS & EVENTS
    // =========================================================

    private void TickWatch()
    {
        if (watch == null || !watch.IsActive || Time.time < nextWatchTick)
            return;

        float distance = watch.NearestDistance;

        float t = distance < 0f ? 1f : Mathf.InverseLerp(3f, watchFarDistance, distance);

        nextWatchTick = Time.time + Mathf.Lerp(watchFastInterval, watchSlowInterval, t);

        AudioSource source = GameSound.Play(GameSound.Get("WatchTick"), transform.position, 0.45f, GameSound.Player);

        if (source != null)
        {
            source.spatialBlend = 0f;
        }
    }


    private void TickEvents()
    {
        bool dying = death != null && death.IsDying;

        if (dying && !wasDying)
        {
            GameSound.Play2D(GameSound.Get("DeathSting"), 1f, GameSound.UI);
        }

        wasDying = dying;

        bool firing = deterrent != null && deterrent.IsFiring;

        if (firing && !wasFiring)
        {
            GameSound.Play(GameSound.Get("CameraFlash"), transform.position + transform.forward * 0.5f,
                0.9f, GameSound.Player, 1f, 20f);
        }

        wasFiring = firing;
    }
}

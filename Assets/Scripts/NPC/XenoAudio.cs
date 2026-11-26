using UnityEngine;

/// <summary>
/// Everything the creature sounds like.
///
/// Audio is most of the fear in this genre. The creature is off screen
/// the overwhelming majority of the time, so for long stretches the
/// sound IS the AI as far as the player is concerned - steps somewhere
/// above, a growl through a wall, the scream that says the hunt is on.
///
/// Every cue follows what the creature knows, read from its
/// PerceptionSystem, never from the player:
///
///   Unaware     a faint growl close up; a distant roar now and then
///   Suspicious  sniffing, growl louder
///   Alerted     growl louder still, sniffing whenever it stops
///   Confirmed   the scream - it has you - and the growl at full
///   Attack      the shriek
///
/// Clips come from GameSound by name. Any that are missing are simply
/// skipped.
/// </summary>
[RequireComponent(typeof(XenoLocomotion))]
public class XenoAudio : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Source")]
    [SerializeField]
    private AudioSource source;

    /// <summary>
    /// How far its vocals carry. A creature you cannot locate by ear is
    /// a creature the player cannot play around.
    /// </summary>
    [SerializeField, Min(1f)]
    private float maxDistance = 45f;


    [Header("Footsteps")]
    /// <summary>
    /// Optional dedicated creature steps. Empty uses the player's steps
    /// pitched down and heavier - a stand-in until creature steps exist.
    /// </summary>
    [SerializeField]
    private AudioClip[] footsteps;

    [SerializeField, Range(0.3f, 1.5f)]
    private float standInPitch = 0.62f;

    /// <summary>Metres between footfalls upright.</summary>
    [SerializeField, Min(0.1f)]
    private float walkStride = 1.5f;

    /// <summary>Metres between footfalls on all fours - a gallop beats faster.</summary>
    [SerializeField, Min(0.1f)]
    private float runStride = 1.0f;

    /// <summary>Speed at which it counts as running on all fours.</summary>
    [SerializeField, Min(0.1f)]
    private float runSpeed = 3.5f;

    [SerializeField, Range(0f, 1f)]
    private float footstepVolume = 0.75f;

    [SerializeField, Min(1f)]
    private float footstepDistance = 28f;


    [Header("Growl (looping, close range)")]
    [SerializeField, Range(0f, 1f)]
    private float growlUnaware = 0.15f;

    [SerializeField, Range(0f, 1f)]
    private float growlSuspicious = 0.45f;

    [SerializeField, Range(0f, 1f)]
    private float growlAlerted = 0.7f;

    [SerializeField, Range(0f, 1f)]
    private float growlConfirmed = 0.95f;

    /// <summary>Close range on purpose: the growl means "it is near".</summary>
    [SerializeField, Min(1f)]
    private float growlDistance = 16f;


    [Header("Vocals")]
    [SerializeField, Min(1f)]
    private float roarIntervalMin = 40f;

    [SerializeField, Min(1f)]
    private float roarIntervalMax = 80f;

    [SerializeField, Min(1f)]
    private float sniffIntervalMin = 7f;

    [SerializeField, Min(1f)]
    private float sniffIntervalMax = 12f;

    /// <summary>Stops a flickering lock-on from screaming twice.</summary>
    [SerializeField, Min(0f)]
    private float screamCooldown = 12f;


    // =========================================================
    // STATE
    // =========================================================

    private XenoLocomotion locomotion;

    private PerceptionSystem perception;

    private XenoBehaviourTree tree;

    private AudioSource growl;

    private Vector3 lastPosition;

    private float distanceSinceStep;

    private float nextRoar;

    private float nextSniff;

    private float lastScream = -999f;


    private AwarenessLevel Level =>
        perception != null ? perception.Level : AwarenessLevel.Unaware;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        locomotion = GetComponent<XenoLocomotion>();

        perception = GetComponent<PerceptionSystem>();

        tree = GetComponent<XenoBehaviourTree>();

        if (source == null)
        {
            source = GetComponent<AudioSource>();
        }

        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
        }

        GameSound.Configure(source, GameSound.Creature, 1f, maxDistance);

        lastPosition = transform.position;

        nextRoar = Time.time + Random.Range(roarIntervalMin * 0.5f, roarIntervalMax);
    }


    private void Start()
    {
        growl = GameSound.Loop(gameObject, "CreatureGrowlLoop", GameSound.Creature, 1f, growlDistance);

        if (growl != null)
        {
            growl.transform.localPosition = Vector3.up * 1.3f;
        }
    }


    private void OnEnable()
    {
        if (perception != null)
        {
            perception.OnLevelChanged += HandleLevelChanged;
        }

        if (tree != null)
        {
            tree.OnAttack += HandleAttack;
        }
    }


    private void OnDisable()
    {
        if (perception != null)
        {
            perception.OnLevelChanged -= HandleLevelChanged;
        }

        if (tree != null)
        {
            tree.OnAttack -= HandleAttack;
        }
    }


    private void Update()
    {
        TickFootsteps();

        TickGrowl();

        TickRoar();

        TickSniff();
    }


    // =========================================================
    // FOOTSTEPS
    // =========================================================

    private void TickFootsteps()
    {
        Vector3 delta = transform.position - lastPosition;

        delta.y = 0f;

        lastPosition = transform.position;

        if (!locomotion.IsMoving)
            return;

        bool running = locomotion.CurrentSpeed >= runSpeed;

        distanceSinceStep += delta.magnitude;

        float stride = running ? runStride : walkStride;

        if (distanceSinceStep < stride)
            return;

        distanceSinceStep -= stride;

        bool standIn = footsteps == null || footsteps.Length == 0;

        AudioClip clip = standIn
            ? GameSound.Any(GameSound.Set(running ? "Steps/Run" : "Steps/Walk"))
            : footsteps[Random.Range(0, footsteps.Length)];

        float pitch = (standIn ? standInPitch : 1f) * Random.Range(0.93f, 1.07f);

        /* Quieter when creeping, so a prowl is genuinely hard to hear. */

        float volume = footstepVolume *
                       Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(1f, runSpeed, locomotion.CurrentSpeed));

        GameSound.Play(clip, transform.position, volume, GameSound.Creature, pitch, footstepDistance);
    }


    // =========================================================
    // GROWL
    // =========================================================

    private void TickGrowl()
    {
        if (growl == null)
            return;

        float target;

        switch (Level)
        {
            case AwarenessLevel.Confirmed: target = growlConfirmed; break;
            case AwarenessLevel.Alerted: target = growlAlerted; break;
            case AwarenessLevel.Suspicious: target = growlSuspicious; break;
            default: target = growlUnaware; break;
        }

        growl.volume = Mathf.MoveTowards(growl.volume, target, Time.deltaTime * 0.6f);
    }


    // =========================================================
    // VOCALS
    // =========================================================

    private void HandleLevelChanged(AwarenessLevel previous, AwarenessLevel next)
    {
        if (next == AwarenessLevel.Confirmed && previous < AwarenessLevel.Confirmed)
        {
            if (Time.time - lastScream > screamCooldown)
            {
                lastScream = Time.time;

                Vocal("CreatureScream", 1f, 55f);
            }

            return;
        }

        if (next == AwarenessLevel.Suspicious && previous == AwarenessLevel.Unaware)
        {
            Sniff();
        }
    }


    private void HandleAttack(Vector3 position, float reach)
    {
        Vocal("CreatureShriek", 1f, maxDistance);
    }


    /// <summary>
    /// A distant roar while it has no idea where you are - a reminder
    /// that it exists, and roughly where, without telling you anything
    /// about its intent.
    /// </summary>
    private void TickRoar()
    {
        if (Time.time < nextRoar)
            return;

        nextRoar = Time.time + Random.Range(roarIntervalMin, roarIntervalMax);

        if (Level == AwarenessLevel.Unaware)
        {
            Vocal("CreatureRoar", 0.85f, 60f);
        }
    }


    /// <summary>Sniffs whenever it stops to search.</summary>
    private void TickSniff()
    {
        bool searching =
            (Level == AwarenessLevel.Suspicious || Level == AwarenessLevel.Alerted) &&
            !locomotion.IsMoving;

        if (!searching || Time.time < nextSniff)
            return;

        Sniff();
    }


    private void Sniff()
    {
        nextSniff = Time.time + Random.Range(sniffIntervalMin, sniffIntervalMax);

        Vocal(Random.value < 0.5f ? "CreatureSniff_1" : "CreatureSniff_2", 0.8f, 20f);
    }


    private void Vocal(string name, float volume, float distance)
    {
        AudioClip clip = GameSound.Get(name);

        if (clip == null)
            return;

        source.maxDistance = distance;

        source.pitch = Random.Range(0.95f, 1.05f);

        /*
         * PlayOneShot rather than Play, so a footfall does not cut off
         * a scream that is still ringing.
         */

        source.PlayOneShot(clip, volume);
    }
}

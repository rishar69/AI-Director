using UnityEngine;

/// <summary>
/// Everything the creature sounds like.
///
/// Audio is most of the fear in this genre. The creature is off screen
/// the overwhelming majority of the time, so for long stretches the
/// sound IS the AI as far as the player is concerned - footfalls
/// somewhere above, a duct clanging two rooms away, the hiss that tells
/// you the stalking is over.
///
/// Every clip field is optional and the component is silent with none
/// assigned. The project currently ships no audio, so this is the
/// plumbing waiting for it - identical in spirit to how
/// XenoAnimatorDriver waited for a mesh.
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
    /// Fully 3D by default. A creature you cannot locate by ear is a
    /// creature the player cannot play around.
    /// </summary>
    [SerializeField, Range(0f, 1f)]
    private float spatialBlend = 1f;

    [SerializeField, Min(1f)]
    private float maxDistance = 45f;


    [Header("Footsteps")]
    [SerializeField]
    private AudioClip[] footsteps;

    /// <summary>Metres between footfalls at a walk.</summary>
    [SerializeField, Min(0.1f)]
    private float strideLength = 1.6f;

    [SerializeField, Range(0f, 1f)]
    private float footstepVolume = 0.7f;


    [Header("Vocalisations")]
    [SerializeField]
    private AudioClip[] idleVocals;

    [SerializeField]
    private AudioClip[] stalkVocals;

    [SerializeField]
    private AudioClip[] chaseVocals;

    [SerializeField]
    private AudioClip[] attackVocals;

    [SerializeField, Min(1f)]
    private float idleVocalIntervalMin = 12f;

    [SerializeField, Min(1f)]
    private float idleVocalIntervalMax = 30f;


    [Header("Vents")]
    [SerializeField]
    private AudioClip[] ventClanks;

    [SerializeField, Range(0f, 1f)]
    private float ventVolume = 0.9f;


    // =========================================================
    // STATE
    // =========================================================

    private XenoLocomotion locomotion;

    private XenoBrain brain;

    private Vector3 lastPosition;

    private float distanceSinceStep;

    private float nextIdleVocalTime;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        locomotion = GetComponent<XenoLocomotion>();

        brain = GetComponent<XenoBrain>();

        EnsureSource();

        lastPosition = transform.position;

        ScheduleIdleVocal();
    }


    private void OnEnable()
    {
        if (brain != null)
        {
            brain.OnStateChanged += HandleStateChanged;
        }

        if (locomotion != null)
        {
            locomotion.OnLinkTraversalStarted += HandleLinkTraversal;
        }
    }


    private void OnDisable()
    {
        if (brain != null)
        {
            brain.OnStateChanged -= HandleStateChanged;
        }

        if (locomotion != null)
        {
            locomotion.OnLinkTraversalStarted -= HandleLinkTraversal;
        }
    }


    private void Update()
    {
        TickFootsteps();

        TickIdleVocals();
    }


    private void EnsureSource()
    {
        if (source == null)
        {
            source = GetComponent<AudioSource>();
        }

        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
        }

        source.playOnAwake = false;

        source.spatialBlend = spatialBlend;

        source.maxDistance = maxDistance;

        source.rolloffMode = AudioRolloffMode.Linear;
    }


    // =========================================================
    // FOOTSTEPS
    // =========================================================

    private void TickFootsteps()
    {
        Vector3 delta = transform.position - lastPosition;

        delta.y = 0f;

        lastPosition = transform.position;

        if (locomotion.IsTraversingLink)
            return;

        if (!locomotion.IsMoving)
            return;

        distanceSinceStep += delta.magnitude;

        if (distanceSinceStep < strideLength)
            return;

        distanceSinceStep -= strideLength;

        PlayOneOf(footsteps, footstepVolume);
    }


    // =========================================================
    // VOCALISATIONS
    // =========================================================

    private void HandleStateChanged(
        XenoStateId previous,
        XenoStateId next)
    {
        switch (next)
        {
            case XenoStateId.Stalk:
                PlayOneOf(stalkVocals, 1f);
                break;

            case XenoStateId.Chase:
                PlayOneOf(chaseVocals, 1f);
                break;

            case XenoStateId.Attack:
                PlayOneOf(attackVocals, 1f);
                break;
        }
    }


    private void TickIdleVocals()
    {
        if (Time.time < nextIdleVocalTime)
            return;

        ScheduleIdleVocal();

        /*
         * Only while it has no idea. An idle rumble during a chase
         * would undercut the chase vocal.
         */

        if (brain != null &&
            brain.CurrentState != XenoStateId.Patrol)
        {
            return;
        }

        PlayOneOf(idleVocals, 0.8f);
    }


    private void ScheduleIdleVocal()
    {
        nextIdleVocalTime =
            Time.time +
            Random.Range(
                idleVocalIntervalMin,
                Mathf.Max(idleVocalIntervalMin, idleVocalIntervalMax)
            );
    }


    // =========================================================
    // VENTS
    // =========================================================

    private void HandleLinkTraversal(
        Vector3 from,
        Vector3 to)
    {
        PlayOneOf(ventClanks, ventVolume);
    }


    // =========================================================
    // PLAYBACK
    // =========================================================

    private void PlayOneOf(
        AudioClip[] clips,
        float volume)
    {
        if (source == null || clips == null || clips.Length == 0)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];

        if (clip == null)
            return;

        /*
         * PlayOneShot rather than Play, so a footfall does not cut off
         * a hiss that is still ringing.
         */

        source.PlayOneShot(clip, volume);
    }
}

using UnityEngine;

/// <summary>
/// A grandfather clock that has to be wound - and then counted.
///
/// When winding finishes the clock strikes its number, one chime at a
/// time, each a noise the caretaker hears. The number of strikes is one
/// digit of the strongroom's combination, so the player has to stay and
/// listen to the whole count.
///
/// Winding is the game's central risk: six seconds standing still,
/// ratcheting a loud mechanism, in a room the caretaker patrols. Every
/// ratchet is a noise on the bus; the chime at the end is a much bigger
/// one. Progress survives letting go, so backing off mid-wind is a
/// choice rather than a failure.
/// </summary>
public class ClockObjective : Interactable
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private GalleryKey key;

    [Header("Parts")]
    [SerializeField]
    private Transform hourHand;

    [SerializeField]
    private Transform minuteHand;

    [SerializeField]
    private Transform pendulum;

    /// <summary>Lights the face: dim red when stopped, warm when running.</summary>
    [SerializeField]
    private Light faceLight;


    [Header("Noise")]
    /// <summary>How far each ratchet of the key carries.</summary>
    [SerializeField, Min(0f)]
    private float windNoiseRadius = 14f;

    [SerializeField, Min(0.1f)]
    private float windNoiseInterval = 1.3f;

    /// <summary>The chime - heard across most of the museum.</summary>
    [SerializeField, Min(0f)]
    private float chimeNoiseRadius = 34f;


    private float nextRatchet;

    private float lastWound = -1f;

    /// <summary>The winding ratchet, playing only while E is held.</summary>
    private AudioSource windSource;

    private float swing;

    /// <summary>Counting out its strikes right now.</summary>
    private bool striking;

    [SerializeField, Min(0.3f)]
    private float strikeInterval = 1.1f;


    public GalleryKey Key => key;

    public bool Wound => ObjectiveManager.Instance != null &&
                         ObjectiveManager.Instance.IsClockWound(key);

    public override string Label => ObjectiveManager.ClockName(key);


    public void Setup(
        GalleryKey galleryKey,
        Transform hour,
        Transform minute,
        Transform bob,
        Light light)
    {
        key = galleryKey;
        hourHand = hour;
        minuteHand = minute;
        pendulum = bob;
        faceLight = light;
    }


    // =========================================================
    // PROMPT
    // =========================================================

    public override InteractPrompt GetPrompt()
    {
        ObjectiveManager objectives = ObjectiveManager.Instance;

        if (objectives == null || Wound || striking)
            return InteractPrompt.None;

        return new InteractPrompt
        {
            State = InteractState.Available,
            Action = "Wind the " + Label,
            Detail = HoldProgress > 0f
                ? "Wound " + Mathf.RoundToInt(HoldProgress * 100f) + "% - keep going"
                : "Loud. When it is wound, count the strikes.",
            Hold = true,
            Loud = true
        };
    }


    // =========================================================
    // WINDING
    // =========================================================

    protected override void OnHoldTick(GameObject who)
    {
        lastWound = Time.time;

        if (Time.time < nextRatchet)
            return;

        nextRatchet = Time.time + windNoiseInterval;

        /* Without the recording, fall back to a tick per ratchet. */

        if (GameSound.Get("ClockWinding") == null)
        {
            GameArt.Play(GameArt.Tick, FocusPoint, 0.9f);
        }

        NoiseBus.Emit(FocusPoint, windNoiseRadius, NoiseType.Object, gameObject);
    }


    protected override void OnHoldComplete(GameObject who)
    {
        StartCoroutine(Strike());
    }


    /// <summary>Strikes the hour, one chime at a time, then records the digit.</summary>
    private System.Collections.IEnumerator Strike()
    {
        striking = true;

        ObjectiveManager objectives = ObjectiveManager.Instance;

        int count = objectives != null ? objectives.Digit(key) : 1;

        objectives?.Say("The " + Label + " begins to strike - count.", MessageTone.Info);

        for (int i = 0; i < count; i++)
        {
            GameArt.Play(GameArt.Chime, FocusPoint, 1f);

            NoiseBus.Emit(FocusPoint, chimeNoiseRadius, NoiseType.Object, gameObject);

            if (faceLight != null)
            {
                faceLight.intensity = 3.5f;
            }

            yield return new WaitForSeconds(strikeInterval);
        }

        striking = false;

        objectives?.MarkClockWound(key);
    }


    /// <summary>
    /// The ratchet runs exactly as long as the key is turning, and stops
    /// the instant E is released - the player hears themselves stop.
    /// </summary>
    private void TickWindingSound(bool winding)
    {
        if (windSource == null)
        {
            if (!winding)
                return;

            windSource = GameSound.Loop(gameObject, "ClockWinding", GameSound.World, 0.85f, 25f);

            if (windSource == null)
                return;

            windSource.transform.position = FocusPoint;
        }

        windSource.volume = Mathf.MoveTowards(
            windSource.volume, winding ? 0.9f : 0f, Time.deltaTime * (winding ? 12f : 6f));
    }


    // =========================================================
    // PRESENTATION
    // =========================================================

    private void Update()
    {
        bool wound = Wound;

        bool winding = !wound && Time.time - lastWound < 0.1f;

        TickWindingSound(winding);

        /* The hands race while being wound, then keep real time. */

        if (minuteHand != null)
        {
            float rate = wound ? 6f : (winding ? 360f : 0f);

            minuteHand.localRotation *= Quaternion.Euler(0f, 0f, -rate * Time.deltaTime);
        }

        if (hourHand != null)
        {
            float rate = wound ? 0.5f : (winding ? 30f : 0f);

            hourHand.localRotation *= Quaternion.Euler(0f, 0f, -rate * Time.deltaTime);
        }

        if (pendulum != null && wound)
        {
            swing += Time.deltaTime * Mathf.PI;

            pendulum.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(swing) * 12f);
        }

        if (faceLight != null)
        {
            faceLight.color = wound
                ? new Color(1f, 0.85f, 0.55f)
                : new Color(0.9f, 0.35f, 0.25f);

            faceLight.intensity = wound ? 1.6f : 0.8f;
        }
    }
}

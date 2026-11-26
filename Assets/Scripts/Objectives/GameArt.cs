using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural art for the objective layer: icons, the UI font and a
/// handful of synthesized sounds.
///
/// Everything is generated at runtime so the objective system has no
/// asset dependencies to lose or forget to import. When real art
/// arrives, swapping it in is a change to this file only.
/// </summary>
public static class GameArt
{
    // =========================================================
    // PALETTE
    // =========================================================

    /// <summary>Brass - things you can do.</summary>
    public static readonly Color Brass = new Color(0.90f, 0.72f, 0.38f);

    /// <summary>Parchment - body text.</summary>
    public static readonly Color Parchment = new Color(0.91f, 0.86f, 0.75f);

    /// <summary>Faded ink - finished or not-yet-relevant.</summary>
    public static readonly Color Faded = new Color(0.55f, 0.52f, 0.47f);

    /// <summary>Oxblood - things you cannot do yet.</summary>
    public static readonly Color Locked = new Color(0.86f, 0.33f, 0.28f);

    /// <summary>Pale blue - hiding places.</summary>
    public static readonly Color Hide = new Color(0.58f, 0.78f, 0.95f);

    /// <summary>Near-black panel backing.</summary>
    public static readonly Color Panel = new Color(0.06f, 0.05f, 0.045f, 0.82f);


    // =========================================================
    // FONT
    // =========================================================

    private static Font font;

    /// <summary>
    /// A serif OS font for a museum after dark. Segoe UI Symbol is last
    /// in the list so glyphs Georgia lacks (ticks, arrows) fall back to
    /// it instead of rendering as boxes.
    /// </summary>
    public static Font Font
    {
        get
        {
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(
                    new[]
                    {
                        "Georgia", "Palatino Linotype", "Book Antiqua",
                        "Times New Roman", "Segoe UI Symbol"
                    },
                    32);
            }

            return font;
        }
    }


    // =========================================================
    // ICONS
    // =========================================================

    private static readonly Dictionary<string, Sprite> sprites =
        new Dictionary<string, Sprite>();


    public static Sprite White => Get("white", 4, (x, y) => 1f);

    /// <summary>Soft round dot - the crosshair.</summary>
    public static Sprite Dot => Get("dot", 64, (x, y) =>
        Smooth(0.5f - Len(x, y), 0.06f));

    /// <summary>Thin ring - the hold progress track.</summary>
    public static Sprite Ring => Get("ring", 128, (x, y) =>
    {
        float d = Len(x, y);

        return Smooth(0.47f - d, 0.02f) * Smooth(d - 0.39f, 0.02f);
    });

    /// <summary>Diamond with a hollow centre - "do this".</summary>
    public static Sprite Diamond => Get("diamond", 64, (x, y) =>
    {
        float d = Mathf.Abs(x) + Mathf.Abs(y);

        return Smooth(0.46f - d, 0.03f) * (0.35f + 0.65f * Smooth(d - 0.26f, 0.03f));
    });

    /// <summary>A padlock - "not yet".</summary>
    public static Sprite Lock => Get("lock", 64, (x, y) =>
    {
        /* Body. */

        float body =
            Smooth(0.30f - Mathf.Abs(x), 0.02f) *
            Smooth(0.22f - Mathf.Abs(y + 0.14f), 0.02f);

        /* Shackle: a ring clipped to its upper half. */

        float r = Len(x, y - 0.10f);

        float shackle =
            Smooth(0.21f - r, 0.02f) * Smooth(r - 0.13f, 0.02f) *
            Smooth(y - 0.08f, 0.02f);

        /* Keyhole punched through the body. */

        float hole = Smooth(0.05f - Len(x, y + 0.12f), 0.015f);

        return Mathf.Clamp01(Mathf.Max(body, shackle) - hole);
    });

    /// <summary>An eye - "hide here".</summary>
    public static Sprite Eye => Get("eye", 64, (x, y) =>
    {
        /* Almond: the intersection of two offset circles. */

        float upper = Smooth(0.42f - Len(x, y + 0.26f), 0.02f);
        float lower = Smooth(0.42f - Len(x, y - 0.26f), 0.02f);

        float almond = upper * lower;

        float inner =
            Smooth(0.35f - Len(x * 1.05f, y + 0.26f), 0.02f) *
            Smooth(0.35f - Len(x * 1.05f, y - 0.26f), 0.02f);

        float pupil = Smooth(0.10f - Len(x, y), 0.02f);

        return Mathf.Clamp01(almond - inner + pupil);
    });

    /// <summary>A soft vertical gradient for panel backgrounds.</summary>
    public static Sprite Vignette => Get("vignette", 64, (x, y) =>
        Mathf.Clamp01(1f - Len(x * 1.2f, y * 1.6f) * 1.4f));


    private static float Len(float x, float y) => Mathf.Sqrt(x * x + y * y);

    private static float Smooth(float v, float width) =>
        Mathf.Clamp01(v / width + 0.5f);


    /// <summary>
    /// Rasterises a white-on-transparent icon from a coverage function
    /// over the square -0.5..0.5, so it can be tinted by the renderer.
    /// </summary>
    private static Sprite Get(
        string key,
        int size,
        System.Func<float, float, float> coverage)
    {
        if (sprites.TryGetValue(key, out Sprite cached) && cached != null)
            return cached;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "GameArt " + key,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color[] pixels = new Color[size * size];

        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float x = (px + 0.5f) / size - 0.5f;
                float y = (py + 0.5f) / size - 0.5f;

                pixels[py * size + px] =
                    new Color(1f, 1f, 1f, Mathf.Clamp01(coverage(x, y)));
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            size);

        sprite.hideFlags = HideFlags.DontSave;

        sprites[key] = sprite;

        return sprite;
    }


    // =========================================================
    // SOUND
    // =========================================================

    private static readonly Dictionary<string, AudioClip> clips =
        new Dictionary<string, AudioClip>();

    private const int Rate = 44100;


    /// <summary>A dry wooden click - clock escapement.</summary>
    private static AudioClip SynthTick => Clip("tick", 0.06f, t =>
        Noise(t) * Mathf.Exp(-t * 90f) * 0.5f +
        Mathf.Sin(t * 2f * Mathf.PI * 1900f) * Mathf.Exp(-t * 120f) * 0.4f);

    /// <summary>A struck bell with inharmonic partials.</summary>
    private static AudioClip SynthChime => Clip("chime", 3.2f, t =>
    {
        float f = 523.25f;

        float bell =
            Mathf.Sin(t * 2f * Mathf.PI * f) * 0.50f +
            Mathf.Sin(t * 2f * Mathf.PI * f * 2.01f) * 0.25f +
            Mathf.Sin(t * 2f * Mathf.PI * f * 2.76f) * 0.18f +
            Mathf.Sin(t * 2f * Mathf.PI * f * 5.40f) * 0.08f;

        return bell * Mathf.Exp(-t * 1.3f) * Mathf.Clamp01(t * 400f) * 0.6f;
    });

    /// <summary>Breaking glass: bright noise with glints.</summary>
    private static AudioClip SynthSmash => Clip("smash", 0.9f, t =>
        Noise(t) * Mathf.Exp(-t * 6f) * 0.55f +
        Mathf.Sin(t * 2f * Mathf.PI * 3100f) * Mathf.Exp(-t * 9f) * 0.2f +
        Mathf.Sin(t * 2f * Mathf.PI * 4700f) * Mathf.Exp(-t * 14f) * 0.15f);

    /// <summary>Two soft rising notes - something gained.</summary>
    private static AudioClip SynthPickup => Clip("pickup", 0.5f, t =>
    {
        float f = t < 0.14f ? 659.25f : 987.77f;

        float env = Mathf.Exp(-((t < 0.14f) ? t : t - 0.14f) * 9f);

        return Mathf.Sin(t * 2f * Mathf.PI * f) * env * 0.35f;
    });

    /// <summary>A low dull thud - "no".</summary>
    private static AudioClip SynthDenied => Clip("denied", 0.25f, t =>
        Mathf.Sin(t * 2f * Mathf.PI * 110f) * Mathf.Exp(-t * 18f) * 0.5f +
        Noise(t) * Mathf.Exp(-t * 40f) * 0.15f);

    /// <summary>A small stone striking a hard floor.</summary>
    private static AudioClip SynthClack => Clip("clack", 0.2f, t =>
        Noise(t) * Mathf.Exp(-t * 55f) * 0.55f +
        Mathf.Sin(t * 2f * Mathf.PI * 2300f) * Mathf.Exp(-t * 70f) * 0.35f +
        Mathf.Sin(t * 2f * Mathf.PI * 3900f) * Mathf.Exp(-t * 110f) * 0.15f);

    /// <summary>Old hinges under strain.</summary>
    private static AudioClip SynthCreak => Clip("creak", 1.4f, t =>
    {
        float wobble = 180f + 60f * Mathf.Sin(t * 7f) + 25f * Mathf.Sin(t * 23f);

        float saw = Mathf.Repeat(t * wobble, 1f) * 2f - 1f;

        float env = Mathf.Sin(Mathf.Clamp01(t / 1.4f) * Mathf.PI);

        return (saw * 0.25f + Noise(t) * 0.08f) * env;
    });


    /*
     * The recorded sound when it has been delivered and baked, the
     * synthesized one until then. Call sites never need to know which.
     */

    /// <summary>A small mechanical click (lock work, generic ticks).</summary>
    public static AudioClip Tick => GameSound.Get("WatchTick") ?? SynthTick;

    /// <summary>One strike of a grandfather clock.</summary>
    public static AudioClip Chime => GameSound.Get("ClockChime") ?? SynthChime;

    public static AudioClip Smash => GameSound.Get("GlassSmash") ?? SynthSmash;

    public static AudioClip Pickup => GameSound.Get("ItemPickup") ?? SynthPickup;

    /// <summary>A locked door rattling - "not yet".</summary>
    public static AudioClip Denied => GameSound.Get("DoorLocked") ?? SynthDenied;

    /// <summary>A pebble landing.</summary>
    public static AudioClip Clack => GameSound.Get("PebbleImpact") ?? SynthClack;

    /// <summary>Old hinges. No recording yet.</summary>
    public static AudioClip Creak => GameSound.Get("DoorCreak") ?? SynthCreak;

    public static AudioClip Unlock => GameSound.Get("DoorUnlock") ?? SynthTick;


    private static uint noiseState = 22222;

    private static float Noise(float t)
    {
        noiseState ^= noiseState << 13;
        noiseState ^= noiseState >> 17;
        noiseState ^= noiseState << 5;

        return (noiseState / (float)uint.MaxValue) * 2f - 1f;
    }


    private static AudioClip Clip(
        string key,
        float seconds,
        System.Func<float, float> wave)
    {
        if (clips.TryGetValue(key, out AudioClip cached) && cached != null)
            return cached;

        int length = Mathf.CeilToInt(seconds * Rate);

        float[] data = new float[length];

        noiseState = 22222;

        for (int i = 0; i < length; i++)
        {
            data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("GameArt " + key, length, 1, Rate, false);

        clip.SetData(data, 0);

        clips[key] = clip;

        return clip;
    }


    /// <summary>
    /// A one-shot 3D sound. The volume is what the player hears; what
    /// the caretaker hears is a separate NoiseBus call, on purpose -
    /// audio is presentation, the bus is gameplay.
    /// </summary>
    public static void Play(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null)
            return;

        AudioSource source = GameSound.Play(clip, position, volume, GameSound.World);

        /* Mostly positional, a little present - the player's own actions. */

        source.spatialBlend = 0.85f;
        source.minDistance = 2f;
    }
}

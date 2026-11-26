using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the raw sound downloads in Assets/Audio/Source into the game's
/// finished clips in Assets/Resources/Audio, where GameSound loads them
/// by name.
///
/// The raw files are recordings, not game sounds: the footsteps are one
/// long take of someone walking, the clock strikes twelve times, the
/// flashlight file is an on click and an off click back to back, and
/// several are recorded far too quietly. This cuts, trims, fades and
/// normalises them, so swapping a source file and re-baking is the whole
/// workflow.
/// </summary>
public static class AudioBaker
{
    private const string SourceDir = "Assets/Audio/Source/";

    private const string OutDir = "Assets/Resources/Audio/";


    private struct Cut
    {
        public string Source, Output;
        public float Start, End;      /* seconds; End <= 0 means to the end */
        public float Peak;            /* normalise to this; 0 leaves the level */
        public bool Loop;

        public Cut(string source, string output, float start = 0f, float end = 0f,
                   float peak = 0.85f, bool loop = false)
        {
            Source = source; Output = output; Start = start; End = end;
            Peak = peak; Loop = loop;
        }
    }


    private static readonly Cut[] Cuts =
    {
        /* Creature */
        new Cut("CreatureScream", "CreatureScream"),
        new Cut("CreatureShriek", "CreatureShriek"),
        new Cut("CreatureRoar", "CreatureRoar"),
        new Cut("CreatureSniff", "CreatureSniff_1", 0.15f, 3.9f),
        new Cut("CreatureSniff", "CreatureSniff_2", 5.85f, 9.2f),
        new Cut("CreatureGrowlLoop", "CreatureGrowlLoop", peak: 0.9f, loop: true),

        /* Player */
        new Cut("HidingBreath", "HidingBreath", peak: 0.55f, loop: true),
        new Cut("Heartbeat", "Heartbeat", peak: 0.8f, loop: true),
        new Cut("FlashlightClick", "FlashlightOn", 0f, 0.25f),
        new Cut("FlashlightClick", "FlashlightOff", 0.26f, 0.7f),
        new Cut("WatchTick", "WatchTick", peak: 0.7f),
        new Cut("CameraFlash", "CameraFlash", 0f, 2.2f),
        new Cut("DeathSting", "DeathSting"),

        /* World */
        new Cut("ClockWinding", "ClockWinding", peak: 0.8f, loop: true),
        new Cut("ClockChimesLong", "ClockChime", 0.9f, 2.95f, 0.9f),
        new Cut("GlassSmash", "GlassSmash"),
        new Cut("ItemPickup", "ItemPickup", peak: 0.7f),
        new Cut("PebbleImpact", "PebbleImpact", peak: 0.8f),
        new Cut("DoorLocked", "DoorLocked", peak: 0.8f),
        new Cut("DoorUnlock", "DoorUnlock"),
        new Cut("AmbienceHum", "AmbienceHum", peak: 0.5f, loop: true),
    };


    private static readonly StringBuilder report = new StringBuilder();


    [MenuItem("Tools/AI Director/Audio - Bake Sounds", priority = 75)]
    public static void BakeFromMenu()
    {
        EditorUtility.DisplayDialog("Audio", Bake(), "OK");
    }


    /// <summary>Callable from the Unity CLI; returns the report.</summary>
    public static string Bake()
    {
        report.Clear();

        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(OutDir + "Steps/Walk");
        Directory.CreateDirectory(OutDir + "Steps/Run");

        foreach (Cut cut in Cuts)
        {
            if (!Load(cut.Source, out float[] mono, out int rate))
                continue;

            float[] slice = Slice(mono, rate, cut.Start, cut.End);

            Write(OutDir + cut.Output + ".wav", Shape(slice, rate, cut.Peak, !cut.Loop), rate);
        }

        SliceSteps("PlayerWalkingLong", "Steps/Walk/Walk", 0.03f, 0.42f, 10);
        SliceSteps("PlayerSprintingLong", "Steps/Run/Run", 0.02f, 0.24f, 10);

        AssetDatabase.Refresh();

        ConfigureImports();

        BuildMixer();

        report.Insert(0, "Baked into " + OutDir + "\n");

        Debug.Log("[AudioBaker]\n" + report);

        return report.ToString();
    }


    // =========================================================
    // FOOTSTEPS
    // =========================================================

    /// <summary>
    /// Finds each footfall in a long walking take and cuts it out as its
    /// own clip, so steps can be played one per stride, in time with the
    /// player, and varied.
    /// </summary>
    private static void SliceSteps(
        string source, string outPrefix,
        float preRoll, float length, int max)
    {
        if (!Load(source, out float[] mono, out int rate))
            return;

        List<float> onsets = Onsets(mono, rate, 0.35f, 0.18f);

        int written = 0;

        foreach (float t in onsets)
        {
            if (written >= max)
                break;

            float[] slice = Slice(mono, rate, Mathf.Max(0f, t - preRoll), t + length);

            if (slice.Length < rate * 0.1f)
                continue;

            written++;

            Write(OutDir + outPrefix + "_" + written.ToString("00") + ".wav",
                Shape(slice, rate, 0.85f, true), rate);
        }

        report.AppendLine(source + ": " + written + " steps cut from " + onsets.Count + " found");
    }


    private static List<float> Onsets(float[] mono, int rate, float threshold, float minGap)
    {
        int win = rate / 50;

        int n = mono.Length / win;

        float[] env = new float[n];

        float max = 0f;

        for (int w = 0; w < n; w++)
        {
            float e = 0f;

            for (int i = 0; i < win; i++)
            {
                e += Mathf.Abs(mono[w * win + i]);
            }

            env[w] = e / win;

            max = Mathf.Max(max, env[w]);
        }

        List<float> result = new List<float>();

        float last = -1f;

        for (int w = 1; w < n; w++)
        {
            float t = w * 0.02f;

            if (env[w] > max * threshold && env[w] > env[w - 1] * 1.8f && t - last > minGap)
            {
                result.Add(t);

                last = t;
            }
        }

        return result;
    }


    // =========================================================
    // PROCESSING
    // =========================================================

    private static bool Load(string name, out float[] mono, out int rate)
    {
        mono = null;
        rate = 0;

        AudioClip clip = null;

        foreach (string ext in new[] { ".mp3", ".wav", ".ogg" })
        {
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SourceDir + name + ext);

            if (clip != null) break;
        }

        if (clip == null)
        {
            report.AppendLine("MISSING source " + name);

            return false;
        }

        /* Raw samples need a decompressed, preloaded clip. */

        AudioImporter importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));

        AudioImporterSampleSettings s = importer.defaultSampleSettings;

        if (s.loadType != AudioClipLoadType.DecompressOnLoad || importer.loadInBackground)
        {
            s.loadType = AudioClipLoadType.DecompressOnLoad;

            importer.defaultSampleSettings = s;

            importer.loadInBackground = false;

            importer.SaveAndReimport();

            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GetAssetPath(clip));
        }

        clip.LoadAudioData();

        float[] data = new float[clip.samples * clip.channels];

        clip.GetData(data, 0);

        mono = new float[clip.samples];

        for (int i = 0; i < clip.samples; i++)
        {
            float sum = 0f;

            for (int c = 0; c < clip.channels; c++)
            {
                sum += data[i * clip.channels + c];
            }

            mono[i] = sum / clip.channels;
        }

        rate = clip.frequency;

        return true;
    }


    private static float[] Slice(float[] mono, int rate, float start, float end)
    {
        int a = Mathf.Clamp(Mathf.RoundToInt(start * rate), 0, mono.Length);

        int b = end > 0f
            ? Mathf.Clamp(Mathf.RoundToInt(end * rate), a, mono.Length)
            : mono.Length;

        float[] slice = new float[b - a];

        System.Array.Copy(mono, a, slice, 0, slice.Length);

        return slice;
    }


    /// <summary>
    /// Normalises to a peak and fades the edges. Fades stop a cut from
    /// clicking; loops get a much shorter one so they still join.
    /// </summary>
    private static float[] Shape(float[] samples, int rate, float peak, bool oneShot)
    {
        float max = 0f;

        foreach (float v in samples)
        {
            max = Mathf.Max(max, Mathf.Abs(v));
        }

        float gain = peak > 0f && max > 0.0001f ? peak / max : 1f;

        int fadeIn = Mathf.Min(samples.Length / 4, Mathf.RoundToInt(rate * 0.004f));

        int fadeOut = Mathf.Min(samples.Length / 3,
            Mathf.RoundToInt(rate * (oneShot ? 0.08f : 0.01f)));

        float[] result = new float[samples.Length];

        for (int i = 0; i < samples.Length; i++)
        {
            float env = 1f;

            if (i < fadeIn) env = i / (float)fadeIn;

            int fromEnd = samples.Length - 1 - i;

            if (fromEnd < fadeOut) env = Mathf.Min(env, fromEnd / (float)fadeOut);

            result[i] = Mathf.Clamp(samples[i] * gain * env, -1f, 1f);
        }

        return result;
    }


    private static void Write(string path, float[] samples, int rate)
    {
        using (FileStream fs = new FileStream(path, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            int bytes = samples.Length * 2;

            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + bytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);          /* PCM */
            w.Write((short)1);          /* mono */
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(bytes);

            foreach (float s in samples)
            {
                w.Write((short)Mathf.RoundToInt(s * 32767f));
            }
        }

        report.AppendLine("  " + Path.GetFileName(path) + "  " +
                          (samples.Length / (float)rate).ToString("F2") + "s");
    }


    // =========================================================
    // MIXER
    // =========================================================

    private const string MixerPath = OutDir + "GameMixer.mixer";

    /// <summary>
    /// One group per category under Master, so each can be balanced with
    /// a single fader. Unity has no public API for building a mixer, so
    /// this goes through the editor's own (internal) controller type; if
    /// a future Unity renames it, the game still plays - GameSound just
    /// routes straight to the listener.
    /// </summary>
    private static void BuildMixer()
    {
        if (File.Exists(MixerPath))
        {
            report.AppendLine("Mixer already exists - left as tuned");

            return;
        }

        try
        {
            const BindingFlags any =
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance;

            System.Type type = typeof(Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController");

            object controller = type.GetMethod("CreateMixerControllerAtPath", any)
                .Invoke(null, new object[] { MixerPath });

            object master = type.GetProperty("masterGroup", any).GetValue(controller);

            MethodInfo create = type.GetMethod("CreateNewGroup", any);

            MethodInfo addChild = type.GetMethod("AddChildToParent", any);

            foreach (string name in new[]
                     {
                         GameSound.Creature, GameSound.Player, GameSound.World,
                         GameSound.UI, GameSound.Ambience
                     })
            {
                object group = create.Invoke(controller, new object[] { name, false });

                addChild.Invoke(controller, new[] { group, master });
            }

            EditorUtility.SetDirty((Object)controller);

            AssetDatabase.SaveAssets();

            report.AppendLine("Mixer: " + MixerPath + " (Creature, Player, World, UI, Ambience)");
        }
        catch (System.Exception e)
        {
            report.AppendLine("Mixer NOT created (" + e.GetType().Name + ") - sounds play without one");
        }
    }


    // =========================================================
    // IMPORT SETTINGS
    // =========================================================

    /// <summary>
    /// Short effects decompress into memory so they fire with no
    /// latency; long loops stay compressed so the heartbeat and growl do
    /// not sit in RAM as raw PCM.
    /// </summary>
    private static void ConfigureImports()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            AudioImporter importer = (AudioImporter)AssetImporter.GetAtPath(path);

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

            AudioImporterSampleSettings s = importer.defaultSampleSettings;

            bool longClip = clip != null && clip.length > 6f;

            s.loadType = longClip
                ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;

            s.compressionFormat = longClip ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;

            s.quality = 0.7f;

            importer.defaultSampleSettings = s;

            importer.forceToMono = true;

            importer.loadInBackground = longClip;

            importer.SaveAndReimport();
        }
    }
}

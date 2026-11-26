using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The game's recorded sounds, loaded by name from Resources/Audio.
///
/// Clips are baked there by AudioBaker. Anything missing returns null,
/// and every caller treats null as "no sound" (or falls back to a
/// synthesized placeholder), so a half-delivered sound set never breaks
/// the game.
///
/// Like GameArt.Play, this is presentation only. What the caretaker
/// hears goes through NoiseBus; nothing here is gameplay.
/// </summary>
public static class GameSound
{
    public const string Creature = "Creature";
    public const string Player = "Player";
    public const string World = "World";
    public const string UI = "UI";
    public const string Ambience = "Ambience";


    private static readonly Dictionary<string, AudioClip> clips =
        new Dictionary<string, AudioClip>();

    private static readonly Dictionary<string, AudioClip[]> sets =
        new Dictionary<string, AudioClip[]>();

    private static readonly Dictionary<string, AudioMixerGroup> groups =
        new Dictionary<string, AudioMixerGroup>();

    private static AudioMixer mixer;

    private static bool mixerLoaded;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        clips.Clear();
        sets.Clear();
        groups.Clear();
        mixer = null;
        mixerLoaded = false;
    }


    // =========================================================
    // CLIPS
    // =========================================================

    /// <summary>One clip by name, or null.</summary>
    public static AudioClip Get(string name)
    {
        if (!clips.TryGetValue(name, out AudioClip clip))
        {
            clip = Resources.Load<AudioClip>("Audio/" + name);

            clips[name] = clip;
        }

        return clip;
    }


    /// <summary>Every clip in a folder (e.g. "Steps/Walk"), possibly empty.</summary>
    public static AudioClip[] Set(string folder)
    {
        if (!sets.TryGetValue(folder, out AudioClip[] found))
        {
            found = Resources.LoadAll<AudioClip>("Audio/" + folder);

            sets[folder] = found;
        }

        return found;
    }


    /// <summary>A random clip from a set, or from named variants.</summary>
    public static AudioClip Any(params string[] names)
    {
        List<AudioClip> pool = new List<AudioClip>();

        foreach (string n in names)
        {
            AudioClip c = Get(n);

            if (c != null) pool.Add(c);
        }

        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }


    public static AudioClip Any(AudioClip[] set)
    {
        return set != null && set.Length > 0 ? set[Random.Range(0, set.Length)] : null;
    }


    // =========================================================
    // MIXER
    // =========================================================

    /// <summary>
    /// The mixer group for a category, if the GameMixer asset exists.
    /// Without it, sources play straight to the listener - still fine.
    /// </summary>
    public static AudioMixerGroup Group(string name)
    {
        if (!mixerLoaded)
        {
            mixerLoaded = true;

            mixer = Resources.Load<AudioMixer>("Audio/GameMixer");
        }

        if (mixer == null)
            return null;

        if (!groups.TryGetValue(name, out AudioMixerGroup group))
        {
            AudioMixerGroup[] found = mixer.FindMatchingGroups(name);

            group = found.Length > 0 ? found[0] : null;

            groups[name] = group;
        }

        return group;
    }


    /// <summary>
    /// Applies a mixer snapshot (e.g. "Hiding") if the mixer has one.
    /// </summary>
    public static void Snapshot(string name, float seconds)
    {
        Group(Ambience);

        AudioMixerSnapshot snapshot = mixer != null ? mixer.FindSnapshot(name) : null;

        if (snapshot != null)
        {
            snapshot.TransitionTo(seconds);
        }
    }


    // =========================================================
    // PLAYBACK
    // =========================================================

    /// <summary>A positional one-shot on a throwaway source.</summary>
    public static AudioSource Play(
        AudioClip clip, Vector3 position, float volume = 1f,
        string group = World, float pitch = 1f, float maxDistance = 40f)
    {
        if (clip == null)
            return null;

        GameObject go = new GameObject("Sound " + clip.name);

        go.transform.position = position;

        AudioSource source = Configure(go.AddComponent<AudioSource>(), group, 1f, maxDistance);

        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.Play();

        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);

        return source;
    }


    /// <summary>A non-positional one-shot - stings, UI.</summary>
    public static void Play2D(AudioClip clip, float volume = 1f, string group = UI)
    {
        AudioSource source = Play(clip, Vector3.zero, volume, group);

        if (source != null)
        {
            source.spatialBlend = 0f;
        }
    }


    /// <summary>Common source setup: group, falloff, no play on awake.</summary>
    public static AudioSource Configure(
        AudioSource source, string group, float spatialBlend, float maxDistance)
    {
        source.playOnAwake = false;
        source.outputAudioMixerGroup = Group(group);
        source.spatialBlend = spatialBlend;
        source.minDistance = 1.5f;
        source.maxDistance = maxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;

        return source;
    }


    /// <summary>A looping source that lives on a host, created on demand.</summary>
    public static AudioSource Loop(
        GameObject host, string clipName, string group,
        float spatialBlend, float maxDistance = 30f)
    {
        AudioClip clip = Get(clipName);

        if (clip == null)
            return null;

        GameObject child = new GameObject("Loop " + clipName);

        child.transform.SetParent(host.transform, false);

        AudioSource source = Configure(child.AddComponent<AudioSource>(), group, spatialBlend, maxDistance);

        source.clip = clip;
        source.loop = true;
        source.volume = 0f;

        /* Start somewhere random, so two loops never phase together. */

        source.time = Random.Range(0f, clip.length * 0.9f);

        source.Play();

        return source;
    }
}

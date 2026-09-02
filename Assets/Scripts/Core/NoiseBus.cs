using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The global sound channel.
///
/// Anything that makes a noise calls NoiseBus.Emit. Anything that
/// listens - in Phase 1 that will be the xenomorph's HearingSense -
/// subscribes to OnNoise.
///
/// Deliberately a plain static class rather than a MonoBehaviour
/// singleton: noises are fire-and-forget and there is nothing to
/// place in a scene or forget to place.
/// </summary>
public static class NoiseBus
{
    /// <summary>
    /// Raised the instant a noise is made.
    ///
    /// Handlers must not assume they are on any particular object,
    /// and must not hold onto the event beyond the callback.
    /// </summary>
    public static event Action<NoiseEvent> OnNoise;


    // =========================================================
    // HISTORY
    // =========================================================

    /*
     * A short rolling log, purely so the debug overlay and scene
     * gizmos can show what just happened. Gameplay must never read
     * from this - it is a mirror, not a source of truth.
     */

    private const int MaxHistory = 32;

    private const float HistoryLifetime = 5f;

    private static readonly List<NoiseEvent> history =
        new List<NoiseEvent>(MaxHistory);


    public static IReadOnlyList<NoiseEvent> History =>
        history;


    /// <summary>
    /// Total noises emitted this play session. Debug readout only.
    /// </summary>
    public static int EmitCount { get; private set; }


    // =========================================================
    // DOMAIN RELOAD
    // =========================================================

    /*
     * Static state survives play mode when Enter Play Mode Options
     * has domain reload disabled. Without this reset, subscribers
     * from the previous session linger as dead delegates.
     */

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnNoise = null;
        history.Clear();
        EmitCount = 0;
    }


    // =========================================================
    // EMISSION
    // =========================================================

    public static void Emit(
        Vector3 position,
        float radius,
        NoiseType type,
        GameObject source = null)
    {
        Emit(
            new NoiseEvent(
                position,
                radius,
                type,
                source,
                Time.time
            )
        );
    }


    public static void Emit(NoiseEvent noise)
    {
        if (noise.Radius <= 0f)
            return;

        EmitCount++;

        RecordInHistory(noise);

        OnNoise?.Invoke(noise);
    }


    // =========================================================
    // HISTORY UPKEEP
    // =========================================================

    private static void RecordInHistory(NoiseEvent noise)
    {
        history.Add(noise);

        if (history.Count > MaxHistory)
        {
            history.RemoveAt(0);
        }
    }


    /// <summary>
    /// Drops history entries older than HistoryLifetime.
    ///
    /// Called by the debug overlay each frame. Nothing else needs to
    /// call this - if no overlay is present the list simply caps out
    /// at MaxHistory and stops growing.
    /// </summary>
    public static void PruneHistory()
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Age > HistoryLifetime)
            {
                history.RemoveAt(i);
            }
        }
    }
}

using UnityEngine;

/// <summary>
/// The only channel through which the AI Director may influence the
/// xenomorph.
///
/// THIS INTERFACE IS THE WHOLE POINT OF THE TWO-BRAIN DESIGN.
///
/// The Director is allowed to know exactly where the player is. The
/// alien is not. So the Director never writes a destination, never
/// forges a perception, and never drives a state transition. It offers
/// a rough area worth looking at and a couple of coarse moods, and the
/// alien decides for itself what to do with them.
///
/// Implemented in Phase 4. Until then the brain finds no implementation
/// and behaves entirely autonomously, which is exactly how it must
/// still work if the Director is ever disabled.
/// </summary>
public interface IXenoDirectorHints
{
    /// <summary>
    /// True when the Director would like a general area investigated.
    /// </summary>
    bool HasSearchHint { get; }

    /// <summary>
    /// Centre of the suggested area. Deliberately an AREA and not the
    /// player's position - a hint the alien walks straight to would be
    /// indistinguishable from cheating.
    /// </summary>
    Vector3 SearchHintPosition { get; }

    float SearchHintRadius { get; }

    /// <summary>
    /// The Director wants the alien gone - menace is too high, or the
    /// player needs breathing room.
    /// </summary>
    bool ShouldRetreat { get; }

    /// <summary>
    /// The Director wants it to stop toying and commit. Cuts a stalk
    /// short.
    /// </summary>
    bool ShouldCommit { get; }

    /// <summary>
    /// Told when the alien has finished with the hinted area, so the
    /// Director can pick somewhere else.
    /// </summary>
    void NotifyHintReached();
}

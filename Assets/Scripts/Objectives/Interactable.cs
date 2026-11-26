using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What an interactable shows the player right now.
/// </summary>
public enum InteractState
{
    /// <summary>Nothing to do - no prompt, no marker.</summary>
    None,

    /// <summary>Press or hold E to do it.</summary>
    Available,

    /// <summary>Visible, but blocked - the prompt says why.</summary>
    Blocked
}


/// <summary>
/// The prompt under the crosshair.
/// </summary>
public struct InteractPrompt
{
    public InteractState State;

    /// <summary>"Wind the Egyptian clock".</summary>
    public string Action;

    /// <summary>Why it is blocked, or a short note.</summary>
    public string Detail;

    /// <summary>Hold rather than press.</summary>
    public bool Hold;

    /// <summary>The caretaker will hear it - flagged in red.</summary>
    public bool Loud;


    public static InteractPrompt None =>
        new InteractPrompt { State = InteractState.None };
}


/// <summary>
/// Something the player can aim at and press E on.
///
/// Anything without one of these is scenery - and says so by having no
/// marker and no prompt. That is the whole visual contract: if it glows
/// and has an icon over it, it matters.
///
/// Hold interactions keep their progress when released. Winding a clock
/// halfway, backing off because the caretaker came round the corner,
/// and returning later is exactly the decision the game is about.
/// </summary>
public abstract class Interactable : MonoBehaviour
{
    // =========================================================
    // REGISTRY
    // =========================================================

    private static readonly List<Interactable> active =
        new List<Interactable>();

    public static IReadOnlyList<Interactable> Active => active;


    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField, Min(0.5f)]
    private float range = 2.6f;

    /// <summary>Where the prompt and marker point at, locally.</summary>
    [SerializeField]
    private Vector3 focusOffset = Vector3.zero;

    [SerializeField, Min(0.1f)]
    protected float holdSeconds = 1f;

    /// <summary>
    /// Colliders outside this object that should not block aiming at it
    /// - the glass of the case a key sits in.
    /// </summary>
    [SerializeField]
    private Transform aimThrough;


    // =========================================================
    // STATE
    // =========================================================

    public float Range => range;

    public Vector3 FocusPoint => transform.TransformPoint(focusOffset);

    public float HoldProgress { get; protected set; }

    public float HoldSeconds => holdSeconds;

    /// <summary>Short name for the Tab markers.</summary>
    public abstract string Label { get; }

    /// <summary>The prompt for a player standing in range.</summary>
    public abstract InteractPrompt GetPrompt();

    /// <summary>
    /// Is this an outstanding task? Drives the world marker and the
    /// Tab overlay. Hiding places are not tasks.
    /// </summary>
    public virtual InteractState TaskState => GetPrompt().State;


    public void Configure(
        float interactRange,
        Vector3 focus,
        float seconds,
        Transform aimThroughRoot = null)
    {
        range = interactRange;
        focusOffset = focus;
        holdSeconds = seconds;
        aimThrough = aimThroughRoot;
    }


    /// <summary>
    /// True for colliders that belong to this interactable, so a ray
    /// that hits them still counts as looking at it.
    /// </summary>
    public bool Owns(Collider collider)
    {
        Transform t = collider.transform;

        return t.IsChildOf(transform) ||
               (aimThrough != null && t.IsChildOf(aimThrough));
    }


    // =========================================================
    // UNITY
    // =========================================================

    protected virtual void OnEnable()
    {
        if (!active.Contains(this))
        {
            active.Add(this);
        }
    }


    protected virtual void OnDisable()
    {
        active.Remove(this);
    }


    // =========================================================
    // INTERACTION
    // =========================================================

    /// <summary>A press, for non-hold prompts.</summary>
    public virtual void Interact(GameObject who) { }


    /// <summary>
    /// Advances a hold. Returns true on the frame it completes.
    /// </summary>
    public bool AdvanceHold(float deltaTime, GameObject who)
    {
        if (HoldProgress >= 1f)
            return false;

        HoldProgress = Mathf.Clamp01(HoldProgress + deltaTime / holdSeconds);

        OnHoldTick(who);

        if (HoldProgress < 1f)
            return false;

        OnHoldComplete(who);

        return true;
    }


    public virtual void HoldReleased() { }

    protected virtual void OnHoldTick(GameObject who) { }

    protected virtual void OnHoldComplete(GameObject who) { }


    /// <summary>
    /// Pressing E on something blocked says why, out loud, so "nothing
    /// happened" is never the feedback.
    /// </summary>
    public virtual void Refuse()
    {
        InteractPrompt prompt = GetPrompt();

        GameArt.Play(GameArt.Denied, FocusPoint, 0.6f);

        ObjectiveManager.Instance?.Say(
            prompt.Detail, MessageTone.Warning);
    }
}

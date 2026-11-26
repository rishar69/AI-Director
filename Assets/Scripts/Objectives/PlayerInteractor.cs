using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Finds what the player is looking at and drives E on it.
///
/// Aim is forgiving on purpose: the target is whichever interactable's
/// focus point sits closest to the view ray, within reach and in line
/// of sight - not whatever collider the exact centre pixel happens to
/// hit. A key on a cluttered table should not need pixel hunting in a
/// dark room while something is walking towards you.
///
/// E is shared with hiding. When an interactable has focus it owns the
/// key; otherwise PlayerHiding gets it. PlayerHiding checks
/// <see cref="HasFocus"/> for exactly that reason.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerInteractor : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private Key interactKey = Key.E;

    /// <summary>How far off the view ray a target may sit and still count.</summary>
    [SerializeField, Min(0.05f)]
    private float aimTolerance = 0.55f;

    /// <summary>
    /// Movement is frozen while holding - you cannot wind a clock while
    /// walking away from it.
    /// </summary>
    [SerializeField]
    private bool lockMovementWhileHolding = true;


    // =========================================================
    // STATE
    // =========================================================

    private PlayerController player;

    private PlayerHiding hiding;

    private PlayerDeathSequence death;

    private Transform view;

    private bool lockedByUs;


    public Interactable Focus { get; private set; }

    public InteractPrompt Prompt { get; private set; }

    public bool HasFocus => Focus != null;

    public bool IsHolding { get; private set; }

    /// <summary>Suspends everything - the intro and win screens.</summary>
    public bool Suspended { get; set; }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        player = GetComponent<PlayerController>();

        hiding = GetComponent<PlayerHiding>();

        death = GetComponent<PlayerDeathSequence>();

        Camera camera = GetComponentInChildren<Camera>();

        view = camera != null ? camera.transform : transform;
    }


    private void OnDisable()
    {
        StopHolding();
    }


    private void Update()
    {
        bool busy =
            Suspended ||
            (hiding != null && hiding.IsHiding) ||
            (death != null && death.IsDying);

        if (busy)
        {
            StopHolding();

            Focus = null;

            Prompt = InteractPrompt.None;

            return;
        }

        /* Keep the target mid-hold even if the aim drifts slightly. */

        if (!IsHolding)
        {
            Focus = FindFocus();
        }

        Prompt = Focus != null ? Focus.GetPrompt() : InteractPrompt.None;

        if (Focus != null && Prompt.State == InteractState.None)
        {
            Focus = null;
        }

        HandleInput();
    }


    // =========================================================
    // INPUT
    // =========================================================

    private void HandleInput()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (Focus == null)
        {
            StopHolding();

            return;
        }

        bool pressed = keyboard[interactKey].wasPressedThisFrame;

        bool held = keyboard[interactKey].isPressed;

        if (Prompt.State == InteractState.Blocked)
        {
            StopHolding();

            if (pressed)
            {
                Focus.Refuse();
            }

            return;
        }

        if (!Prompt.Hold)
        {
            StopHolding();

            if (pressed)
            {
                Focus.Interact(gameObject);
            }

            return;
        }

        if (!held)
        {
            StopHolding();

            return;
        }

        if (!IsHolding && !pressed)
            return;

        IsHolding = true;

        if (lockMovementWhileHolding && !player.MovementLocked)
        {
            player.MovementLocked = true;

            lockedByUs = true;
        }

        if (Focus.AdvanceHold(Time.deltaTime, gameObject))
        {
            StopHolding();
        }
    }


    private void StopHolding()
    {
        if (IsHolding && Focus != null)
        {
            Focus.HoldReleased();
        }

        IsHolding = false;

        if (lockedByUs)
        {
            player.MovementLocked = false;

            lockedByUs = false;
        }
    }


    // =========================================================
    // AIM
    // =========================================================

    private Interactable FindFocus()
    {
        Vector3 origin = view.position;

        Vector3 forward = view.forward;

        Interactable best = null;

        float bestScore = float.MaxValue;

        foreach (Interactable candidate in Interactable.Active)
        {
            if (candidate == null)
                continue;

            Vector3 to = candidate.FocusPoint - origin;

            float along = Vector3.Dot(to, forward);

            if (along <= 0.1f || to.magnitude > candidate.Range)
                continue;

            /* Distance of the focus point from the view ray. */

            float off = (to - forward * along).magnitude;

            float tolerance = aimTolerance * Mathf.Clamp(along / 2f, 0.6f, 1.2f);

            if (off > tolerance)
                continue;

            if (!InSight(origin, candidate))
                continue;

            if (candidate.GetPrompt().State == InteractState.None)
                continue;

            float score = off / tolerance + along * 0.05f;

            if (score < bestScore)
            {
                bestScore = score;

                best = candidate;
            }
        }

        return best;
    }


    private bool InSight(Vector3 origin, Interactable target)
    {
        Vector3 to = target.FocusPoint - origin;

        RaycastHit[] hits = Physics.RaycastAll(
            origin, to.normalized, to.magnitude,
            ~0, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform))
                continue;

            if (target.Owns(hit.collider))
                continue;

            return false;
        }

        return true;
    }
}

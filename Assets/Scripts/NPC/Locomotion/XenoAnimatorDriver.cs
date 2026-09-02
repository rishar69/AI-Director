using UnityEngine;

/// <summary>
/// Translates locomotion state into Animator parameters.
///
/// Sits between XenoLocomotion and the Animator so that neither the
/// brain nor locomotion needs to know a single clip name. When the
/// placeholder rig is finally swapped for a real xenomorph, this is the
/// only file that should need touching.
///
/// Tolerates a missing Animator on purpose. The project currently has
/// Mixamo animation clips but no skinned mesh to play them on, so the
/// whole locomotion layer has to be testable with nothing attached.
/// </summary>
[RequireComponent(typeof(XenoLocomotion))]
public class XenoAnimatorDriver : MonoBehaviour
{
    /// <summary>
    /// How the Animator is driven.
    /// </summary>
    public enum DriveMode
    {
        /// <summary>
        /// Fires Idle / Run / LookAround triggers. Matches the existing
        /// NPC.controller, whose three parameters are all triggers.
        /// </summary>
        Triggers = 0,

        /// <summary>
        /// Writes a single float. Use this after replacing the
        /// controller with a 1D blend tree, which is the better setup
        /// once a walk clip exists.
        /// </summary>
        SpeedParameter = 1
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Animator")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private DriveMode mode = DriveMode.Triggers;

    /*
     * Mixamo's Running clip has root motion baked into it. With Apply
     * Root Motion left on, the animation drives the transform at the
     * same time as the NavMeshAgent does, and the two disagree - the
     * creature slides, and ends up facing somewhere other than the way
     * it is travelling.
     *
     * Locomotion here is agent-driven by design, so root motion is
     * forced off unless someone deliberately turns this back on.
     */

    [SerializeField]
    private bool disableRootMotion = true;


    [Header("Trigger Names")]
    [SerializeField]
    private string idleTrigger = "Idle";

    [SerializeField]
    private string runTrigger = "Run";

    [SerializeField]
    private string lookAroundTrigger = "LookAround";


    [Header("Speed Parameter")]
    [SerializeField]
    private string speedParameter = "Speed";

    [SerializeField, Min(0.01f)]
    private float speedDamping = 0.12f;


    [Header("Missing Walk Clip")]
    /*
     * There is no walk animation in the project, only Idle, Look Around
     * and Running. Playing the run clip at reduced playback speed is a
     * passable stand-in for a walk, and is far less ugly than blending
     * a run against an idle.
     *
     * Delete this once a real walk clip exists and switch mode to
     * SpeedParameter.
     */

    [SerializeField]
    private bool scaleClipSpeedToMovement = true;

    /// <summary>
    /// The ground speed the Running clip was authored at. Playback is
    /// scaled by actualSpeed / this.
    /// </summary>
    [SerializeField, Min(0.1f)]
    private float runClipReferenceSpeed = 4.5f;

    [SerializeField, Min(0.05f)]
    private float minimumClipSpeed = 0.35f;

    [SerializeField, Min(0.1f)]
    private float maximumClipSpeed = 1.6f;


    // =========================================================
    // STATE
    // =========================================================

    private enum AnimState
    {
        Idle,
        Moving,
        LookAround
    }

    private XenoLocomotion locomotion;

    private AnimState state = AnimState.Idle;

    private bool lookAroundRequested;

    private float lookAroundHoldUntil;

    private float smoothedSpeed;

    private bool warnedAboutAnimator;


    public bool HasAnimator => animator != null;

    public bool IsPlayingLookAround =>
        state == AnimState.LookAround;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        locomotion = GetComponent<XenoLocomotion>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        EnforceRootMotion();
    }


    private void OnEnable()
    {
        /*
         * Re-applied here as well as in Awake, because swapping the
         * model or reassigning the Animator at runtime would otherwise
         * quietly reintroduce the conflict.
         */

        EnforceRootMotion();
    }


    private void EnforceRootMotion()
    {
        if (animator == null || !disableRootMotion)
            return;

        if (!animator.applyRootMotion)
            return;

        animator.applyRootMotion = false;

        Debug.Log(
            "[XenoAnimatorDriver] Disabled Apply Root Motion on " +
            animator.name +
            " - locomotion is agent-driven, and root motion fights " +
            "the NavMeshAgent for control of the transform.",
            this
        );
    }


    private void Update()
    {
        AnimState desired = ResolveDesiredState();

        if (desired != state)
        {
            state = desired;

            ApplyState(state);
        }

        ApplyClipSpeed();
    }


    // =========================================================
    // STATE RESOLUTION
    // =========================================================

    private AnimState ResolveDesiredState()
    {
        if (locomotion.IsMoving)
        {
            /*
             * Movement always wins. Being told to look around and then
             * immediately walking off should not leave the creature
             * sliding along in a scan animation.
             */

            lookAroundRequested = false;

            return AnimState.Moving;
        }

        if (lookAroundRequested)
        {
            if (Time.time < lookAroundHoldUntil)
                return AnimState.LookAround;

            lookAroundRequested = false;
        }

        return AnimState.Idle;
    }


    private void ApplyState(AnimState next)
    {
        if (animator == null)
        {
            WarnOnceAboutAnimator();

            return;
        }

        if (mode == DriveMode.SpeedParameter)
        {
            /*
             * A blend tree reads the speed float continuously, so only
             * the one-shot scan still needs a trigger.
             */

            if (next == AnimState.LookAround)
            {
                FireTrigger(lookAroundTrigger);
            }

            return;
        }

        switch (next)
        {
            case AnimState.Moving:
                FireTrigger(runTrigger);
                break;

            case AnimState.LookAround:
                FireTrigger(lookAroundTrigger);
                break;

            default:
                FireTrigger(idleTrigger);
                break;
        }
    }


    // =========================================================
    // ANIMATOR PLUMBING
    // =========================================================

    /// <summary>
    /// Sets one trigger and clears the others.
    ///
    /// Triggers queue rather than overwrite. Without clearing the
    /// siblings, a stale Run trigger set while the creature was moving
    /// fires the moment it next enters a state that can consume it, and
    /// the animation jumps back to a run it is not doing.
    /// </summary>
    private void FireTrigger(string trigger)
    {
        ResetTrigger(idleTrigger);

        ResetTrigger(runTrigger);

        ResetTrigger(lookAroundTrigger);

        if (HasParameter(trigger))
        {
            animator.SetTrigger(trigger);
        }
    }


    private void ResetTrigger(string trigger)
    {
        if (HasParameter(trigger))
        {
            animator.ResetTrigger(trigger);
        }
    }


    /// <summary>
    /// Guards every parameter write. Setting a parameter an Animator
    /// does not have logs an error every single frame.
    /// </summary>
    private bool HasParameter(string parameterName)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        for (int i = 0; i < animator.parameterCount; i++)
        {
            if (animator.GetParameter(i).name == parameterName)
                return true;
        }

        return false;
    }


    // =========================================================
    // CLIP SPEED
    // =========================================================

    private void ApplyClipSpeed()
    {
        if (animator == null)
            return;

        if (mode == DriveMode.SpeedParameter)
        {
            smoothedSpeed =
                Mathf.Lerp(
                    smoothedSpeed,
                    locomotion.CurrentSpeed,
                    Time.deltaTime / speedDamping
                );

            if (HasParameter(speedParameter))
            {
                animator.SetFloat(speedParameter, smoothedSpeed);
            }

            animator.speed = 1f;

            return;
        }

        if (!scaleClipSpeedToMovement ||
            state != AnimState.Moving)
        {
            animator.speed = 1f;

            return;
        }

        /*
         * Only safe because the whole animator is in the run state
         * while moving - scaling Animator.speed would otherwise also
         * slow down idle and the scan.
         */

        animator.speed =
            Mathf.Clamp(
                locomotion.CurrentSpeed / runClipReferenceSpeed,
                minimumClipSpeed,
                maximumClipSpeed
            );
    }


    // =========================================================
    // PUBLIC API
    // =========================================================

    /// <summary>
    /// Plays the scan animation for a while. Phase 3 calls this when a
    /// search reaches a point of interest and looks around.
    /// </summary>
    public void RequestLookAround(float duration = 2.5f)
    {
        lookAroundRequested = true;

        lookAroundHoldUntil = Time.time + duration;
    }


    public void CancelLookAround()
    {
        lookAroundRequested = false;
    }


    private void WarnOnceAboutAnimator()
    {
        if (warnedAboutAnimator)
            return;

        warnedAboutAnimator = true;

        Debug.Log(
            "[XenoAnimatorDriver] " + name + " has no Animator. " +
            "Locomotion still works; nothing will be animated until a " +
            "skinned character model is added.",
            this
        );
    }
}

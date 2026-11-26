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
    private string walkTrigger = "Walk";

    /// <summary>
    /// Optional slow, stalking walk. Animators without it play the
    /// normal walk at every non-running speed.
    /// </summary>
    [SerializeField]
    private string prowlTrigger = "Prowl";

    [SerializeField]
    private string runTrigger = "Run";

    [SerializeField]
    private string lookAroundTrigger = "LookAround";

    /// <summary>
    /// Optional. No attack clip exists yet, so when this parameter is
    /// absent the strike falls back to the run trigger - a creature
    /// visibly charging is a far better stand-in than one standing
    /// still in its idle pose while the screen fades out.
    /// </summary>
    [SerializeField]
    private string attackTrigger = "Attack";


    [Header("Speed Parameter")]
    [SerializeField]
    private string speedParameter = "Speed";

    [SerializeField, Min(0.01f)]
    private float speedDamping = 0.12f;


    [Header("Gait Selection")]
    /// <summary>
    /// Ground speed at or above which the run clip is used.
    ///
    /// Sits between the Walk gait (2.4) and Run gait (5.2), so prowling
    /// and walking both play the walk clip and only a real chase plays
    /// the run.
    /// </summary>
    [SerializeField, Min(0.1f)]
    private float runSpeedThreshold = 3.5f;

    [SerializeField, Min(0f)]
    private float runSpeedHysteresis = 0.6f;

    /// <summary>
    /// Ground speed below which the prowl clip is used. Sits between
    /// the Prowl gait (1.2) and the Walk gait (2.4).
    /// </summary>
    [SerializeField, Min(0.1f)]
    private float prowlSpeedThreshold = 1.8f;


    [Header("Clip Speed")]
    /*
     * Playback is scaled by actual speed over the speed the clip was
     * authored at, which is what stops the feet sliding when a gait
     * does not match its clip - prowling at 1.2 against a walk clip
     * built for 1.5, say.
     */

    [SerializeField]
    private bool scaleClipSpeedToMovement = true;

    /// <summary>Ground speed the Walking clip was authored at.</summary>
    [SerializeField, Min(0.1f)]
    private float walkClipReferenceSpeed = 1.5f;

    /// <summary>Ground speed the prowl clip was authored at.</summary>
    [SerializeField, Min(0.1f)]
    private float prowlClipReferenceSpeed = 1.0f;

    /// <summary>Ground speed the Running clip was authored at.</summary>
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
        Prowling,
        Walking,
        Running,
        LookAround,
        Attack
    }

    private XenoLocomotion locomotion;

    private AnimState state = AnimState.Idle;

    private bool lookAroundRequested;

    private float lookAroundHoldUntil;

    private float attackHoldUntil;

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
        /*
         * The strike outranks everything, including movement - it is a
         * lunge, so the creature is moving while it happens.
         */

        if (Time.time < attackHoldUntil)
            return AnimState.Attack;

        if (locomotion.IsMoving)
        {
            /*
             * Movement always wins. Being told to look around and then
             * immediately walking off should not leave the creature
             * sliding along in a scan animation.
             */

            lookAroundRequested = false;

            /*
             * Chosen from actual speed rather than the gait, because
             * the gait is intent and the animation has to match what
             * the feet are really doing - a creature still accelerating
             * into a run is not running yet.
             */

            /*
             * A dead band around the threshold. Without it, speed
             * hovering right on 3.5 flips the state every few frames
             * and machine-guns triggers at the animator.
             */

            float threshold =
                state == AnimState.Running
                    ? runSpeedThreshold - runSpeedHysteresis
                    : runSpeedThreshold;

            if (locomotion.CurrentSpeed >= threshold)
                return AnimState.Running;

            /*
             * Same dead band idea for prowl/walk, and only when the
             * animator actually has a prowl state to play.
             */

            float prowlBelow =
                state == AnimState.Prowling
                    ? prowlSpeedThreshold + 0.3f
                    : prowlSpeedThreshold;

            return HasParameter(prowlTrigger) &&
                   locomotion.CurrentSpeed < prowlBelow
                ? AnimState.Prowling
                : AnimState.Walking;
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
            case AnimState.Running:
                FireTrigger(runTrigger);
                break;

            case AnimState.Prowling:
                FireTrigger(prowlTrigger);
                break;

            case AnimState.Walking:
                /*
                 * Falls back to the run trigger when no Walk parameter
                 * exists yet, so an animator that has not been updated
                 * still moves instead of standing idle while sliding.
                 */

                FireTrigger(
                    HasParameter(walkTrigger)
                        ? walkTrigger
                        : runTrigger
                );
                break;

            case AnimState.Attack:
                FireTrigger(
                    HasParameter(attackTrigger)
                        ? attackTrigger
                        : runTrigger
                );
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

        ResetTrigger(walkTrigger);

        ResetTrigger(prowlTrigger);

        ResetTrigger(runTrigger);

        ResetTrigger(lookAroundTrigger);

        ResetTrigger(attackTrigger);

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

        /*
         * Attack is excluded so the strike always plays at its authored
         * speed, whether that is a real attack clip or the run clip
         * standing in for one.
         */

        bool locomoting =
            state == AnimState.Prowling ||
            state == AnimState.Walking ||
            state == AnimState.Running;

        if (!scaleClipSpeedToMovement || !locomoting)
        {
            animator.speed = 1f;

            return;
        }

        /*
         * Scaled against whichever clip is actually playing. Only safe
         * because the animator is wholly in that locomotion state -
         * Animator.speed would otherwise also slow the idle and scan.
         */

        float reference =
            state == AnimState.Running ? runClipReferenceSpeed :
            state == AnimState.Prowling ? prowlClipReferenceSpeed :
            walkClipReferenceSpeed;

        animator.speed =
            Mathf.Clamp(
                locomotion.CurrentSpeed / reference,
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


    /// <summary>
    /// Plays the strike for a while. Called by AttackState as the lunge
    /// begins.
    /// </summary>
    public void PlayAttack(float duration)
    {
        attackHoldUntil = Time.time + Mathf.Max(0.05f, duration);

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

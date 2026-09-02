using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First person player movement.
///
/// Reads directly from the InputSystem_Actions asset by name rather
/// than a generated wrapper class, so the asset does not need
/// "Generate C# Class" enabled.
///
/// Other systems should treat this component as read-only state:
/// PlayerNoiseEmitter asks it how fast and how quietly the player is
/// moving, and the AI Director will later ask it whether the player
/// is exposed or hidden.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public enum Stance
    {
        Standing,
        Crouching
    }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Input")]
    [SerializeField]
    private InputActionAsset inputActions;

    [SerializeField]
    private string actionMapName = "Player";


    [Header("Camera")]
    [SerializeField]
    private Transform cameraPivot;

    [SerializeField, Min(0.001f)]
    private float lookSensitivity = 0.1f;

    [SerializeField]
    private float minPitch = -85f;

    [SerializeField]
    private float maxPitch = 85f;

    [SerializeField]
    private bool invertVertical = false;

    [SerializeField]
    private bool lockCursor = true;


    [Header("Movement")]
    [SerializeField, Min(0f)]
    private float walkSpeed = 2.6f;

    [SerializeField, Min(0f)]
    private float sprintSpeed = 5.0f;

    [SerializeField, Min(0f)]
    private float crouchSpeed = 1.2f;

    [SerializeField, Min(0.01f)]
    private float acceleration = 14f;

    [SerializeField, Min(0.01f)]
    private float deceleration = 18f;


    [Header("Stance")]
    [SerializeField, Min(0.2f)]
    private float standingHeight = 1.8f;

    [SerializeField, Min(0.2f)]
    private float crouchHeight = 1.0f;

    [SerializeField, Min(0.01f)]
    private float stanceTransitionSpeed = 9f;

    [SerializeField]
    private float eyeOffsetFromTop = -0.15f;

    [SerializeField]
    private LayerMask ceilingMask = ~0;


    [Header("Gravity")]
    [SerializeField]
    private float gravity = -18f;

    [SerializeField]
    private float groundStickForce = -2f;

    [SerializeField]
    private bool allowJump = false;

    [SerializeField, Min(0f)]
    private float jumpHeight = 1.0f;


    // =========================================================
    // STATE
    // =========================================================

    private CharacterController controller;

    private InputActionMap playerMap;

    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction crouchAction;
    private InputAction sprintAction;
    private InputAction jumpAction;

    private Vector3 horizontalVelocity;

    private float verticalVelocity;

    private float pitch;

    private float currentHeight;

    private Stance stance = Stance.Standing;

    private bool wasGroundedLastFrame = true;

    private bool inputReady;

    private readonly Collider[] ceilingHits = new Collider[8];


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    /// <summary>Horizontal speed in metres per second.</summary>
    public float CurrentSpeed => horizontalVelocity.magnitude;

    public bool IsMoving => CurrentSpeed > 0.15f;

    public bool IsGrounded => controller != null && controller.isGrounded;

    public Stance CurrentStance => stance;

    public bool IsCrouching => stance == Stance.Crouching;

    /// <summary>
    /// True only while actually sprinting. Holding the sprint key while
    /// crouched or standing still does not count.
    /// </summary>
    public bool IsSprinting =>
        stance == Stance.Standing &&
        IsMoving &&
        CurrentSpeed > walkSpeed + 0.2f;

    /// <summary>Speed as a fraction of the fastest possible speed.</summary>
    public float NormalizedSpeed =>
        sprintSpeed > 0f
            ? Mathf.Clamp01(CurrentSpeed / sprintSpeed)
            : 0f;

    /// <summary>
    /// Where the player's head is.
    ///
    /// The xenomorph's senses and the Director should test against this,
    /// not the transform origin down at the player's feet.
    /// </summary>
    public Vector3 EyePosition =>
        cameraPivot != null
            ? cameraPivot.position
            : transform.position + Vector3.up * currentHeight;

    /// <summary>
    /// Raised on the frame the player lands, carrying impact speed.
    /// </summary>
    public event System.Action<float> OnLanded;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        currentHeight = standingHeight;

        ApplyHeight(currentHeight);

        ResolveInput();
    }


    private void OnEnable()
    {
        playerMap?.Enable();

        SetCursorLocked(lockCursor);
    }


    private void OnDisable()
    {
        playerMap?.Disable();

        SetCursorLocked(false);
    }


    private void Update()
    {
        if (!inputReady)
            return;

        UpdateLook();

        UpdateStance();

        UpdateMovement();

        UpdateGravityAndLanding();

        ApplyMotion();
    }


    private void OnValidate()
    {
        crouchHeight =
            Mathf.Min(
                crouchHeight,
                standingHeight
            );

        sprintSpeed =
            Mathf.Max(
                sprintSpeed,
                walkSpeed
            );
    }


    // =========================================================
    // INPUT
    // =========================================================

    private void ResolveInput()
    {
        if (inputActions == null)
        {
            Debug.LogError(
                "[PlayerController] No InputActionAsset assigned. Drag " +
                "Assets/InputSystem_Actions.inputactions onto the " +
                "Input Actions field.",
                this
            );

            return;
        }

        playerMap =
            inputActions.FindActionMap(
                actionMapName,
                throwIfNotFound: false
            );

        if (playerMap == null)
        {
            Debug.LogError(
                "[PlayerController] Action map " + actionMapName +
                " was not found in the assigned asset.",
                this
            );

            return;
        }

        moveAction = playerMap.FindAction("Move", false);
        lookAction = playerMap.FindAction("Look", false);
        crouchAction = playerMap.FindAction("Crouch", false);
        sprintAction = playerMap.FindAction("Sprint", false);
        jumpAction = playerMap.FindAction("Jump", false);

        inputReady =
            moveAction != null &&
            lookAction != null;

        if (!inputReady)
        {
            Debug.LogError(
                "[PlayerController] The Move and Look actions are " +
                "required but were not found.",
                this
            );

            return;
        }

        playerMap.Enable();
    }


    private Vector2 ReadMove() =>
        moveAction != null
            ? moveAction.ReadValue<Vector2>()
            : Vector2.zero;


    private Vector2 ReadLook() =>
        lookAction != null
            ? lookAction.ReadValue<Vector2>()
            : Vector2.zero;


    private bool ReadCrouchHeld() =>
        crouchAction != null && crouchAction.IsPressed();


    private bool ReadSprintHeld() =>
        sprintAction != null && sprintAction.IsPressed();


    private bool ReadJumpPressed() =>
        jumpAction != null && jumpAction.WasPressedThisFrame();


    // =========================================================
    // LOOK
    // =========================================================

    private void UpdateLook()
    {
        Vector2 look = ReadLook();

        /*
         * Deliberately NOT scaled by Time.deltaTime.
         *
         * The Look action is bound to mouse delta, which is already a
         * per-frame displacement. Multiplying it by deltaTime would make
         * sensitivity depend on framerate.
         */

        float yawDelta = look.x * lookSensitivity;

        float pitchDelta = look.y * lookSensitivity;

        if (!invertVertical)
        {
            pitchDelta = -pitchDelta;
        }

        transform.Rotate(
            Vector3.up,
            yawDelta,
            Space.Self
        );

        pitch =
            Mathf.Clamp(
                pitch + pitchDelta,
                minPitch,
                maxPitch
            );

        if (cameraPivot != null)
        {
            cameraPivot.localRotation =
                Quaternion.Euler(pitch, 0f, 0f);
        }
    }


    // =========================================================
    // STANCE
    // =========================================================

    private void UpdateStance()
    {
        bool wantsCrouch = ReadCrouchHeld();

        if (wantsCrouch)
        {
            stance = Stance.Crouching;
        }
        else if (stance == Stance.Crouching && CanStandUp())
        {
            stance = Stance.Standing;
        }

        float targetHeight =
            stance == Stance.Crouching
                ? crouchHeight
                : standingHeight;

        currentHeight =
            Mathf.MoveTowards(
                currentHeight,
                targetHeight,
                stanceTransitionSpeed * Time.deltaTime
            );

        ApplyHeight(currentHeight);
    }


    /// <summary>
    /// Sweeps the space a standing capsule would occupy.
    ///
    /// Without this, releasing crouch inside a vent or under a desk
    /// pops the player up through the geometry.
    ///
    /// Uses an overlap query rather than Physics.CheckCapsule because
    /// the swept volume always contains the player's own crouched
    /// capsule. CheckCapsule only returns a bool, so it would report a
    /// blocked ceiling every single frame and the player could never
    /// stand back up. Here the hits can be filtered against self.
    /// </summary>
    private bool CanStandUp()
    {
        float radius =
            Mathf.Max(0.01f, controller.radius - 0.05f);

        Vector3 bottom =
            transform.position +
            Vector3.up * (controller.radius + controller.skinWidth);

        Vector3 top =
            transform.position +
            Vector3.up * (standingHeight - controller.radius);

        if (top.y <= bottom.y)
            return true;

        int count =
            Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                ceilingHits,
                ceilingMask,
                QueryTriggerInteraction.Ignore
            );

        for (int i = 0; i < count; i++)
        {
            Collider hit = ceilingHits[i];

            if (hit == null)
                continue;

            /*
             * Skip the player's own colliders, including anything
             * parented under them such as a held item.
             */

            if (hit.transform.IsChildOf(transform))
                continue;

            return false;
        }

        return true;
    }


    private void ApplyHeight(float height)
    {
        if (controller == null)
            return;

        controller.height = height;

        /*
         * The transform origin sits at the feet, so the capsule centre
         * has to track half the height as it changes.
         */

        controller.center =
            new Vector3(0f, height * 0.5f, 0f);

        if (cameraPivot != null)
        {
            Vector3 local = cameraPivot.localPosition;

            local.y = height + eyeOffsetFromTop;

            cameraPivot.localPosition = local;
        }
    }


    // =========================================================
    // MOVEMENT
    // =========================================================

    private void UpdateMovement()
    {
        Vector2 input = ReadMove();

        Vector3 wish =
            transform.right * input.x +
            transform.forward * input.y;

        if (wish.sqrMagnitude > 1f)
        {
            wish.Normalize();
        }

        Vector3 targetVelocity = wish * TargetSpeed();

        /*
         * Decelerating harder than accelerating keeps stopping crisp
         * without making starts feel twitchy.
         */

        float rate =
            targetVelocity.sqrMagnitude >
            horizontalVelocity.sqrMagnitude
                ? acceleration
                : deceleration;

        horizontalVelocity =
            Vector3.MoveTowards(
                horizontalVelocity,
                targetVelocity,
                rate * Time.deltaTime
            );
    }


    private float TargetSpeed()
    {
        if (stance == Stance.Crouching)
            return crouchSpeed;

        return ReadSprintHeld()
            ? sprintSpeed
            : walkSpeed;
    }


    // =========================================================
    // GRAVITY
    // =========================================================

    private void UpdateGravityAndLanding()
    {
        bool grounded = controller.isGrounded;

        if (grounded)
        {
            if (!wasGroundedLastFrame)
            {
                /*
                 * verticalVelocity still holds the impact speed on the
                 * frame the controller reports grounded again, so this
                 * has to fire before it gets reset below.
                 */

                OnLanded?.Invoke(
                    Mathf.Abs(verticalVelocity)
                );
            }

            if (verticalVelocity < 0f)
            {
                verticalVelocity = groundStickForce;
            }

            if (allowJump &&
                stance == Stance.Standing &&
                ReadJumpPressed())
            {
                verticalVelocity =
                    Mathf.Sqrt(
                        jumpHeight * -2f * gravity
                    );
            }
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        wasGroundedLastFrame = grounded;
    }


    private void ApplyMotion()
    {
        Vector3 motion =
            horizontalVelocity +
            Vector3.up * verticalVelocity;

        controller.Move(motion * Time.deltaTime);
    }


    // =========================================================
    // CURSOR
    // =========================================================

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState =
            locked
                ? CursorLockMode.Locked
                : CursorLockMode.None;

        Cursor.visible = !locked;
    }
}

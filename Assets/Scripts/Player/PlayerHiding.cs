using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Getting into and out of hiding spots.
///
/// The interesting part is peeking. Hiding perfectly still is safe but
/// blind - you cannot tell whether the thing has gone, and the longer
/// you sit there the more you want to look. Leaning out to check raises
/// visibility by an order of magnitude, which is exactly the decision
/// this mechanic exists to force.
/// </summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(CharacterController))]
public class PlayerHiding : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Input")]
    [SerializeField]
    private Key enterKey = Key.E;

    /// <summary>
    /// Held to lean out and look. Read from the keyboard directly
    /// rather than through the actions asset, which has no spare
    /// binding for it.
    /// </summary>
    [SerializeField]
    private Key peekKey = Key.W;


    [Header("Feel")]
    [SerializeField, Min(0f)]
    private float enterDuration = 0.35f;

    /// <summary>
    /// How far the camera leans forward while peeking.
    /// </summary>
    [SerializeField, Min(0f)]
    private float peekOffset = 0.45f;

    [SerializeField, Min(0.01f)]
    private float peekSpeed = 6f;


    [Header("Noise")]
    /// <summary>
    /// Climbing into a locker is not silent. Small, but enough that
    /// hiding while something is already looking at you is a bad idea.
    /// </summary>
    [SerializeField, Min(0f)]
    private float enterNoiseRadius = 4f;


    [Header("Debug")]
    [SerializeField]
    private bool logHiding = true;


    // =========================================================
    // STATE
    // =========================================================

    private PlayerController player;

    private CharacterController controller;

    private Concealment concealment;

    private Transform cameraPivot;

    private HidingSpot current;

    private XenoBlackboard board;

    private Vector3 exitPosition;

    private float peekAmount;

    private float busyUntil;

    private Vector3 cameraRestLocalPosition;


    public bool IsHiding => current != null;

    public bool IsPeeking => IsHiding && peekAmount > 0.5f;

    /// <summary>Current visibility multiplier, for the overlay.</summary>
    public float Visibility =>
        concealment != null ? concealment.Visibility : 1f;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        player = GetComponent<PlayerController>();

        controller = GetComponent<CharacterController>();

        concealment = GetComponent<Concealment>();

        if (concealment == null)
        {
            concealment = gameObject.AddComponent<Concealment>();
        }

        Camera camera = GetComponentInChildren<Camera>();

        if (camera != null)
        {
            cameraPivot = camera.transform;
        }
    }


    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (Time.time < busyUntil)
            return;

        if (keyboard[enterKey].wasPressedThisFrame)
        {
            Toggle();
        }

        if (IsHiding)
        {
            TickHidden(keyboard);
        }
    }


    // =========================================================
    // ENTER / EXIT
    // =========================================================

    private void Toggle()
    {
        if (IsHiding)
        {
            Exit();
        }
        else
        {
            TryEnter();
        }
    }


    private void TryEnter()
    {
        if (!HidingSpot.TryGetInteractable(
                transform.position,
                out HidingSpot spot))
        {
            return;
        }

        current = spot;

        spot.Occupied = true;

        exitPosition = transform.position;

        Teleport(spot.HidePosition, spot.HideRotation);

        player.MovementLocked = true;

        peekAmount = 0f;

        if (cameraPivot != null)
        {
            cameraRestLocalPosition = cameraPivot.localPosition;
        }

        ApplyConcealment();

        PublishHidingFlag(true);

        NoiseBus.Emit(
            transform.position,
            enterNoiseRadius,
            NoiseType.Object,
            gameObject
        );

        busyUntil = Time.time + enterDuration;

        if (logHiding)
        {
            Debug.Log(
                "[PlayerHiding] Hidden in " + spot.name,
                this
            );
        }
    }


    private void Exit()
    {
        HidingSpot spot = current;

        current = null;

        if (spot != null)
        {
            spot.Occupied = false;
        }

        /*
         * Step back out to where entry happened rather than to the
         * hide point, which is usually inside geometry.
         */

        Teleport(exitPosition, transform.rotation);

        player.MovementLocked = false;

        peekAmount = 0f;

        RestoreCamera();

        concealment.Clear();

        PublishHidingFlag(false);

        NoiseBus.Emit(
            transform.position,
            enterNoiseRadius,
            NoiseType.Object,
            gameObject
        );

        busyUntil = Time.time + enterDuration;

        if (logHiding)
        {
            Debug.Log("[PlayerHiding] Left cover", this);
        }
    }


    // =========================================================
    // HIDDEN
    // =========================================================

    private void TickHidden(Keyboard keyboard)
    {
        bool wantsPeek = keyboard[peekKey].isPressed;

        peekAmount =
            Mathf.MoveTowards(
                peekAmount,
                wantsPeek ? 1f : 0f,
                peekSpeed * Time.deltaTime
            );

        ApplyConcealment();

        ApplyCameraLean();
    }


    /// <summary>
    /// Publishes PLAYER_HIDING onto the creature's blackboard.
    ///
    /// The tree reads it as TargetLogicCharacterFlags(PLAYER_HIDING)
    /// and the attack branch refuses to run while it is set. Pushing it
    /// from the player's side keeps the creature from having to reach
    /// across and inspect the player directly.
    /// </summary>
    private void PublishHidingFlag(bool value)
    {
        if (board == null)
        {
            board = FindFirstObjectByType<XenoBlackboard>();
        }

        board?.SetTargetFlag(TargetLogicFlag.PlayerHiding, value);
    }


    private void ApplyConcealment()
    {
        if (current == null)
            return;

        /*
         * Visibility slides between the two values with the lean, so
         * half-peeking is genuinely half as risky.
         */

        float visibility =
            Mathf.Lerp(
                current.VisibilityWhileHidden,
                current.VisibilityWhilePeeking,
                peekAmount
            );

        concealment.Set(visibility, current.NoiseWhileHidden);
    }


    private void ApplyCameraLean()
    {
        if (cameraPivot == null)
            return;

        cameraPivot.localPosition =
            cameraRestLocalPosition +
            Vector3.forward * (peekOffset * peekAmount);
    }


    private void RestoreCamera()
    {
        if (cameraPivot == null)
            return;

        cameraPivot.localPosition = cameraRestLocalPosition;
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private void Teleport(
        Vector3 position,
        Quaternion rotation)
    {
        /*
         * CharacterController overwrites transform writes while
         * enabled, so it has to be switched off around the move.
         */

        bool wasEnabled = controller.enabled;

        controller.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        controller.enabled = wasEnabled;
    }


    private void OnDisable()
    {
        if (IsHiding)
        {
            Exit();
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Getting into and out of hiding spots.
///
/// Hiding is a pose, not a teleport: pressing E ducks the player into
/// the spot over a fraction of a second - the body slides to the hide
/// point, the camera drops to the spot's eye height and turns to face
/// out of the opening. While hidden the body is held still and the
/// view can only sweep the way out (under a desk: left and right
/// between the legs, never round into the furniture). E again climbs
/// back out to where the player stood.
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


    [Header("Noise")]
    /// <summary>
    /// Ducking under a desk is not silent. Small, but enough that hiding
    /// while something is already looking at you is a bad idea.
    /// </summary>
    [SerializeField, Min(0f)]
    private float enterNoiseRadius = 4f;


    [Header("Debug")]
    [SerializeField]
    private bool logHiding = true;


    // =========================================================
    // STATE
    // =========================================================

    private enum Phase { Out, Entering, Hidden, Leaving }

    private PlayerController player;

    private CharacterController controller;

    private Concealment concealment;

    private PlayerInteractor interactor;

    private Transform cameraPivot;

    private HidingSpot current;

    private XenoBlackboard board;

    private Phase phase = Phase.Out;

    private float t;

    /* The move being tweened: body position and yaw, camera height and pitch. */

    private Vector3 fromPos, toPos;

    private float fromYaw, toYaw, fromEye, toEye, fromPitch, toPitch;

    /* Where the player stood, and how high their eyes were, before hiding. */

    private Vector3 exitPosition;

    private float standingEye;


    public bool IsHiding => current != null;

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

        interactor = GetComponent<PlayerInteractor>();

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
        if (phase == Phase.Entering || phase == Phase.Leaving)
        {
            Tween();

            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || !keyboard[enterKey].wasPressedThisFrame)
            return;

        if (phase == Phase.Hidden)
        {
            Leave();

            return;
        }

        /*
         * E is shared with objectives. Aiming at one means E is for
         * that, not for ducking under the nearest desk.
         */

        if (interactor != null && interactor.HasFocus)
            return;

        TryEnter();
    }


    // =========================================================
    // ENTER / LEAVE
    // =========================================================

    private void TryEnter()
    {
        if (!HidingSpot.TryGetInteractable(transform.position, out HidingSpot spot))
            return;

        current = spot;

        spot.Occupied = true;

        exitPosition = transform.position;

        standingEye = cameraPivot != null ? cameraPivot.localPosition.y : 1.6f;

        player.HoldStill = true;
        player.MovementLocked = true;
        player.LookLocked = true;

        BeginTween(
            spot.HidePosition,
            spot.HideRotation.eulerAngles.y,
            spot.EyeHeight >= 0f ? spot.EyeHeight : standingEye,
            4f,
            spot.TransitionTime);

        phase = Phase.Entering;

        concealment.Set(spot.VisibilityWhileHidden, spot.NoiseWhileHidden);

        PublishHidingFlag(true);

        NoiseBus.Emit(transform.position, enterNoiseRadius, NoiseType.Object, gameObject);

        if (logHiding)
        {
            Debug.Log("[PlayerHiding] Hiding in " + spot.name, this);
        }
    }


    private void Leave()
    {
        player.ClearLookLimit();
        player.LookLocked = true;

        BeginTween(
            exitPosition,
            transform.eulerAngles.y,
            standingEye,
            0f,
            current != null ? current.TransitionTime : 0.4f);

        phase = Phase.Leaving;

        NoiseBus.Emit(transform.position, enterNoiseRadius, NoiseType.Object, gameObject);
    }


    private void BeginTween(Vector3 position, float yaw, float eye, float pitch, float seconds)
    {
        fromPos = transform.position;
        toPos = position;

        fromYaw = transform.eulerAngles.y;
        toYaw = yaw;

        fromEye = cameraPivot != null ? cameraPivot.localPosition.y : eye;
        toEye = eye;

        fromPitch = player.Pitch;
        toPitch = pitch;

        tweenTime = Mathf.Max(0.05f, seconds);

        t = 0f;
    }


    private float tweenTime;


    private void Tween()
    {
        t = Mathf.Min(1f, t + Time.deltaTime / tweenTime);

        float k = Mathf.SmoothStep(0f, 1f, t);

        MoveBody(Vector3.Lerp(fromPos, toPos, k));

        player.SetLook(Mathf.LerpAngle(fromYaw, toYaw, k), Mathf.Lerp(fromPitch, toPitch, k));

        SetEye(Mathf.Lerp(fromEye, toEye, k));

        if (t < 1f)
            return;

        if (phase == Phase.Entering)
        {
            phase = Phase.Hidden;

            player.LookLocked = false;

            player.LimitLook(
                toYaw,
                current.LookYawRange,
                current.LookPitchUp,
                current.LookPitchDown);

            return;
        }

        /* Out again. */

        phase = Phase.Out;

        if (current != null)
        {
            current.Occupied = false;
        }

        current = null;

        player.LookLocked = false;
        player.HoldStill = false;
        player.MovementLocked = false;

        concealment.Clear();

        PublishHidingFlag(false);

        if (logHiding)
        {
            Debug.Log("[PlayerHiding] Left cover", this);
        }
    }


    // =========================================================
    // HELPERS
    // =========================================================

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
        /*
         * No longer written. Telling the creature the moment you hide
         * was the biggest cheat it had - it "knew" you were in a locker
         * whenever it had any lead at all. The belief is now formed by
         * XenoHuntMemory from the creature's own senses: it lost sight of
         * you right beside a hiding spot, or heard one.
         */
    }


    /// <summary>
    /// CharacterController overwrites transform writes while enabled,
    /// so it is switched off around each move.
    /// </summary>
    private void MoveBody(Vector3 position)
    {
        bool wasEnabled = controller.enabled;

        controller.enabled = false;

        transform.position = position;

        controller.enabled = wasEnabled;
    }


    private void SetEye(float height)
    {
        if (cameraPivot == null)
            return;

        Vector3 local = cameraPivot.localPosition;

        local.y = height;

        cameraPivot.localPosition = local;
    }


    private void OnDisable()
    {
        if (current != null)
        {
            current.Occupied = false;

            current = null;
        }

        if (player != null)
        {
            player.ClearLookLimit();
            player.LookLocked = false;
            player.HoldStill = false;
            player.MovementLocked = false;
        }

        if (phase != Phase.Out)
        {
            SetEye(standingEye);
        }

        phase = Phase.Out;

        concealment?.Clear();
    }
}

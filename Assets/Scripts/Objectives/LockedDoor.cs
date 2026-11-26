using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A door that stays shut until the night's progress allows it.
///
///   Gate    the Special Collections gate - two locks, the east and west
///           History keys. Turning them clanks.
///   Vault   the Treasury strongroom - a dial. Open once all three
///           clocks have been heard; dialling is quiet.
///   Bolted  the Special Collections fire door - bolted from inside,
///           so it is an escape route for whoever is already in.
///
/// Shut, a box collider stops the player and a carving NavMesh
/// obstacle stops the caretaker - the wing is sealed to both until the
/// gate opens. Open, both go and the leaves swing away from the
/// approach side.
/// </summary>
public class LockedDoor : Interactable
{
    public enum Lock { Gate, Vault, Bolted }


    // =========================================================
    // INSPECTOR
    // =========================================================

    [SerializeField]
    private Lock kind;

    /// <summary>Pivots at each leaf's hinge; the leaf geometry is their child.</summary>
    [SerializeField]
    private Transform[] leaves = new Transform[0];

    /// <summary>World direction the leaves swing towards.</summary>
    [SerializeField]
    private Vector3 swingTowards = Vector3.forward;

    /// <summary>For the bolt: world direction of the side it opens from.</summary>
    [SerializeField]
    private Vector3 boltSide = Vector3.back;

    [SerializeField]
    private Collider blocker;

    [SerializeField]
    private NavMeshObstacle obstacle;

    [SerializeField, Range(60f, 120f)]
    private float openAngle = 100f;


    [Header("Noise")]
    [SerializeField, Min(0f)]
    private float workNoiseRadius = 10f;

    [SerializeField, Min(0.1f)]
    private float workNoiseInterval = 1.0f;

    [SerializeField, Min(0f)]
    private float openNoiseRadius = 18f;


    private readonly List<float> signs = new List<float>();

    private readonly List<Quaternion> closed = new List<Quaternion>();

    private bool opened;

    private float angle;

    private float nextNoise;

    private Transform player;


    public Lock Kind => kind;

    public bool Opened => opened;


    public override string Label
    {
        get
        {
            switch (kind)
            {
                case Lock.Gate: return "Special Collections gate";
                case Lock.Vault: return "Strongroom door";
                default: return "Fire door";
            }
        }
    }


    public void Setup(
        Lock lockKind, Transform[] leafPivots, Vector3 swing, Vector3 openSide,
        Collider block, NavMeshObstacle navBlock)
    {
        kind = lockKind;
        leaves = leafPivots;
        swingTowards = swing.normalized;
        boltSide = openSide.normalized;
        blocker = block;
        obstacle = navBlock;
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Start()
    {
        /* The builder saves these off so the NavMesh bake ignores them. */

        if (!opened)
        {
            if (blocker != null) blocker.enabled = true;
            if (obstacle != null) obstacle.enabled = true;
        }

        PlayerController pc = FindFirstObjectByType<PlayerController>();

        player = pc != null ? pc.transform : null;

        /* Pick each leaf's swing sign so its free edge moves away from the approach. */

        foreach (Transform pivot in leaves)
        {
            closed.Add(pivot.localRotation);

            Renderer r = pivot.GetComponentInChildren<Renderer>();

            Vector3 arm = r != null ? r.bounds.center - pivot.position : pivot.right;

            arm.y = 0f;

            Vector3 swung = Quaternion.AngleAxis(10f, Vector3.up) * arm;

            signs.Add(Vector3.Dot(swung - arm, swingTowards) >= 0f ? 1f : -1f);
        }
    }


    private void Update()
    {
        if (!opened || angle >= openAngle)
            return;

        angle = Mathf.MoveTowards(angle, openAngle, Time.deltaTime * 70f);

        for (int i = 0; i < leaves.Length; i++)
        {
            leaves[i].localRotation =
                Quaternion.AngleAxis(angle * signs[i], Vector3.up) * closed[i];
        }
    }


    // =========================================================
    // PROMPT
    // =========================================================

    /// <summary>
    /// A bolted door seen from the wrong side is scenery, not a task,
    /// so it never raises a lock marker on the corridor side.
    /// </summary>
    public override InteractState TaskState =>
        kind == Lock.Bolted && GetPrompt().State != InteractState.Available
            ? InteractState.None
            : GetPrompt().State;


    public override InteractPrompt GetPrompt()
    {
        ObjectiveManager objectives = ObjectiveManager.Instance;

        if (objectives == null || opened)
            return InteractPrompt.None;

        switch (kind)
        {
            case Lock.Gate:
                return GatePrompt(objectives);

            case Lock.Vault:
                return VaultPrompt(objectives);

            default:
                return BoltPrompt();
        }
    }


    private InteractPrompt GatePrompt(ObjectiveManager objectives)
    {
        if (!objectives.CanOpenGate)
        {
            int have = (objectives.HasKey(GalleryKey.Egyptian) ? 1 : 0) +
                       (objectives.HasKey(GalleryKey.NaturalHistory) ? 1 : 0);

            return new InteractPrompt
            {
                State = InteractState.Blocked,
                Action = "The Special Collections gate is locked",
                Detail = "Two locks - the east gallery key and the west gallery key (" +
                         have + "/2)"
            };
        }

        return new InteractPrompt
        {
            State = InteractState.Available,
            Action = "Unlock the Special Collections gate",
            Detail = HoldProgress > 0f
                ? "Turning the locks " + Mathf.RoundToInt(HoldProgress * 100f) + "%"
                : "Two heavy locks - they will clank",
            Hold = true,
            Loud = true
        };
    }


    private InteractPrompt VaultPrompt(ObjectiveManager objectives)
    {
        if (!objectives.CodeKnown)
        {
            return new InteractPrompt
            {
                State = InteractState.Blocked,
                Action = "The strongroom is locked",
                Detail = "A three-number dial. Count each clock's strikes: " +
                         objectives.CodeDisplay
            };
        }

        return new InteractPrompt
        {
            State = InteractState.Available,
            Action = "Dial " + objectives.CodeDisplay,
            Detail = HoldProgress > 0f
                ? "Dialling " + Mathf.RoundToInt(HoldProgress * 100f) + "%"
                : "Quiet clicks - East, West, Special Collections",
            Hold = true,
            Loud = false
        };
    }


    private InteractPrompt BoltPrompt()
    {
        /*
         * Inside means just behind the door on the bolt's side - the far
         * half-space also holds the whole central hall.
         */

        Vector3 offset = player != null ? player.position - transform.position : Vector3.zero;

        bool inside = player != null &&
                      Vector3.Dot(offset, boltSide) > 0f &&
                      offset.magnitude < 6f;

        if (!inside)
        {
            return new InteractPrompt
            {
                State = InteractState.Blocked,
                Action = "Bolted from the other side",
                Detail = "It only opens from inside Special Collections"
            };
        }

        return new InteractPrompt
        {
            State = InteractState.Available,
            Action = "Draw the bolt",
            Detail = "Opens a way out to the service corridor",
            Hold = true,
            Loud = false
        };
    }


    // =========================================================
    // OPENING
    // =========================================================

    protected override void OnHoldTick(GameObject who)
    {
        if (Time.time < nextNoise)
            return;

        nextNoise = Time.time + workNoiseInterval;

        GameArt.Play(GameArt.Tick, FocusPoint, kind == Lock.Gate ? 1f : 0.5f);

        if (kind == Lock.Gate)
        {
            NoiseBus.Emit(FocusPoint, workNoiseRadius, NoiseType.Door, gameObject);
        }
    }


    protected override void OnHoldComplete(GameObject who)
    {
        Open();

        ObjectiveManager objectives = ObjectiveManager.Instance;

        switch (kind)
        {
            case Lock.Gate:
                objectives?.MarkGateOpen();
                break;

            case Lock.Vault:
                objectives?.MarkVaultOpen();
                break;

            default:
                objectives?.Say("The bolt slides back. A way out to the corridor.",
                    MessageTone.Success);
                break;
        }
    }


    /// <summary>Unblocks the way and starts the leaves swinging.</summary>
    public void Open()
    {
        if (opened)
            return;

        opened = true;

        if (blocker != null)
        {
            blocker.enabled = false;
        }

        if (obstacle != null)
        {
            obstacle.enabled = false;
        }

        GameArt.Play(GameArt.Unlock, FocusPoint, kind == Lock.Gate ? 1f : 0.8f);

        GameArt.Play(GameArt.Creak, FocusPoint, kind == Lock.Gate ? 1f : 0.6f);

        float radius = kind == Lock.Gate ? openNoiseRadius : openNoiseRadius * 0.5f;

        NoiseBus.Emit(FocusPoint, radius, NoiseType.Door, gameObject);
    }
}

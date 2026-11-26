using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The museum's front doors - locked until the master key from the
/// strongroom turns them, then a long, loud heave to get them open.
///
/// The heave is the final stand: ten seconds at a fixed spot the player
/// cannot leave without losing ground, every creak of the hinges a noise
/// the caretaker hears. The doors open a little with every second held,
/// so the player can see how close they are.
/// </summary>
public class ExitDoorObjective : Interactable
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    /// <summary>World direction the doors swing towards - outside.</summary>
    [SerializeField]
    private Vector3 outward = Vector3.back;

    [SerializeField, Range(0f, 60f)]
    private float ajarAngle = 22f;

    [SerializeField, Range(60f, 120f)]
    private float openAngle = 100f;


    [Header("Noise")]
    [SerializeField, Min(0f)]
    private float creakNoiseRadius = 20f;

    [SerializeField, Min(0.1f)]
    private float creakInterval = 1.8f;

    [SerializeField, Min(0f)]
    private float openNoiseRadius = 40f;


    private class Leaf
    {
        public Transform Transform;
        public Vector3 Hinge;
        public Vector3 StartPosition;
        public Quaternion StartRotation;
        public float Sign;
        public Collider[] Colliders;
    }

    private readonly List<Leaf> leaves = new List<Leaf>();

    private float nextCreak;

    private float angle;

    private bool opened;

    private float escapeAt = -1f;


    public override string Label => "Front doors";


    public void Setup(Vector3 outwardDirection)
    {
        outward = outwardDirection.normalized;
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Start()
    {
        FindLeaves();
    }


    /// <summary>
    /// The door model's two leaves are its child objects. Each swings
    /// about its outer edge, in whichever direction takes it outside.
    /// </summary>
    private void FindLeaves()
    {
        foreach (Transform child in transform)
        {
            Renderer r = child.GetComponentInChildren<Renderer>();

            if (r == null)
                continue;

            Bounds b = r.bounds;

            Vector3 across = Vector3.Cross(Vector3.up, outward).normalized;

            /* Hinge on the edge furthest from the doorway's centre. */

            float side = Vector3.Dot(b.center - transform.position, across);

            Vector3 hinge =
                b.center +
                across * (Mathf.Sign(side) * Vector3.Scale(b.extents, Abs(across)).magnitude);

            hinge.y = b.center.y;

            /* Swing sign that moves the free edge outwards. */

            Vector3 arm = b.center - hinge;

            Vector3 swung = Quaternion.AngleAxis(10f, Vector3.up) * arm;

            float sign = Vector3.Dot(swung - arm, outward) >= 0f ? 1f : -1f;

            leaves.Add(new Leaf
            {
                Transform = child,
                Hinge = hinge,
                StartPosition = child.position,
                StartRotation = child.rotation,
                Sign = sign,
                Colliders = child.GetComponentsInChildren<Collider>()
            });
        }
    }


    private static Vector3 Abs(Vector3 v) =>
        new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));


    // =========================================================
    // PROMPT
    // =========================================================

    public override InteractPrompt GetPrompt()
    {
        ObjectiveManager objectives = ObjectiveManager.Instance;

        if (objectives == null || opened)
            return InteractPrompt.None;

        if (!objectives.HasKey(GalleryKey.FineArt))
        {
            return new InteractPrompt
            {
                State = InteractState.Blocked,
                Action = "The front doors are locked",
                Detail = "Needs the master key - it is kept in the Treasury strongroom"
            };
        }

        return new InteractPrompt
        {
            State = InteractState.Available,
            Action = "Unlock and heave the front doors open",
            Detail = HoldProgress > 0f
                ? "Open " + Mathf.RoundToInt(HoldProgress * 100f) + "% - don't stop"
                : "The hinges scream. He will come.",
            Hold = true,
            Loud = true
        };
    }


    // =========================================================
    // HEAVING
    // =========================================================

    protected override void OnHoldTick(GameObject who)
    {
        SetAngle(HoldProgress * ajarAngle);

        if (Time.time < nextCreak)
            return;

        nextCreak = Time.time + creakInterval;

        GameArt.Play(GameArt.Creak, FocusPoint, 1f);

        NoiseBus.Emit(FocusPoint, creakNoiseRadius, NoiseType.Door, gameObject);
    }


    protected override void OnHoldComplete(GameObject who)
    {
        opened = true;

        GameArt.Play(GameArt.Creak, FocusPoint, 1f);

        NoiseBus.Emit(FocusPoint, openNoiseRadius, NoiseType.Door, gameObject);

        foreach (Leaf leaf in leaves)
        {
            foreach (Collider c in leaf.Colliders)
            {
                c.enabled = false;
            }
        }

        ObjectiveManager.Instance?.Say(
            "Cold night air. You're out.", MessageTone.Success);

        escapeAt = Time.time + 1.2f;
    }


    private void Update()
    {
        if (!opened)
            return;

        angle = Mathf.MoveTowards(angle, openAngle, Time.deltaTime * 90f);

        SetAngle(angle);

        if (escapeAt > 0f && Time.time >= escapeAt)
        {
            escapeAt = -1f;

            ObjectiveManager.Instance?.MarkEscaped();
        }
    }


    private void SetAngle(float degrees)
    {
        angle = Mathf.Max(angle, degrees);

        foreach (Leaf leaf in leaves)
        {
            Quaternion turn = Quaternion.AngleAxis(angle * leaf.Sign, Vector3.up);

            leaf.Transform.SetPositionAndRotation(
                leaf.Hinge + turn * (leaf.StartPosition - leaf.Hinge),
                turn * leaf.StartRotation);
        }
    }
}

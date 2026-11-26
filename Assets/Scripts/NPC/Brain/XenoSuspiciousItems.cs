using UnityEngine;

/// <summary>
/// The stages alien_suspicious_item_common walks an item through.
///
/// Straight from SuspiciousItemShouldDoStage / SuspiciousItemDoneStage
/// in the export. Ordered, because the graph only ever moves forward
/// through them.
/// </summary>
public enum SuspiciousItemStage
{
    InitialReaction = 0,
    MoveCloseTo = 1,
    CloseToReaction = 2,
    CloseToWaitForGroupMembers = 3,
    SearchArea = 4,
    Finished = 5
}


/// <summary>
/// One creature's relationship with one suspicious item.
///
/// The behaviour tree owns the DECISIONS - which stage to run, when to
/// move, when to react. This owns the BOOKKEEPING: which item is being
/// processed, how far through it the creature is, and when to let go.
/// Keeping them apart is what lets the tree read exactly like the
/// export, with SuspiciousItemShouldDoStage as a one line condition.
///
/// THIS IS NOT A TWO-BRAIN VIOLATION, and it is worth being explicit
/// about why. An item is an object in the world that made a noise the
/// creature could hear. Holding a reference to one is no different from
/// remembering a door. The rule is about the player, and the player is
/// never reachable from here.
/// </summary>
[DisallowMultipleComponent]
public class XenoSuspiciousItems : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Acquisition")]
    /// <summary>
    /// How long the creature will persist with one item before writing
    /// it off. Without a cap an unreachable flare behind glass would
    /// hold the creature forever.
    /// </summary>
    [SerializeField, Min(1f)]
    private float maximumProcessingTime = 40f;


    [Header("Debug")]
    [SerializeField]
    private bool logStages = false;


    // =========================================================
    // STATE
    // =========================================================

    private SuspiciousItem current;

    private SuspiciousItemStage stage;

    private float acquiredAt;


    /// <summary>
    /// SuspiciousItemInProgress.
    /// </summary>
    public bool InProgress =>
        current != null &&
        !current.Consumed &&
        !current.IsExpired &&
        stage != SuspiciousItemStage.Finished;


    /// <summary>
    /// The priority the TREE sees: the item's own, lowered by Demotion.
    /// Learning raises Demotion once decoys keep leading nowhere, which
    /// moves a thrown noisemaker below the hunt in the root selector -
    /// the creature stops dropping everything for it.
    /// </summary>
    public SuspiciousItemPriority Priority =>
        current != null
            ? Demote(current.Priority, Demotion)
            : SuspiciousItemPriority.Low;

    /// <summary>The item's own priority, before any learned demotion.</summary>
    public SuspiciousItemPriority RawPriority =>
        current != null ? current.Priority : SuspiciousItemPriority.Low;

    /// <summary>How many levels to lower HIGH items by. Written by XenoLearning.</summary>
    public int Demotion { get; set; }

    public float AcquiredAt => acquiredAt;

    /// <summary>Raised when an item is let go: its raw priority, when it was taken on, and whether it was worked to the end.</summary>
    public event System.Action<SuspiciousItemPriority, float, bool> OnItemReleased;


    private static SuspiciousItemPriority Demote(SuspiciousItemPriority p, int levels)
    {
        if (p != SuspiciousItemPriority.High || levels <= 0)
            return p;

        return levels == 1 ? SuspiciousItemPriority.Medium : SuspiciousItemPriority.Low;
    }


    public Vector3 Position =>
        current != null ? current.Position : transform.position;


    public SuspiciousItemStage Stage => stage;


    // =========================================================
    // UNITY
    // =========================================================

    private void Update()
    {
        if (current == null)
        {
            TryAcquire();

            return;
        }

        bool stale =
            current.Consumed ||
            current.IsExpired ||
            Time.time - acquiredAt > maximumProcessingTime;

        if (stale)
        {
            /*
             * Consumed on the way out even when it merely timed out.
             * An item the creature gave up on must not immediately
             * re-attract it, which is the loop that would otherwise
             * pin it in place next to an unreachable object.
             */

            OnItemReleased?.Invoke(current.Priority, acquiredAt, false);

            current.MarkConsumed();

            Clear();
        }
    }


    private void TryAcquire()
    {
        SuspiciousItem found =
            SuspiciousItemBus.FindBest(transform.position);

        if (found == null)
            return;

        current = found;

        stage = SuspiciousItemStage.InitialReaction;

        acquiredAt = Time.time;

        if (logStages)
        {
            Debug.Log(
                "[SuspiciousItem] acquired " + found.Priority +
                " at " + found.Position,
                this
            );
        }
    }


    // =========================================================
    // TREE SURFACE
    // =========================================================

    /// <summary>SuspiciousItemShouldDoStage(STAGE).</summary>
    public bool ShouldDoStage(SuspiciousItemStage value)
    {
        return InProgress && stage == value;
    }


    /// <summary>
    /// SuspiciousItemDoneStage(STAGE).
    ///
    /// Only ever advances, and only from the stage actually running -
    /// a done for a stage already passed is ignored rather than
    /// rewinding, because the branches call it from more than one
    /// place.
    /// </summary>
    public void DoneStage(SuspiciousItemStage value)
    {
        if (!InProgress || stage != value)
            return;

        stage = (SuspiciousItemStage)((int)stage + 1);

        if (logStages)
        {
            Debug.Log(
                "[SuspiciousItem] " + value + " done -> " + stage,
                this
            );
        }

        if (stage == SuspiciousItemStage.Finished)
        {
            OnItemReleased?.Invoke(current.Priority, acquiredAt, true);

            current.MarkConsumed();

            Clear();
        }
    }


    /// <summary>SuspiciousItemIsWithinDistance(n).</summary>
    public bool IsWithinDistance(float metres)
    {
        if (current == null)
            return false;

        Vector3 delta = current.Position - transform.position;

        delta.y = 0f;

        return delta.magnitude <= metres;
    }


    public void Clear()
    {
        current = null;

        stage = SuspiciousItemStage.Finished;
    }


    /// <summary>For the debug overlay.</summary>
    public string Describe()
    {
        if (!InProgress)
            return "none";

        return current.Priority + " / " + stage;
    }
}

using UnityEngine;

/// <summary>
/// The creature adapting to how THIS player plays.
///
/// XenoProgression makes it look like it learns by switching behaviours
/// on at fixed times. This is the other half: it counts what the
/// creature has actually experienced, from its own senses, and changes
/// how it behaves once a habit is clear.
///
///   HIDING AWARENESS  from finding you in hiding spots, and from losing
///                     you right next to one.
///                     Level 1: while roaming it peeks into hiding spots
///                              it passes, instead of walking by.
///                     Level 2: hunts check more spots, and the spot it
///                              suspects comes first.
///
///   DECOY FATIGUE     from investigating thrown decoys that led nowhere.
///                     Level 1: a decoy no longer outranks hunting you.
///                     Level 2: a decoy ranks with a swinging door.
///                     A decoy that DID lead to you takes fatigue back
///                     down - the trick only wears out while it fails.
///
/// TWO-BRAIN RULE: every counter here is fed by the creature's own
/// experience (what it found, what it lost, what led nowhere). Nothing
/// reads the player.
/// </summary>
[DisallowMultipleComponent]
public class XenoLearning : MonoBehaviour
{
    [Header("Hiding awareness")]
    [SerializeField, Min(1)]
    private int catchesForLevel1 = 1;

    [SerializeField, Min(1)]
    private int suspicionsForLevel1 = 2;

    [SerializeField, Min(1)]
    private int catchesForLevel2 = 2;

    [SerializeField, Min(1)]
    private int suspicionsForLevel2 = 4;

    [Header("Decoy fatigue")]
    [SerializeField, Min(1)]
    private int fruitlessForLevel1 = 2;

    [SerializeField, Min(1)]
    private int fruitlessForLevel2 = 4;

    [Header("Debug")]
    [SerializeField]
    private bool logLearning = true;


    private XenoSuspiciousItems items;

    private XenoHuntMemory hunt;


    public int CatchesInHiding { get; private set; }

    public int SuspectedHides { get; private set; }

    public int FruitlessDecoys { get; private set; }

    public int FruitfulDecoys { get; private set; }

    public int HidingAwareness { get; private set; }

    public int DecoyFatigue { get; private set; }


    private void Awake()
    {
        items = GetComponent<XenoSuspiciousItems>();

        hunt = GetComponent<XenoHuntMemory>();
    }


    // =========================================================
    // CARRY-OVER ACROSS LEVEL RESTARTS
    // =========================================================

    /// <summary>
    /// Being caught reloads the level. Without this the NPC would forget
    /// everything it learned about the player each time; with it, a
    /// player who keeps hiding under desks meets an NPC that already
    /// checks desks on the next attempt.
    /// </summary>
    [SerializeField]
    private bool keepAcrossRestarts = true;

    private struct Snapshot
    {
        public int Catches, Suspected, Fruitless, Fruitful;
    }

    private static Snapshot? carried;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCarried()
    {
        carried = null;
    }

    /// <summary>Stores the current creature's counters for the next scene load.</summary>
    public static void CarryOver()
    {
        XenoLearning live = FindFirstObjectByType<XenoLearning>();

        if (live == null || !live.keepAcrossRestarts)
            return;

        carried = new Snapshot
        {
            Catches = live.CatchesInHiding,
            Suspected = live.SuspectedHides,
            Fruitless = live.FruitlessDecoys,
            Fruitful = live.FruitfulDecoys
        };
    }

    private void Start()
    {
        if (!keepAcrossRestarts || carried == null)
            return;

        Snapshot s = carried.Value;

        CatchesInHiding = s.Catches;
        SuspectedHides = s.Suspected;
        FruitlessDecoys = s.Fruitless;
        FruitfulDecoys = s.Fruitful;

        Recompute("carried over from the previous attempt");
    }


    private void OnEnable()
    {
        if (items == null) items = GetComponent<XenoSuspiciousItems>();

        if (items != null) items.OnItemReleased += HandleItemReleased;
    }


    private void OnDisable()
    {
        if (items != null) items.OnItemReleased -= HandleItemReleased;
    }


    // =========================================================
    // EXPERIENCE
    // =========================================================

    public void NoteCaughtInHiding(HidingSpot spot)
    {
        CatchesInHiding++;

        Recompute("caught the target hiding in " + (spot != null ? spot.name : "a spot"));
    }


    public void NoteSuspectedHide(HidingSpot spot)
    {
        SuspectedHides++;

        Recompute("lost the target right beside " + (spot != null ? spot.name : "a spot"));
    }


    /// <summary>
    /// A decoy is judged by whether the creature sensed its target while
    /// it was dealing with it. A noisemaker that brought it face to face
    /// with you worked; one that had it circling an empty room did not.
    /// </summary>
    private void HandleItemReleased(SuspiciousItemPriority raw, float acquiredAt, bool completed)
    {
        if (raw != SuspiciousItemPriority.High)
            return;

        if (hunt == null) hunt = GetComponent<XenoHuntMemory>();

        /*
         * Vision only. The decoy's own sound is a hearing contact, so
         * "sensed anything since" would count every decoy as having worked.
         */
        bool fruitful = hunt != null && hunt.LastVisualSightingTime > acquiredAt;

        if (fruitful)
        {
            FruitfulDecoys++;
            FruitlessDecoys = Mathf.Max(0, FruitlessDecoys - 1);
            Recompute("a decoy led to the target");
        }
        else
        {
            FruitlessDecoys++;
            Recompute("a decoy led nowhere");
        }
    }


    private void Recompute(string why)
    {
        int hiding =
            CatchesInHiding >= catchesForLevel2 || SuspectedHides >= suspicionsForLevel2 ? 2 :
            CatchesInHiding >= catchesForLevel1 || SuspectedHides >= suspicionsForLevel1 ? 1 : 0;

        int fatigue =
            FruitlessDecoys >= fruitlessForLevel2 ? 2 :
            FruitlessDecoys >= fruitlessForLevel1 ? 1 : 0;

        if (logLearning)
        {
            string change = "";

            if (hiding != HidingAwareness) change += "  hiding awareness " + HidingAwareness + " -> " + hiding;

            if (fatigue != DecoyFatigue) change += "  decoy fatigue " + DecoyFatigue + " -> " + fatigue;

            Debug.Log("[Learning] " + name + ": " + why + change, this);
        }

        HidingAwareness = hiding;

        DecoyFatigue = fatigue;

        if (items != null) items.Demotion = DecoyFatigue;

        /*
         * The item ranking alone is not enough: the decoy's SOUND is
         * also heard as a lead on the target, and the hunt goes to
         * leads. Recognising the sound is what actually stops it
         * running to every noisemaker.
         */
        HearingSense hearing = GetComponentInChildren<HearingSense>();

        if (hearing != null)
        {
            hearing.DistractionScale = DecoyFatigue == 0 ? 1f : DecoyFatigue == 1 ? 0.45f : 0.12f;
        }
    }


    public void ResetAll()
    {
        CatchesInHiding = SuspectedHides = FruitlessDecoys = FruitfulDecoys = 0;

        HidingAwareness = DecoyFatigue = 0;

        if (items != null) items.Demotion = 0;

        HearingSense hearing = GetComponentInChildren<HearingSense>();

        if (hearing != null) hearing.DistractionScale = 1f;
    }


    public string Describe()
    {
        return "hiding awareness " + HidingAwareness + " (caught " + CatchesInHiding + ", suspected " + SuspectedHides + ")\n" +
               "decoy fatigue " + DecoyFatigue + " (fruitless " + FruitlessDecoys + ", fruitful " + FruitfulDecoys + ")";
    }
}

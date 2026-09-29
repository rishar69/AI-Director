using System;
using UnityEngine;

/// <summary>
/// The four things EventAOccuredAfterB compares.
///
/// Not a general event system - these are the only four the exports
/// ever ask about, and they are exactly the four behaviours that
/// compete inside SEL_52.
/// </summary>
public enum XenoEvent
{
    SensedTarget = 0,
    TargetHiding = 1,
    SensedSuspiciousItem = 2,
    SuspectTargetResponse = 3
}


/// <summary>
/// When each of the four competing stimuli last happened.
///
/// WHAT THIS IS FOR. alien_all_search_variants does not pick between
/// hiding, suspicious items, searching and the suspect response by
/// priority - it picks by RECENCY. Each child of SEL_52 is gated on its
/// own event being more recent than every event belonging to a child
/// below it. Without these timestamps that whole discrimination is
/// missing and the branches just run in fixed order.
///
/// Three of the four are observed here by polling rather than pushed in
/// from outside, because the things that would push them - the senses,
/// the blackboard, the item tracker - already hold the information and
/// should not have to know this exists.
/// </summary>
[DisallowMultipleComponent]
public class XenoEventLog : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Sensing")]
    /// <summary>
    /// The threshold that counts as having SENSED_TARGET. Activated,
    /// not Lower - a faint rustle is not "I sensed them", and using
    /// Lower would keep this event permanently the freshest and starve
    /// every other branch.
    /// </summary>
    [SerializeField]
    private SenseThreshold sensedTargetThreshold =
        SenseThreshold.Activated;


    [Header("Debug")]
    [SerializeField]
    private bool logEvents = false;


    // =========================================================
    // STATE
    // =========================================================

    private readonly float[] stamps =
        new float[Enum.GetValues(typeof(XenoEvent)).Length];

    private XenoSenses senses;

    private XenoBlackboard blackboard;

    private XenoSuspiciousItems items;

    private bool wasHiding;

    private bool hadItem;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        senses = GetComponent<XenoSenses>();

        blackboard = GetComponent<XenoBlackboard>();

        items = GetComponent<XenoSuspiciousItems>();

        for (int i = 0; i < stamps.Length; i++)
        {
            stamps[i] = -1f;
        }
    }


    private void Update()
    {
        Observe();
    }


    /// <summary>
    /// Rising edges only.
    ///
    /// A level that is merely still true is not an event - stamping it
    /// every frame would make whichever stimulus is ongoing eternally
    /// the most recent, and recency would stop meaning anything.
    /// </summary>
    private void Observe()
    {
        if (senses != null &&
            senses.IsAnySenseActivationAbove(sensedTargetThreshold))
        {
            /*
             * The exception: sensing is continuous by nature, so this
             * one restamps while it holds. It is the baseline every
             * other event has to beat.
             */

            Record(XenoEvent.SensedTarget);
        }

        if (blackboard != null)
        {
            bool hiding =
                blackboard.GetTargetFlag(TargetLogicFlag.PlayerHiding);

            if (hiding && !wasHiding)
            {
                Record(XenoEvent.TargetHiding);
            }

            wasHiding = hiding;
        }

        if (items != null)
        {
            bool hasItem = items.InProgress;

            if (hasItem && !hadItem)
            {
                Record(XenoEvent.SensedSuspiciousItem);
            }

            hadItem = hasItem;
        }
    }


    // =========================================================
    // API
    // =========================================================

    public void Record(XenoEvent value)
    {
        stamps[(int)value] = Time.time;

        if (logEvents)
        {
            Debug.Log("[EventLog] " + value, this);
        }
    }


    public bool HasOccurred(XenoEvent value)
    {
        return stamps[(int)value] >= 0f;
    }


    public float SecondsSince(XenoEvent value)
    {
        float stamp = stamps[(int)value];

        return stamp < 0f ? -1f : Time.time - stamp;
    }


    /// <summary>
    /// EventAOccuredAfterB(A, B).
    ///
    /// An A that never happened is never "after" anything. A B that
    /// never happened loses to any A that did - which is what makes a
    /// fresh creature able to act at all, since on the first contact
    /// nothing else has ever occurred.
    /// </summary>
    public bool OccurredAfter(
        XenoEvent a,
        XenoEvent b)
    {
        float stampA = stamps[(int)a];

        if (stampA < 0f)
            return false;

        float stampB = stamps[(int)b];

        if (stampB < 0f)
            return true;

        return stampA >= stampB;
    }


    public void ResetAll()
    {
        for (int i = 0; i < stamps.Length; i++)
        {
            stamps[i] = -1f;
        }

        wasHiding = false;

        hadItem = false;
    }


    /// <summary>For the debug overlay.</summary>
    public string Describe()
    {
        string result = null;

        for (int i = 0; i < stamps.Length; i++)
        {
            if (stamps[i] < 0f)
                continue;

            string line =
                (XenoEvent)i + " " +
                (Time.time - stamps[i]).ToString("0.0") + "s";

            result = result == null ? line : result + ", " + line;
        }

        return result ?? "nothing yet";
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The three keys, one per section (Docs/MuseumLayout.md): Egyptian is
/// the east galleries, NaturalHistory the west galleries - together the
/// two locks on the Special Collections gate. FineArt is the master key
/// in the strongroom; it opens the front doors.
/// </summary>
public enum GalleryKey
{
    Egyptian,
    NaturalHistory,
    FineArt
}


public enum MessageTone
{
    Info,
    Success,
    Warning
}


/// <summary>
/// Tonight's tasks, and the one place their state lives.
///
///   1. West and east galleries: in each, find the key and wind the
///      clock (one is upstairs). A wound clock strikes a number.
///   2. Both keys open the Special Collections gate. Wind the third clock.
///   3. The three numbers, in section order, are the strongroom's
///      combination. Inside is the master key.
///   4. The master key opens the front doors. Heave them open and leave.
///
/// Interactables ask this whether they are allowed to act and tell it
/// when they have; the HUD only ever reads it. Nothing here knows where
/// anything is in the world.
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    public static readonly GalleryKey[] AllKeys =
    {
        GalleryKey.Egyptian,
        GalleryKey.NaturalHistory,
        GalleryKey.FineArt
    };


    // =========================================================
    // STATE
    // =========================================================

    private readonly HashSet<GalleryKey> keys = new HashSet<GalleryKey>();

    private readonly HashSet<GalleryKey> clocks = new HashSet<GalleryKey>();

    /// <summary>Tonight's combination: what each clock strikes, 1-9.</summary>
    private readonly int[] digits = new int[3];

    private PlayerDeathSequence death;

    private float startTime = -1f;

    private float finishTime = -1f;


    /// <summary>Anything changed - the HUD rebuilds its task list.</summary>
    public event Action Changed;

    /// <summary>A line for the player, shown as a toast.</summary>
    public event Action<string, MessageTone> Message;


    public int KeysFound => keys.Count;

    public int ClocksWound => clocks.Count;

    public bool AllClocksWound => clocks.Count == AllKeys.Length;

    public bool GateOpen { get; private set; }

    public bool VaultOpen { get; private set; }

    /// <summary>Both section keys in hand - the gate's two locks can be turned.</summary>
    public bool CanOpenGate =>
        HasKey(GalleryKey.Egyptian) && HasKey(GalleryKey.NaturalHistory);

    /// <summary>Every clock heard - the whole combination is known.</summary>
    public bool CodeKnown => AllClocksWound;

    public bool Escaped { get; private set; }

    public bool Started => startTime >= 0f;

    public int Deaths => death != null ? death.Deaths : 0;

    public float Elapsed =>
        !Started ? 0f :
        (Escaped ? finishTime : Time.time) - startTime;


    public bool HasKey(GalleryKey key) => keys.Contains(key);

    public bool IsClockWound(GalleryKey key) => clocks.Contains(key);

    /// <summary>How many times this clock strikes.</summary>
    public int Digit(GalleryKey key) => digits[(int)key];


    /// <summary>"4 · 7 · _" - the digits heard so far, in section order.</summary>
    public string CodeDisplay
    {
        get
        {
            string[] parts = new string[AllKeys.Length];

            for (int i = 0; i < AllKeys.Length; i++)
            {
                parts[i] = IsClockWound(AllKeys[i]) ? Digit(AllKeys[i]).ToString() : "_";
            }

            return string.Join("  ·  ", parts);
        }
    }


    // =========================================================
    // NAMES
    // =========================================================

    public static string KeyName(GalleryKey key)
    {
        switch (key)
        {
            case GalleryKey.Egyptian: return "east gallery key";
            case GalleryKey.NaturalHistory: return "west gallery key";
            default: return "master key";
        }
    }

    public static string ClockName(GalleryKey key) => WingName(key) + " clock";

    public static string WingName(GalleryKey key)
    {
        switch (key)
        {
            case GalleryKey.Egyptian: return "east gallery";
            case GalleryKey.NaturalHistory: return "west gallery";
            default: return "Special Collections";
        }
    }

    /// <summary>Where the key is - the task list's hint.</summary>
    public static string KeyHint(GalleryKey key)
    {
        switch (key)
        {
            case GalleryKey.Egyptian: return "locked case, Antiquities";
            case GalleryKey.NaturalHistory: return "the Director's desk, upstairs";
            default: return "the strongroom, Special Collections";
        }
    }

    /// <summary>Where the clock is - the task list's hint.</summary>
    public static string ClockHint(GalleryKey key)
    {
        switch (key)
        {
            case GalleryKey.Egyptian: return "lecture hall, upstairs";
            case GalleryKey.NaturalHistory: return "sculpture gallery";
            default: return "temporary exhibition";
        }
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        Instance = this;

        /* A new combination every night, so it has to be listened for. */

        System.Random random = new System.Random(Environment.TickCount);

        for (int i = 0; i < digits.Length; i++)
        {
            digits[i] = random.Next(1, 10);
        }
    }


    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }


    private void Start()
    {
        death = FindFirstObjectByType<PlayerDeathSequence>();
    }


    // =========================================================
    // PROGRESS
    // =========================================================

    /// <summary>Called by the HUD when the intro card is dismissed.</summary>
    public void Begin()
    {
        if (Started)
            return;

        startTime = Time.time;

        Changed?.Invoke();
    }


    public void GiveKey(GalleryKey key)
    {
        if (!keys.Add(key))
            return;

        if (key == GalleryKey.FineArt)
        {
            Say("Found the master key. It opens the front doors.", MessageTone.Success);
        }
        else if (CanOpenGate)
        {
            Say("Found the " + KeyName(key) + ". Both gate keys - the Special Collections gate can be opened.",
                MessageTone.Success);
        }
        else
        {
            Say("Found the " + KeyName(key) + ". One of the two locks on the Special Collections gate.",
                MessageTone.Success);
        }

        Changed?.Invoke();
    }


    /// <summary>
    /// Called when the clock has finished striking - the number is
    /// heard, so it goes into the notebook.
    /// </summary>
    public void MarkClockWound(GalleryKey key)
    {
        if (!clocks.Add(key))
            return;

        int n = Digit(key);

        string struck = "The " + ClockName(key) + " struck " + n +
                        (n == 1 ? " time." : " times.");

        if (CodeKnown)
        {
            Say(struck + " The combination is " + CodeDisplay + ".", MessageTone.Success);
        }
        else
        {
            Say(struck + " Combination so far: " + CodeDisplay, MessageTone.Success);
        }

        Changed?.Invoke();
    }


    public void MarkGateOpen()
    {
        if (GateOpen)
            return;

        GateOpen = true;

        Say("Both locks turn. The Special Collections gate swings open.", MessageTone.Success);

        Changed?.Invoke();
    }


    public void MarkVaultOpen()
    {
        if (VaultOpen)
            return;

        VaultOpen = true;

        Say("The last tumbler drops. The strongroom door swings open.", MessageTone.Success);

        Changed?.Invoke();
    }


    public void MarkEscaped()
    {
        if (Escaped)
            return;

        Escaped = true;

        finishTime = Time.time;

        Changed?.Invoke();
    }


    public void Say(string text, MessageTone tone = MessageTone.Info)
    {
        Message?.Invoke(text, tone);
    }
}

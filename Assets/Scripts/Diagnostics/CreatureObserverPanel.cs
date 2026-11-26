using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Left-hand panel for the observation scene: what the creature
/// senses, what it wants, what it remembers, and what the Director is
/// doing about it.
///
/// Laid out as the two brains, deliberately:
///
///   MICRO (the creature)  - senses, motivations, flags, timers, memory.
///                           Everything here is what the creature
///                           itself knows.
///   MACRO (the Director)  - its gauges and permissions. The Director
///                           knows where the player is; the creature
///                           only ever sees what the Director writes to
///                           the blackboard.
///   GROUND TRUTH          - the real player position, shown so an
///                           observer can see the gap between what the
///                           creature believes and what is true. Nothing
///                           in the AI reads this section.
/// </summary>
public class CreatureObserverPanel : MonoBehaviour
{
    [Header("Targets (found automatically when empty)")]
    [SerializeField]
    private XenoBehaviourTree tree;

    [SerializeField]
    private XenoDirector director;

    [SerializeField]
    private PlayerController player;

    [Header("Toggle")]
    [SerializeField]
    private Key toggleKey = Key.F3;

    [SerializeField]
    private bool visibleOnStart = true;

    [Header("Layout")]
    [SerializeField, Min(220f)]
    private float panelWidth = 330f;

    [SerializeField, Min(8)]
    private int fontSize = 12;


    private bool visible;

    private XenoBlackboard blackboard;

    private XenoSenses senses;

    private PerceptionSystem perception;

    private XenoLocomotion locomotion;

    private XenoEventLog events;

    private XenoHuntMemory huntMemory;

    private GUIStyle text;

    private GUIStyle bold;

    private float line;

    private float contentHeight = 400f;

    private static readonly Color Panel = new Color(0.06f, 0.07f, 0.08f, 0.86f);
    private static readonly Color Dim = new Color(0.55f, 0.57f, 0.6f);
    private static readonly Color Lit = new Color(1f, 0.72f, 0.2f);
    private static readonly Color Micro = new Color(0.55f, 0.8f, 1f);
    private static readonly Color Macro = new Color(0.9f, 0.6f, 1f);
    private static readonly Color Truth = new Color(0.45f, 0.9f, 0.5f);

    private static readonly int ChannelCount = Enum.GetValues(typeof(SenseChannelId)).Length;
    private static readonly Array Motivations = Enum.GetValues(typeof(Motivation));
    private static readonly Array Flags = Enum.GetValues(typeof(LogicFlag));
    private static readonly Array Timers = Enum.GetValues(typeof(TimerId));
    private static readonly Array Permissions = Enum.GetValues(typeof(AlienPermission));


    public bool Visible
    {
        get => visible;
        set => visible = value;
    }


    private void Awake()
    {
        visible = visibleOnStart;

        if (tree == null) tree = FindFirstObjectByType<XenoBehaviourTree>();

        if (director == null) director = FindFirstObjectByType<XenoDirector>();

        if (player == null) player = FindFirstObjectByType<PlayerController>();

        if (tree != null)
        {
            blackboard = tree.GetComponent<XenoBlackboard>();
            senses = tree.GetComponent<XenoSenses>();
            perception = tree.GetComponent<PerceptionSystem>();
            locomotion = tree.GetComponent<XenoLocomotion>();
            events = tree.GetComponent<XenoEventLog>();
            huntMemory = tree.GetComponent<XenoHuntMemory>();
        }
    }


    private void Update()
    {
        Keyboard kb = Keyboard.current;

        if (kb != null && kb[toggleKey].wasPressedThisFrame)
        {
            visible = !visible;
        }
    }


    private void OnGUI()
    {
        if (!visible || tree == null || blackboard == null)
            return;

        EnsureStyles();

        /* Sized to last frame's content, so the panel covers no more of the view than it needs. */
        Rect panel = new Rect(10f, 10f, panelWidth, Mathf.Min(contentHeight, Screen.height - 66f));

        Fill(panel, Panel);

        float x = panel.x + 8f;

        float w = panel.width - 16f;

        float y = panel.y + 6f;

        Label(ref y, x, w, "CREATURE   [" + toggleKey + "] hide", bold, Color.white);

        if (senses == null)
        {
            senses = tree.GetComponent<XenoSenses>();
        }

        // ---------------- MICRO ----------------

        Section(ref y, x, w, "MICRO AI - the creature's own view", Micro);

        Label(ref y, x, w, "branch   " + tree.ActiveBranchName(), text, Lit);

        Label(ref y, x, w,
            "role     " + blackboard.CurrentRole +
            "    gait " + (locomotion != null ? locomotion.CurrentGait.ToString() : "-") +
            "  " + (locomotion != null ? locomotion.CurrentSpeed.ToString("F1") : "0") + " m/s",
            text, Color.white);

        if (locomotion != null && locomotion.HasDestination)
        {
            Label(ref y, x, w,
                "moving   to " + Flat(locomotion.Destination) +
                "  (" + (float.IsInfinity(locomotion.RemainingDistance) ? "path pending" : locomotion.RemainingDistance.ToString("F1") + " m left") +
                (locomotion.PathFailed ? ", PATH FAILED" : "") + ")",
                text, Color.white);
        }

        y += 3f;

        Label(ref y, x, w, "senses", bold, Dim);

        if (senses != null)
        {
            for (int c = 0; c < ChannelCount; c++)
            {
                Bar(ref y, x, w, (SenseChannelId)c, senses.GetActivation((SenseChannelId)c));
            }
        }

        y += 3f;

        Label(ref y, x, w, "motivations (raised = permitted)", bold, Dim);

        string on = "";

        foreach (Motivation m in Motivations)
        {
            if (blackboard.HasMotivation(m))
            {
                on += (on.Length > 0 ? ", " : "") + m;
            }
        }

        LabelWrapped(ref y, x, w, on.Length > 0 ? on : "none", on.Length > 0 ? Lit : Dim);

        string flags = "";

        foreach (LogicFlag f in Flags)
        {
            if (blackboard.GetFlag(f))
            {
                flags += (flags.Length > 0 ? ", " : "") + f;
            }
        }

        Label(ref y, x, w, "flags", bold, Dim);

        LabelWrapped(ref y, x, w, flags.Length > 0 ? flags : "none", flags.Length > 0 ? Color.white : Dim);

        Label(ref y, x, w, "timers (cooling down)", bold, Dim);

        bool anyTimer = false;

        foreach (TimerId t in Timers)
        {
            if (!blackboard.IsTimerRunning(t))
                continue;

            anyTimer = true;

            Label(ref y, x, w, "  " + t + "  " + blackboard.TimeRemaining(t).ToString("F1") + "s", text, Color.white);
        }

        if (!anyTimer)
        {
            Label(ref y, x, w, "  none", text, Dim);
        }

        Label(ref y, x, w, "memory", bold, Dim);

        if (perception != null && perception.HasLastKnownPosition)
        {
            Label(ref y, x, w,
                "  last known " + Flat(perception.LastKnownPosition) + "  " +
                perception.TimeSinceLastKnownPosition.ToString("F1") + "s ago",
                text, new Color(1f, 0.55f, 0.25f));
        }
        else
        {
            Label(ref y, x, w, "  no last known position", text, Dim);
        }

        if (huntMemory == null) huntMemory = tree.GetComponent<XenoHuntMemory>();

        if (huntMemory != null)
        {
            if (huntMemory.HuntStep.Length > 0)
            {
                Label(ref y, x, w, "  hunt: " + huntMemory.HuntStep, text, Lit);
            }

            if (huntMemory.HasHeading)
            {
                Label(ref y, x, w, "  saw it heading " + Flat(huntMemory.Heading) + " at " +
                    huntMemory.HeadingSpeed.ToString("F1") + " m/s", text, Color.white);
            }

            if (huntMemory.SuspectedSpot != null)
            {
                Label(ref y, x, w, "  suspects you are in " + huntMemory.SuspectedSpot.name.Replace("Hiding Spot - ", ""), text, new Color(1f, 0.55f, 0.25f));
            }

            if (huntMemory.LastSightingTime > 0f)
            {
                Label(ref y, x, w, "  last sensed " + huntMemory.SecondsSinceSighting.ToString("F0") + "s ago, trail " +
                    huntMemory.Trail.Count, text, Color.white);
            }
        }

        if (senses != null && senses.HasLastSearchPosition)
        {
            Label(ref y, x, w, "  last searched " + Flat(senses.LastSearchPosition), text, Color.white);
        }

        if (events != null)
        {
            foreach (XenoEvent e in Enum.GetValues(typeof(XenoEvent)))
            {
                if (!events.HasOccurred(e))
                    continue;

                Label(ref y, x, w, "  " + e + "  " + events.SecondsSince(e).ToString("F0") + "s ago", text, Color.white);
            }
        }

        XenoLearning learning = tree.GetComponent<XenoLearning>();

        if (learning != null)
        {
            Label(ref y, x, w, "learned about this player", bold, Dim);

            Label(ref y, x, w, "  hides: level " + learning.HidingAwareness + "  (caught " + learning.CatchesInHiding +
                ", lost beside a spot " + learning.SuspectedHides + ")", text, learning.HidingAwareness > 0 ? Lit : Color.white);

            Label(ref y, x, w, "  decoys: fatigue " + learning.DecoyFatigue + "  (led nowhere " + learning.FruitlessDecoys +
                ", worked " + learning.FruitfulDecoys + ")", text, learning.DecoyFatigue > 0 ? Lit : Color.white);
        }

        // ---------------- MACRO ----------------

        y += 4f;

        Section(ref y, x, w, "MACRO AI - the Director", Macro);

        Gauge(ref y, x, w, "stun gauge", blackboard.GetGaugeAmount(GaugeId.StunDamage), blackboard.GetGaugeLevel(GaugeId.StunDamage));

        if (director != null)
        {
            int filled = Mathf.RoundToInt(director.Menace * 20f);

            Label(ref y, x, w, "menace   " + new string('|', filled) + new string('.', 20 - filled) + "  " +
                director.Menace.ToString("F2") + "   path " +
                (director.PathDistance > 999f ? "-" : director.PathDistance.ToString("F0") + " m"), text, Macro);

            string modeLine = "mode     " + (director.Mode == XenoDirector.DirectorMode.EaseOff ? "EASE OFF" : "PRESSURE");

            if (director.Mode == XenoDirector.DirectorMode.EaseOff)
                modeLine += "  " + director.EaseOffRemaining.ToString("F0") + "s left";
            else if (director.EaseOffPending)
                modeLine += "  (gauge full - waits for the hunt to end)";

            if (director.CreatureHunting)
                modeLine += "  [NPC hunting: area ignored]";

            Label(ref y, x, w, modeLine, text, Macro);
        }

        if (blackboard.HasDirectorArea)
        {
            string dist = "";

            if (player != null)
            {
                Vector3 d = blackboard.DirectorAreaCenter - player.transform.position;
                d.y = 0f;
                dist = "  " + d.magnitude.ToString("F0") + " m from player";
            }

            Label(ref y, x, w, "area     " + blackboard.DirectorIntent + " " + Flat(blackboard.DirectorAreaCenter) +
                " r" + blackboard.DirectorAreaRadius.ToString("F0") + dist, text, Color.white);
        }
        else
        {
            Label(ref y, x, w, "area     none", text, Dim);
        }

        string denied = "";

        foreach (AlienPermission p in Permissions)
        {
            if (!blackboard.AlienIsAllowed(p))
            {
                denied += (denied.Length > 0 ? ", " : "") + p;
            }
        }

        Label(ref y, x, w, "denied   " + (denied.Length > 0 ? denied : "nothing"), text, denied.Length > 0 ? Color.white : Dim);

        if (director == null)
        {
            Label(ref y, x, w, "(no Director in scene)", text, Dim);
        }

        // ---------------- TRUTH ----------------

        if (player != null)
        {
            y += 4f;

            Section(ref y, x, w, "GROUND TRUTH - the AI cannot read this", Truth);

            Vector3 delta = player.transform.position - tree.transform.position;

            delta.y = 0f;

            Label(ref y, x, w,
                "player   " + Flat(player.transform.position) + "   " + delta.magnitude.ToString("F1") + " m away",
                text, Truth);

            if (perception != null && perception.HasLastKnownPosition)
            {
                Vector3 err = player.transform.position - perception.LastKnownPosition;

                err.y = 0f;

                Label(ref y, x, w, "belief error " + err.magnitude.ToString("F1") + " m (last known vs real)", text, Truth);
            }
        }

        contentHeight = y - panel.y + 6f;
    }


    // =========================================================
    // DRAWING
    // =========================================================

    private void Bar(ref float y, float x, float w, SenseChannelId channel, float value)
    {
        float labelW = 128f;

        Rect bar = new Rect(x + labelW, y + 3f, w - labelW - 40f, line - 6f);

        GUI.color = value > 0.01f ? Color.white : Dim;

        GUI.Label(new Rect(x, y, labelW, line), "  " + channel, text);

        Fill(bar, new Color(1f, 1f, 1f, 0.08f));

        Color c = Dim;

        if (value >= senses.UpperThreshold) c = new Color(1f, 0.35f, 0.3f);
        else if (value >= senses.ActivatedThreshold) c = Lit;
        else if (value >= senses.LowerThreshold) c = new Color(0.95f, 0.9f, 0.45f);

        Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(value), bar.height), c);

        foreach (float t in new[] { senses.LowerThreshold, senses.ActivatedThreshold, senses.UpperThreshold })
        {
            Fill(new Rect(bar.x + bar.width * t, bar.y - 1f, 1f, bar.height + 2f), new Color(1f, 1f, 1f, 0.5f));
        }

        GUI.color = value > 0.01f ? Color.white : Dim;

        GUI.Label(new Rect(bar.xMax + 4f, y, 40f, line), value.ToString("F2"), text);

        GUI.color = Color.white;

        y += line;
    }


    private void Gauge(ref float y, float x, float w, string name, float amount, GaugeLevel level)
    {
        float labelW = 100f;

        Rect bar = new Rect(x + labelW, y + 3f, w - labelW - 70f, line - 6f);

        GUI.color = Color.white;

        GUI.Label(new Rect(x, y, labelW, line), name, text);

        Fill(bar, new Color(1f, 1f, 1f, 0.08f));

        Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(amount), bar.height), Macro);

        GUI.Label(new Rect(bar.xMax + 4f, y, 70f, line), amount.ToString("F2") + " " + level, text);

        y += line;
    }


    private void Section(ref float y, float x, float w, string title, Color color)
    {
        Fill(new Rect(x, y + 2f, w, 1f), new Color(color.r, color.g, color.b, 0.5f));

        y += 4f;

        Label(ref y, x, w, title, bold, color);
    }


    private void Label(ref float y, float x, float w, string s, GUIStyle style, Color color)
    {
        GUI.color = color;

        GUI.Label(new Rect(x, y, w, line), s, style);

        GUI.color = Color.white;

        y += line;
    }


    private void LabelWrapped(ref float y, float x, float w, string s, Color color)
    {
        GUIStyle wrap = new GUIStyle(text) { wordWrap = true };

        float h = wrap.CalcHeight(new GUIContent("  " + s), w);

        GUI.color = color;

        GUI.Label(new Rect(x, y, w, h), "  " + s, wrap);

        GUI.color = Color.white;

        y += h + 1f;
    }


    private static string Flat(Vector3 v)
    {
        return "(" + v.x.ToString("F1") + ", " + v.z.ToString("F1") + ")";
    }


    private static void Fill(Rect r, Color c)
    {
        Color old = GUI.color;

        GUI.color = c;

        GUI.DrawTexture(r, Texture2D.whiteTexture);

        GUI.color = old;
    }


    private void EnsureStyles()
    {
        if (text != null)
            return;

        text = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            wordWrap = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0)
        };

        text.normal.textColor = Color.white;

        bold = new GUIStyle(text) { fontStyle = FontStyle.Bold };

        line = fontSize + 5f;
    }
}

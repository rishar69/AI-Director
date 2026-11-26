using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Live view of the behaviour tree, drawn in the Game view.
///
/// Every node is one row, indented by depth. What each row looks like
/// depends on what happened to that node on the tree's latest tick:
///
///   RUNNING   amber, with how long it has been running
///   SUCCESS   green
///   FAILURE   red - evaluated this tick and said no
///   (grey)    not evaluated this tick at all
///
/// Grey vs red is the interesting distinction. The root selector stops
/// at the first child that does not fail, so the red rows above the
/// amber one are exactly the higher priorities that were checked and
/// rejected - the creature's reasoning for what it is doing - and the
/// grey rows below it are the options it never got to.
///
/// In Auto mode only nodes visited this tick are expanded, so the view
/// follows the live path and stays readable on a tree of hundreds of
/// nodes. Full mode expands everything. Click a row to pin it open or
/// shut (needs a free cursor - see ObserverCamera's inspect key).
///
/// Purely an observer: it reads node status and never ticks anything.
/// </summary>
public class BTLiveViewer : MonoBehaviour
{
    [Header("Target")]
    [SerializeField]
    private XenoBehaviourTree tree;

    [Header("Toggle")]
    [SerializeField]
    private Key toggleKey = Key.F2;

    [SerializeField]
    private Key modeKey = Key.F5;

    [SerializeField]
    private bool visibleOnStart = true;

    [Header("Layout")]
    [SerializeField, Min(240f)]
    private float panelWidth = 470f;

    [SerializeField, Min(8)]
    private int fontSize = 12;

    [SerializeField, Min(2)]
    private int historyLength = 8;

    [SerializeField]
    [Tooltip("Hide stub nodes (unported branches) entirely.")]
    private bool hideStubs = true;


    private enum ExpandMode
    {
        Auto = 0,
        Full = 1,
        RootOnly = 2
    }


    private struct Row
    {
        public BTNode Node;
        public int Depth;
        public bool HasChildren;
        public bool Expanded;
    }


    private struct HistoryEntry
    {
        public float Time;
        public string Branch;
    }


    private bool visible;

    private ExpandMode mode = ExpandMode.Auto;

    private readonly List<Row> rows = new List<Row>(512);

    private readonly Dictionary<BTNode, bool> pinned = new Dictionary<BTNode, bool>();

    private readonly List<HistoryEntry> history = new List<HistoryEntry>();

    private string lastBranch;

    private float branchSince;

    private Vector2 scroll;

    private GUIStyle rowStyle;

    private GUIStyle headerStyle;

    private Texture2D white;


    private static readonly Color Panel = new Color(0.06f, 0.07f, 0.08f, 0.86f);
    private static readonly Color RunningColor = new Color(1f, 0.72f, 0.2f);
    private static readonly Color SuccessColor = new Color(0.45f, 0.9f, 0.5f);
    private static readonly Color FailureColor = new Color(0.95f, 0.42f, 0.4f);
    private static readonly Color IdleColor = new Color(0.55f, 0.57f, 0.6f, 0.75f);
    private static readonly Color AbortColor = new Color(0.8f, 0.5f, 1f);


    public bool Visible
    {
        get => visible;
        set => visible = value;
    }


    private void Awake()
    {
        visible = visibleOnStart;

        if (tree == null)
        {
            tree = FindFirstObjectByType<XenoBehaviourTree>();
        }
    }


    private void Update()
    {
        Keyboard kb = Keyboard.current;

        if (kb != null)
        {
            if (kb[toggleKey].wasPressedThisFrame) visible = !visible;

            if (kb[modeKey].wasPressedThisFrame)
            {
                mode = (ExpandMode)(((int)mode + 1) % 3);
            }
        }

        if (tree == null || tree.Root == null)
            return;

        string branch = tree.ActiveBranchName();

        if (branch != lastBranch)
        {
            lastBranch = branch;

            branchSince = Time.time;

            history.Insert(0, new HistoryEntry { Time = Time.time, Branch = branch });

            if (history.Count > historyLength)
            {
                history.RemoveAt(history.Count - 1);
            }
        }
    }


    private void OnGUI()
    {
        if (!visible || tree == null || tree.Root == null)
            return;

        EnsureStyles();

        rows.Clear();

        Collect(tree.Root, 0, tree.TraceStamp);

        float line = fontSize + 5f;

        float header = 6f + (line + 4f) + line + (history.Count - 1) * (line - 1f) + 8f;

        float height = Mathf.Min(header + rows.Count * line + 10f, Screen.height - 66f);

        Rect panel = new Rect(Screen.width - panelWidth - 10f, 10f, panelWidth, height);

        DrawRect(panel, Panel);

        float y = panel.y + 6f;

        float x = panel.x + 8f;

        float w = panel.width - 16f;

        GUI.Label(new Rect(x, y, w, line + 2f),
            "BEHAVIOUR TREE   [" + toggleKey + "] hide   [" + modeKey + "] " + mode, headerStyle);

        y += line + 4f;

        GUI.color = RunningColor;
        GUI.Label(new Rect(x, y, w, line),
            "now: " + lastBranch + "   (" + (Time.time - branchSince).ToString("F1") + "s)", rowStyle);
        GUI.color = Color.white;

        y += line;

        for (int i = 1; i < history.Count; i++)
        {
            GUI.color = new Color(0.75f, 0.77f, 0.8f, 1f - i * 0.08f);

            GUI.Label(new Rect(x, y, w, line),
                "  " + FormatAge(Time.time - history[i].Time) + "  " + history[i].Branch, rowStyle);

            y += line - 1f;
        }

        GUI.color = Color.white;

        y += 4f;

        DrawRect(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.15f));

        y += 4f;

        Rect view = new Rect(panel.x, y, panel.width, panel.yMax - y - 4f);

        Rect content = new Rect(0f, 0f, panel.width - 20f, rows.Count * line + 4f);

        scroll = GUI.BeginScrollView(view, scroll, content);

        int stamp = tree.TraceStamp;

        for (int i = 0; i < rows.Count; i++)
        {
            Row row = rows[i];

            float ry = i * line;

            if (ry + line < scroll.y || ry > scroll.y + view.height)
                continue;

            BTNode node = row.Node;

            bool visited = node.LastTickStamp == stamp;

            bool aborted = node.AbortedStamp == stamp;

            Color color;
            string glyph;

            if (aborted && !(visited && node.LastStatus == BTStatus.Running))
            {
                color = AbortColor;
                glyph = "↺";
            }
            else if (!visited)
            {
                color = IdleColor;
                glyph = "·";
            }
            else if (node.LastStatus == BTStatus.Running)
            {
                color = RunningColor;
                glyph = "▶";
            }
            else if (node.LastStatus == BTStatus.Success)
            {
                color = SuccessColor;
                glyph = "✓";
            }
            else
            {
                color = FailureColor;
                glyph = "✗";
            }

            float indent = 6f + row.Depth * 14f;

            Rect r = new Rect(indent, ry, content.width - indent, line);

            if (visited && node.LastStatus == BTStatus.Running)
            {
                DrawRect(new Rect(0f, ry, content.width, line), new Color(1f, 0.72f, 0.2f, 0.10f));
            }

            string fold = row.HasChildren ? (row.Expanded ? "▾ " : "▸ ") : "  ";

            string label = fold + glyph + " " + node.Name;

            if (visited && node.LastStatus == BTStatus.Running)
            {
                label += "   " + (Time.time - node.RunningSince).ToString("F1") + "s";
            }
            else if (!row.Expanded && row.HasChildren)
            {
                label += "   [+" + node.TraceChildren.Count + "]";
            }

            GUI.color = color;

            if (GUI.Button(r, label, rowStyle) && row.HasChildren)
            {
                pinned[node] = !row.Expanded;
            }
        }

        GUI.color = Color.white;

        GUI.EndScrollView();
    }


    private void Collect(BTNode node, int depth, int stamp)
    {
        if (node == null)
            return;

        if (hideStubs && node is BTNotImplemented)
            return;

        IReadOnlyList<BTNode> children = node.TraceChildren;

        bool hasChildren = children.Count > 0;

        bool expanded;

        if (pinned.TryGetValue(node, out bool pin))
        {
            expanded = pin;
        }
        else
        {
            switch (mode)
            {
                case ExpandMode.Full:
                    expanded = true;
                    break;

                case ExpandMode.RootOnly:
                    expanded = depth == 0;
                    break;

                default:
                    /*
                     * Open what ran this tick. A node that failed on its
                     * guard is visited too, but opening every rejected
                     * root branch would bury the live one - so failed
                     * nodes only open below depth 1.
                     */
                    expanded = depth == 0 ||
                               (node.LastTickStamp == stamp &&
                                (node.LastStatus != BTStatus.Failure || depth > 1));
                    break;
            }
        }

        rows.Add(new Row { Node = node, Depth = depth, HasChildren = hasChildren, Expanded = expanded });

        if (!expanded)
            return;

        for (int i = 0; i < children.Count; i++)
        {
            Collect(children[i], depth + 1, stamp);
        }
    }


    private static string FormatAge(float seconds)
    {
        return seconds < 60f
            ? seconds.ToString("F0").PadLeft(3) + "s ago"
            : (seconds / 60f).ToString("F1").PadLeft(3) + "m ago";
    }


    private void EnsureStyles()
    {
        if (rowStyle != null)
            return;

        white = Texture2D.whiteTexture;

        rowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            wordWrap = false
        };

        rowStyle.normal.textColor = Color.white;
        rowStyle.hover.textColor = Color.white;
        rowStyle.active.textColor = Color.white;
        rowStyle.normal.background = null;
        rowStyle.hover.background = null;
        rowStyle.active.background = null;

        headerStyle = new GUIStyle(rowStyle)
        {
            fontStyle = FontStyle.Bold,
            fontSize = fontSize + 1
        };
    }


    private void DrawRect(Rect r, Color c)
    {
        Color old = GUI.color;

        GUI.color = c;

        GUI.DrawTexture(r, Texture2D.whiteTexture);

        GUI.color = old;
    }
}

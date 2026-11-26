using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The creature's behaviour tree drawn as a node diagram.
///
/// Laid out left to right, the same orientation as the OpenCAGE
/// exports: depth runs across, sibling order runs down, and a parent
/// sits level with the middle of its children. Each box is tagged with
/// its node kind (SEL, SEQ, COND, ACT ...).
///
/// IN PLAY MODE it is live. Boxes and edges take the colour of what
/// happened to them on the latest tick:
///
///   amber   running - the live path is drawn thick
///   green   succeeded
///   red     evaluated and failed (a priority that was checked and rejected)
///   grey    not evaluated this tick
///   purple  aborted this tick
///
/// IN EDIT MODE it builds a preview of the tree from the selected (or
/// first) creature, so the structure can be read and screenshotted
/// without playing. Statuses are all grey there.
///
/// Controls: scroll = zoom at the cursor, right or middle drag = pan,
/// click a box = collapse or expand it, F = frame the running node,
/// A = frame everything.
///
/// Window > AI Director > Behaviour Tree Graph.
/// </summary>
public class BTGraphWindow : EditorWindow
{
    // =========================================================
    // LAYOUT CONSTANTS (graph units, before zoom)
    // =========================================================

    private const float NodeHeight = 24f;

    private const float RowGap = 6f;

    private const float ColumnGap = 46f;

    private const float MaxNodeWidth = 330f;

    private const float MinNodeWidth = 70f;

    private const float TagWidth = 46f;

    private const float FontSize = 11f;


    private enum ExpandMode
    {
        Full = 0,
        Live = 1,
        Manual = 2
    }


    private class Laid
    {
        public BTNode Node;
        public int Depth;
        public Rect Rect;
        public Laid Parent;
        public readonly List<Laid> Children = new List<Laid>();
        public bool HasHiddenChildren;
        public int HiddenCount;
    }


    // =========================================================
    // STATE
    // =========================================================

    private ExpandMode mode = ExpandMode.Full;

    private bool hideStubs = true;

    private bool follow;

    private float zoom = 0.85f;

    private Vector2 pan = new Vector2(20f, 40f);

    private readonly Dictionary<BTNode, bool> pinned = new Dictionary<BTNode, bool>();

    private readonly List<Laid> laid = new List<Laid>(1024);

    private readonly List<float> columnWidth = new List<float>();

    private float nextRow;

    private Laid rootLaid;

    private XenoBehaviourTree target;

    private BTNode previewRoot;

    private XenoBehaviourTree previewSource;

    private string previewError;

    private double lastRepaint;

    private GUIStyle textStyle;

    private GUIStyle tagStyle;

    private Laid hovered;

    private bool frameAllPending = true;


    private static readonly Color Background = new Color(0.13f, 0.14f, 0.15f);
    private static readonly Color Grid = new Color(1f, 1f, 1f, 0.035f);
    private static readonly Color RunningColor = new Color(1f, 0.72f, 0.2f);
    private static readonly Color SuccessColor = new Color(0.4f, 0.85f, 0.45f);
    private static readonly Color FailureColor = new Color(0.92f, 0.38f, 0.36f);
    private static readonly Color IdleColor = new Color(0.45f, 0.47f, 0.5f);
    private static readonly Color AbortColor = new Color(0.75f, 0.45f, 1f);


    [MenuItem("Window/AI Director/Behaviour Tree Graph")]
    [MenuItem("Tools/AI Director/Behaviour Tree Graph", priority = -90)]
    public static void Open()
    {
        BTGraphWindow w = GetWindow<BTGraphWindow>();

        w.titleContent = new GUIContent("BT Graph");

        w.minSize = new Vector2(500f, 300f);

        w.Show();
    }


    private void OnEnable()
    {
        EditorApplication.update += Tick;

        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        Selection.selectionChanged += OnSelectionChanged;

        wantsMouseMove = true;
    }


    private void OnDisable()
    {
        EditorApplication.update -= Tick;

        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        Selection.selectionChanged -= OnSelectionChanged;
    }


    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        target = null;

        previewRoot = null;

        pinned.Clear();

        frameAllPending = true;

        Repaint();
    }


    private void OnSelectionChanged()
    {
        GameObject go = Selection.activeGameObject;

        if (go != null && go.GetComponentInParent<XenoBehaviourTree>() != null)
        {
            target = null;

            previewRoot = null;

            frameAllPending = true;

            Repaint();
        }
    }


    private void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;

        /* ~20 fps is plenty to read, and cheap. */
        if (EditorApplication.timeSinceStartup - lastRepaint < 0.05)
            return;

        lastRepaint = EditorApplication.timeSinceStartup;

        Repaint();
    }


    // =========================================================
    // SOURCE
    // =========================================================

    private XenoBehaviourTree FindTarget()
    {
        GameObject go = Selection.activeGameObject;

        if (go != null)
        {
            XenoBehaviourTree selected = go.GetComponentInParent<XenoBehaviourTree>();

            if (selected != null)
                return selected;
        }

        return Object.FindFirstObjectByType<XenoBehaviourTree>();
    }


    /// <summary>
    /// The tree to draw: the live one in play mode, otherwise a preview
    /// built by running the component's own BuildRoot. Building only
    /// constructs nodes; nothing ticks, so it has no side effects on the
    /// scene.
    /// </summary>
    private BTNode CurrentRoot(out bool live)
    {
        live = EditorApplication.isPlaying;

        if (target == null)
        {
            target = FindTarget();
        }

        if (target == null)
            return null;

        if (live)
            return target.Root;

        if (previewRoot != null && previewSource == target)
            return previewRoot;

        previewSource = target;

        previewError = null;

        try
        {
            MethodInfo build = typeof(XenoBehaviourTree).GetMethod(
                "BuildRoot", BindingFlags.Instance | BindingFlags.NonPublic);

            previewRoot = build != null ? build.Invoke(target, null) as BTNode : null;

            if (previewRoot == null)
            {
                previewError = "BuildRoot not found on XenoBehaviourTree.";
            }
        }
        catch (System.Exception e)
        {
            previewRoot = null;

            previewError = (e.InnerException ?? e).Message;
        }

        frameAllPending = true;

        return previewRoot;
    }


    // =========================================================
    // GUI
    // =========================================================

    private void OnGUI()
    {
        EnsureStyles();

        Rect toolbar = new Rect(0f, 0f, position.width, 22f);

        Rect canvas = new Rect(0f, toolbar.height, position.width, position.height - toolbar.height);

        BTNode root = CurrentRoot(out bool live);

        DrawToolbar(toolbar, live);

        EditorGUI.DrawRect(canvas, Background);

        if (root == null)
        {
            string msg = target == null
                ? "No XenoBehaviourTree in the open scene."
                : "Could not build a preview: " + previewError;

            GUI.Label(new Rect(canvas.x + 20f, canvas.y + 20f, canvas.width - 40f, 40f), msg, EditorStyles.whiteLabel);

            return;
        }

        int stamp = live ? target.TraceStamp : -1;

        Layout(root, stamp);

        if (frameAllPending && Event.current.type == EventType.Repaint)
        {
            FrameAll(canvas);

            frameAllPending = false;
        }

        if (follow && live)
        {
            FrameRunning(canvas, smooth: true);
        }

        HandleInput(canvas);

        GUI.BeginClip(canvas);

        Rect local = new Rect(0f, 0f, canvas.width, canvas.height);

        DrawGrid(local);

        if (Event.current.type == EventType.Repaint)
        {
            foreach (Laid l in laid)
            {
                foreach (Laid c in l.Children)
                {
                    DrawEdge(l, c, stamp, live);
                }
            }
        }

        hovered = null;

        foreach (Laid l in laid)
        {
            DrawNode(l, stamp, live, local);
        }

        DrawLegend(local, live);

        if (hovered != null)
        {
            DrawTooltip(hovered, stamp, live);
        }

        GUI.EndClip();
    }


    private void DrawToolbar(Rect r, bool live)
    {
        GUILayout.BeginArea(r, EditorStyles.toolbar);

        GUILayout.BeginHorizontal();

        GUILayout.Label(
            live ? "LIVE  " + (target != null ? target.name : "") : "PREVIEW (edit mode)",
            EditorStyles.toolbarButton, GUILayout.Width(150f));

        mode = (ExpandMode)EditorGUILayout.EnumPopup(mode, EditorStyles.toolbarPopup, GUILayout.Width(70f));

        bool stubs = GUILayout.Toggle(hideStubs, "Hide stubs", EditorStyles.toolbarButton, GUILayout.Width(75f));

        if (stubs != hideStubs)
        {
            hideStubs = stubs;
            frameAllPending = true;
        }

        follow = GUILayout.Toggle(follow, "Follow", EditorStyles.toolbarButton, GUILayout.Width(55f));

        if (GUILayout.Button("Frame all (A)", EditorStyles.toolbarButton, GUILayout.Width(85f)))
        {
            frameAllPending = true;
        }

        if (GUILayout.Button("Running (F)", EditorStyles.toolbarButton, GUILayout.Width(80f)))
        {
            FrameRunning(new Rect(0f, 22f, position.width, position.height - 22f), smooth: false);
        }

        if (GUILayout.Button("Expand all", EditorStyles.toolbarButton, GUILayout.Width(70f)))
        {
            pinned.Clear();
            mode = ExpandMode.Full;
        }

        if (!live && GUILayout.Button("Rebuild", EditorStyles.toolbarButton, GUILayout.Width(60f)))
        {
            previewRoot = null;
            target = null;
        }

        GUILayout.FlexibleSpace();

        GUILayout.Label(laid.Count + " nodes   zoom " + (zoom * 100f).ToString("F0") + "%", EditorStyles.miniLabel);

        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }


    // =========================================================
    // LAYOUT
    // =========================================================

    private void Layout(BTNode root, int stamp)
    {
        laid.Clear();

        columnWidth.Clear();

        nextRow = 0f;

        rootLaid = Place(root, null, 0, stamp);

        /* Column x positions from the widest node at each depth. */
        List<float> columnX = new List<float>();

        float x = 0f;

        foreach (float w in columnWidth)
        {
            columnX.Add(x);

            x += w + ColumnGap;
        }

        foreach (Laid l in laid)
        {
            Rect r = l.Rect;

            r.x = columnX[l.Depth];

            l.Rect = r;
        }
    }


    private Laid Place(BTNode node, Laid parent, int depth, int stamp)
    {
        Laid me = new Laid { Node = node, Depth = depth, Parent = parent };

        laid.Add(me);

        float width = Mathf.Clamp(
            textStyle.CalcSize(new GUIContent(node.Name)).x + TagWidth + 16f,
            MinNodeWidth, MaxNodeWidth);

        while (columnWidth.Count <= depth) columnWidth.Add(0f);

        columnWidth[depth] = Mathf.Max(columnWidth[depth], width);

        List<BTNode> kids = new List<BTNode>();

        foreach (BTNode c in node.TraceChildren)
        {
            if (hideStubs && c is BTNotImplemented)
                continue;

            kids.Add(c);
        }

        bool expanded = kids.Count > 0 && IsExpanded(node, depth, stamp);

        if (!expanded)
        {
            me.HasHiddenChildren = kids.Count > 0;

            me.HiddenCount = kids.Count;

            me.Rect = new Rect(0f, nextRow, width, NodeHeight);

            nextRow += NodeHeight + RowGap;

            return me;
        }

        foreach (BTNode c in kids)
        {
            me.Children.Add(Place(c, me, depth + 1, stamp));
        }

        float top = me.Children[0].Rect.center.y;

        float bottom = me.Children[me.Children.Count - 1].Rect.center.y;

        me.Rect = new Rect(0f, (top + bottom) * 0.5f - NodeHeight * 0.5f, width, NodeHeight);

        return me;
    }


    private bool IsExpanded(BTNode node, int depth, int stamp)
    {
        if (pinned.TryGetValue(node, out bool pin))
            return pin;

        switch (mode)
        {
            case ExpandMode.Full:
                return true;

            case ExpandMode.Manual:
                return depth < 2;

            default:
                if (stamp < 0)
                    return depth < 2;

                return depth == 0 ||
                       (node.LastTickStamp == stamp &&
                        (node.LastStatus != BTStatus.Failure || depth > 1));
        }
    }


    // =========================================================
    // DRAWING
    // =========================================================

    private Rect ToScreen(Rect graph)
    {
        return new Rect(
            graph.x * zoom + pan.x,
            graph.y * zoom + pan.y,
            graph.width * zoom,
            graph.height * zoom);
    }


    private Vector2 ToGraph(Vector2 screen)
    {
        return (screen - pan) / zoom;
    }


    private void DrawGrid(Rect local)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        float step = 50f * zoom;

        if (step < 8f)
            return;

        for (float x = pan.x % step; x < local.width; x += step)
        {
            EditorGUI.DrawRect(new Rect(x, 0f, 1f, local.height), Grid);
        }

        for (float y = pan.y % step; y < local.height; y += step)
        {
            EditorGUI.DrawRect(new Rect(0f, y, local.width, 1f), Grid);
        }
    }


    private void DrawEdge(Laid parent, Laid child, int stamp, bool live)
    {
        Rect a = ToScreen(parent.Rect);

        Rect b = ToScreen(child.Rect);

        Vector3 start = new Vector3(a.xMax, a.center.y);

        Vector3 end = new Vector3(b.xMin, b.center.y);

        float bend = Mathf.Max(20f, (end.x - start.x) * 0.5f);

        Color color = new Color(1f, 1f, 1f, 0.16f);

        float width = 1.5f;

        if (live && child.Node.LastTickStamp == stamp)
        {
            color = StatusColor(child.Node, stamp);

            width = child.Node.LastStatus == BTStatus.Running ? 4f : 2f;

            if (child.Node.LastStatus == BTStatus.Failure)
            {
                color.a = 0.55f;
            }
        }

        Handles.DrawBezier(start, end,
            start + Vector3.right * bend, end - Vector3.right * bend,
            color, null, width * Mathf.Clamp(zoom, 0.6f, 1.4f));
    }


    private void DrawNode(Laid l, int stamp, bool live, Rect local)
    {
        Rect r = ToScreen(l.Rect);

        if (r.xMax < 0f || r.yMax < 0f || r.x > local.width || r.y > local.height)
            return;

        BTNode node = l.Node;

        bool visited = live && node.LastTickStamp == stamp;

        Color status = live ? StatusColor(node, stamp) : IdleColor;

        Event e = Event.current;

        if (r.Contains(e.mousePosition))
        {
            hovered = l;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (l.HasHiddenChildren || l.Children.Count > 0)
                {
                    pinned[node] = !(l.Children.Count > 0);
                }

                e.Use();
            }
        }

        if (e.type != EventType.Repaint)
            return;

        Color body = visited ? Color.Lerp(new Color(0.2f, 0.21f, 0.23f), status, 0.28f) : new Color(0.2f, 0.21f, 0.23f);

        if (!live || !visited)
        {
            body.a = 0.95f;
        }

        EditorGUI.DrawRect(r, body);

        /* Status edge on the left, kind tag next to it. */
        EditorGUI.DrawRect(new Rect(r.x, r.y, 3f * Mathf.Max(zoom, 0.6f), r.height), status);

        float tagW = TagWidth * zoom;

        Rect tag = new Rect(r.x + 4f * zoom, r.y + 2f * zoom, tagW - 6f * zoom, r.height - 4f * zoom);

        EditorGUI.DrawRect(tag, KindColor(node));

        tagStyle.fontSize = Mathf.Max(5, Mathf.RoundToInt(9f * zoom));

        GUI.Label(tag, Kind(node), tagStyle);

        /* Border: thick amber for the live path. */
        bool running = visited && node.LastStatus == BTStatus.Running;

        Outline(r, running ? RunningColor : new Color(0f, 0f, 0f, 0.6f), running ? 2f : 1f);

        textStyle.fontSize = Mathf.Max(5, Mathf.RoundToInt(FontSize * zoom));

        textStyle.normal.textColor = live && !visited ? new Color(0.72f, 0.74f, 0.77f) : Color.white;

        string label = node.Name;

        if (running)
        {
            label += "  " + (Time.time - node.RunningSince).ToString("F1") + "s";
        }

        if (l.HasHiddenChildren)
        {
            label += "  [+" + l.HiddenCount + "]";
        }

        if (zoom > 0.3f)
        {
            GUI.Label(new Rect(r.x + tagW + 2f, r.y, r.width - tagW - 4f, r.height), label, textStyle);
        }
    }


    private void Outline(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }


    private void DrawLegend(Rect local, bool live)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        Rect box = new Rect(local.width - 236f, local.height - (live ? 124f : 58f), 226f, live ? 114f : 48f);

        EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.55f));

        float y = box.y + 6f;

        if (live)
        {
            LegendRow(ref y, box.x, RunningColor, "running (live path is thick)");
            LegendRow(ref y, box.x, SuccessColor, "succeeded this tick");
            LegendRow(ref y, box.x, FailureColor, "checked and failed this tick");
            LegendRow(ref y, box.x, IdleColor, "not evaluated this tick");
            LegendRow(ref y, box.x, AbortColor, "aborted this tick");
        }

        GUI.Label(new Rect(box.x + 8f, y, box.width - 12f, 36f),
            "scroll zoom, right-drag pan,\nclick box to fold, F running, A all",
            EditorStyles.whiteMiniLabel);
    }


    private void LegendRow(ref float y, float x, Color c, string s)
    {
        EditorGUI.DrawRect(new Rect(x + 8f, y + 4f, 10f, 10f), c);

        GUI.Label(new Rect(x + 24f, y, 200f, 18f), s, EditorStyles.whiteMiniLabel);

        y += 15f;
    }


    private void DrawTooltip(Laid l, int stamp, bool live)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        BTNode n = l.Node;

        string s = n.Name + "\n" + n.GetType().Name;

        if (live)
        {
            bool visited = n.LastTickStamp == stamp;

            s += "\n" + (visited ? "this tick: " + n.LastStatus : "not evaluated this tick (last: " + n.LastStatus + ")");

            if (visited && n.LastStatus == BTStatus.Running)
            {
                s += "\nrunning for " + (Time.time - n.RunningSince).ToString("F1") + "s";
            }
            else if (n.LastTickTime > 0f)
            {
                s += "\nlast evaluated " + (Time.time - n.LastTickTime).ToString("F1") + "s ago";
            }
        }

        GUIContent c = new GUIContent(s);

        Vector2 size = EditorStyles.helpBox.CalcSize(c) + new Vector2(8f, 4f);

        Vector2 m = Event.current.mousePosition + new Vector2(14f, 14f);

        Rect r = new Rect(m.x, m.y, size.x, size.y);

        EditorGUI.DrawRect(r, new Color(0.05f, 0.05f, 0.06f, 0.95f));

        GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width, r.height), s, EditorStyles.whiteMiniLabel);
    }


    // =========================================================
    // INPUT
    // =========================================================

    private void HandleInput(Rect canvas)
    {
        Event e = Event.current;

        Vector2 local = e.mousePosition - canvas.position;

        if (!canvas.Contains(e.mousePosition) && e.type != EventType.MouseDrag)
            return;

        switch (e.type)
        {
            case EventType.ScrollWheel:
            {
                Vector2 before = ToGraph(local);

                zoom = Mathf.Clamp(zoom * (e.delta.y > 0f ? 0.9f : 1.1f), 0.15f, 2.5f);

                pan = local - before * zoom;

                e.Use();

                break;
            }

            case EventType.MouseDrag when e.button == 1 || e.button == 2:
                pan += e.delta;

                e.Use();

                break;

            case EventType.KeyDown when e.keyCode == KeyCode.F:
                FrameRunning(canvas, smooth: false);

                e.Use();

                break;

            case EventType.KeyDown when e.keyCode == KeyCode.A:
                frameAllPending = true;

                e.Use();

                break;
        }

        /* Event positions inside the clip are local to the canvas. */
        if (e.type == EventType.MouseDown || e.type == EventType.MouseMove)
        {
            Repaint();
        }
    }


    private void FrameAll(Rect canvas)
    {
        if (laid.Count == 0)
            return;

        Rect bounds = laid[0].Rect;

        foreach (Laid l in laid)
        {
            bounds = Rect.MinMaxRect(
                Mathf.Min(bounds.xMin, l.Rect.xMin),
                Mathf.Min(bounds.yMin, l.Rect.yMin),
                Mathf.Max(bounds.xMax, l.Rect.xMax),
                Mathf.Max(bounds.yMax, l.Rect.yMax));
        }

        zoom = Mathf.Clamp(
            Mathf.Min((canvas.width - 40f) / bounds.width, (canvas.height - 40f) / bounds.height),
            0.15f, 1.2f);

        pan = new Vector2(20f - bounds.x * zoom, 20f - bounds.y * zoom);

        /* A very tall tree at minimum zoom: start from the top rather than centred. */
    }


    private void FrameRunning(Rect canvas, bool smooth)
    {
        Laid deepest = null;

        if (target == null || !EditorApplication.isPlaying)
            return;

        int stamp = target.TraceStamp;

        foreach (Laid l in laid)
        {
            if (l.Node.LastTickStamp == stamp && l.Node.LastStatus == BTStatus.Running &&
                (deepest == null || l.Depth > deepest.Depth))
            {
                deepest = l;
            }
        }

        if (deepest == null)
            return;

        Vector2 want = new Vector2(canvas.width * 0.6f, canvas.height * 0.5f) - deepest.Rect.center * zoom;

        pan = smooth ? Vector2.Lerp(pan, want, 0.15f) : want;
    }


    // =========================================================
    // NODE KINDS
    // =========================================================

    private static string Kind(BTNode n)
    {
        switch (n)
        {
            case BTPercentageSelector _: return "RAND%";
            case BTSelector _: return "SEL ?";
            case BTSequenceStateless _: return "SEQ*";
            case BTSequence _: return "SEQ →";
            case BTBranch _: return "BRANCH";
            case BTTimerScope _: return "TIMER";
            case BTInverter _: return "NOT";
            case BTAlwaysFail _: return "PUB";
            case BTCondition _: return "COND";
            case BTAction _: return "ACT";
            case BTNotImplemented _: return "STUB";
            default: return n.TraceChildren.Count > 0 ? "NODE" : "LEAF";
        }
    }


    private static Color KindColor(BTNode n)
    {
        switch (n)
        {
            case BTSelector _:
            case BTPercentageSelector _:
                return new Color(0.25f, 0.42f, 0.7f);

            case BTSequence _:
                return new Color(0.3f, 0.55f, 0.45f);

            case BTBranch _:
                return new Color(0.5f, 0.35f, 0.65f);

            case BTTimerScope _:
            case BTInverter _:
            case BTAlwaysFail _:
                return new Color(0.55f, 0.45f, 0.25f);

            case BTCondition _:
                return new Color(0.42f, 0.42f, 0.46f);

            case BTAction _:
                return new Color(0.62f, 0.38f, 0.28f);

            default:
                return new Color(0.35f, 0.35f, 0.35f);
        }
    }


    private static Color StatusColor(BTNode n, int stamp)
    {
        if (n.AbortedStamp == stamp && !(n.LastTickStamp == stamp && n.LastStatus == BTStatus.Running))
            return AbortColor;

        if (n.LastTickStamp != stamp)
            return IdleColor;

        switch (n.LastStatus)
        {
            case BTStatus.Running: return RunningColor;
            case BTStatus.Success: return SuccessColor;
            default: return FailureColor;
        }
    }


    private void EnsureStyles()
    {
        if (textStyle != null)
            return;

        textStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            fontSize = (int)FontSize
        };

        textStyle.normal.textColor = Color.white;

        tagStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            fontSize = 9
        };

        tagStyle.normal.textColor = Color.white;
    }
}

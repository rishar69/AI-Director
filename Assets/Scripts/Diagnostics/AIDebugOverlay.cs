using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// On-screen readout of everything the AI systems are thinking.
///
/// Unglamorous, but from Phase 1 onward a stalker AI is untunable
/// without it: awareness, last known position and Director hints are all
/// invisible otherwise, and "the alien did something weird" is not a
/// debuggable report.
///
/// Right now it only knows about the player and the noise bus. Each
/// later phase adds a section.
/// </summary>
public class AIDebugOverlay : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Toggle")]
    [SerializeField]
    private Key toggleKey = Key.F1;

    [SerializeField]
    private bool visibleOnStart = true;


    [Header("References")]
    [SerializeField]
    private PlayerController player;

    [SerializeField]
    private PlayerNoiseEmitter noiseEmitter;

    /// <summary>
    /// Which creature's perception to display. Left empty, the first
    /// one in the scene is used, which is all Phase 1 needs.
    /// </summary>
    [SerializeField]
    private PerceptionSystem perception;


    [Header("Display")]
    [SerializeField]
    private int fontSize = 13;

    [SerializeField]
    private Vector2 padding = new Vector2(12f, 12f);

    [SerializeField]
    private float panelWidth = 340f;

    [SerializeField]
    private bool showNoiseGizmos = true;


    // =========================================================
    // STATE
    // =========================================================

    private bool visible;

    private GUIStyle panelStyle;

    private GUIStyle textStyle;

    private Texture2D panelTexture;

    private readonly StringBuilder builder = new StringBuilder(512);

    private float smoothedDeltaTime;

    private bool navMeshPresent;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        visible = visibleOnStart;

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
        }

        if (noiseEmitter == null && player != null)
        {
            noiseEmitter =
                player.GetComponent<PlayerNoiseEmitter>();
        }

        if (perception == null)
        {
            perception = FindFirstObjectByType<PerceptionSystem>();
        }

        CheckNavMesh();
    }


    private void OnDestroy()
    {
        if (panelTexture != null)
        {
            Destroy(panelTexture);
        }
    }


    private void Update()
    {
        HandleToggle();

        NoiseBus.PruneHistory();

        smoothedDeltaTime +=
            (Time.unscaledDeltaTime - smoothedDeltaTime) * 0.1f;
    }


    private void HandleToggle()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard[toggleKey].wasPressedThisFrame)
        {
            visible = !visible;
        }
    }


    /// <summary>
    /// A baked NavMesh produces triangulation data. An empty result
    /// means Phase 0's bake step was skipped, which will silently break
    /// everything from Phase 2 onward.
    /// </summary>
    private void CheckNavMesh()
    {
        NavMeshTriangulation triangulation =
            NavMesh.CalculateTriangulation();

        navMeshPresent =
            triangulation.vertices != null &&
            triangulation.vertices.Length > 0;
    }


    // =========================================================
    // GUI
    // =========================================================

    private void OnGUI()
    {
        if (!visible)
            return;

        EnsureStyles();

        BuildReadout();

        float height =
            textStyle.CalcHeight(
                new GUIContent(builder.ToString()),
                panelWidth - padding.x * 2f
            ) + padding.y * 2f;

        Rect panel =
            new Rect(
                padding.x,
                padding.y,
                panelWidth,
                height
            );

        GUI.Box(panel, GUIContent.none, panelStyle);

        Rect text =
            new Rect(
                panel.x + padding.x,
                panel.y + padding.y,
                panel.width - padding.x * 2f,
                panel.height - padding.y * 2f
            );

        GUI.Label(text, builder.ToString(), textStyle);
    }


    private void EnsureStyles()
    {
        if (panelTexture == null)
        {
            panelTexture = new Texture2D(1, 1);

            panelTexture.SetPixel(
                0,
                0,
                new Color(0f, 0f, 0f, 0.72f)
            );

            panelTexture.Apply();
        }

        if (panelStyle == null)
        {
            panelStyle = new GUIStyle(GUI.skin.box);

            panelStyle.normal.background = panelTexture;

            panelStyle.border = new RectOffset(0, 0, 0, 0);
        }

        if (textStyle == null)
        {
            textStyle = new GUIStyle(GUI.skin.label);

            textStyle.alignment = TextAnchor.UpperLeft;

            textStyle.richText = true;

            textStyle.wordWrap = true;
        }

        textStyle.fontSize = fontSize;
    }


    // =========================================================
    // READOUT
    // =========================================================

    private void BuildReadout()
    {
        builder.Clear();

        Header("AI DIRECTOR - DEBUG");

        float fps =
            smoothedDeltaTime > 0f
                ? 1f / smoothedDeltaTime
                : 0f;

        Line("fps", fps.ToString("F0"));

        Line("toggle", toggleKey.ToString());

        AppendPlayerSection();

        AppendPerceptionSection();

        AppendNoiseSection();

        AppendSetupSection();
    }


    // =========================================================
    // PERCEPTION
    // =========================================================

    private void AppendPerceptionSection()
    {
        builder.AppendLine();

        Header("PERCEPTION");

        if (perception == null)
        {
            Warn("no PerceptionSystem in scene");

            return;
        }

        float awareness = perception.Awareness;

        Line(
            "awareness",
            Bar(awareness) + "  " + awareness.ToString("F2")
        );

        Line(
            "level",
            ColorForLevel(perception.Level) +
            perception.Level +
            "</color>"
        );

        AppendChannel("vision", SenseChannel.Vision);

        AppendChannel("hearing", SenseChannel.Hearing);

        AppendChannel("proximity", SenseChannel.Proximity);

        Line(
            "since contact",
            perception.TimeSinceContact > 3600f
                ? "never"
                : perception.TimeSinceContact.ToString("F1") + "s"
        );

        if (perception.HasLastKnownPosition)
        {
            float distance =
                Vector3.Distance(
                    perception.transform.position,
                    perception.LastKnownPosition
                );

            Line(
                "last known",
                distance.ToString("F1") + "m away, " +
                perception.TimeSinceLastKnownPosition
                    .ToString("F0") + "s old"
            );
        }
        else
        {
            Line("last known", "none");
        }
    }


    private void AppendChannel(
        string label,
        SenseChannel channel)
    {
        float strength = perception.StrengthOf(channel);

        if (strength <= 0f)
            return;

        Line(
            label,
            Bar(strength) + "  " + strength.ToString("F2")
        );
    }


    /// <summary>
    /// A ten cell text meter. Cheaper to read at a glance than a
    /// number when values are changing every frame.
    /// </summary>
    private static string Bar(float value01)
    {
        const int cells = 10;

        int filled =
            Mathf.RoundToInt(
                Mathf.Clamp01(value01) * cells
            );

        return
            "<color=#FFFFFF>" +
            new string('|', filled) +
            "</color><color=#3A4149>" +
            new string('|', cells - filled) +
            "</color>";
    }


    private static string ColorForLevel(AwarenessLevel level)
    {
        switch (level)
        {
            case AwarenessLevel.Confirmed:
                return "<color=#FF5555>";

            case AwarenessLevel.Alerted:
                return "<color=#FF9436>";

            case AwarenessLevel.Suspicious:
                return "<color=#FFE14D>";

            default:
                return "<color=#8FD98F>";
        }
    }


    private void AppendPlayerSection()
    {
        builder.AppendLine();

        Header("PLAYER");

        if (player == null)
        {
            Warn("no PlayerController in scene");

            return;
        }

        Line("stance", player.CurrentStance.ToString());

        Line(
            "speed",
            player.CurrentSpeed.ToString("F2") + " m/s"
        );

        Line(
            "state",
            player.IsSprinting
                ? "sprinting"
                : player.IsMoving
                    ? "moving"
                    : "still"
        );

        Line(
            "grounded",
            player.IsGrounded ? "yes" : "no"
        );

        if (noiseEmitter != null)
        {
            Line(
                "step noise",
                noiseEmitter.CurrentNoiseRadius.ToString("F1") + " m"
            );
        }
        else
        {
            Warn("no PlayerNoiseEmitter on player");
        }
    }


    private void AppendNoiseSection()
    {
        builder.AppendLine();

        Header("NOISE BUS");

        Line("emitted", NoiseBus.EmitCount.ToString());

        var history = NoiseBus.History;

        if (history.Count == 0)
        {
            Line("recent", "silence");

            return;
        }

        /*
         * Newest first, capped so a burst of footsteps cannot push the
         * rest of the panel off screen.
         */

        int shown = 0;

        for (int i = history.Count - 1;
             i >= 0 && shown < 5;
             i--, shown++)
        {
            NoiseEvent noise = history[i];

            builder.Append("  ");
            builder.Append(noise.Type);
            builder.Append("  r=");
            builder.Append(noise.Radius.ToString("F1"));
            builder.Append("m  ");
            builder.Append(noise.Age.ToString("F1"));
            builder.AppendLine("s ago");
        }
    }


    private void AppendSetupSection()
    {
        bool layersOk = GameLayers.CoreLayersReady;

        if (layersOk && navMeshPresent)
            return;

        builder.AppendLine();

        Header("SETUP");

        if (!layersOk)
        {
            Warn("core layers missing - check Tags and Layers");
        }

        if (!navMeshPresent)
        {
            Warn("no NavMesh baked - Window > AI > Navigation");
        }
    }


    // =========================================================
    // FORMATTING
    // =========================================================

    private void Header(string title)
    {
        builder.Append("<b><color=#7FD4FF>");
        builder.Append(title);
        builder.AppendLine("</color></b>");
    }


    private void Line(string label, string value)
    {
        builder.Append("  <color=#9AA5B1>");
        builder.Append(label);
        builder.Append("</color>  ");
        builder.AppendLine(value);
    }


    private void Warn(string message)
    {
        builder.Append("  <color=#FFB454>! ");
        builder.Append(message);
        builder.AppendLine("</color>");
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!showNoiseGizmos)
            return;

        if (!Application.isPlaying)
            return;

        var history = NoiseBus.History;

        for (int i = 0; i < history.Count; i++)
        {
            NoiseEvent noise = history[i];

            float fade = Mathf.Clamp01(1f - noise.Age / 5f);

            if (fade <= 0f)
                continue;

            Gizmos.color =
                new Color(1f, 0.4f, 0.25f, fade * 0.5f);

            Gizmos.DrawWireSphere(
                noise.Position,
                noise.Radius
            );
        }
    }
}

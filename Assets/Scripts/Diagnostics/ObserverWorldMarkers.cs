using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Draws the creature's state into the world, visible in the Game view
/// from any camera.
///
///   cyan        the path it is walking and where it is heading
///   white cone  what it can see (VisionSense range and angle)
///   orange      last known position - what it BELIEVES
///   grey        the last place it finished searching
///   yellow      noises as they happen, at their real radius
///   magenta     suspicious items it could notice
///   blue        hiding spots (red while occupied)
///   green       the player's real position - ground truth, which no
///               part of the AI reads
///
/// The orange-versus-green gap is the point of the whole view: it is
/// the distance between what the creature knows and what is true, and
/// a creature that reads as intelligent is one that closes that gap
/// for believable reasons.
/// </summary>
public class ObserverWorldMarkers : MonoBehaviour
{
    [Header("Targets (found automatically when empty)")]
    [SerializeField]
    private XenoBehaviourTree tree;

    [SerializeField]
    private PlayerController player;

    [Header("Toggle")]
    [SerializeField]
    private Key toggleKey = Key.F4;

    [SerializeField]
    private bool visibleOnStart = true;

    [Header("What to draw")]
    [SerializeField]
    private bool drawVisionCone = true;

    [SerializeField]
    private bool drawHidingSpots = true;

    [SerializeField]
    private bool drawGroundTruth = true;

    [SerializeField, Min(0.5f)]
    private float noiseLifetime = 2.5f;

    [SerializeField]
    private bool drawLabels = true;


    private struct NoiseMark
    {
        public Vector3 Position;
        public float Radius;
        public float Time;
        public NoiseType Type;
    }


    private bool visible;

    private Material lineMaterial;

    private NavMeshAgent agent;

    private PerceptionSystem perception;

    private XenoSenses senses;

    private XenoLocomotion locomotion;

    private VisionSense vision;

    private XenoHuntMemory huntMemory;

    private readonly List<NoiseMark> noises = new List<NoiseMark>();

    private GUIStyle labelStyle;


    private static readonly Color PathColor = new Color(0.3f, 0.9f, 1f);
    private static readonly Color BeliefColor = new Color(1f, 0.55f, 0.2f);
    private static readonly Color SearchedColor = new Color(0.8f, 0.8f, 0.8f);
    private static readonly Color NoiseColor = new Color(1f, 0.92f, 0.3f);
    private static readonly Color ItemColor = new Color(1f, 0.35f, 0.9f);
    private static readonly Color SpotColor = new Color(0.35f, 0.55f, 1f);
    private static readonly Color TruthColor = new Color(0.4f, 1f, 0.45f);
    private static readonly Color ConeColor = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color BeamColor = new Color(1f, 0.95f, 0.6f, 0.8f);


    public bool Visible
    {
        get => visible;
        set => visible = value;
    }


    private void Awake()
    {
        visible = visibleOnStart;

        if (tree == null) tree = FindFirstObjectByType<XenoBehaviourTree>();

        if (player == null) player = FindFirstObjectByType<PlayerController>();

        if (tree != null)
        {
            agent = tree.GetComponent<NavMeshAgent>();
            perception = tree.GetComponent<PerceptionSystem>();
            senses = tree.GetComponent<XenoSenses>();
            locomotion = tree.GetComponent<XenoLocomotion>();
            vision = tree.GetComponentInChildren<VisionSense>();
            huntMemory = tree.GetComponent<XenoHuntMemory>();
        }

        Shader shader = Shader.Find("Hidden/Internal-Colored");

        lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", (int)CullMode.Off);
        lineMaterial.SetInt("_ZWrite", 0);
        lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
    }


    private void OnEnable()
    {
        NoiseBus.OnNoise += HandleNoise;

        RenderPipelineManager.endCameraRendering += HandleEndCamera;
    }


    private void OnDisable()
    {
        NoiseBus.OnNoise -= HandleNoise;

        RenderPipelineManager.endCameraRendering -= HandleEndCamera;
    }


    private void OnDestroy()
    {
        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
        }
    }


    private void HandleNoise(NoiseEvent noise)
    {
        noises.Add(new NoiseMark
        {
            Position = noise.Position,
            Radius = noise.Radius,
            Time = Time.time,
            Type = noise.Type
        });
    }


    private void Update()
    {
        Keyboard kb = Keyboard.current;

        if (kb != null && kb[toggleKey].wasPressedThisFrame)
        {
            visible = !visible;
        }

        noises.RemoveAll(n => Time.time - n.Time > noiseLifetime);
    }


    // =========================================================
    // LINES
    // =========================================================

    private void HandleEndCamera(ScriptableRenderContext context, Camera cam)
    {
        if (!visible || tree == null || lineMaterial == null)
            return;

        if (cam.cameraType != CameraType.Game)
            return;

        GL.PushMatrix();

        GL.LoadProjectionMatrix(cam.projectionMatrix);

        GL.modelview = cam.worldToCameraMatrix;

        lineMaterial.SetPass(0);

        GL.Begin(GL.LINES);

        DrawAll();

        GL.End();

        GL.PopMatrix();
    }


    private void DrawAll()
    {
        Vector3 lift = Vector3.up * 0.05f;

        Vector3 self = tree.transform.position + lift;

        // Path.
        if (agent != null && agent.isOnNavMesh && agent.hasPath)
        {
            Vector3[] corners = agent.path.corners;

            for (int i = 1; i < corners.Length; i++)
            {
                Line(corners[i - 1] + lift, corners[i] + lift, PathColor);
            }
        }

        if (locomotion != null && locomotion.HasDestination)
        {
            Ring(locomotion.Destination + lift, 0.5f, PathColor);
            Cross(locomotion.Destination + lift, 0.3f, PathColor);
        }

        // Vision.
        if (drawVisionCone && vision != null)
        {
            Vector3 eye = vision.transform.position;

            Vector3 fwd = tree.transform.forward;

            float half = vision.Angle * 0.5f;

            Vector3 basePos = new Vector3(eye.x, tree.transform.position.y + 0.08f, eye.z);

            Vector3 left = Quaternion.Euler(0f, -half, 0f) * fwd * vision.Range;
            Vector3 right = Quaternion.Euler(0f, half, 0f) * fwd * vision.Range;

            Line(basePos, basePos + left, ConeColor);
            Line(basePos, basePos + right, ConeColor);

            int steps = 16;

            Vector3 prev = basePos + left;

            for (int i = 1; i <= steps; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, Mathf.Lerp(-half, half, i / (float)steps), 0f) * fwd * vision.Range;

                Line(prev, basePos + dir, ConeColor);

                prev = basePos + dir;
            }
        }

        // Belief.
        if (perception != null && perception.HasLastKnownPosition)
        {
            Vector3 lkp = perception.LastKnownPosition + lift;

            Ring(lkp, 0.7f, BeliefColor);
            Ring(lkp, 0.75f, BeliefColor);

            Dashed(self, lkp, BeliefColor);
        }

        if (senses != null && senses.HasLastSearchPosition)
        {
            Ring(senses.LastSearchPosition + lift, 1.2f, SearchedColor);
        }

        if (huntMemory == null && tree != null) huntMemory = tree.GetComponent<XenoHuntMemory>();

        if (huntMemory != null)
        {
            /* Coverage: faint squares, red = not looked at for a minute or more. */
            huntMemory.ForEachCell((p, stale) =>
            {
                Color c = Color.Lerp(new Color(0.3f, 1f, 0.4f, 0.10f), new Color(1f, 0.3f, 0.3f, 0.22f), stale);
                Square(p + lift, 0.4f, c);
            });

            /* Trail of sightings. */
            IReadOnlyList<XenoHuntMemory.Sighting> trail = huntMemory.Trail;

            for (int i = 1; i < trail.Count; i++)
            {
                float age = Mathf.Clamp01((Time.time - trail[i].Time) / 30f);

                Line(trail[i - 1].Position + lift, trail[i].Position + lift,
                    new Color(BeliefColor.r, BeliefColor.g, BeliefColor.b, 0.7f * (1f - age)));
            }

            if (huntMemory.HasHeading && perception != null && perception.HasLastKnownPosition)
            {
                Vector3 a = perception.LastKnownPosition + lift;
                Vector3 b = a + huntMemory.Heading * 3f;

                Line(a, b, BeliefColor);
                Line(b, b - Quaternion.Euler(0f, 25f, 0f) * huntMemory.Heading * 0.8f, BeliefColor);
                Line(b, b - Quaternion.Euler(0f, -25f, 0f) * huntMemory.Heading * 0.8f, BeliefColor);
            }

            if (huntMemory.HasEstimate)
            {
                Cross(huntMemory.Estimate + lift, 0.6f, BeliefColor);
                Ring(huntMemory.Estimate + lift, 1.0f, BeliefColor);
            }
        }

        // Noises.
        foreach (NoiseMark n in noises)
        {
            float age = (Time.time - n.Time) / noiseLifetime;

            Color c = NoiseColor;

            c.a = 1f - age;

            Ring(n.Position + lift, Mathf.Max(0.3f, n.Radius * Mathf.Min(1f, age * 3f)), c, 40);

            Cross(n.Position + lift, 0.25f, c);
        }

        // Suspicious items.
        IReadOnlyList<SuspiciousItem> items = SuspiciousItemBus.Items;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null || items[i].Consumed)
                continue;

            Ring(items[i].Position + lift, 0.4f, ItemColor);

            Ring(items[i].Position + lift, items[i].NoticeRadius, new Color(ItemColor.r, ItemColor.g, ItemColor.b, 0.25f), 40);
        }

        // Hiding spots.
        if (drawHidingSpots)
        {
            IReadOnlyList<HidingSpot> spots = HidingSpot.Active;

            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i] == null)
                    continue;

                Vector3 p = spots[i].transform.position + lift;

                Square(p, 0.45f, spots[i].Occupied ? Color.red : SpotColor);
            }
        }

        // Flashlight beams.
        for (int i = 0; i < Flashlight.Lit.Count; i++)
        {
            Flashlight f = Flashlight.Lit[i];

            if (f == null || !f.IsBeaming)
                continue;

            Vector3 end = f.BeamOrigin + f.BeamDirection * f.BeamRange;

            if (Physics.Raycast(f.BeamOrigin, f.BeamDirection, out RaycastHit hit, f.BeamRange,
                    GameLayers.SightBlockers, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
            }

            Line(f.BeamOrigin, end, BeamColor);

            Ring(end, 0.4f, BeamColor);
        }

        // Director's area of interest.
        XenoBlackboard board = tree.Blackboard;

        if (board != null && board.HasDirectorArea)
        {
            Color area = board.DirectorIntent == DirectorIntent.EaseOff
                ? new Color(0.5f, 0.75f, 1f, 0.8f)
                : new Color(0.9f, 0.6f, 1f, 0.8f);

            Ring(board.DirectorAreaCenter + lift, board.DirectorAreaRadius, area, 48);
            Ring(board.DirectorAreaCenter + lift, board.DirectorAreaRadius - 0.15f, area, 48);
            Cross(board.DirectorAreaCenter + lift, 0.5f, area);
        }

        // Ground truth.
        if (drawGroundTruth && player != null)
        {
            Vector3 p = player.transform.position + lift;

            Ring(p, 0.45f, TruthColor);
            Ring(p, 0.5f, TruthColor);
            Line(p, p + player.transform.forward * 1.2f, TruthColor);
        }
    }


    private static void Line(Vector3 a, Vector3 b, Color c)
    {
        GL.Color(c);
        GL.Vertex(a);
        GL.Vertex(b);
    }


    private static void Dashed(Vector3 a, Vector3 b, Color c)
    {
        float len = Vector3.Distance(a, b);

        int dashes = Mathf.Clamp(Mathf.FloorToInt(len / 0.6f), 1, 200);

        for (int i = 0; i < dashes; i += 2)
        {
            Line(Vector3.Lerp(a, b, i / (float)dashes), Vector3.Lerp(a, b, (i + 1) / (float)dashes), c);
        }
    }


    private static void Ring(Vector3 center, float radius, Color c, int segments = 24)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;

            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);

            Line(prev, next, c);

            prev = next;
        }
    }


    private static void Cross(Vector3 p, float s, Color c)
    {
        Line(p + new Vector3(-s, 0f, -s), p + new Vector3(s, 0f, s), c);
        Line(p + new Vector3(-s, 0f, s), p + new Vector3(s, 0f, -s), c);
    }


    private static void Square(Vector3 p, float s, Color c)
    {
        Vector3 a = p + new Vector3(-s, 0f, -s);
        Vector3 b = p + new Vector3(s, 0f, -s);
        Vector3 d = p + new Vector3(-s, 0f, s);
        Vector3 e = p + new Vector3(s, 0f, s);

        Line(a, b, c);
        Line(b, e, c);
        Line(e, d, c);
        Line(d, a, c);
    }


    // =========================================================
    // LABELS
    // =========================================================

    private void OnGUI()
    {
        if (!visible || !drawLabels || tree == null)
            return;

        Camera cam = ObserverCamera.ActiveCamera != null ? ObserverCamera.ActiveCamera : Camera.main;

        if (cam == null)
            return;

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        Tag(cam, tree.transform.position + Vector3.up * 2.3f, tree.ActiveBranchName().Replace("Branch(", "").Replace(")", ""), new Color(1f, 0.72f, 0.2f));

        if (perception != null && perception.HasLastKnownPosition)
        {
            Tag(cam, perception.LastKnownPosition + Vector3.up * 0.6f, "last known", BeliefColor);
        }

        if (tree.Blackboard != null && tree.Blackboard.HasDirectorArea)
        {
            Tag(cam, tree.Blackboard.DirectorAreaCenter + Vector3.up * 0.5f,
                "Director: " + tree.Blackboard.DirectorIntent,
                tree.Blackboard.DirectorIntent == DirectorIntent.EaseOff ? new Color(0.5f, 0.75f, 1f) : new Color(0.9f, 0.6f, 1f));
        }

        if (huntMemory != null && huntMemory.HasEstimate)
        {
            Tag(cam, huntMemory.Estimate + Vector3.up * 0.9f, "best guess", BeliefColor);
        }

        if (senses != null && senses.HasLastSearchPosition)
        {
            Tag(cam, senses.LastSearchPosition + Vector3.up * 0.4f, "searched", SearchedColor);
        }

        if (drawGroundTruth && player != null && ObserverCamera.ActiveCamera != null &&
            ObserverCamera.ActiveCamera != Camera.main)
        {
            Tag(cam, player.transform.position + Vector3.up * 2.1f, "PLAYER (truth)", TruthColor);
        }
    }


    private void Tag(Camera cam, Vector3 world, string s, Color color)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);

        if (sp.z <= 0f)
            return;

        Rect r = new Rect(sp.x - 120f, Screen.height - sp.y - 10f, 240f, 20f);

        GUI.color = new Color(0f, 0f, 0f, 0.8f);

        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), s, labelStyle);

        GUI.color = color;

        GUI.Label(r, s, labelStyle);

        GUI.color = Color.white;
    }
}

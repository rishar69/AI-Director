using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/Scenes/AIObservatory.unity: a small, roofless arena
/// for watching the creature think.
///
/// The layout is chosen for behaviour, not looks. Every space exists
/// to make the creature show something:
///
///   central hall + ring corridor  a loop you can be chased around;
///                                 the pillars break line of sight
///   two west offices              linked by an inner door, so there
///                                 are two ways in and out, with desks
///                                 to hide under
///   east storage                  shelving rows to search between
///   north gallery                 a long sightline, with a back door
///                                 into storage (a second loop)
///   south lobby                   where the player starts
///
/// No roof, so the overhead camera sees into every room. No vents.
///
/// The player, creature, Director, debug and item spawner are copied
/// in from the museum, so this scene always runs the same AI the game
/// does. The observation tools (tree viewer, creature panel, world
/// markers, cameras) are added on an "Observatory" object.
///
/// Tools > AI Director > AI Observatory - Build Scene, or
/// ObservatoryBuilder.Build() from the CLI.
/// </summary>
public static class ObservatoryBuilder
{
    private const string MuseumPath = "Assets/Scenes/Museum.unity";

    public const string ScenePath = "Assets/Scenes/AIObservatory.unity";

    private const string NavMeshAssetPath = "Assets/Scenes/AIObservatory_NavMesh.asset";

    private const string DeskPrefab = "Assets/BristishGangstersPack_Museum/Prefabs/Mesh_Table_carre.prefab";

    private static readonly string[] CopiedRoots =
    {
        "Player", "Xenomorph", "AI Director", "AI Debug", "Suspicious Item Spawner"
    };

    private const float WallHeight = 3f;

    private const float WallThickness = 0.3f;

    public static readonly Vector3 PlayerStart = new Vector3(-4f, 0.05f, -12f);

    public static readonly Vector3 CreatureStart = new Vector3(14f, 0.05f, 8f);


    private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

    private static Transform geometry;

    private static Transform hiding;


    private static readonly Color FloorColor = new Color(0.36f, 0.37f, 0.39f);
    private static readonly Color WallColor = new Color(0.82f, 0.82f, 0.8f);
    private static readonly Color PillarColor = new Color(0.68f, 0.68f, 0.67f);
    private static readonly Color ShelfColor = new Color(0.5f, 0.5f, 0.52f);
    private static readonly Color BenchColor = new Color(0.6f, 0.6f, 0.6f);
    private static readonly Color LabelColor = new Color(0.62f, 0.64f, 0.68f);


    [MenuItem("Tools/AI Director/AI Observatory - Build Scene", priority = -81)]
    public static void BuildFromMenu()
    {
        EditorUtility.DisplayDialog("AI Observatory", Build(), "OK");
    }


    [MenuItem("Tools/AI Director/AI Observatory - Open Scene", priority = -80)]
    public static void OpenFromMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }


    public static string Build()
    {
        if (EditorApplication.isPlaying)
            return "Stop play mode first.";

        materials.Clear();

        StringBuilder report = new StringBuilder();

        Scene museum = SceneManager.GetSceneByPath(MuseumPath);

        bool openedMuseum = false;

        if (!museum.IsValid() || !museum.isLoaded)
        {
            museum = EditorSceneManager.OpenScene(MuseumPath, OpenSceneMode.Additive);

            openedMuseum = true;
        }

        Scene previousActive = SceneManager.GetActiveScene();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        SceneManager.SetActiveScene(scene);

        try
        {
            geometry = new GameObject("Arena").transform;

            hiding = new GameObject("Hiding Spots").transform;

            BuildLighting();

            BuildArena(report);

            List<GameObject> copies = CopyRigs(museum, scene, report);

            BakeNavMesh(scene, copies, report);

            PlaceRigs(copies, report);

            AddObservatory(report);

            EditorSceneManager.SaveScene(scene, ScenePath);

            report.Insert(0, "Built " + ScenePath + "\n");
        }
        finally
        {
            if (previousActive.IsValid() && previousActive.isLoaded)
            {
                SceneManager.SetActiveScene(previousActive);
            }

            EditorSceneManager.CloseScene(scene, true);

            if (openedMuseum)
            {
                EditorSceneManager.CloseScene(museum, true);
            }
        }

        AddToBuildSettings();

        return report.ToString();
    }


    // =========================================================
    // LIGHT
    // =========================================================

    /// <summary>
    /// Night, but readable from above: a cool moon with soft shadows and
    /// enough flat ambient that dark corners are still legible. The
    /// creature's vision does not use light levels, so this is purely
    /// for the observer.
    /// </summary>
    private static void BuildLighting()
    {
        GameObject moon = new GameObject("Moon");

        Light light = moon.AddComponent<Light>();

        light.type = LightType.Directional;
        light.intensity = 0.55f;
        light.color = new Color(0.78f, 0.84f, 1f);
        light.shadows = LightShadows.Soft;

        moon.transform.rotation = Quaternion.Euler(62f, -30f, 0f);

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.32f, 0.36f);
        RenderSettings.fog = false;
    }


    // =========================================================
    // ARENA
    // =========================================================

    private static void BuildArena(StringBuilder report)
    {
        /* Floor: 36 x 28, centred on the origin. */
        Box("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(37f, 0.2f, 29f), FloorColor);

        /* Outer boundary. */
        WallX("Outer South", -18f, 18f, -14f);
        WallX("Outer North", -18f, 18f, 14f);
        WallZ("Outer West", -14f, 14f, -18f);
        WallZ("Outer East", -14f, 14f, 18f);

        /* West line: lobby<->SW office, ring<->SW office, ring<->NW office. */
        WallZ("West Line", -14f, 14f, -9f, (-13f, -10f), (-6f, -3f), (1.5f, 4.5f));

        /* East line: ring<->storage, gallery<->storage. */
        WallZ("East Line", -14f, 14f, 9f, (-4.5f, -1.5f), (10f, 13f));

        /* Ring outer walls, north (to gallery) and south (to lobby). */
        WallX("Ring North", -9f, 9f, 9f, (-1.2f, 1.2f));
        WallX("Ring South", -9f, 9f, -9f, (3f, 6f));

        /* Central hall, a door in the middle of every side. */
        WallX("Hall South", -6f, 6f, -6f, (-1.2f, 1.2f));
        WallX("Hall North", -6f, 6f, 6f, (-1.2f, 1.2f));
        WallZ("Hall West", -6f, 6f, -6f, (-1.2f, 1.2f));
        WallZ("Hall East", -6f, 6f, 6f, (-1.2f, 1.2f));

        /* The two west offices, joined by an inner door. */
        WallX("Office Divider", -18f, -9f, 0f, (-15f, -13f));

        /* Hall pillars - line of sight breakers for the chase loop. */
        foreach (Vector2 p in new[] { new Vector2(-3f, -3f), new Vector2(3f, -3f), new Vector2(-3f, 3f), new Vector2(3f, 3f) })
        {
            Box("Pillar", new Vector3(p.x, WallHeight * 0.5f, p.y), new Vector3(0.9f, WallHeight, 0.9f), PillarColor);
        }

        /* Storage shelving: three rows, gaps at alternating ends. */
        Box("Shelf A", new Vector3(12f, 1f, -8f), new Vector3(0.6f, 2f, 7f), ShelfColor);
        Box("Shelf B", new Vector3(14.5f, 1f, -6f), new Vector3(0.6f, 2f, 7f), ShelfColor);
        Box("Shelf C", new Vector3(12f, 1f, 4f), new Vector3(0.6f, 2f, 5f), ShelfColor);
        Box("Crates", new Vector3(16f, 0.6f, 2f), new Vector3(1.4f, 1.2f, 1.4f), ShelfColor);

        /* Gallery benches - low, so they block movement but not sight. */
        Box("Bench West", new Vector3(-5f, 0.25f, 11.5f), new Vector3(3f, 0.5f, 0.7f), BenchColor);
        Box("Bench East", new Vector3(5f, 0.25f, 11.5f), new Vector3(3f, 0.5f, 0.7f), BenchColor);

        /* Lobby reception block. */
        Box("Reception", new Vector3(-6f, 0.55f, -11.5f), new Vector3(0.8f, 1.1f, 3f), BenchColor);

        /* Desks to hide under: two in the NW office, one in each other side room. */
        int desks = 0;

        desks += Desk(new Vector3(-15f, 0f, 8f), 0f, Vector3.back, "NW Office Desk A");
        desks += Desk(new Vector3(-12f, 0f, 11f), 90f, Vector3.right, "NW Office Desk B");
        desks += Desk(new Vector3(-14f, 0f, -8f), 0f, Vector3.forward, "SW Office Desk");
        desks += Desk(new Vector3(16f, 0f, -12f), 0f, Vector3.forward, "Storage Desk");
        desks += Desk(new Vector3(0f, 0f, 12.6f), 0f, Vector3.back, "Gallery Desk");

        report.AppendLine("arena: hall + ring + 5 rooms, " + desks + " desks to hide under");

        /* Room names on the floor, read from the overhead camera. */
        FloorLabel("CENTRAL HALL", new Vector3(0f, 0.02f, 0f), 1.0f);
        FloorLabel("RING", new Vector3(0f, 0.02f, -7.5f), 0.6f);
        FloorLabel("NW OFFICE", new Vector3(-13.5f, 0.02f, 4f), 0.8f);
        FloorLabel("SW OFFICE", new Vector3(-13.5f, 0.02f, -4f), 0.8f);
        FloorLabel("STORAGE", new Vector3(15.5f, 0.02f, 9f), 0.8f);
        FloorLabel("GALLERY", new Vector3(-5f, 0.02f, 13f), 0.8f);
        FloorLabel("LOBBY", new Vector3(2f, 0.02f, -12f), 0.8f);
    }


    /// <summary>A wall along X at a given Z, with gaps as (fromX, toX).</summary>
    private static void WallX(string name, float x0, float x1, float z, params (float, float)[] gaps)
    {
        foreach ((float a, float b) in Segments(x0, x1, gaps))
        {
            Box(name, new Vector3((a + b) * 0.5f, WallHeight * 0.5f, z),
                new Vector3(b - a + WallThickness, WallHeight, WallThickness), WallColor);
        }
    }


    /// <summary>A wall along Z at a given X, with gaps as (fromZ, toZ).</summary>
    private static void WallZ(string name, float z0, float z1, float x, params (float, float)[] gaps)
    {
        foreach ((float a, float b) in Segments(z0, z1, gaps))
        {
            Box(name, new Vector3(x, WallHeight * 0.5f, (a + b) * 0.5f),
                new Vector3(WallThickness, WallHeight, b - a + WallThickness), WallColor);
        }
    }


    private static IEnumerable<(float, float)> Segments(float from, float to, (float, float)[] gaps)
    {
        List<(float, float)> sorted = new List<(float, float)>(gaps);

        sorted.Sort((p, q) => p.Item1.CompareTo(q.Item1));

        float cursor = from;

        foreach ((float a, float b) in sorted)
        {
            if (a - cursor > 0.05f) yield return (cursor, a);

            cursor = b;
        }

        if (to - cursor > 0.05f) yield return (cursor, to);
    }


    private static GameObject Box(string name, Vector3 center, Vector3 size, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);

        box.name = name;

        box.transform.SetParent(geometry, false);

        box.transform.position = center;

        box.transform.localScale = size;

        box.GetComponent<MeshRenderer>().sharedMaterial = Mat(color);

        box.isStatic = true;

        return box;
    }


    private static Material Mat(Color color)
    {
        if (materials.TryGetValue(color, out Material m))
            return m;

        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        m.SetColor("_BaseColor", color);

        m.SetFloat("_Smoothness", 0.15f);

        materials[color] = m;

        return m;
    }


    /// <summary>
    /// A desk with a hiding spot under it, facing out of the side the
    /// creature will approach from. Same pose the museum uses.
    /// </summary>
    private static int Desk(Vector3 at, float yaw, Vector3 facing, string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DeskPrefab);

        if (prefab == null)
            return 0;

        GameObject desk = (GameObject)PrefabUtility.InstantiatePrefab(prefab, geometry.gameObject.scene);

        desk.name = name;

        desk.transform.SetParent(geometry, true);

        desk.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));

        Bounds b = RenderBounds(desk);

        desk.transform.position += Vector3.up * (at.y - b.min.y);

        foreach (Renderer r in desk.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null)
                {
                    mats[i] = HidingTestBuilder.DoubleSided(mats[i]);
                }
            }

            r.sharedMaterials = mats;
        }

        b = RenderBounds(desk);

        Vector3 under = new Vector3(b.center.x, b.min.y, b.center.z) - facing * HidingSpot.DeskTuckBack;

        GameObject go = new GameObject("Hiding Spot - " + name);

        go.transform.SetParent(hiding, false);

        go.transform.SetPositionAndRotation(under, Quaternion.LookRotation(facing, Vector3.up));

        HidingSpot spot = go.AddComponent<HidingSpot>();

        spot.ConfigureDesk(1.8f);

        go.AddComponent<InteractionMarker>().Setup(InteractionMarker.Kind.Hide, null, 1.3f);

        return 1;
    }


    private static void FloorLabel(string text, Vector3 at, float size)
    {
        GameObject go = new GameObject("Label " + text);

        go.transform.SetParent(geometry, false);

        go.transform.SetPositionAndRotation(at, Quaternion.Euler(90f, 0f, 0f));

        TextMesh tm = go.AddComponent<TextMesh>();

        tm.text = text;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 64;
        tm.characterSize = size * 0.08f;
        tm.color = LabelColor;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        tm.font = font;

        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }


    // =========================================================
    // RIGS
    // =========================================================

    private static List<GameObject> CopyRigs(Scene museum, Scene scene, StringBuilder report)
    {
        List<GameObject> copies = new List<GameObject>();

        foreach (string rootName in CopiedRoots)
        {
            GameObject source = null;

            foreach (GameObject g in museum.GetRootGameObjects())
            {
                if (g.name == rootName)
                {
                    source = g;
                    break;
                }
            }

            if (source == null)
            {
                report.AppendLine("MISSING in museum: " + rootName);
                continue;
            }

            GameObject copy = Object.Instantiate(source);

            copy.name = source.name;

            SceneManager.MoveGameObjectToScene(copy, scene);

            copies.Add(copy);
        }

        report.AppendLine("copied from museum: " + copies.Count + "/" + CopiedRoots.Length + " rigs");

        /* The tree draws its own vision cone outline; the museum's filled cone would cover the overhead view. */
        foreach (GameObject c in copies)
        {
            foreach (VisionConeVisualizer v in c.GetComponentsInChildren<VisionConeVisualizer>(true))
            {
                v.gameObject.SetActive(false);
            }
        }

        /* The museum's F1 overlay stays available but starts hidden - the new panels replace it here. */
        foreach (GameObject c in copies)
        {
            AIDebugOverlay overlay = c.GetComponent<AIDebugOverlay>();

            if (overlay != null)
            {
                SerializedObject so = new SerializedObject(overlay);

                SerializedProperty p = so.FindProperty("visibleOnStart");

                if (p != null)
                {
                    p.boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        return copies;
    }


    private static void PlaceRigs(List<GameObject> copies, StringBuilder report)
    {
        foreach (GameObject c in copies)
        {
            PlayerController pc = c.GetComponentInChildren<PlayerController>(true);

            if (pc != null)
            {
                pc.transform.SetPositionAndRotation(PlayerStart, Quaternion.identity);

                foreach (Camera cam in c.GetComponentsInChildren<Camera>(true))
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
                }
            }

            XenoBehaviourTree tree = c.GetComponentInChildren<XenoBehaviourTree>(true);

            if (tree != null)
            {
                Vector3 at = CreatureStart;

                if (NavMesh.SamplePosition(at, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    at = hit.position;
                }

                tree.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 180f, 0f));
            }
        }

        report.AppendLine("player in the lobby, creature in storage");
    }


    private static void AddObservatory(StringBuilder report)
    {
        GameObject go = new GameObject("Observatory");

        go.AddComponent<BTLiveViewer>();
        go.AddComponent<CreatureObserverPanel>();
        go.AddComponent<ObserverWorldMarkers>();

        ObserverCamera cam = go.AddComponent<ObserverCamera>();

        SerializedObject so = new SerializedObject(cam);

        so.FindProperty("freeCentre").vector3Value = Vector3.zero;

        so.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine("observatory: tree viewer, creature panel, world markers, cameras");
    }


    // =========================================================
    // NAVMESH
    // =========================================================

    private static void BakeNavMesh(Scene scene, List<GameObject> rigs, StringBuilder report)
    {
        GameObject host = new GameObject("NavMesh");

        NavMeshSurface surface = host.AddComponent<NavMeshSurface>();

        surface.collectObjects = CollectObjects.All;

        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        int mask = ~0;

        int playerLayer = LayerMask.NameToLayer(GameLayers.PlayerName);

        int npcLayer = LayerMask.NameToLayer(GameLayers.NpcName);

        if (playerLayer >= 0) mask &= ~(1 << playerLayer);

        if (npcLayer >= 0) mask &= ~(1 << npcLayer);

        surface.layerMask = mask;

        /* Only the arena may be collected: hide every other open scene and the copied rigs. */
        List<GameObject> hidden = new List<GameObject>();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene other = SceneManager.GetSceneAt(i);

            if (other == scene || !other.isLoaded)
                continue;

            foreach (GameObject root in other.GetRootGameObjects())
            {
                if (root.activeSelf)
                {
                    root.SetActive(false);
                    hidden.Add(root);
                }
            }
        }

        foreach (GameObject r in rigs)
        {
            if (r.activeSelf)
            {
                r.SetActive(false);
                hidden.Add(r);
            }
        }

        try
        {
            Physics.SyncTransforms();

            surface.BuildNavMesh();
        }
        finally
        {
            foreach (GameObject root in hidden)
            {
                root.SetActive(true);
            }
        }

        if (surface.navMeshData != null)
        {
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(NavMeshAssetPath);
            }

            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAssetPath);

            AssetDatabase.SaveAssets();

            report.AppendLine("navmesh baked -> " + NavMeshAssetPath);
        }
        else
        {
            report.AppendLine("NAVMESH BAKE FAILED");
        }
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private static Bounds RenderBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);

        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);

        foreach (Renderer r in renderers)
        {
            b.Encapsulate(r.bounds);
        }

        return b;
    }


    private static void AddToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        foreach (EditorBuildSettingsScene s in scenes)
        {
            if (s.path == ScenePath)
                return;
        }

        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));

        EditorBuildSettings.scenes = scenes.ToArray();
    }
}

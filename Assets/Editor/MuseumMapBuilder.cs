using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds "The Caretaker" museum - two floors, from text grids.
///
/// The level is data, not hand placement: a layout change is a text
/// edit and a rebuild (spec: Docs/MuseumLayout.md). Cells are 3 m - a
/// real corridor's width - and the builder derives every wall: where
/// two different rooms touch, each builds its own half, in its own
/// finish and at its own height.
///
/// Two floors 5.6 m apart. The grand stair hall and both stairwells
/// are double height; the upper floor's balcony overlooks the hall
/// through a stone balustrade. The stairs are the British Gangsters
/// pack's imperial staircase and wooden stair modules.
///
/// Only the "Museum Map", "Museum Lighting" and "Museum Game" roots are
/// rebuilt; the player, the creature and the Director are left alone.
/// </summary>
public static class MuseumMapBuilder
{
    // =========================================================
    // PATHS
    // =========================================================

    private const string ScenePath = "Assets/Scenes/Museum.unity";

    private const string GeneratedDir = "Assets/Museum";

    private const string MeshAssetPath = GeneratedDir + "/MuseumMeshes.asset";

    private const string NavMeshAssetPath = GeneratedDir + "/MuseumNavMesh.asset";

    private const string PostProfilePath = GeneratedDir + "/MuseumPost.asset";

    private const string RigScenePath =
        "Assets/AngeloMaN87/Prototype Map/Scenes/Prototype Map.unity";

    private const string BgpDir = "Assets/BristishGangstersPack_Museum/";

    private const string AkDir = "Assets/AK Studio Art/Art Gallery Vol.2/";

    private const string MapRootName = "Museum Map";

    private const string LightingRootName = "Museum Lighting";

    private const string GameRootName = "Museum Game";

    private const string MaterialDir = GeneratedDir + "/Materials";


    // =========================================================
    // DIMENSIONS
    // =========================================================

    /// <summary>One grid cell: a corridor's width.</summary>
    private const float Cell = 3.0f;

    /// <summary>Each room's half of a shared wall.</summary>
    private const float Slab = 0.15f;

    private const float PillarSize = 0.3f;

    /// <summary>Floor to floor. The imperial stair is scaled to arrive exactly here.</summary>
    private const float FloorHeight = 5.6f;

    /// <summary>The upper floor's slab.</summary>
    private const float FloorSlab = 0.3f;

    /// <summary>Roof line of the double-height spaces and the upper floor.</summary>
    private const float RoofHeight = 10.0f;

    private const float StaffDoorWidth = 2.3f;

    private const float StaffDoorHeight = 2.45f;

    private const float ExitDoorWidth = 2.6f;

    private const float ExitDoorHeight = 3.4f;

    private const float DoorwayWidth = 2.4f;

    private const float DoorwayHeight = 3.0f;


    // =========================================================
    // LAYOUT
    // =========================================================

    /*
     * Row 0 is the north edge. The lobby cell under the front doors,
     * (14, 17), sits on the world origin.
     *
     * GROUND
     *   hub      H grand stair hall (double height)   L lobby
     *   1 West   N natural history  s sculpture
     *   2 East   X antiquities      I discovery room
     *   3 Locked T temporary exhibition  Z special collection  V strongroom
     *   public   r corridors  M main exhibition  P/E toilets  Q cloakroom
     *            B first aid  K ticket office  Y security  G gift shop
     *            F cafe  k kitchen
     *   staff    S corridors  m maintenance  A storage  D loading dock
     *            C archive  R restoration lab  U lounge  O offices
     *   stairs   w service stairwell  f fire stairwell (both double height)
     *
     * UPPER
     *   b balcony ring  n modern art  l lecture hall  a reading room
     *   c classroom  i director's office  j meeting room  q staff corridor
     *   o open to below (the stair hall, the two stairwells)
     */
    private static readonly string[] Ground =
    {
        "mmmAAASSSSSSSSSSSSSSSSSSTTTT",
        "mmmAAASNNNNMMMMMMMMXXXXfTTTT",
        "mmmAAASNNNNMMMMMMMMXXXXfTTTT",
        "DDDDCCSNNNNMMMMMMMMXXXXfTTTT",
        "DDDDCCSNNNNMMMMMMMMXXXXfTTTT",
        "DDDDCCSrrrrrrrrrrrrrrrrrZZZZ",
        "DDDDCCSssssrHHHHHHrIIIIrZZZZ",
        "RRRRCCSssssrHHHHHHrIIIIrZZZZ",
        "RRRRCCSssssrHHHHHHrIIIIrZZZV",
        "RRRRCCSssssrHHHHHHrIIIIrZZZV",
        "RRRRCCSPPQQrHHHHHHrBBEErFFFF",
        "UUUOOOSPPQQrHHHHHHrBBEErFFFF",
        "UUUOOwSrrrrrrrrrrrrrrrrrFFFF",
        "UUUOOwSKKYYYLLLLLLGGGGGrFFFF",
        "UUUOOwSKKYYYLLLLLLGGGGGrkkkk",
        "UUUOOwSKKYYYLLLLLLGGGGGrkkkk",
        ".....wSKKYYYLLLLLLGGGGGr....",
        "............LLLLLL..........",
    };

    private static readonly string[] Upper =
    {
        "......qqqqqqqqqqqqqqqqqq....",
        "......qiijjnnnnnnnnllllo....",
        "......qiijjnnnnnnnnllllo....",
        "......qiijjnnnnnnnnllllo....",
        "......qiijjnnnnnnnnllllo....",
        "......qbbbbbbbbbbbbbbbb.....",
        "......qaaaaboooooobcccc.....",
        "......qaaaaboooooobcccc.....",
        "......qaaaaboooooobcccc.....",
        "......qaaaaboooooobcccc.....",
        "......qaaaaboooooobcccc.....",
        ".....qqaaaaboooooobcccc.....",
        ".....o.bbbbbbbbbbbbbbbb.....",
        ".....o......................",
        ".....o......................",
        ".....o......................",
        ".....o......................",
        "............................",
    };

    private const int OriginX = 14;

    private const int OriginZ = 17;

    private const char Void = 'o';


    private enum Dir { N, E, S, W }

    /*
     * Doorway  a cased opening between public rooms.
     * Staff    a door frame, leaves left open.
     * Gate / Vault / Bolted  locked doors (LockedDoor) - named so the
     *          objective layer can find them.
     */
    private enum DoorKind { Doorway, Staff, Exit, Gate, Vault, Bolted }


    private struct Door
    {
        public int Floor, X, Z;
        public Dir Side;
        public DoorKind Kind;

        public Door(int floor, int x, int z, Dir side, DoorKind kind)
        {
            Floor = floor;
            X = x;
            Z = z;
            Side = side;
            Kind = kind;
        }
    }


    private static Door G(int x, int z, Dir d, DoorKind k = DoorKind.Doorway) => new Door(0, x, z, d, k);

    private static Door U(int x, int z, Dir d, DoorKind k = DoorKind.Doorway) => new Door(1, x, z, d, k);


    /*
     * Every room has at least two ways out so a chase can loop, except
     * the toilets, the strongroom and the stairwells' far ends.
     */
    private static readonly Door[] Doors =
    {
        /* Stair hall and lobby - the central axis. */
        G(14, 11, Dir.S), G(15, 11, Dir.S), G(12, 9, Dir.W), G(17, 9, Dir.E),
        G(14, 13, Dir.N), G(15, 13, Dir.N),
        G(14, 17, Dir.S, DoorKind.Exit),

        /* Lobby block. */
        G(8, 13, Dir.N, DoorKind.Staff), G(8, 14, Dir.E, DoorKind.Staff),
        G(10, 13, Dir.N, DoorKind.Staff), G(11, 15, Dir.E, DoorKind.Staff),
        G(18, 14, Dir.W), G(20, 13, Dir.N),

        /* Toilets, cloakroom, first aid. */
        G(8, 11, Dir.S), G(10, 10, Dir.E), G(9, 11, Dir.S),
        G(19, 10, Dir.W), G(22, 11, Dir.S),

        /* North galleries. */
        G(8, 4, Dir.S), G(7, 2, Dir.W, DoorKind.Staff), G(10, 2, Dir.E),
        G(12, 4, Dir.S), G(17, 4, Dir.S), G(13, 1, Dir.N, DoorKind.Staff), G(18, 3, Dir.E),
        G(20, 4, Dir.S), G(21, 1, Dir.N, DoorKind.Staff),

        /* Section 1 west and section 2 east. */
        G(8, 6, Dir.N), G(10, 8, Dir.E), G(7, 7, Dir.W, DoorKind.Staff),
        G(20, 6, Dir.N), G(19, 8, Dir.W), G(22, 8, Dir.E),

        /* Section 3 - the locked wing. */
        G(23, 7, Dir.E, DoorKind.Gate),
        G(25, 5, Dir.N),
        G(26, 9, Dir.E, DoorKind.Vault),
        G(24, 0, Dir.W, DoorKind.Bolted),

        /* Cafe and kitchen. */
        G(24, 11, Dir.W), G(25, 14, Dir.N, DoorKind.Staff), G(24, 15, Dir.W, DoorKind.Staff),

        /* Fire stair, from the east corridor. */
        G(23, 4, Dir.S, DoorKind.Staff),

        /* Staff wing. */
        G(2, 1, Dir.E, DoorKind.Staff), G(1, 2, Dir.S, DoorKind.Staff),
        G(5, 1, Dir.E, DoorKind.Staff), G(4, 2, Dir.S, DoorKind.Staff),
        G(3, 4, Dir.E, DoorKind.Staff), G(1, 6, Dir.S, DoorKind.Staff),
        G(5, 5, Dir.E, DoorKind.Staff), G(5, 9, Dir.E, DoorKind.Staff),
        G(4, 8, Dir.W, DoorKind.Staff), G(1, 10, Dir.S, DoorKind.Staff),
        G(2, 13, Dir.E, DoorKind.Staff), G(5, 11, Dir.E, DoorKind.Staff),
        G(5, 16, Dir.E, DoorKind.Staff),
        G(6, 5, Dir.E, DoorKind.Staff), G(6, 12, Dir.E, DoorKind.Staff),

        /* Upper floor. */
        U(7, 2, Dir.W, DoorKind.Staff), U(8, 4, Dir.S), U(8, 2, Dir.E, DoorKind.Staff),
        U(9, 1, Dir.N, DoorKind.Staff),
        U(12, 4, Dir.S), U(17, 4, Dir.S), U(14, 1, Dir.N, DoorKind.Staff), U(19, 2, Dir.W),
        U(20, 4, Dir.S), U(21, 1, Dir.N, DoorKind.Staff),
        U(8, 6, Dir.N), U(10, 9, Dir.E), U(9, 11, Dir.S), U(7, 8, Dir.W, DoorKind.Staff),
        U(20, 6, Dir.N), U(19, 9, Dir.W), U(21, 11, Dir.S),
        U(6, 5, Dir.E, DoorKind.Staff),
    };


    /*
     * Upper-floor edges onto a void that stay open: where a stair
     * arrives. Every other balcony edge onto a void gets a balustrade.
     */
    private static readonly (int x, int z, Dir side)[] Arrivals =
    {
        (11, 6, Dir.E), (11, 7, Dir.E), (18, 6, Dir.W), (18, 7, Dir.W),
        (5, 11, Dir.S),
        (23, 0, Dir.S),
    };


    private class Region
    {
        public char Id;
        public string Name;
        public int Floor;
        public float Height;
        public bool Backstage;
        public string FloorMat;
        public string WallMat;
        public string CeilingMat;

        /// <summary>Fill light multiplier.</summary>
        public float Light = 1f;

        /// <summary>Wainscot or skirting along every plain wall (null = none).</summary>
        public string Skirting;
        public float SkirtingHeight = 0.24f;

        public string Cornice;

        /// <summary>A glowing laylight panel in each ceiling cell.</summary>
        public bool Laylight;

        /// <summary>Wall sconces every other cell - corridors and the balcony.</summary>
        public bool Sconces;
    }


    /* Generated surfaces (PrepareSurfaceMaterials). */

    private static string GenMat(string name) => MaterialDir + "/" + name + ".mat";

    private static readonly string PlasterWhite = GenMat("Plaster White");
    private static readonly string Wainscot = GenMat("Wainscot Grey");
    private static readonly string GreyOak = GenMat("Grey Oak");
    private static readonly string GreyMarble = GenMat("Grey Marble");
    private static readonly string GreyMarbleBorder = GenMat("Grey Marble Border");
    private static readonly string CeilingWhite = GenMat("Ceiling White");
    private static readonly string ServiceWall = GenMat("Service Grey");
    private static readonly string ServiceFloor = GenMat("Service Floor");
    private static readonly string SlateWall = GenMat("Slate Wall");
    private static readonly string StallGrey = GenMat("Stall Grey");
    private static readonly string ConcreteGrey = GenMat("Concrete Grey");
    private static readonly string VaultSteel = GenMat("Vault Steel");
    private static readonly string TombStone = GenMat("Tomb Stone");
    private static readonly string LaylightGlass = GenMat("Laylight");
    private static readonly string Silver = GenMat("Silver");
    private static readonly string Gilt = GenMat("Brass");
    private static readonly string SconceGlass = GenMat("Sconce Glass");
    private static readonly string RunnerRed = GenMat("Runner Red");


    private static Region Room(
        char id, string name, int floor, float height, string floorMat, string wall,
        string ceiling, bool backstage = false, float light = 1f) => new Region
    {
        Id = id, Name = name, Floor = floor, Height = height, Backstage = backstage,
        FloorMat = floorMat, WallMat = wall, CeilingMat = ceiling, Light = light,
    };


    /// <summary>A public room: white plaster over a dark wainscot, a white cornice.</summary>
    private static Region Public(char id, string name, int floor, float height, string floorMat,
        float light = 1f, bool sconces = false)
    {
        Region r = Room(id, name, floor, height, floorMat, PlasterWhite, CeilingWhite, light: light);
        r.Skirting = Wainscot;
        r.SkirtingHeight = 1.05f;
        r.Cornice = CeilingWhite;
        r.Sconces = sconces;
        return r;
    }


    /// <summary>Back of house: grey paint, a black skirting, no ornament.</summary>
    private static Region Staff(char id, string name, int floor, float height, bool sconces = false)
    {
        Region r = Room(id, name, floor, height, ServiceFloor, ServiceWall, CeilingWhite, backstage: true);
        r.Skirting = GenMat("Sign Black");
        r.Sconces = sconces;
        return r;
    }


    private static readonly Dictionary<char, Region> Regions = BuildRegions();


    private static Dictionary<char, Region> BuildRegions()
    {
        const float G1 = 5.3f;         /* ground rooms under the upper floor */
        const float U1 = RoofHeight - FloorHeight;

        Region hall = Public('H', "Grand Stair Hall", 0, RoofHeight, GreyMarbleBorder);
        hall.Laylight = true;

        Region t = Public('T', "Temporary Exhibition", 0, G1, GreyOak, light: 0.9f);
        t.WallMat = SlateWall;
        Region z = Public('Z', "Special Collection", 0, G1, GreyOak, light: 0.9f);
        z.WallMat = SlateWall;

        Region[] all =
        {
            hall,
            Public('L', "Lobby", 0, G1, GreyMarbleBorder),
            Public('r', "Corridors", 0, G1, GreyMarble, sconces: true),
            Public('M', "Main Exhibition Hall", 0, G1, GreyMarble),
            Public('N', "Natural History", 0, G1, GreyOak),
            Public('s', "Sculpture Gallery", 0, G1, GreyMarble),
            Public('X', "Antiquities", 0, G1, GreyMarble),
            Public('I', "Discovery Room", 0, G1, GreyOak),
            t, z,
            Room('V', "Strongroom", 0, 3.0f, ServiceFloor, VaultSteel, VaultSteel, light: 0.7f),
            Public('P', "Toilets West", 0, 3.4f, GreyMarble),
            Public('E', "Toilets East", 0, 3.4f, GreyMarble),
            Public('Q', "Cloakroom", 0, 3.4f, GreyOak),
            Public('B', "First Aid", 0, 3.4f, GreyMarble),
            Public('K', "Ticket Office", 0, 3.4f, GreyOak),
            Public('Y', "Security Office", 0, 3.4f, GreyOak),
            Public('G', "Gift Shop", 0, 4.2f, GreyOak),
            Public('F', "Cafe", 0, 4.2f, GreyOak),
            Staff('k', "Kitchen", 0, 3.2f),

            Staff('S', "Staff Corridors", 0, 3.2f, sconces: true),
            Staff('m', "Maintenance Room", 0, 3.4f),
            Staff('A', "Artifact Storage", 0, 3.4f),
            Staff('D', "Loading Dock", 0, 4.6f),
            Staff('C', "Archive", 0, 3.4f),
            Staff('R', "Restoration Lab", 0, 3.6f),
            Staff('U', "Staff Lounge", 0, 3.2f),
            Staff('O', "Staff Offices", 0, 3.2f),
            Staff('w', "Service Stairwell", 0, RoofHeight),
            Staff('f', "Fire Stairwell", 0, RoofHeight),

            Public('b', "Balcony", 1, U1, GreyMarble, sconces: true),
            Public('n', "Modern Art Gallery", 1, U1, GreyOak),
            Public('l', "Lecture Hall", 1, U1, GreyOak),
            Public('a', "Reading Room", 1, U1, GreyOak),
            Public('c', "Classroom", 1, U1, GreyOak),
            Public('i', "Director's Office", 1, 3.4f, GreyOak, light: 0.8f),
            Staff('j', "Meeting Room", 1, 3.2f),
            Staff('q', "Upper Staff Corridor", 1, 3.2f, sconces: true),
        };

        all[0].Light = 0.8f;

        foreach (Region r in all)
        {
            if (r.Id == 'w' || r.Id == 'f')
            {
                r.WallMat = ConcreteGrey;
            }
        }

        Dictionary<char, Region> map = new Dictionary<char, Region>();

        foreach (Region r in all)
        {
            map[r.Id] = r;
        }

        return map;
    }


    private static string BgpMat(string name) =>
        BgpDir + "Materials/" + name + ".mat";

    private static string AkMat(string name) =>
        AkDir + "Materials/" + name + ".mat";


    /// <summary>World X of a (fractional) grid column - cell centres are whole numbers.</summary>
    private static float WX(float gx) => (gx - OriginX) * Cell;

    /// <summary>World Z of a (fractional) grid row.</summary>
    private static float WZ(float gz) => (OriginZ - gz) * Cell;


    // =========================================================
    // BUILD STATE
    // =========================================================

    private static readonly List<string> report = new List<string>();

    private static readonly Dictionary<string, Mesh> meshCache =
        new Dictionary<string, Mesh>();

    private static readonly Dictionary<string, Material> matCache =
        new Dictionary<string, Material>();

    private static Dictionary<string, string> prefabPaths;

    private static Transform floors, walls, ceilings, doors, props,
        markers, hiding, objectivesGroup, signs;

    /// <summary>The floor every grid lookup and placement is on right now.</summary>
    private static int curFloor;

    /// <summary>Base height of the current floor - added by every placement helper.</summary>
    private static float Y0 => curFloor == 0 ? 0f : FloorHeight;

    private static void SetFloor(int floor) => curFloor = floor;

    /// <summary>The Antiquities case the Egyptian key is locked in.</summary>
    private static GameObject keyCase;

    /// <summary>
    /// The desks chosen as hiding places while dressing: the desk, the
    /// way the hidden player faces (towards the room's main approach),
    /// and a name.
    /// </summary>
    private static readonly List<(GameObject desk, Vector3 facing, string name)> deskHides =
        new List<(GameObject, Vector3, string)>();

    /// <summary>
    /// Accent lights asked for while dressing, made under the lighting
    /// root once it exists. Positions are absolute.
    /// </summary>
    private struct Accent
    {
        public string Name;
        public Vector3 Position, Target;
        public bool Spot;
        public Color Color;
        public float Intensity, Range, Angle;
    }

    private static readonly List<Accent> accents = new List<Accent>();

    /* Light palette: cold night, white sconces, amber only on what matters. */

    private static readonly Color MoonFill = new Color(0.80f, 0.84f, 0.92f);
    private static readonly Color SconceWhite = new Color(1.0f, 0.92f, 0.80f);
    private static readonly Color SpotWhite = new Color(0.95f, 0.96f, 1.0f);
    private static readonly Color CaseCool = new Color(0.86f, 0.93f, 1.0f);
    private static readonly Color StripCold = new Color(0.82f, 0.90f, 1.0f);
    private static readonly Color InterestAmber = new Color(1.0f, 0.62f, 0.22f);
    private static readonly Color AlarmRed = new Color(1.0f, 0.18f, 0.12f);


    // =========================================================
    // ENTRY POINTS
    // =========================================================

    [MenuItem("Tools/AI Director/Museum - Build Map", priority = -90)]
    public static void BuildFromMenu()
    {
        bool go = EditorUtility.DisplayDialog(
            "Build Museum",
            "Builds (or rebuilds) the museum into " + ScenePath + ".\n\n" +
            "Only the map and its lighting are replaced. If the scene " +
            "has no player yet, the rig is moved in from Prototype Map " +
            "(that scene is not saved, so it stays as it is on disk).",
            "Build it",
            "Cancel"
        );

        if (!go)
            return;

        string result = Build(true);

        EditorUtility.DisplayDialog("Build Museum", result, "OK");
    }


    /// <summary>
    /// Callable from the Unity CLI: returns the report instead of
    /// showing a dialog.
    /// </summary>
    public static string Build(bool importRig)
    {
        report.Clear();
        accents.Clear();
        meshCache.Clear();
        matCache.Clear();
        prefabPaths = null;
        keyCase = null;
        deskHides.Clear();
        SetFloor(0);

        Scene scene = AcquireScene();

        SceneManager.SetActiveScene(scene);

        ClearOldBuild(scene);

        PrepareMeshAsset();

        PrepareSurfaceMaterials();

        GameObject map = new GameObject(MapRootName);

        SceneManager.MoveGameObjectToScene(map, scene);

        floors = Group(map, "Floors", walkable: true);
        walls = Group(map, "Walls", walkable: false);
        ceilings = Group(map, "Ceilings", walkable: false);
        doors = Group(map, "Doors", walkable: false);
        props = Group(map, "Props", walkable: false);
        markers = Group(map, "Markers", walkable: false);
        hiding = Group(map, "Hiding Spots", walkable: false);
        objectivesGroup = Group(map, "Objectives", walkable: false);
        signs = Group(map, "Signs", walkable: false);

        for (int f = 0; f < 2; f++)
        {
            SetFloor(f);

            BuildFloorsAndCeilings();
            BuildWalls();
            BuildPillars();
            BuildDoors();
        }

        BuildStairs();
        BuildBalustrades();

        DressRooms();
        BuildSigns();
        BuildAreas();
        BuildMarkers();
        BuildHidingSpots();
        BuildObjectives();

        SetFloor(0);

        StaticFlags(map);

        BuildLighting(scene);

        SaveMeshAsset();

        if (importRig)
        {
            ImportRig(scene);
        }

        BuildGameRoot(scene);

        PreparePlayer(scene);

        EnablePostOnCameras(scene);

        AddToBuildSettings();

        EditorSceneManager.SaveScene(scene, ScenePath);

        BakeNavMesh(scene);

        CheckConnectivity();

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene, ScenePath);

        AssetDatabase.SaveAssets();

        StringBuilder text = new StringBuilder();

        foreach (string line in report)
        {
            text.AppendLine(line);
        }

        Debug.Log("[Museum]\n" + text);

        return text.ToString();
    }


    // =========================================================
    // SCENE
    // =========================================================

    private static Scene AcquireScene()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);

        if (scene.IsValid() && scene.isLoaded)
            return scene;

        if (File.Exists(ScenePath))
        {
            report.Add("Opened " + ScenePath);

            return EditorSceneManager.OpenScene(
                ScenePath, OpenSceneMode.Additive);
        }

        scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        EditorSceneManager.SaveScene(scene, ScenePath);

        report.Add("Created " + ScenePath);

        return scene;
    }


    private static void ClearOldBuild(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == MapRootName || root.name == LightingRootName ||
                root.name == GameRootName)
            {
                Object.DestroyImmediate(root);
            }
        }
    }


    private static Transform Group(
        GameObject map,
        string name,
        bool walkable)
    {
        GameObject group = new GameObject(name);

        group.transform.SetParent(map.transform, false);

        /*
         * Walls, props and ceilings still block the NavMesh, but their
         * tops are never walkable - otherwise the bake leaves little
         * islands on top of display cases and archways, and the zone
         * graph built from the NavMesh would send the creature to
         * search the roof.
         */

        if (!walkable)
        {
            NavMeshModifier modifier =
                group.AddComponent<NavMeshModifier>();

            modifier.overrideArea = true;

            modifier.area = 1;   /* Not Walkable */

            modifier.applyToChildren = true;
        }

        return group.transform;
    }


    private static void StaticFlags(GameObject map)
    {
        foreach (Transform t in map.GetComponentsInChildren<Transform>(true))
        {
            /*
             * Anything that moves must stay dynamic: static batching
             * bakes the mesh in place, and a door leaf or clock hand
             * would then animate its transform while the drawn mesh
             * stayed put.
             */

            if (t.GetComponentInParent<HidingSpot>() != null ||
                t.GetComponentInParent<Interactable>() != null ||
                t.GetComponentInParent<MuseumSign>() != null)
                continue;

            GameObjectUtility.SetStaticEditorFlags(
                t.gameObject,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic
            );
        }
    }


    // =========================================================
    // GRID HELPERS
    // =========================================================

    private static string[] GridOf(int floor) => floor == 0 ? Ground : Upper;

    private static int Width => Ground[0].Length;

    private static int Depth => Ground.Length;


    private static char AtFloor(int floor, int x, int z)
    {
        string[] grid = GridOf(floor);

        if (x < 0 || z < 0 || z >= grid.Length || x >= grid[z].Length)
            return '.';

        return grid[z][x];
    }


    private static char At(int x, int z) => AtFloor(curFloor, x, z);


    private static Region RegionOf(char c)
    {
        Regions.TryGetValue(c, out Region region);

        return region;
    }


    private static Region RegionAt(int x, int z) => RegionOf(At(x, z));


    /// <summary>Is there an upper-floor room (not a void) over this cell?</summary>
    private static bool HasUpper(int x, int z)
    {
        return RegionOf(AtFloor(1, x, z)) != null;
    }


    /// <summary>World centre of a cell, on the current floor.</summary>
    private static Vector3 CellCenter(int x, int z)
    {
        return new Vector3(WX(x), Y0, WZ(z));
    }


    private static void Neighbour(int x, int z, Dir d, out int nx, out int nz)
    {
        nx = x;
        nz = z;

        switch (d)
        {
            case Dir.N: nz = z - 1; break;
            case Dir.S: nz = z + 1; break;
            case Dir.E: nx = x + 1; break;
            case Dir.W: nx = x - 1; break;
        }
    }


    /// <summary>World-space outward direction of a cell side.</summary>
    private static Vector3 Outward(Dir d)
    {
        switch (d)
        {
            case Dir.N: return Vector3.forward;
            case Dir.S: return Vector3.back;
            case Dir.E: return Vector3.right;
            default: return Vector3.left;
        }
    }


    private static Dir Opposite(Dir d)
    {
        switch (d)
        {
            case Dir.N: return Dir.S;
            case Dir.S: return Dir.N;
            case Dir.E: return Dir.W;
            default: return Dir.E;
        }
    }


    private static bool FindDoor(int x, int z, Dir side, out Door found)
    {
        Neighbour(x, z, side, out int nx, out int nz);

        Dir back = Opposite(side);

        foreach (Door door in Doors)
        {
            if (door.Floor != curFloor)
                continue;

            bool mine = door.X == x && door.Z == z && door.Side == side;

            bool theirs = door.X == nx && door.Z == nz && door.Side == back;

            if (mine || theirs)
            {
                found = door;

                return true;
            }
        }

        found = default;

        return false;
    }


    /// <summary>An upper-floor edge where a stair arrives - left open.</summary>
    private static bool IsArrival(int x, int z, Dir side)
    {
        if (curFloor != 1)
            return false;

        Neighbour(x, z, side, out int nx, out int nz);

        foreach ((int ax, int az, Dir ad) in Arrivals)
        {
            if ((ax == x && az == z && ad == side) ||
                (ax == nx && az == nz && ad == Opposite(side)))
                return true;
        }

        return false;
    }


    // =========================================================
    // FLOORS & CEILINGS
    // =========================================================

    private static void BuildFloorsAndCeilings()
    {
        int count = 0;

        for (int z = 0; z < Depth; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                Region region = RegionAt(x, z);

                if (region == null)
                    continue;

                Vector3 c = CellCenter(x, z);

                string label = (curFloor == 0 ? "G " : "U ") + x + "," + z;

                if (curFloor == 0)
                {
                    Box(floors, "Floor " + label, c + Vector3.down * 0.1f,
                        new Vector3(Cell, 0.2f, Cell), region.FloorMat, collider: true);
                }
                else
                {
                    /* The upper floor's slab - its underside is the ceiling below. */

                    Box(floors, "Floor " + label, c + Vector3.down * (FloorSlab * 0.5f),
                        new Vector3(Cell, FloorSlab, Cell), region.FloorMat, collider: true);
                }

                /*
                 * A ground room under the upper floor that reaches the
                 * slab needs no ceiling of its own; a lower room (a
                 * staff corridor) gets a dropped ceiling.
                 */

                bool underSlab = curFloor == 0 && HasUpper(x, z) &&
                                 region.Height >= FloorHeight - FloorSlab - 0.01f;

                if (!underSlab)
                {
                    Box(ceilings, "Ceiling " + label,
                        c + Vector3.up * (region.Height + 0.125f),
                        new Vector3(Cell, 0.25f, Cell),
                        region.CeilingMat, collider: false);
                }

                if (region.Laylight)
                {
                    Box(ceilings, "Laylight " + label,
                        c + Vector3.up * (region.Height - 0.03f),
                        new Vector3(Cell * 0.8f, 0.06f, Cell * 0.8f),
                        LaylightGlass, collider: false);

                    float inner = Cell * 0.8f, bar = 0.1f;
                    float off = (inner + bar) * 0.5f;
                    float y = region.Height - 0.07f;

                    Box(ceilings, "Laylight Frame", c + new Vector3(0f, y, off),
                        new Vector3(inner + bar * 2f, 0.08f, bar), Silver, collider: false);
                    Box(ceilings, "Laylight Frame", c + new Vector3(0f, y, -off),
                        new Vector3(inner + bar * 2f, 0.08f, bar), Silver, collider: false);
                    Box(ceilings, "Laylight Frame", c + new Vector3(off, y, 0f),
                        new Vector3(bar, 0.08f, inner), Silver, collider: false);
                    Box(ceilings, "Laylight Frame", c + new Vector3(-off, y, 0f),
                        new Vector3(bar, 0.08f, inner), Silver, collider: false);
                }

                count++;
            }
        }

        report.Add((curFloor == 0 ? "Ground" : "Upper") + ": " + count + " cells floored");
    }


    // =========================================================
    // WALLS
    // =========================================================

    private static void BuildWalls()
    {
        int slabs = 0;

        for (int z = 0; z < Depth; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                Region region = RegionAt(x, z);

                if (region == null)
                    continue;

                for (Dir d = Dir.N; d <= Dir.W; d++)
                {
                    Neighbour(x, z, d, out int nx, out int nz);

                    char other = At(nx, nz);

                    if (other == region.Id)
                        continue;

                    /* Upstairs, onto a void: a stair arrival stays open, the balcony gets a balustrade. */

                    if (curFloor == 1 && other == Void)
                    {
                        if (IsArrival(x, z, d) || region.Id == 'b')
                            continue;
                    }

                    float h = region.Height;

                    /*
                     * A double-height room stops at the upper floor
                     * wherever it overhangs the next cell - the room up
                     * there builds the rest, or the balustrade does.
                     */

                    if (curFloor == 0 && h > FloorHeight && HasUpper(nx, nz))
                    {
                        /* Stop under the slab, so no wall top crosses a stair's arrival. */

                        h = FloorHeight - FloorSlab;
                    }

                    float openW = 0f, openH = 0f;

                    if (FindDoor(x, z, d, out Door door))
                    {
                        switch (door.Kind)
                        {
                            case DoorKind.Doorway:
                            case DoorKind.Gate:
                                openW = DoorwayWidth;
                                openH = Mathf.Min(DoorwayHeight, h - 0.3f);
                                break;

                            case DoorKind.Exit:
                                openW = ExitDoorWidth;
                                openH = ExitDoorHeight;
                                break;

                            default:
                                openW = StaffDoorWidth;
                                openH = StaffDoorHeight;
                                break;
                        }
                    }

                    slabs += WallSlab(x, z, d, region, openW, openH, h);
                }
            }
        }

        report.Add((curFloor == 0 ? "Ground" : "Upper") + " walls: " + slabs + " slabs");
    }


    /// <summary>
    /// A plain wall half, with an optional door-shaped hole in it.
    /// Returns how many boxes it took.
    /// </summary>
    private static int WallSlab(
        int x, int z, Dir d, Region region,
        float openW, float openH, float h)
    {
        Vector3 outward = Outward(d);

        Vector3 along =
            (d == Dir.N || d == Dir.S) ? Vector3.right : Vector3.forward;

        Vector3 edge =
            CellCenter(x, z) +
            outward * (Cell * 0.5f - Slab * 0.5f);

        string label = "Wall " + curFloor + ":" + x + "," + z + " " + d;

        float half = Cell * 0.5f;

        /* The room-side face of the wall, where trims sit. */

        Vector3 face = CellCenter(x, z) + outward * (Cell * 0.5f - Slab);

        if (openW <= 0f)
        {
            SlabBox(label, edge, along, -half, half, 0f, h, region);

            Trims(label, face, outward, along, -half, half, region, h);

            return 1;
        }

        float o = openW * 0.5f;

        SlabBox(label + " L", edge, along, -half, -o, 0f, h, region);
        SlabBox(label + " R", edge, along, o, half, 0f, h, region);
        SlabBox(label + " Top", edge, along, -o, o, openH, h, region);

        Trims(label + " L", face, outward, along, -half, -o, region, h);
        Trims(label + " R", face, outward, along, o, half, region, h);

        if (region.Cornice != null && h >= region.Height - 0.01f)
        {
            TrimBox(label + " Cornice", face, outward, along, -o, o,
                h - 0.25f, h - 0.02f, 0.1f, region.Cornice);
        }

        return 3;
    }


    /// <summary>
    /// Wainscot (or skirting) along the bottom of a stretch of wall and
    /// a cornice under the ceiling.
    /// </summary>
    private static void Trims(
        string label, Vector3 face, Vector3 outward, Vector3 along,
        float a0, float a1, Region region, float h)
    {
        if (region.Skirting != null)
        {
            TrimBox(label + " Wainscot", face, outward, along, a0, a1,
                0f, region.SkirtingHeight, 0.04f, region.Skirting);

            if (region.SkirtingHeight > 0.5f)
            {
                /* A dado rail caps the panelling. */

                TrimBox(label + " Dado", face, outward, along, a0, a1,
                    region.SkirtingHeight, region.SkirtingHeight + 0.06f, 0.07f, Silver);
            }
        }

        if (region.Cornice != null && h >= region.Height - 0.01f)
        {
            TrimBox(label + " Cornice", face, outward, along, a0, a1,
                h - 0.25f, h - 0.02f, 0.1f, region.Cornice);
        }
    }


    private static void TrimBox(
        string label, Vector3 face, Vector3 outward, Vector3 along,
        float a0, float a1, float y0, float y1, float depth, string mat)
    {
        float length = a1 - a0;

        if (length < 0.05f)
            return;

        Vector3 center =
            face - outward * (depth * 0.5f) +
            along * ((a0 + a1) * 0.5f) +
            Vector3.up * ((y0 + y1) * 0.5f);

        Vector3 size = along == Vector3.right
            ? new Vector3(length, y1 - y0, depth)
            : new Vector3(depth, y1 - y0, length);

        Box(walls, label, center, size, mat, collider: false);
    }


    /// <summary>
    /// One box of a wall, from <paramref name="a0"/> to
    /// <paramref name="a1"/> along the wall and from <paramref name="y0"/>
    /// to <paramref name="y1"/> up it (relative to the floor).
    /// </summary>
    private static void SlabBox(
        string label,
        Vector3 edge,
        Vector3 along,
        float a0, float a1,
        float y0, float y1,
        Region region)
    {
        if (a1 - a0 < 0.01f || y1 - y0 < 0.01f)
            return;

        float length = a1 - a0;

        Vector3 center =
            edge +
            along * ((a0 + a1) * 0.5f) +
            Vector3.up * ((y0 + y1) * 0.5f);

        Vector3 size = along == Vector3.right
            ? new Vector3(length, y1 - y0, Slab)
            : new Vector3(Slab, y1 - y0, length);

        Box(walls, label, center, size, region.WallMat, collider: true);
    }


    // =========================================================
    // PILLARS
    // =========================================================

    /// <summary>
    /// A slim pilaster at every grid corner a wall touches - it hides
    /// the seams where wall halves meet. Upstairs, none stand at the
    /// edge of a void: the balustrade's own posts do that job.
    /// </summary>
    private static void BuildPillars()
    {
        int count = 0;

        for (int vz = 0; vz <= Depth; vz++)
        {
            for (int vx = 0; vx <= Width; vx++)
            {
                char nw = At(vx - 1, vz - 1);
                char ne = At(vx, vz - 1);
                char sw = At(vx - 1, vz);
                char se = At(vx, vz);

                bool touched =
                    Differs(nw, ne) || Differs(sw, se) ||
                    Differs(nw, sw) || Differs(ne, se);

                if (!touched)
                    continue;

                if (curFloor == 1 && (nw == Void || ne == Void || sw == Void || se == Void))
                    continue;

                float height = 0f;

                string mat = null;

                bool overhung = false;

                (int, int)[] cells = { (vx - 1, vz - 1), (vx, vz - 1), (vx - 1, vz), (vx, vz) };

                foreach ((int cx, int cz) in cells)
                {
                    Region r = RegionAt(cx, cz);

                    if (r == null)
                        continue;

                    if (curFloor == 0 && HasUpper(cx, cz))
                    {
                        overhung = true;
                    }

                    if (r.Height > height)
                    {
                        height = r.Height;

                        mat = r.WallMat;
                    }
                }

                if (height <= 0f)
                    continue;

                if (overhung)
                {
                    height = Mathf.Min(height, FloorHeight - FloorSlab);
                }

                Vector3 p = new Vector3(
                    (vx - OriginX - 0.5f) * Cell,
                    Y0 + height * 0.5f,
                    (OriginZ - vz + 0.5f) * Cell);

                Box(walls, "Pillar " + curFloor + ":" + vx + "," + vz, p,
                    new Vector3(PillarSize, height, PillarSize),
                    mat, collider: true);

                count++;
            }
        }

        report.Add((curFloor == 0 ? "Ground" : "Upper") + ": " + count + " pillars");
    }


    private static bool Differs(char a, char b)
    {
        if (a == b)
            return false;

        return Regions.ContainsKey(a) || Regions.ContainsKey(b);
    }


    // =========================================================
    // DOORS
    // =========================================================

    /// <summary>
    /// A dark architrave round a doorway hole, proud of the wall on
    /// both sides.
    /// </summary>
    private static void CasedDoorway(Vector3 edge, bool alongX, float height)
    {
        const float jamb = 0.16f, depth = Slab * 2f + 0.08f;

        Vector3 along = alongX ? Vector3.right : Vector3.forward;

        Vector3 Size(float a, float h) =>
            alongX ? new Vector3(a, h, depth) : new Vector3(depth, h, a);

        float half = DoorwayWidth * 0.5f + jamb * 0.5f;

        Box(doors, "Doorway Jamb", edge - along * half + Vector3.up * (height * 0.5f),
            Size(jamb, height), Wainscot, collider: true);
        Box(doors, "Doorway Jamb", edge + along * half + Vector3.up * (height * 0.5f),
            Size(jamb, height), Wainscot, collider: true);
        Box(doors, "Doorway Head", edge + Vector3.up * (height + 0.12f),
            Size(DoorwayWidth + jamb * 2f + 0.08f, 0.24f), Wainscot, collider: true);
    }


    private static void BuildDoors()
    {
        int doorways = 0, staff = 0, bad = 0;

        foreach (Door door in Doors)
        {
            if (door.Floor != curFloor)
                continue;

            Neighbour(door.X, door.Z, door.Side, out int nx, out int nz);

            char a = At(door.X, door.Z), b = At(nx, nz);

            if (door.Kind != DoorKind.Exit &&
                (a == b || RegionOf(a) == null || RegionOf(b) == null))
            {
                report.Add("BAD DOOR " + curFloor + ":" + door.X + "," + door.Z + " " +
                           door.Side + " (" + a + " | " + b + ")");

                bad++;

                continue;
            }

            Vector3 outward = Outward(door.Side);

            Vector3 edge = CellCenter(door.X, door.Z) + outward * (Cell * 0.5f);

            bool alongX = door.Side == Dir.N || door.Side == Dir.S;

            switch (door.Kind)
            {
                case DoorKind.Doorway:
                case DoorKind.Gate:
                    CasedDoorway(edge, alongX, DoorwayHeight);
                    doorways++;
                    break;

                case DoorKind.Exit:
                {
                    GameObject exit = Fit("porte", doors, edge,
                        new Vector3(ExitDoorWidth, ExitDoorHeight, -1f),
                        alongX ? 0f : 90f, Anchor.Bottom);

                    exit.name = "Exit Doors";

                    break;
                }

                default:
                {
                    /* The frame's width runs along its local X; frame only, leaves hidden. */

                    GameObject frame = Fit("PortePrivee", doors, edge,
                        new Vector3(StaffDoorWidth + 0.2f, StaffDoorHeight + 0.05f, -1f),
                        alongX ? 0f : 90f, Anchor.Bottom);

                    foreach (Transform child in frame.GetComponentsInChildren<Transform>(true))
                    {
                        if (child != frame.transform)
                        {
                            child.gameObject.SetActive(false);
                        }
                    }

                    if (door.Kind == DoorKind.Vault) frame.name = "Vault Door";
                    else if (door.Kind == DoorKind.Bolted) frame.name = "Bolted Door";

                    staff++;

                    break;
                }
            }
        }

        report.Add((curFloor == 0 ? "Ground" : "Upper") + ": " + doorways + " doorways, " +
                   staff + " staff doors" + (bad > 0 ? ", " + bad + " BAD" : ""));
    }


    // =========================================================
    // STAIRS & BALUSTRADES
    // =========================================================

    /// <summary>
    /// The pack's imperial staircase fills the north end of the stair
    /// hall: a central flight up to a half landing, then two flights
    /// turning back to the balcony on either side. It is scaled to
    /// 0.8 of its height so it arrives at the upper floor exactly -
    /// risers of 0.21 m. The service and fire stairs are five of the
    /// pack's wooden stair modules each.
    /// </summary>
    private static void BuildStairs()
    {
        SetFloor(0);

        GameObject prefab = LoadPrefab("Museum_Big_Stair_02");

        if (prefab == null)
        {
            report.Add("MISSING Museum_Big_Stair_02 - no grand stair");
        }
        else
        {
            GameObject stair = (GameObject)PrefabUtility.InstantiatePrefab(prefab, floors.gameObject.scene);

            stair.name = "Grand Stair";

            stair.transform.SetParent(floors, false);

            stair.transform.localScale = Vector3.Scale(stair.transform.localScale, new Vector3(1f, 0.8f, 1f));

            /*
             * The pack's stair stone is a dark brown that the night
             * grade turns black; give it pale grey copies of its own
             * materials, keeping the texture detail.
             */

            foreach (Renderer r in stair.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;

                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null)
                    {
                        mats[i] = PaleCopy(mats[i]);
                    }
                }

                r.sharedMaterials = mats;
            }

            /* The central flight runs along the asset's +X: turn it to climb north. */

            stair.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -90f, 0f));

            Bounds b = RenderBounds(stair);

            float north = WZ(5.5f) - Slab - 0.02f;

            stair.transform.position = new Vector3(WX(14.5f) - b.center.x, 0f, north - b.max.z);

            b = RenderBounds(stair);

            /*
             * The side flights stop a hand's width short of the hall
             * walls; bridge the gap onto the balcony so nothing trips.
             */

            float bandZ0 = b.max.z - 5.9f, bandZ1 = b.max.z - 0.7f;

            float westEdge = WX(11.5f), eastEdge = WX(17.5f);

            Box(floors, "Stair Bridge W",
                new Vector3((b.min.x + westEdge) * 0.5f - 0.15f, FloorHeight - 0.03f, (bandZ0 + bandZ1) * 0.5f),
                new Vector3(Mathf.Abs(b.min.x - westEdge) + 0.9f, 0.1f, bandZ1 - bandZ0),
                GreyMarble, collider: true);

            Box(floors, "Stair Bridge E",
                new Vector3((b.max.x + eastEdge) * 0.5f + 0.15f, FloorHeight - 0.03f, (bandZ0 + bandZ1) * 0.5f),
                new Vector3(Mathf.Abs(b.max.x - eastEdge) + 0.9f, 0.1f, bandZ1 - bandZ0),
                GreyMarble, collider: true);

            report.Add("Grand stair: " + b.size.ToString("F1") + " m, bottom step at z " + b.min.z.ToString("F1"));
        }

        WoodStair("Service Stair", WX(5), WZ(11.5f) - Slab, null);

        WoodStair("Fire Stair", WX(23), WZ(0.5f) - Slab, ConcreteGrey);
    }


    /// <summary>
    /// A lightened grey copy of a pack material, saved beside the other
    /// generated materials and refreshed on every build.
    /// </summary>
    private static Material PaleCopy(Material source)
    {
        string path = MaterialDir + "/Pale " + source.name + ".mat";

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (mat == null)
        {
            mat = new Material(source);

            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.CopyPropertiesFromMaterial(source);
        }

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", new Color(1.9f, 1.9f, 1.95f));
        }

        EditorUtility.SetDirty(mat);

        return mat;
    }


    /// <summary>
    /// Five stair modules climbing north to <paramref name="topZ"/>,
    /// centred on <paramref name="xc"/>, arriving at the upper floor.
    /// </summary>
    private static void WoodStair(string name, float xc, float topZ, string material)
    {
        GameObject prefab = LoadPrefab("Stair_wood");

        if (prefab == null)
        {
            report.Add("MISSING Stair_wood - no " + name);

            return;
        }

        GameObject root = new GameObject(name);

        root.transform.SetParent(floors, false);

        const int modules = 5;

        float rise = FloorHeight / modules;

        Material mat = material != null ? LoadMaterial(material) : null;

        for (int i = 0; i < modules; i++)
        {
            GameObject m = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);

            m.transform.SetParent(root.transform, false);

            m.transform.localScale = Vector3.Scale(m.transform.localScale, new Vector3(1f, rise, 1f));

            /* A module climbs towards its own -X over 2 m; turned 90 it climbs north. */

            m.transform.SetPositionAndRotation(
                new Vector3(xc + 1f, i * rise, topZ - 2f * (modules - i)),
                Quaternion.Euler(0f, 90f, 0f));

            if (mat != null)
            {
                foreach (Renderer r in m.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterial = mat;
                }
            }
        }

        /* A short landing plate onto the upper floor. */

        Box(floors, name + " Top Plate", new Vector3(xc, FloorHeight - 0.03f, topZ + 0.15f),
            new Vector3(2.2f, 0.1f, 0.9f), material ?? GreyOak, collider: true);
    }


    /// <summary>The pack's stone balustrade along every balcony edge onto a void.</summary>
    private static void BuildBalustrades()
    {
        SetFloor(1);

        int count = 0;

        for (int z = 0; z < Depth; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (At(x, z) != 'b')
                    continue;

                for (Dir d = Dir.N; d <= Dir.W; d++)
                {
                    Neighbour(x, z, d, out int nx, out int nz);

                    if (At(nx, nz) != Void || IsArrival(x, z, d))
                        continue;

                    Vector3 outward = Outward(d);

                    Vector3 p = CellCenter(x, z) + outward * (Cell * 0.5f - 0.22f);

                    bool alongX = d == Dir.N || d == Dir.S;

                    Fit("Museum_Ark_Fence", props, p,
                        new Vector3(-1f, 1.1f, Cell + 0.02f),
                        alongX ? 90f : 0f, Anchor.Bottom);

                    count++;
                }
            }
        }

        SetFloor(0);

        report.Add(count + " balustrade bays");
    }


    // =========================================================
    // DRESSING
    // =========================================================

    /*
     * Positions are in grid units (gx, gz): whole numbers are cell
     * centres, .5 is a cell boundary. A room spanning cells x0..x1 has
     * its west wall face at x0 - 0.45 and its east face at x1 + 0.45
     * (likewise north z0 - 0.45, south z1 + 0.45). Heights are relative
     * to the current floor.
     */

    private static GameObject P(string name, float gx, float gz, float yaw,
        float scale = 1f, float y = 0f, float scaleY = 1f) =>
        Prop(name, WX(gx), WZ(gz), yaw, scale, y, scaleY);

    private static void St(string name, float gx, float gz, float yaw,
        float plinth = 1.1f, float scale = 1.7f) =>
        Statue(name, WX(gx), WZ(gz), yaw, plinth, scale);

    private static void Pic(float gx, float gz, float yaw, float y = 2.2f) =>
        Painting(WX(gx), WZ(gz), yaw, y);

    private static void Plin(float gx, float gz, Vector3 size, string mat) =>
        Plinth(WX(gx), WZ(gz), size, mat);

    private static GameObject BoxAt(string name, float gx, float gz, float y, Vector3 size,
        string mat, bool collider = true) =>
        Box(props, name, new Vector3(WX(gx), Y0 + y, WZ(gz)), size, mat, collider);

    private static void Spot(string name, float fx, float fy, float fz,
        float tx, float ty, float tz, float intensity = 4f, float range = 8f, float angle = 36f) =>
        SpotOn(name, new Vector3(WX(fx), fy, WZ(fz)), new Vector3(WX(tx), ty, WZ(tz)),
            intensity, range, angle);

    private static void Case(float gx, float y, float gz, float intensity = 1.4f, float range = 2.6f) =>
        CaseLight(WX(gx), y, WZ(gz), intensity, range);


    private static void DressRooms()
    {
        int before = props.childCount;

        SetFloor(0);

        DressHub();
        DressWest();
        DressEast();
        DressLockedWing();
        DressVisitorRooms();
        DressStaffWing();

        SetFloor(1);

        DressUpper();

        SetFloor(0);

        report.Add((props.childCount - before) + " props placed");
    }


    private static void DressHub()
    {
        /* Stair hall x12..17 z6..11 - the stair fills z6..9. */

        P("Lion_Droite", 13.2f, 10.1f, 180f);
        Lion(WX(15.8f), WZ(10.1f), 180f);

        P("EasterIslandStatue", 12.45f, 11.05f, 45f, scale: 1.2f);
        St("Gir Statue", 16.55f, 11.05f, -45f);

        Spot("Stair Light W", 12.6f, 9.4f, 8.9f, 13.2f, 2.5f, 7.6f, 7f, 14f, 50f);
        Spot("Stair Light E", 16.4f, 9.4f, 8.9f, 15.8f, 2.5f, 7.6f, 7f, 14f, 50f);
        Spot("Stair Front W", 13.0f, 8.4f, 11.6f, 14.2f, 2.0f, 8.4f, 11f, 20f, 50f);
        Spot("Stair Front E", 16.0f, 8.4f, 11.6f, 14.8f, 2.0f, 8.4f, 11f, 20f, 50f);
        Spot("Lion W", 13.2f, 5.0f, 11.3f, 13.2f, 1.2f, 10.1f, 3.5f, 7f, 30f);
        Spot("Lion E", 15.8f, 5.0f, 11.3f, 15.8f, 1.2f, 10.1f, 3.5f, 7f, 30f);

        /* Main exhibition hall x11..18 z1..4. */

        Plin(14.5f, 2.5f, new Vector3(14.5f, 0.4f, 4.0f), GreyMarble);
        P("diplodocus", 14.5f, 2.5f, 90f, y: 0.4f);

        foreach (float x in new[] { 12.5f, 14.5f, 16.5f })
        {
            P("Corde_01", x, 3.45f, 90f);
        }

        P("banc", 17.6f, 1.5f, 0f);
        P("banc", 11.5f, 3.6f, 0f);

        Spot("Diplodocus W", 12.0f, 4.8f, 4.0f, 13.6f, 1.8f, 2.5f, 6f, 12f, 40f);
        Spot("Diplodocus E", 17.0f, 4.8f, 4.0f, 15.4f, 1.8f, 2.5f, 6f, 12f, 40f);

        /* Lobby x12..17 z13..17. */

        P("Mesh_Table_carre", 12.8f, 14.0f, 90f);
        P("Mesh_Table_carre", 16.2f, 14.0f, 90f);
        P("Corde_01", 14.0f, 16.3f, 0f);
        P("Corde_01", 15.0f, 16.3f, 0f);
        P("banc", 12.6f, 16.5f, 0f);
        P("banc", 16.4f, 16.5f, 0f);

        /* Corridors. */

        P("banc", 14.5f, 5.25f, 90f);
        P("banc", 14.5f, 12.75f, 90f);
        St("Buste_01", 11.0f, 5.25f, 180f, 1.1f, 1f);
        St("Buste_02", 18.0f, 5.25f, 180f, 1.1f, 1f);
    }


    private static void DressWest()
    {
        /* Natural history x7..10 z1..4. */

        P("VitrineMuseum_Basse", 8.3f, 2.6f, 90f);
        P("VitrineMuseum_Basse", 9.7f, 2.6f, 90f);
        Case(8.3f, 1.6f, 2.6f);
        Case(9.7f, 1.6f, 2.6f);

        St("Dodo", 8.0f, 0.95f, 180f, 1.0f, 1f);
        St("Dodo", 9.5f, 0.95f, 180f, 1.0f, 1f);

        P("Paroi_Grotte", 6.62f, 3.5f, 0f, scale: 1.2f);
        DeskHide(P("Mesh_Table_carre", 9.6f, 4.0f, 0f), Vector3.forward, "Stone-Age Table");
        P("StoneAge_Axe", 9.4f, 4.0f, 0f, y: 0.72f);
        P("StoneAge_Rock", 9.8f, 4.0f, 0f, y: 0.72f);

        /* Sculpture x7..10 z6..9 - the west clock stands on its west wall. */

        St("Attalus Statue", 9.7f, 6.8f, -135f);
        St("Beethoven Statue", 8.4f, 9.3f, 0f);
        St("Monkey Statue", 9.8f, 9.2f, -45f);
        P("banc", 8.6f, 7.9f, 90f);
    }


    private static void DressEast()
    {
        /* Antiquities x19..22 z1..4 - the Egyptian collection. */

        keyCase = P("Display_A2", 19.6f, 0.75f, 180f);

        P("EgyptiansColumn_2", 20.2f, 2.6f, 0f);
        P("EgyptiansColumn_2", 21.8f, 2.6f, 0f);
        P("Sarcophage", 22.75f, 1.6f, -90f);
        P("Sarcophage", 22.75f, 3.6f, -90f);
        P("Momie_Decor", 22.8f, 2.6f, -90f);
        P("VitrineMuseum_Basse_compo_1", 21.0f, 3.9f, 0f);
        Case(21.0f, 1.6f, 3.9f);

        St("Anubis Statue", 19.25f, 3.9f, 90f);
        St("Horus Statue", 19.25f, 1.9f, 90f);

        Spot("Sarcophagi", 21.5f, 4.8f, 2.6f, 22.7f, 1.4f, 2.6f, 5f, 7f, 45f);

        /* Discovery room x19..22 z6..9 - hands-on tables. */

        P("Mesh_Table_carre", 21.3f, 7.0f, 0f);
        DeskHide(P("Mesh_Table_carre", 20.3f, 9.0f, 0f), Vector3.forward, "Discovery Table");
        P("StoneAge_Axe", 21.1f, 7.0f, 0f, y: 0.72f);
        P("StoneAge_Rock", 21.5f, 7.0f, 0f, y: 0.72f);
        P("Ankh_01", 20.3f, 9.0f, 0f, y: 0.72f);
        P("Display_B2", 22.75f, 6.8f, -90f);
        P("Display_B1", 19.3f, 6.7f, 90f);
        P("EasterIslandStatue", 22.55f, 9.3f, -135f, scale: 0.9f);
    }


    private static void DressLockedWing()
    {
        /* Temporary exhibition x24..27 z0..4 - "Clockwork & Automata". */

        P("VitrineMuseum_compo_1", 25.8f, 0.8f, 180f);
        P("VitrineMuseum_2", 25.6f, 2.6f, 0f);
        Case(25.8f, 2.9f, 0.8f, 2.0f, 3.2f);
        Case(25.6f, 2.9f, 2.6f, 2.0f, 3.2f);

        /* Special collection x24..27 z5..9 - tall cases of precious things. */

        foreach ((float x, float z) in new[] { (24.6f, 6.0f), (26.4f, 6.0f), (24.6f, 8.6f), (26.0f, 8.0f) })
        {
            P("VitrineMuseum_compo_5", x, z, 0f);
            Case(x, 2.9f, z, 2.2f, 3f);
        }

        /* The gallery attendant's desk, facing the clock and the way in. */

        DeskHide(P("Mesh_Table_carre", 24.4f, 3.7f, 0f), Vector3.forward, "Attendant's Desk");

        /* Strongroom x27 z8..9. */

        P("Caisse_Wood", 27.2f, 9.3f, 0f);
    }


    private static void DressVisitorRooms()
    {
        /* Ticket office x7..8, security x9..11 (z13..16). */

        P("Mesh_Table_carre", 8.0f, 14.6f, 90f);
        P("carton", 7.3f, 16.3f, 0f);

        DeskHide(P("Mesh_Table_carre", 10.0f, 14.2f, 0f), Vector3.forward, "Security Desk");
        BoxAt("Monitors", 10.0f, 14.15f, 0.95f, new Vector3(1.2f, 0.4f, 0.1f), GenMat("Screen"));
        P("EtagereMobile", 11.2f, 16.3f, 0f, scale: 0.45f);

        /* Toilets: stalls with somewhere to crouch. */

        StallsAlongWest(6.55f, 9.55f, 11.45f, 3);
        StallsAlongNorth(9.55f, 20.55f, 22.45f, 3);

        /* Cloakroom x9..10 z10..11, first aid x19..20 z10..11. */

        P("EtagereMobile", 9.5f, 9.75f, 0f, scale: 0.5f);
        P("Mesh_Table_carre", 19.6f, 11.0f, 0f);
        P("Display_B1", 20.3f, 9.8f, 180f);

        /* Gift shop x18..22 z13..16. */

        P("EtagereMobile", 19.8f, 15.0f, 90f, scale: 0.55f, scaleY: 0.9f);
        P("EtagereMobile", 21.3f, 15.0f, 90f, scale: 0.55f, scaleY: 0.9f);
        BoxAt("Shop Counter", 19.4f, 16.2f, 0.5f, new Vector3(2.4f, 1.0f, 0.6f), Wainscot);
        P("carton", 22.2f, 13.4f, 0f);

        /* Cafe x24..27 z10..13, kitchen z14..15. */

        foreach ((float x, float z) in new[] { (25.0f, 11.0f), (26.4f, 11.0f), (25.0f, 12.6f), (26.4f, 12.6f) })
        {
            GameObject table = P("Mesh_Table_carre", x, z, 0f);

            if (x == 25.0f && z == 12.6f)
            {
                DeskHide(table, Vector3.forward, "Cafe Table");
            }
        }

        BoxAt("Cafe Counter", 27.25f, 11.8f, 0.5f, new Vector3(0.6f, 1.0f, 4.0f), Wainscot);

        BoxAt("Kitchen Counter", 25.8f, 15.25f, 0.45f, new Vector3(5.0f, 0.9f, 0.6f), VaultSteel);
        P("Chariot", 26.8f, 14.2f, 0f);
    }


    private static void DressStaffWing()
    {
        /* Maintenance x0..2 z0..2 - the caretaker's den. */

        P("EtagereMobile", 1.0f, -0.3f, 0f, scale: 0.6f);
        P("Mesh_Table_carre", 0.8f, 1.2f, 0f);
        P("Chariot", 1.8f, 2.1f, 0f);
        P("Caisse_Wood", -0.1f, 2.2f, 0f);

        /* Artifact storage x3..5 z0..2. */

        P("EtagereMobile", 4.2f, -0.28f, 0f, scale: 0.6f);
        P("Caisse_Wood", 5.2f, 2.2f, 0f);
        P("Caisse_Wood", 5.0f, 2.25f, 15f, y: 0.84f);
        P("Caisse_Wood", 3.4f, 1.0f, 0f);

        /* Loading dock x0..3 z3..6. */

        P("Caisse_Wood", 0.3f, 3.3f, 0f);
        P("Caisse_Wood", 0.6f, 3.35f, 10f);
        P("Caisse_Wood", 0.45f, 3.3f, 0f, y: 0.84f);
        P("Chariot", 2.0f, 5.0f, 90f);
        P("SAC_DE_BALLES", 3.2f, 6.2f, 0f);
        P("carton", 0.0f, 4.6f, 0f);

        /* Archive x4..5 z3..10 - long stacks to slip between. */

        foreach (float z in new[] { 6.0f, 9.6f })
        {
            P("EtagereMobile", 3.68f, z, 90f, scale: 0.72f, scaleY: 0.97f);
        }

        foreach (float z in new[] { 3.8f, 7.0f })
        {
            P("EtagereMobile", 5.32f, z, 90f, scale: 0.72f, scaleY: 0.97f);
        }

        /* Restoration lab x0..3 z7..10 - where the player works. */

        P("Mesh_Table_carre", 1.0f, 8.0f, 0f);
        DeskHide(P("Mesh_Table_carre", 2.4f, 8.0f, 0f), Vector3.back, "Lab Bench");
        Pic(-0.45f, 8.0f, 90f, 1.8f);
        Pic(-0.45f, 9.6f, 90f, 1.8f);
        P("Display_B1", 3.2f, 10.2f, 180f);
        P("Chariot", 2.6f, 9.6f, 0f);

        /* Lounge x0..2 z11..15, offices x3..4 z11..15. */

        P("banc", 0.0f, 12.5f, 0f);
        P("banc", 1.2f, 12.5f, 0f);
        P("Mesh_Table_carre", 0.6f, 13.8f, 0f);
        P("Mesh_Table_carre", 3.6f, 12.5f, 0f);
        P("Mesh_Table_carre", 3.6f, 14.3f, 0f);
        P("EtagereMobile", 4.4f, 15.3f, 0f, scale: 0.45f);

        /* Corridors. */

        P("Chariot", 6.1f, 3.0f, 90f);
        P("Caisse_Wood", 6.3f, 10.0f, 0f);
        P("Caisse_Wood", 16.0f, 0.2f, 0f);
        P("Caisse_Wood", 16.3f, 0.25f, 12f);
        P("carton", 10.0f, 0.2f, 0f);
    }


    private static void DressUpper()
    {
        /* Director's office x7..8 z1..4 - the west key is on the desk. */

        DeskHide(P("Mesh_Table_carre", 7.9f, 3.0f, 0f), Vector3.back, "Director's Desk");
        P("EtagereMobile", 7.95f, 0.68f, 0f, scale: 0.5f);
        Pic(8.45f, 3.6f, -90f, 1.9f);
        Spot("Desk Lamp", 7.9f, 2.6f, 3.4f, 7.9f, 0.8f, 3.0f, 3f, 4f, 50f);

        /* Meeting room x9..10 z1..4. */

        BoxAt("Meeting Table", 9.5f, 2.6f, 0.375f, new Vector3(1.4f, 0.75f, 6.0f), GreyOak);
        P("banc", 9.0f, 2.6f, 0f);
        P("banc", 10.05f, 2.6f, 0f);

        /* Modern art x11..18 z1..4 - white walls, a few works, room to breathe. */

        foreach (float x in new[] { 11.8f, 13.0f, 15.9f, 17.2f })
        {
            Pic(x, 0.55f, 180f, 2.2f);
        }

        foreach (float x in new[] { 13.7f, 15.3f })
        {
            Pic(x, 4.45f, 0f, 2.2f);
        }

        Pic(10.55f, 2.5f, 90f, 2.2f);
        Pic(18.45f, 3.6f, -90f, 2.2f);
        Pic(18.45f, 1.2f, -90f, 2.2f);

        St("Monkey Statue", 13.0f, 2.6f, 180f);
        St("Moai Statue", 16.0f, 2.6f, 180f);
        P("banc", 14.5f, 2.2f, 90f);

        /* Lecture hall x19..22 z1..4 - the east clock beside the screen. */

        BoxAt("Lectern", 20.2f, 1.0f, 0.55f, new Vector3(0.6f, 1.1f, 0.5f), GreyOak);
        BoxAt("Screen", 20.4f, 0.58f, 2.3f, new Vector3(3.0f, 1.7f, 0.05f), GenMat("Screen"), collider: false);

        foreach (float z in new[] { 2.3f, 3.2f })
        {
            P("banc", 20.1f, z, 90f);
            P("banc", 21.5f, z, 90f);
        }

        P("banc", 21.5f, 3.95f, 90f);

        /* Reading room x7..10 z6..11. */

        P("EtagereMobile", 6.68f, 6.7f, 90f, scale: 0.55f);
        P("EtagereMobile", 6.68f, 9.9f, 90f, scale: 0.55f);
        P("EtagereMobile", 9.6f, 5.68f, 0f, scale: 0.55f);
        P("Mesh_Table_carre", 8.3f, 8.0f, 90f);
        DeskHide(P("Mesh_Table_carre", 8.3f, 10.0f, 90f), Vector3.right, "Reading Table");
        Spot("Reading Lamp", 8.3f, 3.2f, 9.0f, 8.3f, 0.8f, 9.0f, 3f, 5f, 70f);

        /* Classroom x19..22 z6..11. */

        foreach ((float x, float z) in new[] { (20.6f, 7.6f), (21.9f, 7.6f), (20.6f, 8.8f), (21.9f, 8.8f), (20.6f, 10.0f) })
        {
            GameObject desk = P("Mesh_Table_carre", x, z, 0f);

            if (x == 20.6f && z == 10.0f)
            {
                DeskHide(desk, Vector3.forward, "Classroom Desk");
            }
        }

        BoxAt("Blackboard", 22.42f, 8.5f, 1.6f, new Vector3(0.05f, 1.3f, 3.5f), GenMat("Sign Slate"), collider: false);

        /* Balcony and staff corridor. */

        P("banc", 14.5f, 5.25f, 90f);
        P("Caisse_Wood", 16.0f, -0.2f, 0f);
    }


    /// <summary>A row of toilet stalls off a west wall, each somewhere to crouch.</summary>
    private static void StallsAlongWest(float faceGx, float gzFrom, float gzTo, int n)
    {
        float x0 = WX(faceGx);
        float z0 = WZ(gzFrom), z1 = WZ(gzTo);
        float step = (z1 - z0) / n;

        for (int i = 0; i <= n; i++)
        {
            Box(props, "Stall Partition", new Vector3(x0 + 0.75f, Y0 + 1.1f, z0 + step * i),
                new Vector3(1.5f, 2.0f, 0.05f), StallGrey, collider: true);
        }

        for (int i = 0; i < n; i++)
        {
            float zc = z0 + step * (i + 0.5f);

            Box(props, "Toilet", new Vector3(x0 + 0.3f, Y0 + 0.2f, zc),
                new Vector3(0.6f, 0.4f, 0.4f), CeilingWhite, collider: true);

        }
    }


    /// <summary>A row of toilet stalls off a north wall.</summary>
    private static void StallsAlongNorth(float faceGz, float gxFrom, float gxTo, int n)
    {
        float z0 = WZ(faceGz);
        float x0 = WX(gxFrom), x1 = WX(gxTo);
        float step = (x1 - x0) / n;

        for (int i = 0; i <= n; i++)
        {
            Box(props, "Stall Partition", new Vector3(x0 + step * i, Y0 + 1.1f, z0 - 0.75f),
                new Vector3(0.05f, 2.0f, 1.5f), StallGrey, collider: true);
        }

        for (int i = 0; i < n; i++)
        {
            float xc = x0 + step * (i + 0.5f);

            Box(props, "Toilet", new Vector3(xc, Y0 + 0.2f, z0 - 0.3f),
                new Vector3(0.4f, 0.4f, 0.6f), CeilingWhite, collider: true);

        }
    }


    // =========================================================
    // SIGNS
    // =========================================================

    /*
     * Few signs, each with a job, in the building's grey and white:
     * slate plaques with silver edges for the public rooms, plain white
     * plates above staff doors, pale caption cards, and colour only for
     * safety - green exits, a yellow hazard.
     */
    private enum SignStyle { Gallery, Caption, Brass, Staff, Warning, Exit }

    private const float SignDepth = 0.04f;

    private static readonly Color SignWhite = new Color(0.92f, 0.92f, 0.90f);
    private static readonly Color SignGrey = new Color(0.66f, 0.68f, 0.70f);
    private static readonly Color Ink = new Color(0.07f, 0.07f, 0.07f);
    private static readonly Color Graphite = new Color(0.28f, 0.28f, 0.28f);


    private static void BuildSigns()
    {
        SetFloor(0);

        /* Lobby and hall. */

        WallSign(13, 13, Dir.N, 0f, 3.9f, SignStyle.Gallery, 2.6f, 0.75f,
            "RAVENSWORTH MUSEUM", "Founded 1871");
        WallSign(14, 17, Dir.S, 0f, 3.85f, SignStyle.Exit, 1.2f, 0.4f, "EXIT");

        StandBoard(WX(13.0f), WZ(10.5f), 180f, SignStyle.Gallery, 1.3f, 1.0f, 1.5f, new[]
        {
            new MuseumSign.Line("GRAND STAIR HALL", 0.15f, SignWhite),
            new MuseumSign.Line("←  West galleries", 0.1f, SignGrey),
            new MuseumSign.Line("East galleries  →", 0.1f, SignGrey),
            new MuseumSign.Line("↑  Upper floor", 0.1f, SignGrey),
            new MuseumSign.Line("Special Collections closed tonight", 0.075f, SignGrey, FontStyle.Italic),
        });

        StandSign(WX(14.5f), WZ(3.75f), 180f, SignStyle.Caption, 0.8f, 0.3f, 1.0f,
            "Diplodocus", "Late Jurassic  ·  154 million years");

        /* One plaque by each gallery, on the corridor side. */

        WallSign(9, 5, Dir.N, 0f, 2.7f, SignStyle.Gallery, 1.8f, 0.5f, "NATURAL HISTORY", "Gallery 1");
        WallSign(9, 5, Dir.S, 0f, 2.7f, SignStyle.Gallery, 1.8f, 0.5f, "SCULPTURE", "Gallery 2");
        WallSign(14, 5, Dir.N, 0f, 3.6f, SignStyle.Gallery, 2.4f, 0.55f, "MAIN EXHIBITION HALL");
        WallSign(21, 5, Dir.N, 0f, 2.7f, SignStyle.Gallery, 1.8f, 0.5f, "ANTIQUITIES", "Gallery 3");
        WallSign(21, 5, Dir.S, 0f, 2.7f, SignStyle.Gallery, 1.8f, 0.5f, "DISCOVERY ROOM", "Gallery 4");
        WallSign(23, 6, Dir.E, 0f, 2.7f, SignStyle.Gallery, 1.9f, 0.5f, "SPECIAL COLLECTIONS", "Closed to visitors");
        WallSign(23, 12, Dir.E, 0f, 2.6f, SignStyle.Gallery, 1.2f, 0.4f, "CAFE");
        WallSign(20, 12, Dir.S, 0f, 3.5f, SignStyle.Gallery, 1.4f, 0.4f, "GIFT SHOP");
        WallSign(8, 12, Dir.N, 0f, 3.45f, SignStyle.Caption, 0.8f, 0.26f, "Toilets");
        WallSign(22, 12, Dir.N, 0f, 3.45f, SignStyle.Caption, 0.8f, 0.26f, "Toilets");
        WallSign(11, 10, Dir.W, 0f, 3.45f, SignStyle.Caption, 0.9f, 0.26f, "Cloakroom");
        WallSign(18, 10, Dir.E, 0f, 3.45f, SignStyle.Caption, 0.9f, 0.26f, "First aid");

        /* Staff doors, from the public side; the fire stair in green. */

        StaffPlate(8, 12, Dir.S, "TICKET OFFICE", "Staff only");
        StaffPlate(10, 12, Dir.S, "SECURITY", "Staff only");
        StaffPlate(7, 5, Dir.W, "STAFF ONLY", "Private");
        StaffPlate(7, 12, Dir.W, "STAFF ONLY", "Private");
        StaffPlate(7, 2, Dir.W, "STAFF ONLY", "Private");
        StaffPlate(7, 7, Dir.W, "STAFF ONLY", "Private");
        StaffPlate(13, 1, Dir.N, "STAFF ONLY", "Private");
        StaffPlate(21, 1, Dir.N, "STAFF ONLY", "Private");
        StaffPlate(23, 15, Dir.E, "KITCHEN", "Staff only");
        WallSign(23, 5, Dir.N, 0f, 2.85f, SignStyle.Exit, 1.0f, 0.3f, "FIRE EXIT  ↑");

        /* Locked doors. */

        WallSign(26, 9, Dir.E, 0f, 2.75f, SignStyle.Brass, 0.7f, 0.22f, "STRONGROOM");
        WallSign(24, 0, Dir.W, 0f, 2.75f, SignStyle.Staff, 0.7f, 0.22f, "FIRE DOOR", "Keep shut");

        /* Back of house, on the staff corridor. */

        StaffPlate(6, 1, Dir.W, "ARTIFACT STORAGE", "Keep door closed");
        StaffPlate(6, 5, Dir.W, "ARCHIVE", "Gloves must be worn");
        StaffPlate(6, 11, Dir.W, "STAFF OFFICES", "");
        StaffPlate(6, 16, Dir.W, "SERVICE STAIRS", "Upper floor");
        WallSign(0, 1, Dir.W, 0f, 1.6f, SignStyle.Staff, 0.8f, 0.42f,
            "NIGHT ROUNDS", "Every hour, every gallery  ·  lock the strongroom");

        SetFloor(1);

        WallSign(13, 5, Dir.N, 0f, 2.7f, SignStyle.Gallery, 1.8f, 0.5f, "MODERN ART", "Gallery 5");
        WallSign(21, 5, Dir.N, 0f, 2.7f, SignStyle.Gallery, 1.8f, 0.5f, "LECTURE HALL");
        WallSign(9, 5, Dir.S, 0f, 2.7f, SignStyle.Gallery, 1.8f, 0.5f, "READING ROOM");
        WallSign(21, 5, Dir.S, 0f, 2.7f, SignStyle.Gallery, 1.8f, 0.5f, "CLASSROOM");
        WallSign(7, 5, Dir.N, 0f, 2.75f, SignStyle.Brass, 0.9f, 0.24f, "DIRECTOR");
        StaffPlate(7, 5, Dir.W, "STAFF ONLY", "Private");
        StaffPlate(7, 8, Dir.W, "STAFF ONLY", "Private");
        StaffPlate(14, 1, Dir.N, "STAFF ONLY", "Private");
        StaffPlate(21, 1, Dir.N, "STAFF ONLY", "Private");

        SetFloor(0);

        report.Add(signs.childCount + " signs");
    }


    /// <summary>
    /// A sign on the inside face of a cell's wall, on the current floor.
    /// <paramref name="along"/> slides it along the wall from the cell's
    /// centre (+X on a north or south wall, +Z on an east or west one).
    /// </summary>
    private static void WallSign(
        int x, int z, Dir side, float along, float y,
        SignStyle style, float w, float h, string title, string sub = null)
    {
        Vector3 outward = Outward(side);

        Vector3 alongV =
            (side == Dir.N || side == Dir.S) ? Vector3.right : Vector3.forward;

        Vector3 c =
            CellCenter(x, z) +
            outward * (Cell * 0.5f - Slab - SignDepth * 0.5f - 0.01f) +
            alongV * along +
            Vector3.up * y;

        Sign(c, -outward, style, w, h, Lines(style, h, title, sub));
    }


    /// <summary>A plain plate above a staff door.</summary>
    private static void StaffPlate(int x, int z, Dir side, string title, string sub)
    {
        WallSign(x, z, side, 0f, 2.85f, SignStyle.Staff, 0.7f, 0.24f, title,
            string.IsNullOrEmpty(sub) ? null : sub);
    }


    private static void StandSign(
        float x, float z, float yaw, SignStyle style,
        float w, float h, float centerY, string title, string sub)
    {
        StandBoard(x, z, yaw, style, w, h, centerY, Lines(style, h, title, sub));
    }


    /// <summary>A freestanding board on a post, on the current floor.</summary>
    private static void StandBoard(
        float x, float z, float yaw, SignStyle style,
        float w, float h, float centerY, MuseumSign.Line[] lines)
    {
        Vector3 facing = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        GameObject root = Sign(new Vector3(x, Y0 + centerY, z), facing, style, w, h, lines);

        float postH = centerY - h * 0.5f;

        Box(root.transform, "Post",
            root.transform.TransformPoint(new Vector3(0f, -h * 0.5f - postH * 0.5f, -0.03f)),
            new Vector3(0.06f, postH, 0.06f), Silver, collider: false);

        Box(root.transform, "Foot",
            root.transform.TransformPoint(new Vector3(0f, -centerY + 0.015f, -0.03f)),
            new Vector3(0.4f, 0.03f, 0.3f), Silver, collider: false);
    }


    private static MuseumSign.Line[] Lines(SignStyle style, float h, string title, string sub)
    {
        Color titleColor, subColor;

        switch (style)
        {
            case SignStyle.Gallery:
                titleColor = SignWhite;
                subColor = SignGrey;
                break;

            case SignStyle.Exit:
                titleColor = Color.white;
                subColor = Color.white;
                break;

            default:
                titleColor = Ink;
                subColor = Graphite;
                break;
        }

        bool plain = style == SignStyle.Staff || style == SignStyle.Warning || style == SignStyle.Exit;

        FontStyle titleStyle = plain ? FontStyle.Bold : FontStyle.Normal;

        FontStyle subStyle = plain ? FontStyle.Normal : FontStyle.Italic;

        if (string.IsNullOrEmpty(sub))
        {
            return new[] { new MuseumSign.Line(title, h * 0.5f, titleColor, titleStyle) };
        }

        return new[]
        {
            new MuseumSign.Line(title, h * 0.4f, titleColor, titleStyle),
            new MuseumSign.Line(sub, h * 0.22f, subColor, subStyle),
        };
    }


    private static GameObject Sign(
        Vector3 center, Vector3 facing, SignStyle style,
        float w, float h, MuseumSign.Line[] lines)
    {
        string plate, frame = null;
        float bar = 0f;

        switch (style)
        {
            case SignStyle.Gallery: plate = GenMat("Sign Slate"); frame = Silver; bar = 0.03f; break;
            case SignStyle.Brass: plate = Silver; break;
            case SignStyle.Caption: plate = GenMat("Sign Pale"); break;
            case SignStyle.Warning: plate = GenMat("Sign Yellow"); frame = GenMat("Sign Black"); bar = 0.015f; break;
            case SignStyle.Exit: plate = GenMat("Sign Exit"); break;
            default: plate = GenMat("Sign White"); break;
        }

        string title = lines.Length > 0 ? lines[0].text : "";

        GameObject root = new GameObject("Sign - " + title);

        root.transform.SetParent(signs, false);

        root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));

        Transform t = root.transform;

        Box(t, "Plate", center, new Vector3(w, h, SignDepth), plate, collider: false);

        if (frame != null)
        {
            float d = SignDepth + 0.02f;

            Box(t, "Frame Top", t.TransformPoint(new Vector3(0f, (h + bar) * 0.5f, 0f)),
                new Vector3(w + bar * 2f, bar, d), frame, collider: false);
            Box(t, "Frame Bottom", t.TransformPoint(new Vector3(0f, -(h + bar) * 0.5f, 0f)),
                new Vector3(w + bar * 2f, bar, d), frame, collider: false);
            Box(t, "Frame Left", t.TransformPoint(new Vector3(-(w + bar) * 0.5f, 0f, 0f)),
                new Vector3(bar, h, d), frame, collider: false);
            Box(t, "Frame Right", t.TransformPoint(new Vector3((w + bar) * 0.5f, 0f, 0f)),
                new Vector3(bar, h, d), frame, collider: false);
        }

        bool serif = !(style == SignStyle.Staff || style == SignStyle.Warning || style == SignStyle.Exit);

        root.AddComponent<MuseumSign>().Configure(
            serif, w * 0.9f, SignDepth * 0.5f + 0.003f, lines);

        return root;
    }


    // =========================================================
    // AREAS, MARKERS, HIDING
    // =========================================================

    private static void BuildAreas()
    {
        Transform areas = new GameObject("Areas").transform;

        areas.SetParent(markers, false);

        foreach (Region region in Regions.Values)
        {
            List<Bounds> boxes = new List<Bounds>();

            float baseY = region.Floor == 0 ? 0f : FloorHeight;

            for (int z = 0; z < Depth; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (AtFloor(region.Floor, x, z) != region.Id)
                        continue;

                    Vector3 c = new Vector3(WX(x), baseY + region.Height * 0.5f, WZ(z));

                    boxes.Add(new Bounds(c, new Vector3(Cell, region.Height, Cell)));
                }
            }

            GameObject host = new GameObject(region.Name);

            host.transform.SetParent(areas, false);

            host.AddComponent<MapArea>()
                .Configure(region.Name, region.Backstage, boxes);
        }

        report.Add(Regions.Count + " map areas (backstage marked)");
    }


    /* The player starts in the stair hall facing the stair; the caretaker in maintenance. */

    private static readonly Vector3 PlayerStart = new Vector3(WX(14.5f), 0.05f, WZ(10.9f));

    private static readonly Vector3 CreatureStart = new Vector3(WX(1.0f), 0.05f, WZ(1.7f));


    private static void BuildMarkers()
    {
        Marker("Player Spawn", PlayerStart, 0f);

        Marker("Creature Spawn", CreatureStart, 180f);
    }


    /// <summary>Marks a desk placed while dressing as a hiding place.</summary>
    private static void DeskHide(GameObject desk, Vector3 facing, string name)
    {
        deskHides.Add((desk, facing, name));
    }


    /*
     * Hiding is under desks only, and only where it matters: a refuge
     * within a room or two of every loud task (each clock, the key
     * case), at least one per section on each floor, never in a dead
     * end, each facing the way the caretaker will come.
     */
    private static void BuildHidingSpots()
    {
        foreach ((GameObject desk, Vector3 facing, string name) in deskHides)
        {
            if (desk == null)
                continue;

            /* The pack's tabletop is single-sided; from underneath it must still be there. */

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

            Bounds b = RenderBounds(desk);

            Vector3 under = new Vector3(b.center.x, b.min.y, b.center.z) - facing * HidingSpot.DeskTuckBack;

            GameObject go = new GameObject("Hiding Spot - Under the " + name);

            go.transform.SetParent(hiding, false);

            go.transform.SetPositionAndRotation(under, Quaternion.LookRotation(facing, Vector3.up));

            HidingSpot spot = go.AddComponent<HidingSpot>();

            spot.ConfigureDesk(1.8f);

            go.AddComponent<InteractionMarker>()
                .Setup(InteractionMarker.Kind.Hide, null, 1.3f);
        }

        report.Add(hiding.childCount + " hiding spots (desks)");
    }


    private static void Marker(string name, Vector3 position, float yaw)
    {
        GameObject m = new GameObject(name);

        m.transform.SetParent(markers, false);

        m.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }


    // =========================================================
    // OBJECTIVES
    // =========================================================

    /*
     * Docs/MuseumLayout.md. Each section's key and clock are on
     * different floors:
     *
     *   West (NaturalHistory)  clock: sculpture gallery    key: director's desk, upstairs
     *   East (Egyptian)        key: antiquities case       clock: lecture hall, upstairs
     *   Special (FineArt)      clock: temporary exhibition key: strongroom
     *
     * Both section keys open the gate; the three clocks' strikes open
     * the strongroom; the master key opens the front doors.
     */
    private static void BuildObjectives()
    {
        SetFloor(0);

        BuildClock(GalleryKey.NaturalHistory,
            new Vector3(WX(6.5f) + Slab + 0.4f, 0f, WZ(8.8f)), 90f);
        BuildClock(GalleryKey.FineArt,
            new Vector3(WX(27.5f) - Slab - 0.4f, 0f, WZ(3.0f)), -90f);

        if (keyCase != null)
        {
            Bounds b = RenderBounds(keyCase);

            BuildKey(GalleryKey.Egyptian,
                new Vector3(b.center.x, b.max.y - 0.35f, b.center.z), keyCase);
        }
        else
        {
            report.Add("MISSING antiquities case - Egyptian key placed on the floor");

            BuildKey(GalleryKey.Egyptian, new Vector3(WX(19.6f), 1.0f, WZ(1.0f)), null);
        }

        Plin(27.0f, 8.4f, new Vector3(0.6f, 1.0f, 0.6f), VaultSteel);

        BuildKey(GalleryKey.FineArt, new Vector3(WX(27.0f), 1.2f, WZ(8.4f)), null);

        SetFloor(1);

        BuildClock(GalleryKey.Egyptian,
            new Vector3(WX(22.2f), FloorHeight, WZ(0.5f) - Slab - 0.4f), 180f);

        BuildKey(GalleryKey.NaturalHistory,
            new Vector3(WX(7.9f), FloorHeight + 0.95f, WZ(3.0f)), null);

        SetFloor(0);

        /* Objects of interest carry the only warm colour in the building. */

        foreach (KeyPickup key in objectivesGroup.GetComponentsInChildren<KeyPickup>(true))
        {
            accents.Add(new Accent
            {
                Name = "Key Glow", Position = key.transform.position + Vector3.up * 0.4f,
                Spot = false, Color = InterestAmber, Intensity = 1.2f, Range = 2.2f,
            });
        }

        WireExitDoor();

        BuildLocks();

        report.Add("Objectives: 3 keys, 3 clocks, gate, strongroom, fire door, front doors");
    }


    // =========================================================
    // LOCKS
    // =========================================================

    /// <summary>
    /// The three locked doors. Each is its own object at the door's
    /// edge, facing into the room it opens onto, with a blocker for the
    /// player and a carving obstacle for the caretaker - both saved
    /// switched off so the NavMesh bake runs straight through.
    /// </summary>
    private static void BuildLocks()
    {
        foreach (Door door in Doors)
        {
            switch (door.Kind)
            {
                case DoorKind.Gate: BuildGate(door); break;
                case DoorKind.Vault: BuildVaultDoor(door); break;
                case DoorKind.Bolted: BuildFireDoor(door); break;
            }
        }

        SetFloor(0);
    }


    private static Transform LockRoot(Door door, string name, out Vector3 outward)
    {
        SetFloor(door.Floor);

        outward = Outward(door.Side);

        GameObject root = new GameObject(name);

        root.transform.SetParent(objectivesGroup, false);

        root.transform.SetPositionAndRotation(
            CellCenter(door.X, door.Z) + outward * (Cell * 0.5f),
            Quaternion.LookRotation(outward, Vector3.up));

        NavMeshModifier modifier = root.AddComponent<NavMeshModifier>();

        modifier.ignoreFromBuild = true;

        return root.transform;
    }


    private static void Blockers(Transform root, Vector3 size, out BoxCollider box, out NavMeshObstacle obstacle)
    {
        box = root.gameObject.AddComponent<BoxCollider>();

        box.center = new Vector3(0f, size.y * 0.5f, 0f);
        box.size = size;

        obstacle = root.gameObject.AddComponent<NavMeshObstacle>();

        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;

        box.enabled = false;
        obstacle.enabled = false;
    }


    private static Transform Hinge(Transform root, string name, float x)
    {
        GameObject pivot = new GameObject(name);

        pivot.transform.SetParent(root, false);
        pivot.transform.localPosition = new Vector3(x, 0f, 0f);

        return pivot.transform;
    }


    private static GameObject LocalBox(Transform parent, string name, Vector3 localCenter, Vector3 size, string mat)
    {
        return Box(parent, name, parent.TransformPoint(localCenter), size, mat, collider: false);
    }


    private static void BuildGate(Door door)
    {
        Transform root = LockRoot(door, "Lock - Special Collections Gate", out Vector3 outward);

        string iron = GenMat("Wrought Iron");

        const float leafW = 1.15f, leafH = 2.9f;

        List<Transform> leaves = new List<Transform>();

        foreach (float side in new[] { -1f, 1f })
        {
            Transform pivot = Hinge(root, side < 0f ? "Leaf W" : "Leaf E", side * leafW);

            leaves.Add(pivot);

            float dir = -side;

            LocalBox(pivot, "Stile", new Vector3(0f, leafH * 0.5f, 0f), new Vector3(0.07f, leafH, 0.07f), iron);
            LocalBox(pivot, "Stile", new Vector3(dir * leafW, leafH * 0.5f, 0f), new Vector3(0.07f, leafH, 0.07f), iron);

            foreach (float y in new[] { 0.1f, 1.1f, 2.0f, leafH - 0.04f })
            {
                LocalBox(pivot, "Rail", new Vector3(dir * leafW * 0.5f, y, 0f), new Vector3(leafW, 0.06f, 0.06f), iron);
            }

            for (float a = 0.15f; a < leafW - 0.08f; a += 0.15f)
            {
                LocalBox(pivot, "Bar", new Vector3(dir * a, leafH * 0.5f, 0f), new Vector3(0.03f, leafH, 0.03f), iron);
            }

            /* One lock on each leaf - the two keys - picked out in red. */

            LocalBox(pivot, "Lock", new Vector3(dir * (leafW - 0.12f), 1.1f, -0.05f),
                new Vector3(0.18f, 0.26f, 0.1f), GenMat("Lock Red"));
        }

        accents.Add(new Accent
        {
            Name = "Gate Locks", Position = root.TransformPoint(new Vector3(0f, 1.6f, -0.8f)),
            Spot = false, Color = AlarmRed, Intensity = 1.0f, Range = 2.5f,
        });

        Blockers(root, new Vector3(DoorwayWidth + 0.3f, DoorwayHeight + 0.2f, 0.5f), out BoxCollider box, out NavMeshObstacle obstacle);

        LockedDoor lockedDoor = root.gameObject.AddComponent<LockedDoor>();

        lockedDoor.Configure(3.0f, new Vector3(0f, 1.4f, -0.3f), 2.5f);

        lockedDoor.Setup(LockedDoor.Lock.Gate, leaves.ToArray(), outward, -outward, box, obstacle);

        root.gameObject.AddComponent<InteractionMarker>()
            .Setup(InteractionMarker.Kind.Task, lockedDoor, 1.6f);
    }


    private static void BuildVaultDoor(Door door)
    {
        Transform root = LockRoot(door, "Lock - Strongroom Door", out Vector3 outward);

        const float w = 2.2f, h = 2.4f;

        Transform pivot = Hinge(root, "Leaf", -w * 0.5f);

        LocalBox(pivot, "Slab", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(w, h, 0.2f), VaultSteel);
        LocalBox(pivot, "Dial Plate", new Vector3(w * 0.5f + 0.4f, 1.3f, -0.12f), new Vector3(0.38f, 0.38f, 0.04f), GenMat("Lock Red"));
        LocalBox(pivot, "Dial", new Vector3(w * 0.5f + 0.4f, 1.3f, -0.16f), new Vector3(0.18f, 0.18f, 0.05f), GenMat("Sign Black"));
        LocalBox(pivot, "Wheel Bar", new Vector3(w * 0.5f - 0.3f, 1.3f, -0.15f), new Vector3(0.6f, 0.05f, 0.05f), Silver);
        LocalBox(pivot, "Wheel Bar", new Vector3(w * 0.5f - 0.3f, 1.3f, -0.15f), new Vector3(0.05f, 0.6f, 0.05f), Silver);

        Blockers(root, new Vector3(StaffDoorWidth + 0.1f, StaffDoorHeight + 0.1f, 0.5f), out BoxCollider box, out NavMeshObstacle obstacle);

        LockedDoor lockedDoor = root.gameObject.AddComponent<LockedDoor>();

        lockedDoor.Configure(2.8f, new Vector3(0.4f, 1.3f, -0.25f), 3.0f);

        lockedDoor.Setup(LockedDoor.Lock.Vault, new[] { pivot }, outward, -outward, box, obstacle);

        root.gameObject.AddComponent<InteractionMarker>()
            .Setup(InteractionMarker.Kind.Task, lockedDoor, 1.0f);
    }


    private static void BuildFireDoor(Door door)
    {
        Transform root = LockRoot(door, "Lock - Fire Door", out Vector3 outward);

        const float w = 2.2f, h = 2.38f;

        Transform pivot = Hinge(root, "Leaf", -w * 0.5f);

        LocalBox(pivot, "Leaf", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(w, h, 0.07f), ServiceWall);
        LocalBox(pivot, "Bolt", new Vector3(w * 0.5f, h - 0.25f, -0.06f), new Vector3(0.5f, 0.05f, 0.05f), Silver);
        LocalBox(pivot, "Push Bar", new Vector3(w * 0.5f, 1.05f, -0.07f), new Vector3(1.4f, 0.06f, 0.06f), GenMat("Sign Black"));

        Blockers(root, new Vector3(StaffDoorWidth + 0.1f, StaffDoorHeight + 0.1f, 0.5f), out BoxCollider box, out NavMeshObstacle obstacle);

        LockedDoor lockedDoor = root.gameObject.AddComponent<LockedDoor>();

        lockedDoor.Configure(2.6f, new Vector3(0f, 1.3f, 0f), 1.2f);

        /* Fire doors open outwards, into the corridor; the bolt is drawn from inside. */

        lockedDoor.Setup(LockedDoor.Lock.Bolted, new[] { pivot }, outward, -outward, box, obstacle);

        root.gameObject.AddComponent<InteractionMarker>()
            .Setup(InteractionMarker.Kind.Task, lockedDoor, 1.0f);
    }


    /// <summary>
    /// A grandfather clock from primitives - body, hood, face, hands and
    /// a pendulum behind a dark window. Faces local +Z.
    /// </summary>
    private static void BuildClock(GalleryKey key, Vector3 floor, float yaw)
    {
        GameObject clock = new GameObject(ObjectiveManager.ClockName(key));

        clock.transform.SetParent(objectivesGroup, false);

        clock.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));

        Material wood = ClockMaterial("Clock Wood", new Color(0.42f, 0.27f, 0.18f),
            0f, 0.35f, Color.black, AkMat("Wood"));
        Material brass = ClockMaterial("Brass", new Color(0.85f, 0.64f, 0.3f),
            0.9f, 0.6f, new Color(0.12f, 0.08f, 0.02f), null);
        Material face = ClockMaterial("Clock Face", new Color(0.93f, 0.88f, 0.76f),
            0f, 0.3f, new Color(0.18f, 0.16f, 0.12f), null);
        Material dark = ClockMaterial("Clock Glass", new Color(0.05f, 0.05f, 0.06f),
            0.2f, 0.9f, Color.black, null);

        Transform t = clock.transform;

        Part(t, PrimitiveType.Cube, "Base", new Vector3(0f, 0.125f, 0f), new Vector3(0.72f, 0.25f, 0.5f), wood);
        Part(t, PrimitiveType.Cube, "Trunk", new Vector3(0f, 0.9f, 0f), new Vector3(0.56f, 1.3f, 0.4f), wood);
        Part(t, PrimitiveType.Cube, "Hood", new Vector3(0f, 1.86f, 0f), new Vector3(0.68f, 0.62f, 0.46f), wood);
        Part(t, PrimitiveType.Cube, "Crown", new Vector3(0f, 2.21f, 0f), new Vector3(0.74f, 0.08f, 0.5f), wood);
        Part(t, PrimitiveType.Cube, "Window", new Vector3(0f, 0.95f, 0.205f), new Vector3(0.32f, 0.9f, 0.02f), dark);

        Part(t, PrimitiveType.Cylinder, "Bezel", new Vector3(0f, 1.86f, 0.235f),
            new Vector3(0.5f, 0.01f, 0.5f), brass, xRotation: 90f);
        Part(t, PrimitiveType.Cylinder, "Face", new Vector3(0f, 1.86f, 0.245f),
            new Vector3(0.44f, 0.01f, 0.44f), face, xRotation: 90f);

        /* Hands turn about the face's centre. */

        Transform hour = Pivot(t, "Hour Hand", new Vector3(0f, 1.86f, 0.258f));
        Part(hour, PrimitiveType.Cube, "Blade", new Vector3(0f, 0.055f, 0f), new Vector3(0.028f, 0.12f, 0.006f), dark);

        Transform minute = Pivot(t, "Minute Hand", new Vector3(0f, 1.86f, 0.262f));
        Part(minute, PrimitiveType.Cube, "Blade", new Vector3(0f, 0.085f, 0f), new Vector3(0.018f, 0.18f, 0.006f), dark);
        minute.localRotation = Quaternion.Euler(0f, 0f, -140f);

        /* Pendulum swings about its top, behind the window. */

        Transform bob = Pivot(t, "Pendulum", new Vector3(0f, 1.36f, 0.225f));
        Part(bob, PrimitiveType.Cube, "Rod", new Vector3(0f, -0.3f, 0f), new Vector3(0.016f, 0.6f, 0.01f), brass);
        Part(bob, PrimitiveType.Cylinder, "Bob", new Vector3(0f, -0.62f, 0f),
            new Vector3(0.15f, 0.012f, 0.15f), brass, xRotation: 90f);

        GameObject lightGo = new GameObject("Face Light");

        lightGo.transform.SetParent(t, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.86f, 0.7f);

        Light faceLight = lightGo.AddComponent<Light>();

        faceLight.type = LightType.Point;
        faceLight.range = 2.2f;
        faceLight.shadows = LightShadows.None;

        BoxCollider body = clock.AddComponent<BoxCollider>();

        body.center = new Vector3(0f, 1.125f, 0f);
        body.size = new Vector3(0.74f, 2.25f, 0.5f);

        ClockObjective objective = clock.AddComponent<ClockObjective>();

        objective.Configure(2.8f, new Vector3(0f, 1.55f, 0.25f), 6f);

        objective.Setup(key, hour, minute, bob, faceLight);

        clock.AddComponent<InteractionMarker>()
            .Setup(InteractionMarker.Kind.Task, objective, 0.85f);
    }


    /// <summary>
    /// A brass key hovering over its spot, turning slowly. In a case it
    /// has to be smashed out; otherwise it is simply taken.
    /// </summary>
    private static void BuildKey(GalleryKey key, Vector3 position, GameObject lockedCase)
    {
        GameObject root = new GameObject(ObjectiveManager.KeyName(key));

        root.transform.SetParent(objectivesGroup, false);

        root.transform.position = position;

        Material brass = ClockMaterial("Key Brass", new Color(0.95f, 0.72f, 0.32f),
            0.9f, 0.7f, new Color(0.45f, 0.3f, 0.08f), null);

        Transform visual = Pivot(root.transform, "Visual", Vector3.zero);

        visual.localScale = Vector3.one * 1.3f;

        Part(visual, PrimitiveType.Cylinder, "Bow", new Vector3(0f, 0.08f, 0f),
            new Vector3(0.09f, 0.012f, 0.09f), brass, xRotation: 90f);
        Part(visual, PrimitiveType.Cube, "Shaft", new Vector3(0f, -0.03f, 0f),
            new Vector3(0.018f, 0.17f, 0.018f), brass);
        Part(visual, PrimitiveType.Cube, "Bit A", new Vector3(0.022f, -0.1f, 0f),
            new Vector3(0.03f, 0.018f, 0.014f), brass);
        Part(visual, PrimitiveType.Cube, "Bit B", new Vector3(0.018f, -0.07f, 0f),
            new Vector3(0.022f, 0.014f, 0.014f), brass);

        KeyPickup pickup = root.AddComponent<KeyPickup>();

        pickup.Configure(
            2.6f, Vector3.zero,
            lockedCase != null ? 1.2f : 0.5f,
            lockedCase != null ? lockedCase.transform : null);

        pickup.Setup(key, visual, lockedCase);

        root.AddComponent<InteractionMarker>()
            .Setup(InteractionMarker.Kind.Task, pickup, 0.45f);
    }


    private static void WireExitDoor()
    {
        Transform exit = doors.Find("Exit Doors");

        if (exit == null)
        {
            report.Add("MISSING Exit Doors - no escape objective");

            return;
        }

        ExitDoorObjective objective = exit.gameObject.AddComponent<ExitDoorObjective>();

        /* Aim at the middle of the doorway, just inside. */

        Vector3 focus = exit.InverseTransformPoint(new Vector3(0f, 1.6f, WZ(17.5f) + 0.8f));

        objective.Configure(3.2f, focus, 10f);

        objective.Setup(Vector3.back);

        exit.gameObject.AddComponent<InteractionMarker>()
            .Setup(InteractionMarker.Kind.Task, objective, 1.5f);
    }


    private static GameObject Part(
        Transform parent, PrimitiveType type, string name,
        Vector3 localPosition, Vector3 localScale, Material material,
        float xRotation = 0f)
    {
        GameObject part = GameObject.CreatePrimitive(type);

        part.name = name;

        Object.DestroyImmediate(part.GetComponent<Collider>());

        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        part.transform.localScale = localScale;

        part.GetComponent<MeshRenderer>().sharedMaterial = material;

        return part;
    }


    private static Transform Pivot(Transform parent, string name, Vector3 localPosition)
    {
        GameObject pivot = new GameObject(name);

        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = localPosition;

        return pivot.transform;
    }


    /// <summary>
    /// A URP Lit material saved under Assets/Museum/Materials, updated in
    /// place on rebuild so scene references survive.
    /// </summary>
    private static Material ClockMaterial(
        string name, Color color, float metallic, float smoothness,
        Color emission, string textureFrom)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir))
        {
            AssetDatabase.CreateFolder(GeneratedDir, "Materials");
        }

        string path = MaterialDir + "/" + name + ".mat";

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            AssetDatabase.CreateAsset(mat, path);
        }

        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);

        if (!string.IsNullOrEmpty(textureFrom))
        {
            Material source = LoadMaterial(textureFrom);

            if (source != null && source.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                mat.SetTextureScale("_BaseMap", new Vector2(0.5f, 1f));
            }
        }

        if (emission.maxColorComponent > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
        }

        EditorUtility.SetDirty(mat);

        return mat;
    }


    // =========================================================
    // GAME ROOT & PLAYER
    // =========================================================

    private static void BuildGameRoot(Scene scene)
    {
        GameObject game = new GameObject(GameRootName);

        SceneManager.MoveGameObjectToScene(game, scene);

        game.AddComponent<ObjectiveManager>();

        game.AddComponent<GameHUD>();

        report.Add("Game root: objectives + HUD");
    }


    private static void PreparePlayer(Scene scene)
    {
        PlayerController player = FindIn<PlayerController>(scene);

        if (player == null)
        {
            report.Add("NO PLAYER - objectives need one; run BUILD EVERYTHING");

            return;
        }

        if (player.GetComponent<PlayerInteractor>() == null)
        {
            player.gameObject.AddComponent<PlayerInteractor>();

            report.Add("PlayerInteractor added to the player");
        }

        FlashlightSetup.EnsureOnPlayer(player);

        /*
         * The layout moves between rebuilds, so the rig is put back on
         * its marks every time: the player in the hall facing the gate,
         * the caretaker in his lodge.
         */

        player.transform.SetPositionAndRotation(PlayerStart, Quaternion.identity);

        PerceptionSystem creature = FindIn<PerceptionSystem>(scene);

        if (creature != null)
        {
            creature.transform.SetPositionAndRotation(
                CreatureStart, Quaternion.Euler(0f, 180f, 0f));
        }

        report.Add("Player placed in the central hall, caretaker in his lodge");

        /* The themed HUD replaces the debug overlay; F1 still shows it. */

        AIDebugOverlay overlay = FindIn<AIDebugOverlay>(scene);

        if (overlay != null)
        {
            SerializedObject so = new SerializedObject(overlay);

            SerializedProperty visible = so.FindProperty("visibleOnStart");

            if (visible != null)
            {
                visible.boolValue = false;

                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }


    private static void AddToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes =
            new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        foreach (EditorBuildSettingsScene s in scenes)
        {
            if (s.path == ScenePath)
                return;
        }

        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));

        EditorBuildSettings.scenes = scenes.ToArray();

        report.Add("Museum added to Build Settings");
    }


    // =========================================================
    // LIGHTING
    // =========================================================

    private static void BuildLighting(Scene scene)
    {
        GameObject root = new GameObject(LightingRootName);

        SceneManager.MoveGameObjectToScene(root, scene);

        /*
         * Night, grey and white. A cold moonlit fill keeps every floor
         * readable; white wall sconces pool along the corridors and the
         * balcony; exhibits get their own white spots; warm colour is
         * kept for the things that matter - keys, clocks, the gate's
         * locks - and green for the way out.
         */

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.17f, 0.19f, 0.25f);
        RenderSettings.ambientEquatorColor = new Color(0.12f, 0.125f, 0.135f);
        RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.055f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.07f, 0.08f, 0.10f);
        RenderSettings.fogDensity = 0.007f;
        RenderSettings.reflectionIntensity = 0.4f;

        DynamicGI.UpdateEnvironment();

        PointLight(root, "Exit Sign", WX(14f), 3.85f, WZ(17.5f) + 0.5f,
            new Color(0.2f, 1f, 0.45f), 1.4f, 6f, false);

        int fills = 0, strips = 0, sconces = 0;

        for (int f = 0; f < 2; f++)
        {
            SetFloor(f);

            for (int z = 0; z < Depth; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Region r = RegionAt(x, z);

                    if (r == null)
                        continue;

                    Vector3 c = CellCenter(x, z);

                    /*
                     * One light per two cells, checkerboard: Forward+
                     * shades at most 256 visible lights, and the
                     * exhibit spots must not be the ones dropped.
                     */

                    bool lit = (x + z) % 2 == 0;

                    if (r.Backstage && lit)
                    {
                        PointLight(root, "Strip " + f + ":" + x + "," + z,
                            c.x, c.y + Mathf.Min(r.Height, 4f) - 0.35f, c.z,
                            StripCold, 3.0f, 7.5f, false);

                        if (r.Height < 5f)
                        {
                            Fit("Neon", props,
                                c + Vector3.up * (r.Height - 0.05f),
                                new Vector3(-1f, -1f, -1f),
                                (x % 2 == 0) ? 0f : 90f,
                                Anchor.Top);

                            strips++;
                        }
                    }
                    else if (!r.Backstage && lit)
                    {
                        /* The stair hall's fill hangs high, under its laylights. */

                        bool tall = r.Height > FloorHeight;

                        PointLight(root, "Fill " + f + ":" + x + "," + z,
                            c.x, c.y + (tall ? r.Height - 1.4f : Mathf.Min(r.Height - 0.7f, 4.4f)), c.z,
                            MoonFill, (tall ? 6f : 3.4f) * r.Light, tall ? 14f : 9.5f, false);

                        fills++;
                    }

                    if (r.Sconces && (x + z) % 4 == 0 && Sconce(root, x, z, r))
                    {
                        sconces++;
                    }
                }
            }
        }

        SetFloor(0);

        Transform accentRoot = new GameObject("Accents").transform;

        accentRoot.SetParent(root.transform, false);

        foreach (Accent a in accents)
        {
            GameObject go = new GameObject("Light - " + a.Name);

            go.transform.SetParent(accentRoot, false);

            go.transform.position = a.Position;

            Light light = go.AddComponent<Light>();

            light.color = a.Color;
            light.intensity = a.Intensity;
            light.range = a.Range;
            light.shadows = LightShadows.None;

            if (a.Spot)
            {
                light.type = LightType.Spot;
                light.spotAngle = a.Angle;
                light.innerSpotAngle = a.Angle * 0.55f;

                go.transform.rotation = Quaternion.LookRotation(a.Target - a.Position, Vector3.up);
            }
            else
            {
                light.type = LightType.Point;
            }
        }

        BuildPostVolume(root);

        report.Add("Lighting: " + fills + " fills, " + sconces + " sconces, " +
                   accents.Count + " accents, " + strips + " strip fittings");
    }


    /// <summary>
    /// A white wall sconce on the first plain wall of a cell - the
    /// pools of light in the reference corridor.
    /// </summary>
    private static bool Sconce(GameObject root, int x, int z, Region r)
    {
        for (Dir d = Dir.N; d <= Dir.W; d++)
        {
            Neighbour(x, z, d, out int nx, out int nz);

            char other = At(nx, nz);

            if (other == r.Id || other == Void || FindDoor(x, z, d, out _))
                continue;

            Vector3 outward = Outward(d);

            Vector3 wall = CellCenter(x, z) + outward * (Cell * 0.5f - Slab) + Vector3.up * 2.4f;

            Box(props, "Sconce", wall - outward * 0.07f, new Vector3(0.16f, 0.28f, 0.16f), SconceGlass, collider: false);

            Vector3 lamp = wall - outward * 0.35f;

            PointLight(root, "Sconce " + curFloor + ":" + x + "," + z,
                lamp.x, lamp.y, lamp.z, SconceWhite, 1.5f, 4.5f, false);

            return true;
        }

        return false;
    }


    private static void PointLight(
        GameObject root, string name,
        float x, float y, float z,
        Color color, float intensity, float range, bool shadows)
    {
        GameObject go = new GameObject("Light - " + name);

        go.transform.SetParent(root.transform, false);

        go.transform.position = new Vector3(x, y, z);

        Light light = go.AddComponent<Light>();

        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }


    private static void BuildPostVolume(GameObject root)
    {
        VolumeProfile profile =
            AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);

        /* Rebuilt every time, so tuning here always reaches the asset. */

        if (profile != null)
        {
            AssetDatabase.DeleteAsset(PostProfilePath);
        }

        profile = ScriptableObject.CreateInstance<VolumeProfile>();

        AssetDatabase.CreateAsset(profile, PostProfilePath);

        Tonemapping tone = profile.Add<Tonemapping>(true);
        tone.mode.value = TonemappingMode.ACES;

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = 1.0f;
        bloom.intensity.value = 0.35f;
        bloom.scatter.value = 0.6f;

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.value = 0.3f;
        vignette.smoothness.value = 0.45f;

        FilmGrain grain = profile.Add<FilmGrain>(true);
        grain.type.value = FilmGrainLookup.Thin1;
        grain.intensity.value = 0.25f;

        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.value = 0.95f;
        color.saturation.value = -28f;
        color.contrast.value = 14f;

        WhiteBalance balance = profile.Add<WhiteBalance>(true);
        balance.temperature.value = -5f;

        foreach (VolumeComponent component in profile.components)
        {
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        EditorUtility.SetDirty(profile);

        GameObject host = new GameObject("Post Processing");

        host.transform.SetParent(root.transform, false);

        Volume volume = host.AddComponent<Volume>();

        volume.isGlobal = true;

        volume.sharedProfile = profile;
    }


    private static void EnablePostOnCameras(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
            {
                UniversalAdditionalCameraData data =
                    cam.GetUniversalAdditionalCameraData();

                data.renderPostProcessing = true;

                cam.clearFlags = CameraClearFlags.SolidColor;

                cam.backgroundColor = Color.black;
            }
        }
    }


    // =========================================================
    // RIG
    // =========================================================

    /// <summary>
    /// Moves the player, creature and Director in from Prototype Map.
    ///
    /// Moved rather than rebuilt so every value tuned on them so far
    /// comes along. Prototype Map is closed without saving afterwards,
    /// so it is untouched on disk.
    /// </summary>
    private static void ImportRig(Scene museum)
    {
        if (FindIn<PlayerController>(museum) != null)
        {
            report.Add("Rig already in the scene - left alone");

            return;
        }

        if (!File.Exists(RigScenePath))
        {
            report.Add("NO RIG - " + RigScenePath + " not found. " +
                       "Run BUILD EVERYTHING in this scene instead.");

            return;
        }

        Scene source = SceneManager.GetSceneByPath(RigScenePath);

        bool opened = false;

        if (!source.IsValid() || !source.isLoaded)
        {
            source = EditorSceneManager.OpenScene(
                RigScenePath, OpenSceneMode.Additive);

            opened = true;
        }

        System.Type[] rigTypes =
        {
            typeof(PlayerController),
            typeof(PerceptionSystem),
            typeof(AIDirector),
            typeof(XenoDirector),
            typeof(AIDebugOverlay),
            typeof(SuspiciousItemSpawner),
            typeof(MotivationDebugKeys),
            typeof(ZoneGraph),
        };

        List<string> moved = new List<string>();

        foreach (GameObject root in source.GetRootGameObjects())
        {
            bool isRig = false;

            foreach (System.Type type in rigTypes)
            {
                if (root.GetComponentInChildren(type, true) != null)
                {
                    isRig = true;

                    break;
                }
            }

            if (!isRig)
                continue;

            SceneManager.MoveGameObjectToScene(root, museum);

            moved.Add(root.name);
        }

        if (opened)
        {
            EditorSceneManager.CloseScene(source, true);
        }

        report.Add("Rig moved in from Prototype Map: " +
                   string.Join(", ", moved));

        /* PreparePlayer puts the rig on its marks. */
    }


    private static T FindIn<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);

            if (found != null)
                return found;
        }

        return null;
    }


    // =========================================================
    // NAVMESH
    // =========================================================

    private static void BakeNavMesh(Scene scene)
    {
        NavMeshSurface surface = FindIn<NavMeshSurface>(scene);

        if (surface == null)
        {
            GameObject host = new GameObject("NavMesh");

            SceneManager.MoveGameObjectToScene(host, scene);

            surface = host.AddComponent<NavMeshSurface>();
        }

        surface.collectObjects = CollectObjects.All;

        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        int mask = ~0;

        int playerLayer = LayerMask.NameToLayer(GameLayers.PlayerName);

        int npcLayer = LayerMask.NameToLayer(GameLayers.NpcName);

        if (playerLayer >= 0) mask &= ~(1 << playerLayer);

        if (npcLayer >= 0) mask &= ~(1 << npcLayer);

        surface.layerMask = mask;

        /*
         * Other open scenes would be collected too - only this scene's
         * colliders belong in the museum's mesh.
         */

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

        try
        {
            surface.RemoveData();

            surface.navMeshData = null;

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

            surface.AddData();

            EditorUtility.SetDirty(surface);
        }

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();

        report.Add("NavMesh baked (" + tri.vertices.Length + " vertices)");
    }


    /// <summary>
    /// Walks a path from the stair hall to every room on both floors,
    /// then straight through every door. A stair the agent cannot climb
    /// or a doorway narrower than it looks shows up here rather than as
    /// a creature that silently cannot reach a wing.
    /// </summary>
    private static void CheckConnectivity()
    {
        Vector3 start = new Vector3(WX(14.5f), 0f, WZ(10.9f));

        List<string> failed = new List<string>();

        NavMeshPath path = new NavMeshPath();

        foreach (Region region in Regions.Values)
        {
            float baseY = region.Floor == 0 ? 0f : FloorHeight;

            bool ok = false, any = false;

            NavMesh.SamplePosition(start, out NavMeshHit a, 2f, NavMesh.AllAreas);

            for (int z = 0; z < Depth && !ok; z++)
            {
                for (int x = 0; x < Width && !ok; x++)
                {
                    if (AtFloor(region.Floor, x, z) != region.Id)
                        continue;

                    any = true;

                    ok =
                        NavMesh.SamplePosition(new Vector3(WX(x), baseY, WZ(z)), out NavMeshHit b, 1.6f, NavMesh.AllAreas) &&
                        Mathf.Abs(b.position.y - baseY) < 1.0f &&
                        NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete;
                }
            }

            if (any && !ok)
            {
                failed.Add(region.Name);
            }
        }

        report.Add(failed.Count == 0
            ? "Connectivity: every room on both floors reachable from the stair hall"
            : "UNREACHABLE from the stair hall: " + string.Join(", ", failed));

        List<string> blocked = new List<string>();

        int checkedDoors = 0;

        foreach (Door door in Doors)
        {
            if (door.Kind == DoorKind.Exit)
                continue;

            SetFloor(door.Floor);

            Vector3 outward = Outward(door.Side);

            Vector3 edge = CellCenter(door.X, door.Z) + outward * (Cell * 0.5f);

            Vector3 inside = edge - outward * 1.4f;
            Vector3 outside = edge + outward * 1.4f;

            bool through =
                NavMesh.SamplePosition(inside, out NavMeshHit a, 0.9f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(outside, out NavMeshHit b, 0.9f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete &&
                PathLength(path) < 5f;

            checkedDoors++;

            if (!through)
            {
                blocked.Add(door.Kind + " " + door.Floor + ":" + door.X + "," + door.Z + " " + door.Side);
            }
        }

        SetFloor(0);

        report.Add(blocked.Count == 0
            ? "Doors: all " + checkedDoors + " walkable straight through"
            : "BLOCKED DOORS: " + string.Join(", ", blocked));
    }


    private static float PathLength(NavMeshPath path)
    {
        float length = 0f;

        for (int i = 1; i < path.corners.Length; i++)
        {
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return length;
    }


    // =========================================================
    // PLACEMENT
    // =========================================================

    private enum Anchor { Center, Bottom, Top }


    /// <summary>
    /// Places a prefab by its visible bounds rather than its pivot -
    /// the kit's pivots are all over the place (the sarcophagus pivots
    /// at its middle, the wall panel at half height).
    ///
    /// <paramref name="size"/> is in the prefab's own axes, before the
    /// yaw; any component below zero keeps the native size.
    /// </summary>
    private static GameObject Fit(
        string prefabName,
        Transform parent,
        Vector3 target,
        Vector3 size,
        float yaw,
        Anchor anchor)
    {
        GameObject prefab = LoadPrefab(prefabName);

        if (prefab == null)
        {
            report.Add("MISSING prefab " + prefabName);

            GameObject stub = new GameObject("MISSING " + prefabName);

            stub.transform.SetParent(parent, false);

            return stub;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(
            prefab, parent.gameObject.scene);

        go.transform.SetParent(parent, false);

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        Vector3 baseScale = go.transform.localScale;

        Bounds native = RenderBounds(go);

        Vector3 scale = new Vector3(
            size.x > 0f ? size.x / native.size.x : 1f,
            size.y > 0f ? size.y / native.size.y : 1f,
            size.z > 0f ? size.z / native.size.z : 1f);

        go.transform.localScale = Vector3.Scale(baseScale, scale);

        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Bounds placed = RenderBounds(go);

        Vector3 offset = target - placed.center;

        if (anchor == Anchor.Bottom)
        {
            offset.y = target.y - placed.min.y;
        }
        else if (anchor == Anchor.Top)
        {
            offset.y = target.y - placed.max.y;
        }

        go.transform.position += offset;

        return go;
    }


    private static GameObject Prop(
        string name, float x, float z, float yaw,
        float scale = 1f, float y = 0f, float scaleY = 1f)
    {
        GameObject prefab = LoadPrefab(name);

        Vector3 size = new Vector3(-1f, -1f, -1f);

        if (prefab != null && (scale != 1f || scaleY != 1f))
        {
            GameObject probe = Object.Instantiate(prefab);

            Bounds b = RenderBounds(probe);

            Object.DestroyImmediate(probe);

            size = new Vector3(
                b.size.x * scale,
                b.size.y * scale * scaleY,
                b.size.z * scale);
        }

        return Fit(name, props, new Vector3(x, y + Y0, z), size, yaw, Anchor.Bottom);
    }


    /// <summary>
    /// The left-facing lion only exists as a bare model, whose
    /// embedded material is untextured grey.
    /// </summary>
    private static void Lion(float x, float z, float yaw)
    {
        GameObject lion = Prop("Lion_Gauche", x, z, yaw);

        Material mat = LoadMaterial(BgpMat("Lion_MAT"));

        foreach (Renderer r in lion.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterial = mat;
        }

        if (lion.GetComponentInChildren<Collider>() == null)
        {
            lion.AddComponent<MeshCollider>();
        }
    }


    /// <summary>
    /// A statue on a plain plinth. The gallery pack's statues are
    /// tabletop-sized, so they are scaled up to read at museum scale.
    /// </summary>
    private static void Statue(
        string name, float x, float z, float yaw,
        float plinth = 1.1f, float scale = 1.7f)
    {
        Plinth(x, z, new Vector3(0.8f, plinth, 0.8f),
            AkMat("Gray"));

        Prop(name, x, z, yaw, scale: scale, y: plinth);

        Vector3 front = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        Vector3 target = new Vector3(x, plinth + 0.5f * scale, z);

        SpotOn(name, target + front * 1.6f + Vector3.up * 2.6f, target, 4.5f, 6f, 34f);
    }


    /// <summary>A ceiling spot aimed at a point - an exhibit's own light.</summary>
    private static void SpotOn(
        string name, Vector3 from, Vector3 target,
        float intensity = 5f, float range = 9f, float angle = 38f)
    {
        accents.Add(new Accent
        {
            Name = name, Position = from + Vector3.up * Y0, Target = target + Vector3.up * Y0, Spot = true,
            Color = SpotWhite, Intensity = intensity, Range = range, Angle = angle,
        });
    }


    /// <summary>A small cool glow inside a glass case.</summary>
    private static void CaseLight(float x, float y, float z, float intensity = 1.6f, float range = 3f)
    {
        accents.Add(new Accent
        {
            Name = "Case", Position = new Vector3(x, y + Y0, z), Spot = false,
            Color = CaseCool, Intensity = intensity, Range = range,
        });
    }


    private static void Plinth(float x, float z, Vector3 size, string mat)
    {
        Box(props, "Plinth",
            new Vector3(x, Y0 + size.y * 0.5f, z), size, mat, collider: true);
    }


    /// <summary>
    /// A frame on the wall with one of the gallery pack's canvases
    /// in it.
    /// </summary>
    private static void Painting(float x, float z, float yaw, float y = 2.3f)
    {
        const float scale = 1.9f;

        /* Wall positions are given at the wall face; stand just proud of it. */

        Vector3 intoRoom = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        Vector3 centerRel = new Vector3(x, y, z) + intoRoom * 0.04f;

        Vector3 center = centerRel + Vector3.up * Y0;

        GameObject frame = Fit("Picture Frame", props,
            center,
            new Vector3(0.96f * scale, 0.70f * scale, -1f),
            yaw, Anchor.Center);

        int index = 1 + Mathf.Abs(Mathf.RoundToInt(x * 7f + z * 13f + y * 5f)) % 14;

        GameObject canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);

        Object.DestroyImmediate(canvas.GetComponent<Collider>());

        canvas.name = "Canvas";

        /* A quad shows its -Z side; point that into the room. */

        canvas.transform.SetPositionAndRotation(
            center + intoRoom * 0.025f,
            Quaternion.LookRotation(-intoRoom, Vector3.up));

        canvas.transform.localScale =
            new Vector3(0.80f * scale, 0.54f * scale, 1f);

        canvas.transform.SetParent(frame.transform, true);

        canvas.GetComponent<MeshRenderer>().sharedMaterial =
            LoadMaterial(AkDir + "Materials/Images/Image " + index + ".mat");

        /* A brass picture light over the frame, and the pool it throws. */

        Vector3 lamp = center + Vector3.up * (0.70f * scale * 0.5f + 0.16f) + intoRoom * 0.14f;

        GameObject bar = Box(props, "Picture Light", lamp,
            Vector3.one, Silver, collider: false);

        bar.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        bar.GetComponent<MeshFilter>().sharedMesh = BoxMesh(new Vector3(0.55f, 0.05f, 0.1f));

        SpotOn("Picture", centerRel + intoRoom * 0.9f + Vector3.up * 1.0f, centerRel, 2.6f, 3.5f, 55f);
    }


    private static void StripColliders(GameObject go)
    {
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(c);
        }
    }


    private static Bounds RenderBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        Bounds b = renderers[0].bounds;

        foreach (Renderer r in renderers)
        {
            b.Encapsulate(r.bounds);
        }

        return b;
    }


    private static GameObject LoadPrefab(string name)
    {
        if (prefabPaths == null)
        {
            prefabPaths = new Dictionary<string, string>();

            string[] folders =
            {
                BgpDir + "Prefabs",
                AkDir + "Prefabs",
            };

            /* Some kit meshes (Lion_Gauche) ship without a prefab. */

            foreach (string guid in AssetDatabase.FindAssets(
                         "t:Model", new[] { BgpDir + "Mesh" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                prefabPaths[Path.GetFileNameWithoutExtension(path)] = path;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                prefabPaths[Path.GetFileNameWithoutExtension(path)] = path;
            }
        }

        return prefabPaths.TryGetValue(name, out string found)
            ? AssetDatabase.LoadAssetAtPath<GameObject>(found)
            : null;
    }


    private static Material LoadMaterial(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (!matCache.TryGetValue(path, out Material m))
        {
            m = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (m == null)
            {
                report.Add("MISSING material " + path);
            }

            matCache[path] = m;
        }

        return m;
    }


    // =========================================================
    // BOXES
    // =========================================================

    private static GameObject Box(
        Transform parent,
        string name,
        Vector3 center,
        Vector3 size,
        string material,
        bool collider)
    {
        GameObject go = new GameObject(name);

        go.transform.SetParent(parent, false);

        go.transform.position = center;

        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);

        go.AddComponent<MeshRenderer>().sharedMaterial =
            LoadMaterial(material);

        if (collider)
        {
            go.AddComponent<BoxCollider>().size = size;
        }

        return go;
    }


    /// <summary>
    /// A box whose UVs are in cell units, so one texture repeat covers
    /// one 5.3 m module on every face regardless of the box's size -
    /// a stretched default cube would smear the texture on long walls.
    /// </summary>
    private static Mesh BoxMesh(Vector3 size)
    {
        string key = size.x.ToString("F3") + "x" +
                     size.y.ToString("F3") + "x" +
                     size.z.ToString("F3");

        if (meshCache.TryGetValue(key, out Mesh cached))
            return cached;

        Vector3 h = size * 0.5f;

        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        void Face(Vector3 n, Vector3 u, Vector3 v, float hu, float hv, float hn)
        {
            int start = verts.Count;

            Vector3 c = n * hn;

            verts.Add(c - u * hu - v * hv);
            verts.Add(c - u * hu + v * hv);
            verts.Add(c + u * hu + v * hv);
            verts.Add(c + u * hu - v * hv);

            float su = hu * 2f / Cell;
            float sv = hv * 2f / Cell;

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, sv));
            uvs.Add(new Vector2(su, sv));
            uvs.Add(new Vector2(su, 0f));

            /* Clockwise seen from outside - Unity's front face. */

            tris.Add(start);
            tris.Add(start + 2);
            tris.Add(start + 1);
            tris.Add(start);
            tris.Add(start + 3);
            tris.Add(start + 2);
        }

        Face(Vector3.forward, Vector3.right, Vector3.up, h.x, h.y, h.z);
        Face(Vector3.back, Vector3.left, Vector3.up, h.x, h.y, h.z);
        Face(Vector3.right, Vector3.back, Vector3.up, h.z, h.y, h.x);
        Face(Vector3.left, Vector3.forward, Vector3.up, h.z, h.y, h.x);
        Face(Vector3.up, Vector3.right, Vector3.back, h.x, h.z, h.y);
        Face(Vector3.down, Vector3.right, Vector3.forward, h.x, h.z, h.y);

        Mesh mesh = new Mesh { name = "Box " + key };

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        meshCache[key] = mesh;

        return mesh;
    }


    /// <summary>
    /// The building's finishes: white plaster, grey wainscot, grey oak
    /// and marble, slate, concrete - and the few colours the art bible
    /// allows: amber brass, lock red, exit green, hazard yellow.
    /// </summary>
    private static void PrepareSurfaceMaterials()
    {
        string plaster = AkMat("Plaster");
        string wood = AkMat("Wood");

        Surface("Plaster White", new Color(0.80f, 0.80f, 0.78f), 0f, 0.15f, plaster);
        Surface("Wainscot Grey", new Color(0.22f, 0.24f, 0.24f), 0f, 0.35f, wood);
        Surface("Grey Oak", new Color(0.46f, 0.46f, 0.44f), 0f, 0.3f, wood);
        Surface("Grey Marble", new Color(0.66f, 0.66f, 0.66f), 0f, 0.45f, BgpMat("Museum_Archi_Arcades_Floor_Blank_MAT"));
        Surface("Grey Marble Border", new Color(0.70f, 0.70f, 0.70f), 0f, 0.45f, BgpMat("Museum_Archi_Arcades_Floor_Border_MAT"));
        Surface("Ceiling White", new Color(0.72f, 0.72f, 0.72f), 0f, 0.1f, plaster);
        Surface("Service Grey", new Color(0.50f, 0.52f, 0.52f), 0f, 0.2f, plaster);
        Surface("Service Floor", new Color(0.36f, 0.36f, 0.35f), 0f, 0.35f, plaster);
        Surface("Slate Wall", new Color(0.26f, 0.28f, 0.30f), 0f, 0.2f, plaster);
        Surface("Stall Grey", new Color(0.55f, 0.57f, 0.58f), 0f, 0.4f, null);
        Surface("Concrete Grey", new Color(0.45f, 0.45f, 0.45f), 0f, 0.1f, plaster);
        Surface("Vault Steel", new Color(0.33f, 0.35f, 0.37f), 0.6f, 0.4f, null);
        Surface("Tomb Stone", new Color(0.42f, 0.40f, 0.38f), 0f, 0.08f, plaster);
        Surface("Runner Red", new Color(0.36f, 0.06f, 0.07f), 0f, 0.05f, plaster);

        ClockMaterial("Brass", new Color(0.85f, 0.64f, 0.3f),
            0.9f, 0.6f, new Color(0.12f, 0.08f, 0.02f), null);
        ClockMaterial("Silver", new Color(0.62f, 0.63f, 0.66f), 0.8f, 0.55f, Color.black, null);
        ClockMaterial("Wrought Iron", new Color(0.06f, 0.06f, 0.065f), 0.7f, 0.35f, Color.black, null);
        ClockMaterial("Lock Red", new Color(0.55f, 0.06f, 0.05f), 0.4f, 0.5f, new Color(0.5f, 0.03f, 0.02f), null);
        ClockMaterial("Laylight", new Color(0.10f, 0.11f, 0.13f), 0f, 0.2f, new Color(0.22f, 0.26f, 0.34f), null);
        ClockMaterial("Sconce Glass", new Color(0.95f, 0.92f, 0.85f), 0f, 0.6f, new Color(1.6f, 1.45f, 1.2f), null);
        ClockMaterial("Screen", new Color(0.03f, 0.04f, 0.05f), 0f, 0.9f, new Color(0.03f, 0.05f, 0.08f), null);

        ClockMaterial("Sign Black", new Color(0.05f, 0.05f, 0.06f), 0f, 0.5f, Color.black, null);
        ClockMaterial("Sign Slate", new Color(0.10f, 0.11f, 0.12f), 0f, 0.45f, Color.black, null);
        ClockMaterial("Sign Pale", new Color(0.82f, 0.82f, 0.80f), 0f, 0.2f, Color.black, null);
        ClockMaterial("Sign White", new Color(0.92f, 0.92f, 0.90f), 0f, 0.3f, Color.black, null);
        ClockMaterial("Sign Yellow", new Color(0.95f, 0.76f, 0.08f), 0f, 0.3f, Color.black, null);
        ClockMaterial("Sign Exit", new Color(0.0f, 0.55f, 0.22f), 0f, 0.5f, new Color(0.0f, 0.9f, 0.35f), null);
    }


    private static void Surface(string name, Color color, float metallic, float smoothness, string texFrom)
    {
        Material m = ClockMaterial(name, color, metallic, smoothness, Color.black, texFrom);

        if (m.HasProperty("_BaseMap"))
        {
            m.SetTextureScale("_BaseMap", Vector2.one);
        }
    }


    private static void PrepareMeshAsset()
    {
        if (!AssetDatabase.IsValidFolder(GeneratedDir))
        {
            AssetDatabase.CreateFolder("Assets", "Museum");
        }

        /* Old meshes are only referenced by the map just deleted. */

        if (File.Exists(MeshAssetPath))
        {
            AssetDatabase.DeleteAsset(MeshAssetPath);
        }
    }


    private static void SaveMeshAsset()
    {
        Mesh main = null;

        foreach (Mesh mesh in meshCache.Values)
        {
            if (main == null)
            {
                main = mesh;

                AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            }
            else
            {
                AssetDatabase.AddObjectToAsset(mesh, main);
            }
        }

        AssetDatabase.SaveAssets();

        report.Add(meshCache.Count + " box meshes saved to " + MeshAssetPath);
    }


    // =========================================================
    // SCREENSHOTS
    // =========================================================

    /// <summary>
    /// Renders a still from an arbitrary viewpoint to Temp/MuseumShots.
    /// Used to check the build without touching the Scene view.
    /// <paramref name="bright"/> lifts the ambient so geometry can be
    /// judged in a level that is meant to be dark.
    /// </summary>
    public static string Shot(
        string file,
        Vector3 position,
        Vector3 euler,
        float fov = 70f,
        bool bright = false,
        bool topDown = false,
        float orthoSize = 30f,
        bool hideCeilings = false,
        int width = 1280,
        int height = 720)
    {
        string dir = Path.GetFullPath("Temp/MuseumShots");

        Directory.CreateDirectory(dir);

        Color ambient = RenderSettings.ambientLight;
        bool fog = RenderSettings.fog;

        GameObject ceilingRoot = null;

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == MapRootName)
            {
                Transform c = root.transform.Find("Ceilings");

                if (c != null) ceilingRoot = c.gameObject;
            }
        }

        if (hideCeilings && ceilingRoot != null)
        {
            ceilingRoot.SetActive(false);
        }

        /* Other open scenes would photobomb the shot. */

        List<GameObject> hiddenRoots = new List<GameObject>();

        Scene active = SceneManager.GetActiveScene();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene other = SceneManager.GetSceneAt(i);

            if (other == active || !other.isLoaded)
                continue;

            foreach (GameObject root in other.GetRootGameObjects())
            {
                if (root.activeSelf)
                {
                    root.SetActive(false);

                    hiddenRoots.Add(root);
                }
            }
        }

        GameObject fill = null;

        if (bright)
        {
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            RenderSettings.fog = false;

            DynamicGI.UpdateEnvironment();

            /* Shadowless, so it shines straight through the ceilings. */

            fill = new GameObject("Museum Shot Fill")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Light sun = fill.AddComponent<Light>();

            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;

            fill.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
        }

        GameObject camGo = new GameObject("Museum Shot Camera")
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        RenderTexture rt = new RenderTexture(width, height, 24);

        try
        {
            Camera cam = camGo.AddComponent<Camera>();

            cam.enabled = false;
            cam.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            if (topDown)
            {
                cam.orthographic = true;
                cam.orthographicSize = orthoSize;
            }

            UniversalAdditionalCameraData data =
                cam.GetUniversalAdditionalCameraData();

            data.renderPostProcessing = !bright;

            cam.targetTexture = rt;

            UniversalRenderPipeline.SingleCameraRequest request =
                new UniversalRenderPipeline.SingleCameraRequest
                {
                    destination = rt
                };

            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                cam.Render();
            }

            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);

            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = null;

            string path = Path.Combine(dir, file + ".png");

            File.WriteAllBytes(path, tex.EncodeToPNG());

            Object.DestroyImmediate(tex);

            return path;
        }
        finally
        {
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = fog;

            if (bright)
            {
                DynamicGI.UpdateEnvironment();
            }

            if (fill != null)
            {
                Object.DestroyImmediate(fill);
            }

            if (ceilingRoot != null)
            {
                ceilingRoot.SetActive(true);
            }

            foreach (GameObject root in hiddenRoots)
            {
                root.SetActive(true);
            }

            Object.DestroyImmediate(camGo);

            rt.Release();

            Object.DestroyImmediate(rt);
        }
    }
}


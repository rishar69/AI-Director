using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/Scenes/HidingTest.unity - a bench for one hiding
/// object at a time. Right now: the desk.
///
/// Every desk model in the project is lined up (only the British
/// Gangsters pack's trestle table, Mesh_Table_carre, qualifies), each
/// in the placements it gets in the museum, with a hiding spot posed
/// under it. The player is copied in from the museum.
///
/// Tools > AI Director > Hiding Test - Build Scene, or
/// HidingTestBuilder.Build() from the CLI.
/// </summary>
public static class HidingTestBuilder
{
    private const string MuseumPath = "Assets/Scenes/Museum.unity";

    private const string ScenePath = "Assets/Scenes/HidingTest.unity";

    private const string BgpPrefabs = "Assets/BristishGangstersPack_Museum/Prefabs/";


    /* The desk pose: crouched, eyes just under the tabletop, facing out of one long side. */

    /* The pose itself lives in HidingSpot.ConfigureDesk, shared with the museum. */


    [MenuItem("Tools/AI Director/Hiding Test - Build Scene", priority = -80)]
    public static void BuildFromMenu()
    {
        EditorUtility.DisplayDialog("Hiding Test", Build(), "OK");
    }


    public static string Build()
    {
        StringBuilder report = new StringBuilder();

        Scene museum = SceneManager.GetSceneByPath(MuseumPath);

        bool openedMuseum = false;

        if (!museum.IsValid() || !museum.isLoaded)
        {
            museum = EditorSceneManager.OpenScene(MuseumPath, OpenSceneMode.Additive);

            openedMuseum = true;
        }

        PlayerController player = FindIn<PlayerController>(museum);

        if (player == null)
        {
            return "No player in " + MuseumPath + " - build the museum first.";
        }

        Scene test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        Scene previousActive = SceneManager.GetActiveScene();

        SceneManager.SetActiveScene(test);

        BuildStage();

        string[] desks = { "Mesh_Table_carre" };

        int slot = 0;

        foreach (string model in desks)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BgpPrefabs + model + ".prefab");

            if (prefab == null)
            {
                report.AppendLine("MISSING " + model);

                continue;
            }

            slot++;
            DeskSlot(prefab, new Vector3((slot - 1) * 6f, 0f, 0f), slot, model + " - open floor", wallBehind: false);
            report.AppendLine(slot + ". " + model + ", open floor");

            slot++;
            DeskSlot(prefab, new Vector3((slot - 1) * 6f, 0f, 0f), slot, model + " - against a wall", wallBehind: true);
            report.AppendLine(slot + ". " + model + ", against a wall");
        }

        /* The player, copied in, a few steps back and facing the desks. */

        GameObject rig = Object.Instantiate(player.transform.root.gameObject);

        rig.name = player.transform.root.name;

        SceneManager.MoveGameObjectToScene(rig, test);

        rig.GetComponentInChildren<PlayerController>().transform.SetPositionAndRotation(
            new Vector3((slot - 1) * 3f, 0.05f, -5f), Quaternion.identity);

        foreach (Camera cam in rig.GetComponentsInChildren<Camera>(true))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.58f, 0.62f);
        }

        EditorSceneManager.SaveScene(test, ScenePath);

        SceneManager.SetActiveScene(previousActive);

        if (openedMuseum)
        {
            EditorSceneManager.CloseScene(museum, true);
        }

        EditorSceneManager.CloseScene(test, true);

        AddToBuildSettings();

        return "Built " + ScenePath + " - " + slot + " desk setups:\n" + report;
    }


    /// <summary>
    /// One desk with a hiding spot under it. The player crouches under
    /// the middle, tucked back a little so the near edge of the top
    /// frames the view, facing out of the long side towards -Z.
    /// </summary>
    private static void DeskSlot(GameObject prefab, Vector3 at, int number, string title, bool wallBehind)
    {
        GameObject root = new GameObject(number.ToString("00") + " " + title);

        GameObject desk = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);

        desk.transform.SetParent(root.transform, false);
        desk.transform.SetPositionAndRotation(at, Quaternion.identity);

        Bounds b = RenderBounds(desk);

        /* Sit it on the floor whatever its pivot. */

        desk.transform.position += Vector3.up * (at.y - b.min.y);

        b = RenderBounds(desk);

        /*
         * The pack's tabletop is single-sided - modelled to be seen from
         * above. From under it the top is simply not drawn, so desks get
         * a double-sided copy of their own material.
         */

        foreach (Renderer r in desk.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null)
                {
                    mats[i] = DoubleSided(mats[i]);
                }
            }

            r.sharedMaterials = mats;
        }

        if (wallBehind)
        {
            Box(root.transform, "Wall", new Vector3(b.center.x, 1.5f, b.max.z + 0.12f),
                new Vector3(4f, 3f, 0.2f), new Color(0.78f, 0.78f, 0.76f));
        }

        GameObject spotGo = new GameObject("Hiding Spot - Under the Desk");

        spotGo.transform.SetParent(root.transform, false);

        spotGo.transform.SetPositionAndRotation(
            new Vector3(b.center.x, at.y, b.center.z + HidingSpot.DeskTuckBack),
            Quaternion.Euler(0f, 180f, 0f));

        HidingSpot spot = spotGo.AddComponent<HidingSpot>();

        spot.ConfigureDesk();

        spotGo.AddComponent<InteractionMarker>()
            .Setup(InteractionMarker.Kind.Hide, null, 1.3f);

        /* The card stands off to the side, out of the hiding view. */

        Label(root.transform, new Vector3(b.max.x + 1.3f, 0f, b.min.z - 0.4f), number, title);
    }


    /// <summary>
    /// A double-sided copy of a material, saved under Assets/Museum/Materials
    /// and refreshed on every build.
    /// </summary>
    public static Material DoubleSided(Material source)
    {
        const string dir = "Assets/Museum/Materials";

        string path = dir + "/Double Sided " + source.name + ".mat";

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

        if (mat.HasProperty("_Cull"))
        {
            mat.SetFloat("_Cull", (float)CullMode.Off);
        }

        mat.doubleSidedGI = true;

        EditorUtility.SetDirty(mat);

        return mat;
    }


    /// <summary>A plain grey floor, a sun and flat light - nothing to hide the camera's mistakes.</summary>
    private static void BuildStage()
    {
        Box(null, "Floor", new Vector3(6f, -0.1f, 0f), new Vector3(40f, 0.2f, 30f), new Color(0.42f, 0.43f, 0.45f));

        GameObject sun = new GameObject("Sun");

        Light light = sun.AddComponent<Light>();

        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.shadows = LightShadows.Soft;

        sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.56f);
        RenderSettings.fog = false;
    }


    private static void Box(Transform parent, string name, Vector3 center, Vector3 size, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);

        box.name = name;

        if (parent != null)
        {
            box.transform.SetParent(parent, false);
        }

        box.transform.position = center;
        box.transform.localScale = size;

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        mat.SetColor("_BaseColor", color);

        box.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }


    /// <summary>A numbered card on the floor in front of the slot, facing the player.</summary>
    private static void Label(Transform parent, Vector3 at, int number, string title)
    {
        GameObject card = new GameObject("Label");

        card.transform.SetParent(parent, false);

        card.transform.SetPositionAndRotation(at + Vector3.up * 1.1f, Quaternion.Euler(0f, 180f, 0f));

        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);

        Object.DestroyImmediate(plate.GetComponent<Collider>());

        plate.name = "Plate";
        plate.transform.SetParent(card.transform, false);
        plate.transform.localScale = new Vector3(1.8f, 0.5f, 0.04f);

        Material pale = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        pale.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.9f));

        plate.GetComponent<MeshRenderer>().sharedMaterial = pale;

        card.AddComponent<MuseumSign>().Configure(false, 1.6f, 0.025f, new[]
        {
            new MuseumSign.Line("#" + number, 0.2f, new Color(0.07f, 0.07f, 0.07f), FontStyle.Bold),
            new MuseumSign.Line(title, 0.11f, new Color(0.2f, 0.2f, 0.2f)),
        });
    }


    private static Bounds RenderBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);

        Bounds b = renderers[0].bounds;

        foreach (Renderer r in renderers)
        {
            b.Encapsulate(r.bounds);
        }

        return b;
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

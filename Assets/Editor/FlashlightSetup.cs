using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns the imported flashlight model (Assets/Museum/Flashlight, converted
/// from the supplied glb) into a held-item prefab and gives the player a
/// Flashlight that holds it.
///
/// Tools > AI Director > Flashlight - Set Up, or FlashlightSetup.Run()
/// from the CLI. MuseumMapBuilder calls EnsureOnPlayer on every build.
/// </summary>
public static class FlashlightSetup
{
    private const string Dir = "Assets/Museum/Flashlight";

    private const string ModelPath = Dir + "/flashlight.obj";

    private const string TexturePath = Dir + "/flashlight_albedo.png";

    private const string MaterialPath = Dir + "/Flashlight.mat";

    private const string PrefabPath = Dir + "/Flashlight.prefab";

    /// <summary>The model is 0.63 m long; a real flashlight is about 0.25 m.</summary>
    private const float TargetLength = 0.25f;


    [MenuItem("Tools/AI Director/Flashlight - Set Up", priority = -70)]
    public static void RunFromMenu()
    {
        EditorUtility.DisplayDialog("Flashlight", Run(), "OK");
    }


    public static string Run()
    {
        GameObject prefab = BuildPrefab();

        if (prefab == null)
            return "Could not build the flashlight prefab - is " + ModelPath + " imported?";

        int players = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerController player in root.GetComponentsInChildren<PlayerController>(true))
                {
                    EnsureOnPlayer(player, reset: true);

                    players++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);

            EditorSceneManager.SaveScene(scene);
        }

        return "Flashlight prefab at " + PrefabPath + "; given to " + players + " player(s).";
    }


    /// <summary>
    /// Gives a player a Flashlight holding the prefab. With
    /// <paramref name="reset"/> an existing one is replaced, so changed
    /// script defaults reach it; a rebuild never resets, keeping tuning.
    /// </summary>
    public static void EnsureOnPlayer(PlayerController player, bool reset = false)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? BuildPrefab();

        Flashlight flashlight = player.GetComponent<Flashlight>();

        if (flashlight != null && reset)
        {
            Object.DestroyImmediate(flashlight);

            flashlight = null;
        }

        if (flashlight == null)
        {
            flashlight = player.gameObject.AddComponent<Flashlight>();
        }

        flashlight.SetModel(prefab);

        EditorUtility.SetDirty(flashlight);
    }


    /// <summary>
    /// The held prefab: the mesh scaled to a real flashlight's length,
    /// its lamp head turned to +Z (the converted mesh has it at -Z), and
    /// the grip on the origin.
    /// </summary>
    private static GameObject BuildPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        if (model == null)
            return null;

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            AssetDatabase.CreateAsset(mat, MaterialPath);
        }

        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Metallic", 0.4f);
        mat.SetFloat("_Smoothness", 0.45f);

        EditorUtility.SetDirty(mat);

        GameObject root = new GameObject("Flashlight");

        GameObject mesh = (GameObject)PrefabUtility.InstantiatePrefab(model);

        PrefabUtility.UnpackPrefabInstance(mesh, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        mesh.name = "Mesh";

        mesh.transform.SetParent(root.transform, false);

        foreach (Renderer r in mesh.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterial = mat;
        }

        foreach (Collider c in mesh.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(c);
        }

        /* Head forward, scaled, then centred so the holder grips its middle. */

        mesh.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        Bounds b = Bounds(mesh);

        float scale = TargetLength / Mathf.Max(b.size.x, b.size.y, b.size.z);

        mesh.transform.localScale = Vector3.one * scale;

        b = Bounds(mesh);

        mesh.transform.localPosition -= b.center;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

        Object.DestroyImmediate(root);

        return prefab;
    }


    private static Bounds Bounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);

        Bounds b = renderers[0].bounds;

        foreach (Renderer r in renderers)
        {
            b.Encapsulate(r.bounds);
        }

        return b;
    }
}

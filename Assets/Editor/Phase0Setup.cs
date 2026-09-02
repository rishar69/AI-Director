using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click scene setup for Phase 0.
///
/// Everything here is something you could do by hand in the inspector.
/// It exists so the wiring is reproducible and so a fresh scene can be
/// brought up to a testable state without a fifteen step checklist.
/// </summary>
public static class Phase0Setup
{
    private const string InputAssetPath =
        "Assets/InputSystem_Actions.inputactions";

    private const string MapPrefabPath =
        "Assets/AngeloMaN87/Prototype Map/Prefabs/PrototypeMap.prefab";


    // =========================================================
    // MENU
    // =========================================================

    [MenuItem("Tools/AI Director/Phase 0 - Setup Scene", priority = 0)]
    public static void SetupScene()
    {
        int added = AddMissingLayers();

        GameObject player = BuildPlayerRig();

        GameObject overlay = BuildDebugOverlay();

        GameObject surface = EnsureNavMeshSurface();

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        if (player != null)
        {
            Selection.activeGameObject = player;
        }

        string report =
            "Layers added: " + added + "\n" +
            "Player rig: " + Describe(player) + "\n" +
            "Debug overlay: " + Describe(overlay) + "\n" +
            "NavMesh surface: " + Describe(surface) + "\n\n" +
            "Remaining manual step:\n" +
            "Select the NavMesh object and press Bake in the " +
            "NavMeshSurface inspector.";

        Debug.Log("[Phase0Setup]\n" + report);

        EditorUtility.DisplayDialog(
            "Phase 0 Setup",
            report,
            "OK"
        );
    }


    private static string Describe(GameObject go)
    {
        return go != null
            ? go.name
            : "skipped (already present or unavailable)";
    }


    // =========================================================
    // LAYERS
    // =========================================================

    [MenuItem("Tools/AI Director/Phase 0 - Add Missing Layers",
        priority = 20)]
    public static int AddMissingLayers()
    {
        Object[] assets =
            AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TagManager.asset"
            );

        if (assets == null || assets.Length == 0)
        {
            Debug.LogError(
                "[Phase0Setup] Could not open TagManager.asset."
            );

            return 0;
        }

        SerializedObject tagManager =
            new SerializedObject(assets[0]);

        SerializedProperty layers =
            tagManager.FindProperty("layers");

        int added = 0;

        added += AddLayer(layers, GameLayers.VentName);
        added += AddLayer(layers, GameLayers.HidespotName);

        if (added > 0)
        {
            tagManager.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
        }

        return added;
    }


    private static int AddLayer(
        SerializedProperty layers,
        string layerName)
    {
        /*
         * Never reorder or overwrite. Existing layer indices are baked
         * into every collider in the project.
         */

        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue ==
                layerName)
            {
                return 0;
            }
        }

        /*
         * Slots 0-7 are reserved by Unity even when they read as empty.
         */

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot =
                layers.GetArrayElementAtIndex(i);

            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;

                Debug.Log(
                    "[Phase0Setup] Added layer '" + layerName +
                    "' at index " + i
                );

                return 1;
            }
        }

        Debug.LogWarning(
            "[Phase0Setup] No free layer slot for '" + layerName + "'."
        );

        return 0;
    }


    // =========================================================
    // PLAYER RIG
    // =========================================================

    [MenuItem("Tools/AI Director/Phase 0 - Build Player Rig",
        priority = 21)]
    public static GameObject BuildPlayerRig()
    {
        if (Object.FindFirstObjectByType<PlayerController>() != null)
        {
            Debug.Log(
                "[Phase0Setup] A PlayerController already exists in " +
                "the scene. Skipping player rig."
            );

            return null;
        }

        GameObject player = new GameObject("Player");

        Undo.RegisterCreatedObjectUndo(player, "Build Player Rig");

        int playerLayer = LayerMask.NameToLayer(GameLayers.PlayerName);

        if (playerLayer >= 0)
        {
            player.layer = playerLayer;
        }

        player.tag = "Player";

        player.transform.position = FindSpawnPoint();


        // -----------------------------------------------------
        // CHARACTER CONTROLLER
        // -----------------------------------------------------

        CharacterController controller =
            player.AddComponent<CharacterController>();

        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.slopeLimit = 45f;
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.02f;


        // -----------------------------------------------------
        // CAMERA
        // -----------------------------------------------------

        Camera camera = AcquireCamera();

        camera.transform.SetParent(player.transform, false);

        camera.transform.localPosition =
            new Vector3(0f, 1.65f, 0f);

        camera.transform.localRotation = Quaternion.identity;

        camera.nearClipPlane = 0.05f;

        if (camera.GetComponent<AudioListener>() == null)
        {
            camera.gameObject.AddComponent<AudioListener>();
        }


        // -----------------------------------------------------
        // COMPONENTS
        // -----------------------------------------------------

        PlayerController movement =
            player.AddComponent<PlayerController>();

        player.AddComponent<PlayerNoiseEmitter>();

        WirePlayerController(movement, camera.transform, playerLayer);

        return player;
    }


    /// <summary>
    /// Reuses the scene's existing Main Camera if there is one, so the
    /// scene does not end up with two cameras and two AudioListeners.
    /// </summary>
    private static Camera AcquireCamera()
    {
        Camera existing = Camera.main;

        if (existing != null)
        {
            Undo.RecordObject(
                existing.transform,
                "Reparent Camera"
            );

            existing.gameObject.name = "PlayerCamera";

            return existing;
        }

        GameObject created = new GameObject("PlayerCamera");

        Undo.RegisterCreatedObjectUndo(created, "Create Camera");

        created.tag = "MainCamera";

        return created.AddComponent<Camera>();
    }


    /// <summary>
    /// Assigns the private serialized fields the component cannot
    /// discover for itself.
    /// </summary>
    private static void WirePlayerController(
        PlayerController movement,
        Transform cameraPivot,
        int playerLayer)
    {
        SerializedObject so = new SerializedObject(movement);

        InputActionAsset inputAsset =
            AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                InputAssetPath
            );

        if (inputAsset != null)
        {
            so.FindProperty("inputActions").objectReferenceValue =
                inputAsset;
        }
        else
        {
            Debug.LogWarning(
                "[Phase0Setup] Could not find " + InputAssetPath +
                ". Assign Input Actions on the Player by hand."
            );
        }

        so.FindProperty("cameraPivot").objectReferenceValue =
            cameraPivot;

        /*
         * The stand-up check must not see the player's own capsule or
         * anything on Ignore Raycast, or crouch becomes a one way trip.
         */

        int mask = ~0;

        if (playerLayer >= 0)
        {
            mask &= ~(1 << playerLayer);
        }

        mask &= ~(1 << 2);

        so.FindProperty("ceilingMask").intValue = mask;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    /// <summary>
    /// Drops the player slightly above the origin, or above whatever
    /// geometry sits under the origin, so they do not spawn inside the
    /// floor and fall through.
    /// </summary>
    private static Vector3 FindSpawnPoint()
    {
        Vector3 probe = new Vector3(0f, 50f, 0f);

        if (Physics.Raycast(
                probe,
                Vector3.down,
                out RaycastHit hit,
                200f))
        {
            return hit.point + Vector3.up * 0.1f;
        }

        return new Vector3(0f, 1f, 0f);
    }


    // =========================================================
    // DEBUG OVERLAY
    // =========================================================

    private static GameObject BuildDebugOverlay()
    {
        if (Object.FindFirstObjectByType<AIDebugOverlay>() != null)
            return null;

        GameObject overlay = new GameObject("AI Debug");

        Undo.RegisterCreatedObjectUndo(overlay, "Create Debug Overlay");

        overlay.AddComponent<AIDebugOverlay>();

        return overlay;
    }


    // =========================================================
    // NAVMESH
    // =========================================================

    /// <summary>
    /// Adds a NavMeshSurface covering the whole scene.
    ///
    /// Deliberately does not bake. Baking from script produces NavMesh
    /// data that is not written to disk, which silently disappears on
    /// the next scene load. Pressing Bake in the inspector does it
    /// properly.
    /// </summary>
    private static GameObject EnsureNavMeshSurface()
    {
        NavMeshSurface existing =
            Object.FindFirstObjectByType<NavMeshSurface>();

        if (existing != null)
            return null;

        EnsureMapInScene();

        GameObject holder = new GameObject("NavMesh");

        Undo.RegisterCreatedObjectUndo(holder, "Create NavMesh Surface");

        NavMeshSurface surface =
            holder.AddComponent<NavMeshSurface>();

        surface.collectObjects = CollectObjects.All;

        surface.useGeometry =
            UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

        return holder;
    }


    private static void EnsureMapInScene()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);

        if (prefab == null)
            return;

        /*
         * Only place the map if the scene has no renderers worth
         * walking on yet - re-adding it every run would stack copies.
         */

        if (Object.FindFirstObjectByType<MeshRenderer>() != null)
            return;

        GameObject instance =
            (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        Undo.RegisterCreatedObjectUndo(instance, "Place Prototype Map");

        instance.transform.position = Vector3.zero;
    }
}

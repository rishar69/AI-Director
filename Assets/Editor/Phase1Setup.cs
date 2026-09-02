using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a sensor test rig for Phase 1.
///
/// Not the xenomorph - a stationary placeholder that can see, hear and
/// smell, so the sense stack can be tuned before any locomotion or
/// behaviour exists to muddy the picture. Phase 2 replaces the capsule
/// with the real model and gives it a NavMeshAgent.
/// </summary>
public static class Phase1Setup
{
    private const float SensorHeight = 1.6f;


    // =========================================================
    // MENU
    // =========================================================

    [MenuItem("Tools/AI Director/Phase 1 - Build Sensor Test Rig",
        priority = 40)]
    public static void BuildSensorRig()
    {
        if (Object.FindFirstObjectByType<PerceptionSystem>() != null)
        {
            EditorUtility.DisplayDialog(
                "Phase 1 Setup",
                "A PerceptionSystem already exists in this scene.\n\n" +
                "Delete it first if you want a fresh rig.",
                "OK"
            );

            return;
        }

        int npcLayer = LayerMask.NameToLayer(GameLayers.NpcName);

        int playerLayer = LayerMask.NameToLayer(GameLayers.PlayerName);

        if (playerLayer < 0)
        {
            EditorUtility.DisplayDialog(
                "Phase 1 Setup",
                "No 'Player' layer exists. Run Phase 0 setup first.",
                "OK"
            );

            return;
        }

        LayerMask targetMask = 1 << playerLayer;

        LayerMask obstacleMask = BuildObstacleMask(npcLayer, playerLayer);


        // -----------------------------------------------------
        // ROOT
        // -----------------------------------------------------

        GameObject rig = new GameObject("Xenomorph (Sensor Rig)");

        Undo.RegisterCreatedObjectUndo(rig, "Build Sensor Rig");

        rig.transform.position = ChooseSpawnPoint();

        BuildBody(rig);

        BuildConeVisual(rig);


        // -----------------------------------------------------
        // SENSES
        // -----------------------------------------------------

        PerceptionSystem perception =
            rig.AddComponent<PerceptionSystem>();

        VisionSense vision = rig.AddComponent<VisionSense>();

        HearingSense hearing = rig.AddComponent<HearingSense>();

        ProximitySense proximity = rig.AddComponent<ProximitySense>();

        ConfigureSense(vision, targetMask, obstacleMask);

        ConfigureSense(hearing, targetMask, obstacleMask);

        ConfigureSense(proximity, targetMask, obstacleMask);


        // -----------------------------------------------------
        // LAYERS
        // -----------------------------------------------------

        /*
         * Every part of the rig has to be on the NPC layer. The
         * obstacle mask excludes NPC, so if the placeholder body were
         * left on Default the creature's own eye ray would start
         * inside its own collider and report itself as a wall.
         */

        if (npcLayer >= 0)
        {
            SetLayerRecursively(rig, npcLayer);
        }

        WireVisualizer(rig, vision, perception);

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        Selection.activeGameObject = rig;

        Debug.Log(
            "[Phase1Setup] Sensor rig built at " +
            rig.transform.position + ". Press Play and walk into it.",
            rig
        );
    }


    // =========================================================
    // MASKS
    // =========================================================

    /// <summary>
    /// Everything that should block sight, which is everything except
    /// the creatures themselves and Unity's utility layers.
    /// </summary>
    private static LayerMask BuildObstacleMask(
        int npcLayer,
        int playerLayer)
    {
        int mask = ~0;

        if (npcLayer >= 0)
        {
            mask &= ~(1 << npcLayer);
        }

        mask &= ~(1 << playerLayer);

        /* Ignore Raycast */
        mask &= ~(1 << 2);

        /* TransparentFX */
        mask &= ~(1 << 1);

        /* UI */
        mask &= ~(1 << 5);

        return mask;
    }


    // =========================================================
    // PARTS
    // =========================================================

    private static void BuildBody(GameObject rig)
    {
        GameObject body =
            GameObject.CreatePrimitive(PrimitiveType.Capsule);

        body.name = "Body (placeholder)";

        body.transform.SetParent(rig.transform, false);

        body.transform.localPosition = new Vector3(0f, 1f, 0f);

        /*
         * The collider is removed rather than kept. Phase 1 only needs
         * something visible to aim at; a collider here would start
         * interfering with the player's own stand-up check.
         */

        Collider collider = body.GetComponent<Collider>();

        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }
    }


    private static void BuildConeVisual(GameObject rig)
    {
        GameObject cone = new GameObject("Vision Cone");

        cone.transform.SetParent(rig.transform, false);

        cone.transform.localPosition = Vector3.zero;

        cone.AddComponent<MeshFilter>();

        cone.AddComponent<MeshRenderer>();

        cone.AddComponent<VisionConeVisualizer>();
    }


    private static void WireVisualizer(
        GameObject rig,
        VisionSense vision,
        PerceptionSystem perception)
    {
        VisionConeVisualizer visualizer =
            rig.GetComponentInChildren<VisionConeVisualizer>();

        if (visualizer == null)
            return;

        SerializedObject so = new SerializedObject(visualizer);

        so.FindProperty("visionSense").objectReferenceValue = vision;

        so.FindProperty("perception").objectReferenceValue = perception;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    // =========================================================
    // SENSE CONFIG
    // =========================================================

    /// <summary>
    /// Assigns the masks and sensor height, which are protected fields
    /// on the Sense base class and so cannot be set from code directly.
    /// </summary>
    private static void ConfigureSense(
        Sense sense,
        LayerMask targetMask,
        LayerMask obstacleMask)
    {
        SerializedObject so = new SerializedObject(sense);

        so.FindProperty("targetMask").intValue = targetMask.value;

        so.FindProperty("obstacleMask").intValue = obstacleMask.value;

        so.FindProperty("sensorHeight").floatValue = SensorHeight;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    // =========================================================
    // HELPERS
    // =========================================================

    /// <summary>
    /// Places the rig a short way in front of the player so it is
    /// immediately in view when you press Play.
    /// </summary>
    private static Vector3 ChooseSpawnPoint()
    {
        PlayerController player =
            Object.FindFirstObjectByType<PlayerController>();

        Vector3 origin =
            player != null
                ? player.transform.position +
                  player.transform.forward * 8f
                : Vector3.zero;

        Vector3 probe = origin + Vector3.up * 50f;

        if (Physics.Raycast(
                probe,
                Vector3.down,
                out RaycastHit hit,
                200f))
        {
            return hit.point;
        }

        return origin;
    }


    private static void SetLayerRecursively(
        GameObject target,
        int layer)
    {
        target.layer = layer;

        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}

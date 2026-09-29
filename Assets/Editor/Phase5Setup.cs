using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Fits the player with everything they can fight back with.
/// </summary>
public static class Phase5Setup
{
    [MenuItem("Tools/AI Director/Phase 5 - Install Counterplay",
        priority = 120)]
    public static void InstallCounterplay()
    {
        PlayerController player =
            Object.FindFirstObjectByType<PlayerController>();

        if (player == null)
        {
            EditorUtility.DisplayDialog(
                "Phase 5 Setup",
                "No player in the scene. Run Phase 0 setup first.",
                "OK"
            );

            return;
        }

        GameObject target = player.gameObject;

        StringBuilder report = new StringBuilder();

        Undo.RegisterFullObjectHierarchyUndo(
            target,
            "Install Counterplay"
        );

        AddIfMissing<Concealment>(target, report);

        AddIfMissing<PlayerHiding>(target, report);

        AddIfMissing<PlayerThrower>(target, report);

        AddIfMissing<PlayerDeterrent>(target, report);

        AddIfMissing<MotionTracker>(target, report);

        ConfigureMasks(target, report);

        report.AppendLine();

        report.AppendLine("CONTROLS");
        report.AppendLine("  E  hide / leave cover");
        report.AppendLine("  W  lean out and peek (while hidden)");
        report.AppendLine("  F  flamethrower (hold)");
        report.AppendLine("  G  throw noisemaker");
        report.AppendLine("  T  motion tracker on / off");

        report.AppendLine();

        report.AppendLine(
            "No hiding spots exist yet - use Tools > AI Director > " +
            "Create Hiding Spot At View to place some."
        );

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        Selection.activeGameObject = target;

        string text = report.ToString();

        Debug.Log("[Phase5Setup]\n" + text, target);

        EditorUtility.DisplayDialog("Phase 5 Setup", text, "OK");
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private static void AddIfMissing<T>(
        GameObject target,
        StringBuilder report)
        where T : Component
    {
        if (target.GetComponent<T>() != null)
        {
            report.AppendLine(typeof(T).Name + " already present.");

            return;
        }

        Undo.AddComponent<T>(target);

        report.AppendLine("Added " + typeof(T).Name + ".");
    }


    /// <summary>
    /// Points the creature-seeking tools at the NPC layer.
    ///
    /// Left at Everything, the flamethrower would sweep the player's
    /// own collider and the tracker would light up with scenery.
    /// </summary>
    private static void ConfigureMasks(
        GameObject target,
        StringBuilder report)
    {
        int npcLayer = LayerMask.NameToLayer(GameLayers.NpcName);

        if (npcLayer < 0)
        {
            report.AppendLine(
                "WARNING: no NPC layer, so tool masks were left alone."
            );

            return;
        }

        int mask = 1 << npcLayer;

        SetMask(target.GetComponent<PlayerDeterrent>(),
            "creatureMask", mask);

        SetMask(target.GetComponent<MotionTracker>(),
            "creatureMask", mask);

        report.AppendLine("Set tool masks to the NPC layer.");
    }


    private static void SetMask(
        Component component,
        string property,
        int mask)
    {
        if (component == null)
            return;

        SerializedObject so = new SerializedObject(component);

        SerializedProperty found = so.FindProperty(property);

        if (found == null)
            return;

        found.intValue = mask;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    // =========================================================
    // AUTHORING
    // =========================================================

    [MenuItem("Tools/AI Director/Create Hiding Spot At View",
        priority = 121)]
    public static void CreateHidingSpot()
    {
        GameObject spot = new GameObject("Hiding Spot");

        Undo.RegisterCreatedObjectUndo(spot, "Create Hiding Spot");

        spot.AddComponent<HidingSpot>();

        SceneView view = SceneView.lastActiveSceneView;

        Vector3 position =
            view != null ? view.pivot : Vector3.zero;

        if (NavMesh.SamplePosition(
                position,
                out NavMeshHit hit,
                12f,
                NavMesh.AllAreas))
        {
            position = hit.position;
        }

        spot.transform.position = position;

        Selection.activeGameObject = spot;

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );
    }
}

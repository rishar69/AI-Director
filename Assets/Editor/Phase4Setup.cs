using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs the AI Director and wires the creature to receive hints.
/// </summary>
public static class Phase4Setup
{
    [MenuItem("Tools/AI Director/Phase 4 - Install Director",
        priority = 100)]
    public static void InstallDirector()
    {
        XenoBrain brain = Object.FindFirstObjectByType<XenoBrain>();

        if (brain == null)
        {
            EditorUtility.DisplayDialog(
                "Phase 4 Setup",
                "No XenoBrain in the scene. Run Phase 3 setup first.",
                "OK"
            );

            return;
        }

        StringBuilder report = new StringBuilder();

        InstallReceiver(brain, report);

        ZoneGraph zones = InstallDirectorObject(report);

        CheckNavMesh(zones, report);

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        string text = report.ToString();

        Debug.Log("[Phase4Setup]\n" + text);

        EditorUtility.DisplayDialog("Phase 4 Setup", text, "OK");
    }


    // =========================================================
    // STEPS
    // =========================================================

    private static void InstallReceiver(
        XenoBrain brain,
        StringBuilder report)
    {
        if (brain.GetComponent<DirectorHintReceiver>() != null)
        {
            report.AppendLine("Hint receiver already on the creature.");

            return;
        }

        Undo.AddComponent<DirectorHintReceiver>(brain.gameObject);

        report.AppendLine(
            "Added DirectorHintReceiver to the creature."
        );

        report.AppendLine(
            "XenoBrain picks this up automatically at Awake."
        );
    }


    private static ZoneGraph InstallDirectorObject(
        StringBuilder report)
    {
        AIDirector existing =
            Object.FindFirstObjectByType<AIDirector>();

        if (existing != null)
        {
            report.AppendLine("AIDirector already in the scene.");

            return existing.GetComponent<ZoneGraph>();
        }

        GameObject host = new GameObject("AI Director");

        Undo.RegisterCreatedObjectUndo(host, "Create AI Director");

        ZoneGraph zones = host.AddComponent<ZoneGraph>();

        host.AddComponent<AIDirector>();

        report.AppendLine(
            "Created 'AI Director' with ZoneGraph + AIDirector."
        );

        report.AppendLine(
            "It finds the player, camera and zones itself at Awake."
        );

        return zones;
    }


    private static void CheckNavMesh(
        ZoneGraph zones,
        StringBuilder report)
    {
        NavMeshTriangulation triangulation =
            NavMesh.CalculateTriangulation();

        bool baked =
            triangulation.vertices != null &&
            triangulation.vertices.Length > 0;

        report.AppendLine();

        if (!baked)
        {
            report.AppendLine(
                "WARNING: no NavMesh is baked, so the ZoneGraph will " +
                "build zero zones and the Director will have nowhere " +
                "to hint. Bake first."
            );

            return;
        }

        report.AppendLine(
            "NavMesh present - zones build at Play from " +
            triangulation.vertices.Length + " vertices."
        );

        report.AppendLine();

        report.AppendLine(
            "Optional: drop VentPoint components around the level. " +
            "The Director prefers them when it needs the creature " +
            "genuinely gone. Without any, it relocates to a distant " +
            "zone instead."
        );
    }


    // =========================================================
    // RETUNE
    // =========================================================

    /// <summary>
    /// Applies the corrected menace rates to a Director already in the
    /// scene.
    ///
    /// Needed because MenaceGauge's pressure is now normalised to 0..1,
    /// but a Director saved before that change still carries rates
    /// tuned for the old unbounded scale - and serialized values beat
    /// field defaults, so editing the script alone fixes nothing.
    /// </summary>
    [MenuItem("Tools/AI Director/Retune Director Menace",
        priority = 102)]
    public static void RetuneMenace()
    {
        AIDirector director =
            Object.FindFirstObjectByType<AIDirector>();

        if (director == null)
        {
            EditorUtility.DisplayDialog(
                "Retune Menace",
                "No AIDirector in the scene.",
                "OK"
            );

            return;
        }

        SerializedObject so = new SerializedObject(director);

        SerializedProperty rise =
            so.FindProperty("menace.riseRate");

        SerializedProperty decay =
            so.FindProperty("menace.decayRate");

        if (rise == null || decay == null)
        {
            EditorUtility.DisplayDialog(
                "Retune Menace",
                "Could not find the menace rate properties.",
                "OK"
            );

            return;
        }

        float oldRise = rise.floatValue;

        float oldDecay = decay.floatValue;

        rise.floatValue = 0.05f;

        decay.floatValue = 0.035f;

        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        string message =
            "riseRate  " + oldRise + " -> 0.05\n" +
            "decayRate " + oldDecay + " -> 0.035\n\n" +
            "A stalk at close range now takes roughly 30 seconds to " +
            "trip the push-away threshold, instead of about one.";

        Debug.Log("[Phase4Setup] " + message, director);

        EditorUtility.DisplayDialog("Retune Menace", message, "OK");
    }


    // =========================================================
    // VENTS
    // =========================================================

    [MenuItem("Tools/AI Director/Create Vent Point At View",
        priority = 101)]
    public static void CreateVentPoint()
    {
        GameObject vent = new GameObject("Vent Point");

        Undo.RegisterCreatedObjectUndo(vent, "Create Vent Point");

        vent.AddComponent<VentPoint>();

        SceneView view = SceneView.lastActiveSceneView;

        Vector3 position =
            view != null
                ? view.pivot
                : Vector3.zero;

        if (NavMesh.SamplePosition(
                position,
                out NavMeshHit hit,
                12f,
                NavMesh.AllAreas))
        {
            position = hit.position;
        }

        vent.transform.position = position;

        Selection.activeGameObject = vent;

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );
    }
}

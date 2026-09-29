using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Final polish pass: progression, audio, the real death sequence and
/// vent routing.
/// </summary>
public static class Phase6Setup
{
    [MenuItem("Tools/AI Director/Phase 6 - Install Polish",
        priority = 140)]
    public static void InstallPolish()
    {
        XenoBrain brain = Object.FindFirstObjectByType<XenoBrain>();

        if (brain == null)
        {
            EditorUtility.DisplayDialog(
                "Phase 6 Setup",
                "No XenoBrain in the scene. Run Phase 3 setup first.",
                "OK"
            );

            return;
        }

        StringBuilder report = new StringBuilder();

        Undo.RegisterFullObjectHierarchyUndo(
            brain.gameObject,
            "Install Polish"
        );

        AddIfMissing<XenoProgression>(brain.gameObject, report);

        AddIfMissing<XenoAudio>(brain.gameObject, report);

        UpgradeDeathHandling(report);

        ReportVents(report);

        report.AppendLine();

        report.AppendLine(
            "No audio clips exist in the project, so XenoAudio is " +
            "silent until footstep, vocalisation and vent clips are " +
            "assigned in its inspector."
        );

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        Selection.activeGameObject = brain.gameObject;

        string text = report.ToString();

        Debug.Log("[Phase6Setup]\n" + text, brain);

        EditorUtility.DisplayDialog("Phase 6 Setup", text, "OK");
    }


    // =========================================================
    // STEPS
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
    /// Swaps the Phase 3 placeholder for the real sequence.
    /// </summary>
    private static void UpgradeDeathHandling(StringBuilder report)
    {
        PlayerController player =
            Object.FindFirstObjectByType<PlayerController>();

        if (player == null)
        {
            report.AppendLine(
                "WARNING: no player found, death sequence not added."
            );

            return;
        }

        PlayerKillPlaceholder placeholder =
            player.GetComponent<PlayerKillPlaceholder>();

        if (placeholder != null)
        {
            /*
             * Both would subscribe to OnAttack and the player would
             * die twice, so the placeholder has to go before the real
             * one arrives.
             */

            Undo.DestroyObjectImmediate(placeholder);

            report.AppendLine(
                "Removed PlayerKillPlaceholder (superseded)."
            );

            report.AppendLine(
                "Its script file is now unused and safe to delete."
            );
        }

        if (player.GetComponent<PlayerDeathSequence>() == null)
        {
            Undo.AddComponent<PlayerDeathSequence>(player.gameObject);

            report.AppendLine(
                "Added PlayerDeathSequence (camera turn, fade, " +
                "respawn)."
            );
        }
        else
        {
            report.AppendLine("PlayerDeathSequence already present.");
        }
    }


    private static void ReportVents(StringBuilder report)
    {
        VentPoint[] vents =
            Object.FindObjectsByType<VentPoint>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        report.AppendLine();

        if (vents.Length < 2)
        {
            report.AppendLine(
                "Only " + vents.Length + " vent point(s) in the scene. " +
                "Place at least two, then use " +
                "Tools > AI Director > Link Selected Vents to make a " +
                "route the creature can path through."
            );

            return;
        }

        int linked = 0;

        foreach (VentPoint vent in vents)
        {
            if (vent.GetComponent<VentLink>() != null)
            {
                linked++;
            }
        }

        report.AppendLine(
            vents.Length + " vent points, " + linked + " linked."
        );
    }


    // =========================================================
    // VENT AUTHORING
    // =========================================================

    [MenuItem("Tools/AI Director/Link Selected Vents", priority = 141)]
    public static void LinkSelectedVents()
    {
        GameObject[] selection = Selection.gameObjects;

        if (selection.Length != 2)
        {
            EditorUtility.DisplayDialog(
                "Link Vents",
                "Select exactly two VentPoint objects.",
                "OK"
            );

            return;
        }

        VentPoint a = selection[0].GetComponent<VentPoint>();

        VentPoint b = selection[1].GetComponent<VentPoint>();

        if (a == null || b == null)
        {
            EditorUtility.DisplayDialog(
                "Link Vents",
                "Both selected objects need a VentPoint component.",
                "OK"
            );

            return;
        }

        VentLink link = a.GetComponent<VentLink>();

        if (link == null)
        {
            link = Undo.AddComponent<VentLink>(a.gameObject);
        }

        SerializedObject so = new SerializedObject(link);

        so.FindProperty("destination").objectReferenceValue = b;

        so.ApplyModifiedProperties();

        link.Rebuild();

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        Debug.Log(
            "[Phase6Setup] Linked " + a.name + " <-> " + b.name,
            link
        );
    }
}

using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Swaps the Phase 2 test driver for the real brain.
///
/// Upgrades the existing rig in place so all the sense and locomotion
/// tuning done in earlier phases survives.
/// </summary>
public static class Phase3Setup
{
    [MenuItem("Tools/AI Director/Phase 3 - Install Brain", priority = 80)]
    public static void InstallBrain()
    {
        PerceptionSystem perception =
            Object.FindFirstObjectByType<PerceptionSystem>();

        if (perception == null)
        {
            EditorUtility.DisplayDialog(
                "Phase 3 Setup",
                "No sensor rig found. Run Phase 1 and Phase 2 setup " +
                "first.",
                "OK"
            );

            return;
        }

        GameObject rig = perception.gameObject;

        if (rig.GetComponent<XenoLocomotion>() == null)
        {
            EditorUtility.DisplayDialog(
                "Phase 3 Setup",
                "The rig has no XenoLocomotion. Run Phase 2 setup " +
                "first.",
                "OK"
            );

            return;
        }

        StringBuilder report = new StringBuilder();

        Undo.RegisterFullObjectHierarchyUndo(rig, "Install Brain");

        RemoveTestDriver(rig, report);

        InstallBrainComponent(rig, report);

        InstallKillPlaceholder(report);

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        Selection.activeGameObject = rig;

        string text = report.ToString();

        Debug.Log("[Phase3Setup]\n" + text, rig);

        EditorUtility.DisplayDialog("Phase 3 Setup", text, "OK");
    }


    // =========================================================
    // STEPS
    // =========================================================

    private static void RemoveTestDriver(
        GameObject rig,
        StringBuilder report)
    {
        LocomotionTestDriver test =
            rig.GetComponent<LocomotionTestDriver>();

        if (test == null)
        {
            report.AppendLine("No test driver to remove.");

            return;
        }

        /*
         * The component goes, but LocomotionTestDriver.cs stays on
         * disk. Deleting the script while a scene still references it
         * would leave a missing-script entry that is far more annoying
         * to clean up than an unused file.
         */

        Undo.DestroyObjectImmediate(test);

        report.AppendLine(
            "Removed LocomotionTestDriver (the Phase 2 stand-in)."
        );

        report.AppendLine(
            "Its script file is now unused and safe to delete."
        );
    }


    private static void InstallBrainComponent(
        GameObject rig,
        StringBuilder report)
    {
        if (rig.GetComponent<XenoBrain>() != null)
        {
            report.AppendLine("XenoBrain already present.");

            return;
        }

        Undo.AddComponent<XenoBrain>(rig);

        report.AppendLine("Added XenoBrain.");

        report.AppendLine();

        report.AppendLine(
            "States: Patrol, Investigate, Search, Stalk, Chase, " +
            "Attack, Retreat."
        );
    }


    private static void InstallKillPlaceholder(StringBuilder report)
    {
        PlayerController player =
            Object.FindFirstObjectByType<PlayerController>();

        if (player == null)
        {
            report.AppendLine();

            report.AppendLine(
                "WARNING: no player in the scene, so no kill handler " +
                "was added. The Attack state will fire its event into " +
                "nothing."
            );

            return;
        }

        if (player.GetComponent<PlayerKillPlaceholder>() != null)
        {
            report.AppendLine("Kill handler already on the player.");

            return;
        }

        Undo.AddComponent<PlayerKillPlaceholder>(player.gameObject);

        report.AppendLine(
            "Added PlayerKillPlaceholder to the player " +
            "(respawn on catch - placeholder until Phase 5)."
        );
    }
}

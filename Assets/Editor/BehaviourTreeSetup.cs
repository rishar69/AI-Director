using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs the behaviour tree in place of the Phase 3 state machine.
///
/// Everything below the brain - senses, locomotion, animation, the
/// Director, the player's counterplay - is untouched. Only the decision
/// layer changes.
/// </summary>
public static class BehaviourTreeSetup
{
    [MenuItem("Tools/AI Director/BT - Install Behaviour Tree",
        priority = 60)]
    public static void Install()
    {
        PerceptionSystem perception =
            Object.FindFirstObjectByType<PerceptionSystem>();

        if (perception == null)
        {
            EditorUtility.DisplayDialog(
                "Behaviour Tree",
                "No creature found. Run BUILD EVERYTHING first.",
                "OK"
            );

            return;
        }

        GameObject rig = perception.gameObject;

        StringBuilder report = new StringBuilder();

        Undo.RegisterFullObjectHierarchyUndo(rig, "Install BT");

        AddIfMissing<XenoBlackboard>(rig, report);

        AddIfMissing<XenoSenses>(rig, report);

        AddIfMissing<XenoMotivationDriver>(rig, report);

        AddIfMissing<XenoSuspiciousItems>(rig, report);

        AddIfMissing<XenoBehaviourTree>(rig, report);

        DisableLegacyBrain(rig, report);

        InstallDebugKeys(rig, report);

        InstallSuspiciousItemSpawner(report);

        InstallDirector(report);

        report.AppendLine();

        report.AppendLine("PORTED BRANCHES  (root selector order)");
        report.AppendLine("  SCRIPTED_WITHDRAWAL   y=2564 (inert -");
        report.AppendLine("      XenoDirector never requests a");
        report.AppendLine("      withdrawal; it eases off by area)");
        report.AppendLine("  THREAT_AWARE_BRANCH          y=3262");
        report.AppendLine("  ATTACK_CORE_BRANCH           y=3847");
        report.AppendLine("  SUSPECT_TARGET_RESPONSE      y=4162");
        report.AppendLine("  SEQUENCE_93 move-to publisher (disabled");
        report.AppendLine("      in the shipped game)     y=4402");
        report.AppendLine("  PLAYER_HIDING_BRANCH         y=4657");
        report.AppendLine("  SUSPICIOUS_ITEM_BRANCH_HIGH  y=4702");
        report.AppendLine("  SEARCH_BRANCH (systematic)   y=4747");
        report.AppendLine("  SUSPICIOUS_ITEM_BRANCH_MED   y=4882");
        report.AppendLine("  SUSPICIOUS_ITEM_BRANCH_LOW   y=4972");
        report.AppendLine("  AREA_SWEEP_BRANCH  (Stalk)   y=5062");
        report.AppendLine("  BACKSTAGE_AREA_SWEEP_BRANCH  y=5107");
        report.AppendLine("  IDLE_BRANCH      (fallback)  y=5152");
        report.AppendLine();
        report.AppendLine("The rest are stubs that FAIL, so the");
        report.AppendLine("selector falls through to these.");
        report.AppendLine();
        report.AppendLine("BACKSTAGE_AREA_SWEEP now runs front stage,");
        report.AppendLine("so the creature roams from the moment you");
        report.AppendLine("press Play instead of standing still. Turn");
        report.AppendLine("off Allow Frontstage Sweep once the level");
        report.AppendLine("has real backstage volumes.");
        report.AppendLine();
        report.AppendLine("XenoMotivationDriver raises motivations from");
        report.AppendLine("the senses. Without it the creature sits in");
        report.AppendLine("IDLE forever - every branch above IDLE is");
        report.AppendLine("gated on a motivation, and nothing in the");
        report.AppendLine("tree itself ever sets one.");
        report.AppendLine();
        report.AppendLine("SAVE THE SCENE after this or none of it");
        report.AppendLine("survives leaving play mode.");
        report.AppendLine();
        report.AppendLine("TEST KEYS (uncheck Drive From Senses on");
        report.AppendLine("XenoMotivationDriver first, or the driver");
        report.AppendLine("overwrites them every frame)");
        report.AppendLine("  1 STALK   2 BACKSTAGE   3 THREAT_AWARE");
        report.AppendLine("  4 ATTACK  5 SEARCH      6 SUSPECT_RESP");
        report.AppendLine("  F1 overlay (BEHAVIOUR TREE section)");

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        Selection.activeGameObject = rig;

        string text = report.ToString();

        Debug.Log("[BehaviourTreeSetup]\n" + text, rig);

        EditorUtility.DisplayDialog("Behaviour Tree", text, "OK");
    }


    // =========================================================
    // STEPS
    // =========================================================

    private static T AddIfMissing<T>(
        GameObject target,
        StringBuilder report)
        where T : Component
    {
        T existing = target.GetComponent<T>();

        if (existing != null)
        {
            report.AppendLine(typeof(T).Name + " already present.");

            return existing;
        }

        T added = Undo.AddComponent<T>(target);

        report.AppendLine("Added " + typeof(T).Name + ".");

        return added;
    }


    /// <summary>
    /// The tree disables XenoBrain at runtime anyway, but switching it
    /// off in the scene makes the swap visible in the inspector rather
    /// than something that silently happens on Play.
    /// </summary>
    /// <summary>
    /// The second brain. Without it the retreat gauge never moves and
    /// the scripted withdrawal branch never fires, so the creature has
    /// no pacing at all - it just hunts until it wins.
    /// </summary>
    private static void InstallDirector(StringBuilder report)
    {
        XenoDirector existing =
            Object.FindFirstObjectByType<XenoDirector>();

        if (existing != null)
        {
            report.AppendLine("XenoDirector already in scene");

            return;
        }

        AIDirector legacy =
            Object.FindFirstObjectByType<AIDirector>();

        GameObject host =
            legacy != null
                ? legacy.gameObject
                : new GameObject("AI Director");

        if (legacy == null)
        {
            Undo.RegisterCreatedObjectUndo(host, "Add director");
        }

        Undo.AddComponent<XenoDirector>(host);

        report.AppendLine(
            legacy != null
                ? "Added XenoDirector (AIDirector auto-disables)"
                : "Added XenoDirector");
    }


    /// <summary>
    /// Without this nothing ever becomes a suspicious item, and all
    /// three SUSPICIOUS_ITEM branches sit there with nothing to do.
    /// </summary>
    private static void InstallSuspiciousItemSpawner(
        StringBuilder report)
    {
        SuspiciousItemSpawner existing =
            Object.FindFirstObjectByType<SuspiciousItemSpawner>();

        if (existing != null)
        {
            report.AppendLine(
                "SuspiciousItemSpawner already in scene");

            return;
        }

        GameObject host =
            new GameObject("Suspicious Item Spawner");

        Undo.RegisterCreatedObjectUndo(host, "Add spawner");

        Undo.AddComponent<SuspiciousItemSpawner>(host);

        report.AppendLine(
            "Added SuspiciousItemSpawner (listens to NoiseBus)");
    }


    private static void DisableLegacyBrain(
        GameObject rig,
        StringBuilder report)
    {
        XenoBrain legacy = rig.GetComponent<XenoBrain>();

        if (legacy == null)
        {
            report.AppendLine("No XenoBrain to disable.");

            return;
        }

        if (!legacy.enabled)
        {
            report.AppendLine("XenoBrain already disabled.");

            return;
        }

        Undo.RecordObject(legacy, "Disable XenoBrain");

        legacy.enabled = false;

        report.AppendLine(
            "Disabled XenoBrain (kept for reference; the tree " +
            "replaces it)."
        );
    }


    private static void InstallDebugKeys(
        GameObject rig,
        StringBuilder report)
    {
        MotivationDebugKeys keys =
            Object.FindFirstObjectByType<MotivationDebugKeys>();

        if (keys != null)
        {
            report.AppendLine("Motivation debug keys already present.");

            return;
        }

        AIDebugOverlay overlay =
            Object.FindFirstObjectByType<AIDebugOverlay>();

        GameObject host =
            overlay != null ? overlay.gameObject : rig;

        Undo.AddComponent<MotivationDebugKeys>(host);

        report.AppendLine(
            "Added MotivationDebugKeys to " + host.name + "."
        );
    }
}

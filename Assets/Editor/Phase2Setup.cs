using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Upgrades the Phase 1 sensor rig into something that can move.
///
/// Adds the NavMeshAgent and locomotion stack to the existing rig
/// rather than building a new one, so the sense tuning done in Phase 1
/// carries over untouched.
/// </summary>
public static class Phase2Setup
{
    private const string AnimatorControllerPath =
        "Assets/Charactees/NPC.controller";


    // =========================================================
    // MENU
    // =========================================================

    [MenuItem("Tools/AI Director/Phase 2 - Add Locomotion To Rig",
        priority = 60)]
    public static void AddLocomotion()
    {
        PerceptionSystem perception =
            Object.FindFirstObjectByType<PerceptionSystem>();

        if (perception == null)
        {
            EditorUtility.DisplayDialog(
                "Phase 2 Setup",
                "No sensor rig found.\n\n" +
                "Run Phase 1 setup first.",
                "OK"
            );

            return;
        }

        GameObject rig = perception.gameObject;

        StringBuilder report = new StringBuilder();

        Undo.RegisterFullObjectHierarchyUndo(rig, "Add Locomotion");

        ConfigureAgent(rig, report);

        XenoLocomotion locomotion =
            AddIfMissing<XenoLocomotion>(rig, report);

        XenoAnimatorDriver driver =
            AddIfMissing<XenoAnimatorDriver>(rig, report);

        LocomotionTestDriver test =
            AddIfMissing<LocomotionTestDriver>(rig, report);

        WireTestDriver(test, perception, driver);

        ReportAnimatorSituation(rig, report);

        CheckNavMesh(report);

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        Selection.activeGameObject = rig;

        string text = report.ToString();

        Debug.Log("[Phase2Setup]\n" + text, rig);

        EditorUtility.DisplayDialog(
            "Phase 2 Setup",
            text,
            "OK"
        );
    }


    // =========================================================
    // AGENT
    // =========================================================

    private static void ConfigureAgent(
        GameObject rig,
        StringBuilder report)
    {
        NavMeshAgent agent = rig.GetComponent<NavMeshAgent>();

        if (agent == null)
        {
            agent = Undo.AddComponent<NavMeshAgent>(rig);

            report.AppendLine("Added NavMeshAgent.");
        }

        /*
         * Sized to roughly match the placeholder capsule. These want
         * revisiting when a real model arrives - an agent radius that
         * disagrees with the visible body is the usual cause of a
         * creature clipping through door frames.
         */

        agent.radius = 0.4f;

        agent.height = 2f;

        agent.baseOffset = 0f;

        agent.stoppingDistance = 0.6f;

        agent.autoBraking = true;

        agent.autoRepath = true;

        agent.obstacleAvoidanceType =
            ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    }


    private static T AddIfMissing<T>(
        GameObject target,
        StringBuilder report)
        where T : Component
    {
        T existing = target.GetComponent<T>();

        if (existing != null)
        {
            report.AppendLine(
                typeof(T).Name + " already present."
            );

            return existing;
        }

        T added = Undo.AddComponent<T>(target);

        report.AppendLine("Added " + typeof(T).Name + ".");

        return added;
    }


    private static void WireTestDriver(
        LocomotionTestDriver test,
        PerceptionSystem perception,
        XenoAnimatorDriver driver)
    {
        if (test == null)
            return;

        SerializedObject so = new SerializedObject(test);

        so.FindProperty("perception").objectReferenceValue =
            perception;

        so.FindProperty("animatorDriver").objectReferenceValue =
            driver;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    // =========================================================
    // ANIMATION
    // =========================================================

    /// <summary>
    /// Says plainly whether anything can actually be animated.
    ///
    /// The project has Mixamo clips but no skinned mesh, so silently
    /// adding an Animator would produce a component that logs avatar
    /// warnings and animates nothing.
    /// </summary>
    private static void ReportAnimatorSituation(
        GameObject rig,
        StringBuilder report)
    {
        SkinnedMeshRenderer skin =
            rig.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (skin == null)
        {
            report.AppendLine();

            report.AppendLine(
                "No skinned character model on the rig, so no " +
                "Animator was added."
            );

            report.AppendLine(
                "The Mixamo files in Assets/Charactees are " +
                "animation-only exports - they contain the " +
                "mixamorig skeleton but no mesh."
            );

            report.AppendLine();

            report.AppendLine(
                "To see animation: download X Bot from Mixamo WITH " +
                "skin, set every clip and the model to Humanoid, " +
                "then drop the model under the rig and re-run this."
            );

            return;
        }

        Animator animator = rig.GetComponentInChildren<Animator>();

        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(skin.gameObject);

            report.AppendLine("Added Animator to the model.");
        }

        if (animator.applyRootMotion)
        {
            /*
             * Unity turns this on by default for imported humanoid
             * models, and Mixamo's Running clip carries root motion.
             * Left on it fights the NavMeshAgent for the transform.
             */

            Undo.RecordObject(animator, "Disable Root Motion");

            animator.applyRootMotion = false;

            report.AppendLine(
                "Disabled Apply Root Motion (it fights the agent)."
            );
        }

        if (animator.runtimeAnimatorController == null)
        {
            RuntimeAnimatorController controller =
                AssetDatabase
                    .LoadAssetAtPath<RuntimeAnimatorController>(
                        AnimatorControllerPath
                    );

            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;

                report.AppendLine("Assigned NPC.controller.");
            }
            else
            {
                report.AppendLine(
                    "Could not find " + AnimatorControllerPath + "."
                );
            }
        }
    }


    // =========================================================
    // NAVMESH
    // =========================================================

    private static void CheckNavMesh(StringBuilder report)
    {
        NavMeshTriangulation triangulation =
            NavMesh.CalculateTriangulation();

        bool baked =
            triangulation.vertices != null &&
            triangulation.vertices.Length > 0;

        report.AppendLine();

        if (baked)
        {
            report.AppendLine(
                "NavMesh is baked (" +
                triangulation.vertices.Length +
                " vertices)."
            );
        }
        else
        {
            report.AppendLine(
                "WARNING: no NavMesh is baked. The agent cannot " +
                "move until you select the NavMesh object and press " +
                "Bake."
            );
        }
    }
}

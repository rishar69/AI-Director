using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Removes the boundary GameObjects that the old VisionCone leaked into
/// the scene on every recompile.
///
/// VisionCone now reconciles its three children instead of blindly
/// creating more, so this only needs running once to clear what has
/// already piled up. Deleting all of them is safe: the component
/// rebuilds exactly three the next time it initialises.
///
/// The leaked materials go with them. Unity only serialises scene
/// embedded objects that something still references, so once the
/// LineRenderers holding them are gone they stop being written out on
/// the next save.
/// </summary>
public static class VisionConeCleanup
{
    private static readonly string[] BoundaryNames =
    {
        "Vision Left Boundary",
        "Vision Right Boundary",
        "Vision Far Boundary"
    };


    // =========================================================
    // MENU
    // =========================================================

    [MenuItem("Tools/AI Director/Clean Leaked Vision Cone Objects",
        priority = 200)]
    public static void Clean()
    {
        List<GameObject> doomed = FindLeakedObjects();

        if (doomed.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Vision Cone Cleanup",
                "Nothing to clean - no leaked boundary objects found.",
                "OK"
            );

            return;
        }

        bool proceed =
            EditorUtility.DisplayDialog(
                "Vision Cone Cleanup",
                "Found " + doomed.Count +
                " leaked boundary objects.\n\n" +
                "They will be deleted. VisionCone rebuilds the three " +
                "it actually needs automatically.\n\n" +
                "This is undoable, but you must SAVE THE SCENE " +
                "afterwards for the orphaned materials to be dropped " +
                "from the file.",
                "Delete them",
                "Cancel"
            );

        if (!proceed)
            return;

        int removed = 0;

        foreach (GameObject target in doomed)
        {
            if (target == null)
                continue;

            Undo.DestroyObjectImmediate(target);

            removed++;
        }

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        string message =
            "Deleted " + removed + " leaked boundary objects.\n\n" +
            "Now save the scene (Ctrl+S) to drop the orphaned " +
            "materials from the scene file.";

        Debug.Log("[VisionConeCleanup] " + message);

        EditorUtility.DisplayDialog(
            "Vision Cone Cleanup",
            message,
            "OK"
        );
    }


    // =========================================================
    // SCANNING
    // =========================================================

    /// <summary>
    /// Collects every boundary object in every loaded scene, including
    /// ones whose parent VisionCone has since been deleted.
    /// </summary>
    private static List<GameObject> FindLeakedObjects()
    {
        List<GameObject> found = new List<GameObject>();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);

            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                CollectRecursive(root.transform, found);
            }
        }

        return found;
    }


    private static void CollectRecursive(
        Transform current,
        List<GameObject> found)
    {
        for (int i = 0; i < current.childCount; i++)
        {
            Transform child = current.GetChild(i);

            if (IsBoundaryObject(child))
            {
                found.Add(child.gameObject);

                /*
                 * No need to walk into it - boundary objects have no
                 * children worth keeping.
                 */

                continue;
            }

            CollectRecursive(child, found);
        }
    }


    private static bool IsBoundaryObject(Transform candidate)
    {
        if (System.Array.IndexOf(
                BoundaryNames,
                candidate.name) < 0)
        {
            return false;
        }

        /*
         * Name alone is not enough - only delete things that really are
         * generated line renderers with nothing else attached.
         */

        if (candidate.GetComponent<LineRenderer>() == null)
            return false;

        return candidate.GetComponents<Component>().Length <= 3;
    }


    // =========================================================
    // REPORTING
    // =========================================================

    [MenuItem("Tools/AI Director/Count Leaked Vision Cone Objects",
        priority = 201)]
    public static void Count()
    {
        int count = FindLeakedObjects().Count;

        int expected = 0;

        foreach (VisionCone cone in
                 Object.FindObjectsByType<VisionCone>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (cone != null)
            {
                expected += 3;
            }
        }

        string message =
            count + " boundary objects present, " +
            expected + " expected (" +
            (expected / 3) + " VisionCone components x 3).";

        Debug.Log("[VisionConeCleanup] " + message);

        EditorUtility.DisplayDialog(
            "Vision Cone Cleanup",
            message,
            "OK"
        );
    }
}

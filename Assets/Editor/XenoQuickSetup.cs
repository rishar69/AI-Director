using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the entire game into a scene that contains only the map.
///
/// Replaces the six per-phase setup tools for a from-scratch build. It
/// also takes over the three steps that were previously left to hand
/// and kept going wrong: baking the NavMesh, authoring the animator
/// controller with correct transitions, and giving the creature a
/// collider so the player's tools can actually find it.
///
/// Safe to re-run. Anything already present is reused rather than
/// duplicated.
/// </summary>
public static class XenoQuickSetup
{
    private const string CharDir = "Assets/Charactees/";

    private const string ModelPath = CharDir + "X Bot.fbx";

    private const string IdleClipPath = CharDir + "X Bot@Standing Idle.fbx";

    private const string WalkClipPath = CharDir + "X Bot@Walking.fbx";

    private const string RunClipPath = CharDir + "X Bot@Running.fbx";

    private const string LookClipPath = CharDir + "X Bot@Look Around.fbx";

    private const string ControllerPath = CharDir + "Xenomorph.controller";

    private const string InputAssetPath =
        "Assets/InputSystem_Actions.inputactions";

    private static readonly List<string> report = new List<string>();


    // =========================================================
    // ENTRY POINT
    // =========================================================

    [MenuItem("Tools/AI Director/BUILD EVERYTHING", priority = -100)]
    public static void BuildEverything()
    {
        bool go = EditorUtility.DisplayDialog(
            "Build Everything",
            "This builds the player, the creature, the Director, the " +
            "NavMesh and the animator controller into the current " +
            "scene.\n\n" +
            "Your map is left alone. Anything already set up is " +
            "reused, not duplicated.\n\n" +
            "Make sure the scene has your map in it and is saved.",
            "Build it",
            "Cancel"
        );

        if (!go)
            return;

        report.Clear();

        int playerLayer, npcLayer;

        Step("Layers", () => SetupLayers());

        ResolveLayers(out playerLayer, out npcLayer);

        Step("Animation clips", () => SetupClips());

        AnimatorController controller = null;

        Step("Animator controller",
            () => controller = BuildController());

        NavMeshSurface surface = null;

        Step("NavMesh",
            () => surface = SetupNavMesh(playerLayer, npcLayer));

        GameObject player = null;

        Step("Player",
            () => player = BuildPlayer(playerLayer, npcLayer));

        GameObject creature = null;

        Step("Creature",
            () => creature = BuildCreature(
                controller, playerLayer, npcLayer));

        Step("AI Director", () => BuildDirector());

        Step("Debug overlay", () => BuildOverlay());

        Step("Wiring", () => WireEverything(player, creature, npcLayer));

        EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene()
        );

        if (creature != null)
        {
            Selection.activeGameObject = creature;
        }

        Finish();
    }


    private static void Step(
        string label,
        System.Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception e)
        {
            report.Add("FAILED  " + label + " - " + e.Message);

            Debug.LogException(e);
        }
    }


    private static void Finish()
    {
        StringBuilder text = new StringBuilder();

        foreach (string line in report)
        {
            text.AppendLine(line);
        }

        text.AppendLine();
        text.AppendLine("CONTROLS");
        text.AppendLine("  WASD / mouse   move, look");
        text.AppendLine("  C  crouch      Shift  sprint");
        text.AppendLine("  E  hide        W  peek while hidden");
        text.AppendLine("  F  flamethrower (hold)");
        text.AppendLine("  G  throw noisemaker");
        text.AppendLine("  T  motion tracker");
        text.AppendLine("  F1 debug overlay");
        text.AppendLine();
        text.AppendLine("OPTIONAL, PLACE BY HAND");
        text.AppendLine("  Tools > AI Director > Create Hiding Spot At View");
        text.AppendLine("  Tools > AI Director > Create Vent Point At View");
        text.AppendLine("  (two vents, then select both > Link Selected Vents)");

        string message = text.ToString();

        Debug.Log("[QuickSetup]\n" + message);

        EditorUtility.DisplayDialog("Build Everything", message, "OK");
    }


    // =========================================================
    // LAYERS
    // =========================================================

    private static void SetupLayers()
    {
        int added = Phase0Setup.AddMissingLayers();

        report.Add("Layers ready (" + added + " added)");
    }


    private static void ResolveLayers(
        out int playerLayer,
        out int npcLayer)
    {
        playerLayer = LayerMask.NameToLayer(GameLayers.PlayerName);

        npcLayer = LayerMask.NameToLayer(GameLayers.NpcName);
    }


    // =========================================================
    // CLIPS
    // =========================================================

    /// <summary>
    /// Forces every clip to Humanoid and looping.
    ///
    /// A Generic clip will not retarget onto the Humanoid avatar and
    /// simply does not play; a non-looping walk plays once and freezes.
    /// Both failures look like broken code rather than an import
    /// setting, so they are fixed here rather than left to be
    /// remembered.
    /// </summary>
    private static void SetupClips()
    {
        int fixedCount = 0;

        string[] paths =
        {
            ModelPath, IdleClipPath, WalkClipPath,
            RunClipPath, LookClipPath
        };

        foreach (string path in paths)
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(path) as ModelImporter;

            if (importer == null)
            {
                report.Add("MISSING  " + path);

                continue;
            }

            bool changed = false;

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType =
                    ModelImporterAnimationType.Human;

                changed = true;
            }

            /* The base model owns the avatar; clips copy nothing. */

            if (path != ModelPath)
            {
                ModelImporterClipAnimation[] clips =
                    importer.defaultClipAnimations;

                for (int i = 0; i < clips.Length; i++)
                {
                    if (!clips[i].loopTime)
                    {
                        clips[i].loopTime = true;

                        changed = true;
                    }
                }

                if (changed && clips.Length > 0)
                {
                    importer.clipAnimations = clips;
                }
            }

            if (!changed)
                continue;

            importer.SaveAndReimport();

            fixedCount++;
        }

        report.Add(
            "Clips set to Humanoid + looping (" +
            fixedCount + " reimported)"
        );
    }


    // =========================================================
    // ANIMATOR
    // =========================================================

    /// <summary>
    /// Authors a controller from scratch rather than repairing one.
    ///
    /// Every transition is Any State, with exit time OFF - a single
    /// transition left with exit time on stalls the animator until the
    /// current clip is three quarters played, which reads exactly like
    /// the code failing to fire a trigger.
    /// </summary>
    private static AnimatorController BuildController()
    {
        AnimationClip idle = LoadClip(IdleClipPath);
        AnimationClip walk = LoadClip(WalkClipPath);
        AnimationClip run = LoadClip(RunClipPath);
        AnimationClip look = LoadClip(LookClipPath);

        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ControllerPath);

        if (controller == null)
        {
            controller =
                AnimatorController.CreateAnimatorControllerAtPath(
                    ControllerPath);
        }

        string[] triggers = { "Idle", "Walk", "Run", "LookAround" };

        foreach (string trigger in triggers)
        {
            EnsureTrigger(controller, trigger);
        }

        AnimatorStateMachine machine =
            controller.layers[0].stateMachine;

        /* Rebuilt wholesale, so a bad transition cannot survive. */

        foreach (AnimatorStateTransition existing in
                 machine.anyStateTransitions)
        {
            machine.RemoveAnyStateTransition(existing);
        }

        AnimatorState idleState = EnsureState(machine, "Idle", idle);
        AnimatorState walkState = EnsureState(machine, "Walk", walk);
        AnimatorState runState = EnsureState(machine, "Run", run);
        AnimatorState lookState =
            EnsureState(machine, "LookAround", look);

        machine.defaultState = idleState;

        Link(machine, idleState, "Idle");
        Link(machine, walkState, "Walk");
        Link(machine, runState, "Run");
        Link(machine, lookState, "LookAround");

        EditorUtility.SetDirty(controller);

        AssetDatabase.SaveAssets();

        int missing = 0;

        if (idle == null) missing++;
        if (walk == null) missing++;
        if (run == null) missing++;
        if (look == null) missing++;

        report.Add(
            "Animator built at " + ControllerPath +
            (missing > 0
                ? "  (" + missing + " clip(s) not found)"
                : "  (4 clips wired)")
        );

        return controller;
    }


    private static void Link(
        AnimatorStateMachine machine,
        AnimatorState state,
        string trigger)
    {
        AnimatorStateTransition transition =
            machine.AddAnyStateTransition(state);

        transition.hasExitTime = false;

        transition.exitTime = 0f;

        transition.duration = 0.2f;

        transition.hasFixedDuration = true;

        /*
         * Off, so re-firing a trigger the animator is already in does
         * not restart the clip mid-stride.
         */

        transition.canTransitionToSelf = false;

        transition.AddCondition(
            AnimatorConditionMode.If, 0f, trigger);
    }


    private static void EnsureTrigger(
        AnimatorController controller,
        string name)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
        {
            if (p.name == name)
                return;
        }

        controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
    }


    private static AnimatorState EnsureState(
        AnimatorStateMachine machine,
        string name,
        AnimationClip clip)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name != name)
                continue;

            if (clip != null)
            {
                child.state.motion = clip;
            }

            return child.state;
        }

        AnimatorState state = machine.AddState(name);

        state.motion = clip;

        return state;
    }


    private static AnimationClip LoadClip(string path)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

        foreach (Object asset in assets)
        {
            AnimationClip clip = asset as AnimationClip;

            if (clip == null)
                continue;

            if (clip.name.StartsWith("__preview__"))
                continue;

            return clip;
        }

        return null;
    }


    // =========================================================
    // NAVMESH
    // =========================================================

    private static NavMeshSurface SetupNavMesh(
        int playerLayer,
        int npcLayer)
    {
        NavMeshSurface surface =
            Object.FindFirstObjectByType<NavMeshSurface>();

        if (surface == null)
        {
            GameObject host = new GameObject("NavMesh");

            Undo.RegisterCreatedObjectUndo(host, "Create NavMesh");

            surface = host.AddComponent<NavMeshSurface>();
        }

        surface.collectObjects = CollectObjects.All;

        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        /*
         * The player and the creature carry colliders, and baking them
         * into the mesh would punch holes in the floor wherever they
         * happen to be standing.
         */

        int mask = ~0;

        if (playerLayer >= 0) mask &= ~(1 << playerLayer);

        if (npcLayer >= 0) mask &= ~(1 << npcLayer);

        surface.layerMask = mask;

        NavMeshAssetManager.instance.StartBakingSurfaces(
            new Object[] { surface }
        );

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();

        int vertices = tri.vertices != null ? tri.vertices.Length : 0;

        report.Add(
            vertices > 0
                ? "NavMesh baked (" + vertices + " vertices)"
                : "NAVMESH EMPTY - is there walkable collider geometry?"
        );

        return surface;
    }


    // =========================================================
    // PLAYER
    // =========================================================

    private static GameObject BuildPlayer(
        int playerLayer,
        int npcLayer)
    {
        PlayerController existing =
            Object.FindFirstObjectByType<PlayerController>();

        GameObject player;

        if (existing != null)
        {
            player = existing.gameObject;

            report.Add("Player reused");
        }
        else
        {
            player = new GameObject("Player");

            Undo.RegisterCreatedObjectUndo(player, "Create Player");

            player.transform.position = FindGround(Vector3.zero);

            report.Add("Player created");
        }

        if (playerLayer >= 0) player.layer = playerLayer;

        player.tag = "Player";

        CharacterController controller =
            Ensure<CharacterController>(player);

        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.slopeLimit = 45f;
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.02f;

        Camera camera = AcquireCamera(player);

        Ensure<PlayerController>(player);
        Ensure<PlayerNoiseEmitter>(player);
        Ensure<Concealment>(player);
        Ensure<PlayerHiding>(player);
        Ensure<PlayerThrower>(player);
        Ensure<PlayerDeterrent>(player);
        Ensure<MotionTracker>(player);
        Ensure<PlayerDeathSequence>(player);

        /* The Phase 3 stand-in double-kills alongside the real one. */

        PlayerKillPlaceholder stale =
            player.GetComponent<PlayerKillPlaceholder>();

        if (stale != null)
        {
            Object.DestroyImmediate(stale);

            report.Add("Removed old PlayerKillPlaceholder");
        }

        WirePlayerController(
            player.GetComponent<PlayerController>(),
            camera.transform,
            playerLayer
        );

        return player;
    }


    private static Camera AcquireCamera(GameObject player)
    {
        Camera existing = player.GetComponentInChildren<Camera>();

        if (existing == null)
        {
            existing = Camera.main;
        }

        if (existing == null)
        {
            GameObject made = new GameObject("PlayerCamera");

            made.tag = "MainCamera";

            existing = made.AddComponent<Camera>();
        }

        existing.gameObject.name = "PlayerCamera";

        existing.transform.SetParent(player.transform, false);

        existing.transform.localPosition = new Vector3(0f, 1.65f, 0f);

        existing.transform.localRotation = Quaternion.identity;

        existing.nearClipPlane = 0.05f;

        if (existing.GetComponent<AudioListener>() == null)
        {
            existing.gameObject.AddComponent<AudioListener>();
        }

        return existing;
    }


    private static void WirePlayerController(
        PlayerController movement,
        Transform cameraPivot,
        int playerLayer)
    {
        SerializedObject so = new SerializedObject(movement);

        InputActionAsset asset =
            AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                InputAssetPath);

        if (asset != null)
        {
            so.FindProperty("inputActions").objectReferenceValue = asset;
        }

        so.FindProperty("cameraPivot").objectReferenceValue =
            cameraPivot;

        int mask = ~0;

        if (playerLayer >= 0) mask &= ~(1 << playerLayer);

        mask &= ~(1 << 2);

        so.FindProperty("ceilingMask").intValue = mask;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    // =========================================================
    // CREATURE
    // =========================================================

    private static GameObject BuildCreature(
        AnimatorController controller,
        int playerLayer,
        int npcLayer)
    {
        PerceptionSystem existing =
            Object.FindFirstObjectByType<PerceptionSystem>();

        GameObject rig;

        if (existing != null)
        {
            rig = existing.gameObject;

            report.Add("Creature reused");
        }
        else
        {
            rig = new GameObject("Xenomorph");

            Undo.RegisterCreatedObjectUndo(rig, "Create Xenomorph");

            rig.transform.position = ChooseCreatureSpawn();

            report.Add("Creature created");
        }

        AttachModel(rig, controller);

        /*
         * A collider is required, not cosmetic: the flamethrower and
         * the motion tracker both find the creature with an overlap
         * query against the NPC layer, and without one they silently
         * never hit anything.
         */

        CapsuleCollider body = Ensure<CapsuleCollider>(rig);

        body.height = 2f;
        body.radius = 0.4f;
        body.center = new Vector3(0f, 1f, 0f);

        NavMeshAgent agent = Ensure<NavMeshAgent>(rig);

        agent.radius = 0.4f;
        agent.height = 2f;
        agent.baseOffset = 0f;
        agent.stoppingDistance = 0.6f;
        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.obstacleAvoidanceType =
            ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        PerceptionSystem perception = Ensure<PerceptionSystem>(rig);

        VisionSense vision = Ensure<VisionSense>(rig);
        HearingSense hearing = Ensure<HearingSense>(rig);
        ProximitySense proximity = Ensure<ProximitySense>(rig);

        Ensure<XenoLocomotion>(rig);
        Ensure<XenoAnimatorDriver>(rig);
        Ensure<XenoBrain>(rig);
        Ensure<XenoProgression>(rig);
        Ensure<XenoAudio>(rig);
        Ensure<DirectorHintReceiver>(rig);

        /* The Phase 2 stand-in fights the brain for control. */

        LocomotionTestDriver stale =
            rig.GetComponent<LocomotionTestDriver>();

        if (stale != null)
        {
            Object.DestroyImmediate(stale);

            report.Add("Removed old LocomotionTestDriver");
        }

        LayerMask targetMask =
            playerLayer >= 0 ? (1 << playerLayer) : 0;

        LayerMask obstacleMask =
            BuildObstacleMask(playerLayer, npcLayer);

        ConfigureSense(vision, targetMask, obstacleMask);
        ConfigureSense(hearing, targetMask, obstacleMask);
        ConfigureSense(proximity, targetMask, obstacleMask);

        BuildConeVisual(rig, vision, perception);

        if (npcLayer >= 0)
        {
            SetLayerRecursively(rig, npcLayer);
        }

        return rig;
    }


    /// <summary>
    /// Parents the X Bot model and points its Animator at the built
    /// controller, with root motion off.
    /// </summary>
    private static void AttachModel(
        GameObject rig,
        AnimatorController controller)
    {
        SkinnedMeshRenderer skin =
            rig.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (skin == null)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

            if (prefab == null)
            {
                report.Add("MISSING  " + ModelPath + " - no model");

                return;
            }

            GameObject model =
                (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            model.name = "Model";

            model.transform.SetParent(rig.transform, false);

            model.transform.localPosition = Vector3.zero;

            model.transform.localRotation = Quaternion.identity;

            skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);

            report.Add("Model attached");
        }

        Animator animator = rig.GetComponentInChildren<Animator>();

        if (animator == null && skin != null)
        {
            animator = skin.transform.root == rig.transform
                ? rig.AddComponent<Animator>()
                : skin.gameObject.AddComponent<Animator>();
        }

        if (animator == null)
            return;

        if (controller != null)
        {
            animator.runtimeAnimatorController = controller;
        }

        /* Mixamo's run clip carries root motion; it fights the agent. */

        animator.applyRootMotion = false;

        animator.cullingMode =
            AnimatorCullingMode.AlwaysAnimate;
    }


    private static void BuildConeVisual(
        GameObject rig,
        VisionSense vision,
        PerceptionSystem perception)
    {
        VisionConeVisualizer visualizer =
            rig.GetComponentInChildren<VisionConeVisualizer>(true);

        if (visualizer == null)
        {
            GameObject cone = new GameObject("Vision Cone");

            cone.transform.SetParent(rig.transform, false);

            cone.transform.localPosition = Vector3.zero;

            cone.AddComponent<MeshFilter>();
            cone.AddComponent<MeshRenderer>();

            visualizer = cone.AddComponent<VisionConeVisualizer>();
        }

        SerializedObject so = new SerializedObject(visualizer);

        so.FindProperty("visionSense").objectReferenceValue = vision;

        so.FindProperty("perception").objectReferenceValue = perception;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    private static LayerMask BuildObstacleMask(
        int playerLayer,
        int npcLayer)
    {
        int mask = ~0;

        if (npcLayer >= 0) mask &= ~(1 << npcLayer);

        if (playerLayer >= 0) mask &= ~(1 << playerLayer);

        mask &= ~(1 << 1);   /* TransparentFX */
        mask &= ~(1 << 2);   /* Ignore Raycast */
        mask &= ~(1 << 5);   /* UI */

        return mask;
    }


    private static void ConfigureSense(
        Sense sense,
        LayerMask targetMask,
        LayerMask obstacleMask)
    {
        SerializedObject so = new SerializedObject(sense);

        so.FindProperty("targetMask").intValue = targetMask.value;

        so.FindProperty("obstacleMask").intValue = obstacleMask.value;

        so.FindProperty("sensorHeight").floatValue = 1.6f;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    // =========================================================
    // DIRECTOR & OVERLAY
    // =========================================================

    private static void BuildDirector()
    {
        AIDirector existing =
            Object.FindFirstObjectByType<AIDirector>();

        if (existing != null)
        {
            RetuneMenace(existing);

            report.Add("Director reused and retuned");

            return;
        }

        GameObject host = new GameObject("AI Director");

        Undo.RegisterCreatedObjectUndo(host, "Create AI Director");

        host.AddComponent<ZoneGraph>();

        AIDirector director = host.AddComponent<AIDirector>();

        RetuneMenace(director);

        report.Add("Director created");
    }


    /// <summary>
    /// Menace pressure is normalised now; a Director carrying rates
    /// from before that change pushes the creature away almost the
    /// instant it finds the player.
    /// </summary>
    private static void RetuneMenace(AIDirector director)
    {
        SerializedObject so = new SerializedObject(director);

        SerializedProperty rise = so.FindProperty("menace.riseRate");

        SerializedProperty decay = so.FindProperty("menace.decayRate");

        if (rise != null) rise.floatValue = 0.05f;

        if (decay != null) decay.floatValue = 0.035f;

        so.ApplyModifiedPropertiesWithoutUndo();
    }


    private static void BuildOverlay()
    {
        if (Object.FindFirstObjectByType<AIDebugOverlay>() != null)
        {
            report.Add("Debug overlay reused");

            return;
        }

        GameObject host = new GameObject("AI Debug");

        Undo.RegisterCreatedObjectUndo(host, "Create Debug Overlay");

        host.AddComponent<AIDebugOverlay>();

        report.Add("Debug overlay created");
    }


    // =========================================================
    // WIRING
    // =========================================================

    private static void WireEverything(
        GameObject player,
        GameObject creature,
        int npcLayer)
    {
        if (player == null || npcLayer < 0)
            return;

        int mask = 1 << npcLayer;

        SetMask(player.GetComponent<PlayerDeterrent>(),
            "creatureMask", mask);

        SetMask(player.GetComponent<MotionTracker>(),
            "creatureMask", mask);

        report.Add("Player tools pointed at the NPC layer");

        if (creature == null)
            return;

        float distance =
            Vector3.Distance(
                player.transform.position,
                creature.transform.position);

        report.Add(
            "Creature spawned " + distance.ToString("F0") +
            "m from the player"
        );
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
    // HELPERS
    // =========================================================

    private static T Ensure<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();

        return component != null
            ? component
            : target.AddComponent<T>();
    }


    private static Vector3 FindGround(Vector3 around)
    {
        if (Physics.Raycast(
                around + Vector3.up * 60f,
                Vector3.down,
                out RaycastHit hit,
                250f))
        {
            return hit.point + Vector3.up * 0.15f;
        }

        return around + Vector3.up * 1f;
    }


    /// <summary>
    /// Puts the creature somewhere on the NavMesh well away from the
    /// player, so a fresh scene does not open with it standing on top
    /// of them.
    /// </summary>
    private static Vector3 ChooseCreatureSpawn()
    {
        PlayerController player =
            Object.FindFirstObjectByType<PlayerController>();

        Vector3 origin =
            player != null ? player.transform.position : Vector3.zero;

        for (int i = 0; i < 24; i++)
        {
            Vector3 candidate =
                origin +
                Quaternion.Euler(0f, i * 15f, 0f) *
                Vector3.forward * 18f;

            if (NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit hit,
                    8f,
                    NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        return FindGround(origin + Vector3.forward * 12f);
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

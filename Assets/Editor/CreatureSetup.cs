using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs the Mixamo-rigged creature in place of the X Bot.
///
/// Every clip file carries the same skinned creature on the same 65-bone
/// Mixamo skeleton, so one of them (the idle) is the model and owns the
/// avatar; the rest copy it. The creature runs and creeps on all fours,
/// which decides two import settings that look like details and are not:
///
///   - Root height is baked into the pose, based on the original. Left
///     unbaked, the body height would be discarded along with the rest
///     of the root motion and a crawling creature would hover at standing
///     height.
///   - Root XZ is NOT baked. With Apply Root Motion off (the agent moves
///     the creature), unbaked horizontal motion is simply dropped, which
///     keeps every clip in place whether or not "In Place" was ticked.
///
/// Safe to re-run: the controller is rebuilt and the model re-swapped.
/// </summary>
public static class CreatureSetup
{
    private const string Dir = "Assets/Charactees/Creature/";

    private const string ModelPath = Dir + "Creature@Mutant Idle.fbx";

    private const string ControllerPath = Dir + "Creature.controller";

    private const string MaterialPath = Dir + "Creature Skin.mat";

    private const string TextureDir = Dir + "Textures/";


    private struct Clip
    {
        public string File;
        public string State;
        public bool Loop;

        /// <summary>Plays once, then returns to idle on its own.</summary>
        public bool OneShot;

        /// <summary>
        /// Height follows the feet rather than the clip's root. For
        /// kneeling clips, where the hips drop and the body would
        /// otherwise hang in the air.
        /// </summary>
        public bool FeetGrounded;

        public Clip(string file, string state, bool loop, bool oneShot = false, bool feet = false)
        {
            File = file;
            State = state;
            Loop = loop;
            OneShot = oneShot;
            FeetGrounded = feet;
        }
    }


    /*
     * The first five are what XenoAnimatorDriver drives today. The rest
     * are ready states with their own triggers, waiting for the
     * behaviour tree to call them (alert, inspect, flash stun...).
     */
    private static readonly Clip[] Clips =
    {
        new Clip("Mutant Idle", "Idle", true),
        new Clip("Old Man Walk", "Prowl", true),
        new Clip("Mutant Walking", "Walk", true),
        new Clip("Running Crawl", "Run", true),
        new Clip("Nervously Look Around", "LookAround", true),
        new Clip("Mutant Swiping", "Attack", false),

        new Clip("Zombie Scream", "Alert", false, true),
        new Clip("Mutant Roaring", "Roar", false, true),
        new Clip("Kneeling Inspecting", "Inspect", true, feet: true),
        new Clip("Zombie Reaction Hit", "Flinch", false, true),
        new Clip("Zombie Agonizing", "Stunned", true),
        new Clip("Crawling", "Crawl", true),
        new Clip("Low Crawl", "LowCrawl", true),
        new Clip("Mutant Left Turn 45", "TurnLeft45", false, true),
        new Clip("Mutant Right Turn 45", "TurnRight45", false, true),
        new Clip("Mutant Right Turn 90", "TurnRight90", false, true),
        new Clip("Zombie Neck Bite", "Feed", true, feet: true),
    };


    private static readonly List<string> report = new List<string>();


    [MenuItem("Tools/AI Director/Creature - Install Model", priority = 70)]
    public static void InstallFromMenu()
    {
        EditorUtility.DisplayDialog("Creature", Install(), "OK");
    }


    /// <summary>Callable from the Unity CLI; returns the report.</summary>
    public static string Install()
    {
        report.Clear();

        Avatar avatar = ImportModel();

        if (avatar == null || !avatar.isHuman)
        {
            report.Add("FAILED - the model did not produce a valid Humanoid avatar");

            return Report();
        }

        Dictionary<string, AnimationClip> clips = ImportClips(avatar);

        AnimatorController controller = BuildController(clips);

        Material skin = BuildMaterial();

        SwapModel(controller, avatar, skin, clips);

        return Report();
    }


    private static string Report()
    {
        StringBuilder text = new StringBuilder();

        foreach (string line in report)
        {
            text.AppendLine(line);
        }

        Debug.Log("[CreatureSetup]\n" + text);

        return text.ToString();
    }


    // =========================================================
    // IMPORT
    // =========================================================

    private static Avatar ImportModel()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;

        if (importer == null)
        {
            report.Add("MISSING " + ModelPath);

            return null;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.useFileScale = true;

        ConfigureClips(importer, Clips[0]);

        importer.SaveAndReimport();

        Avatar avatar = null;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Avatar a) avatar = a;
        }

        report.Add(avatar != null && avatar.isHuman
            ? "Avatar: Humanoid, valid (" + avatar.name + ")"
            : "Avatar: INVALID");

        return avatar;
    }


    private static Dictionary<string, AnimationClip> ImportClips(Avatar avatar)
    {
        Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();

        foreach (Clip spec in Clips)
        {
            string path = Dir + "Creature@" + spec.File + ".fbx";

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;

            if (importer == null)
            {
                report.Add("MISSING clip " + path);

                continue;
            }

            if (path != ModelPath)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;

                ConfigureClips(importer, spec);

                importer.SaveAndReimport();
            }

            AnimationClip clip = LoadClip(path);

            if (clip == null)
            {
                report.Add("NO CLIP in " + path);

                continue;
            }

            clip.name = spec.File;

            result[spec.State] = clip;
        }

        report.Add(result.Count + "/" + Clips.Length + " clips imported as Humanoid");

        return result;
    }


    private static void ConfigureClips(ModelImporter importer, Clip spec)
    {
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].name = spec.File;
            clips[i].loopTime = spec.Loop;
            clips[i].loopPose = spec.Loop;

            /* Facing stays with the agent; height stays with the pose. */

            clips[i].lockRootRotation = true;
            clips[i].keepOriginalOrientation = true;

            clips[i].lockRootHeightY = true;
            clips[i].keepOriginalPositionY = true;
            clips[i].heightFromFeet = spec.FeetGrounded;

            clips[i].lockRootPositionXZ = false;
            clips[i].keepOriginalPositionXZ = true;
        }

        importer.clipAnimations = clips;
    }


    private static AnimationClip LoadClip(string path)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        }

        return null;
    }


    // =========================================================
    // CONTROLLER
    // =========================================================

    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idle = null;

        foreach (Clip spec in Clips)
        {
            if (!clips.TryGetValue(spec.State, out AnimationClip clip))
                continue;

            controller.AddParameter(spec.State, AnimatorControllerParameterType.Trigger);

            AnimatorState state = machine.AddState(spec.State);

            state.motion = clip;

            if (spec.State == "Idle")
            {
                idle = state;

                machine.defaultState = state;
            }

            /*
             * Any State, exit time off, no self-transition: the same
             * shape XenoQuickSetup settled on, for the same reasons.
             */

            AnimatorStateTransition enter = machine.AddAnyStateTransition(state);

            enter.hasExitTime = false;
            enter.duration = spec.State == "Run" ? 0.25f : 0.2f;
            enter.hasFixedDuration = true;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, spec.State);
        }

        /* One-shots drift back to idle unless something else is asked for. */

        if (idle != null)
        {
            foreach (ChildAnimatorState child in machine.states)
            {
                Clip spec = System.Array.Find(Clips, c => c.State == child.state.name);

                if (!spec.OneShot)
                    continue;

                AnimatorStateTransition back = child.state.AddTransition(idle);

                back.hasExitTime = true;
                back.exitTime = 0.92f;
                back.duration = 0.25f;
                back.hasFixedDuration = true;
            }
        }

        EditorUtility.SetDirty(controller);

        AssetDatabase.SaveAssets();

        report.Add("Controller: " + ControllerPath + " (" + machine.states.Length + " states)");

        return controller;
    }


    // =========================================================
    // MATERIAL
    // =========================================================

    private static Material BuildMaterial()
    {
        Texture2D baseMap = Texture(TextureDir + "Skin_Base_color.png", TextureImporterType.Default, true);
        Texture2D normal = Texture(TextureDir + "Skin_Normal_OpenGL.png", TextureImporterType.NormalMap, false);
        Texture2D gloss = Texture(TextureDir + "Skin_MetallicSmoothness.png", TextureImporterType.Default, false);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            AssetDatabase.CreateAsset(mat, MaterialPath);
        }

        mat.SetTexture("_BaseMap", baseMap);
        mat.SetColor("_BaseColor", Color.white);

        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");

        /* Smoothness lives in the alpha of the metallic map (1 - roughness). */

        mat.SetTexture("_MetallicGlossMap", gloss);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.SetFloat("_Smoothness", 0.85f);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");

        EditorUtility.SetDirty(mat);

        report.Add("Material: " + MaterialPath);

        return mat;
    }


    private static Texture2D Texture(string path, TextureImporterType type, bool srgb)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer != null && (importer.textureType != type || importer.sRGBTexture != srgb))
        {
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }


    // =========================================================
    // SWAP
    // =========================================================

    private static void SwapModel(
        AnimatorController controller,
        Avatar avatar,
        Material skin,
        Dictionary<string, AnimationClip> clips)
    {
        PerceptionSystem creature = Object.FindFirstObjectByType<PerceptionSystem>();

        if (creature == null)
        {
            report.Add("No creature in the open scene - assets are ready, nothing swapped");

            return;
        }

        GameObject rig = creature.gameObject;

        Transform old = rig.transform.Find("Model");

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rig.scene);

        model.name = "Model";

        model.transform.SetParent(rig.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        Animator animator = model.GetComponent<Animator>();

        if (animator == null)
        {
            animator = model.AddComponent<Animator>();
        }

        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.sharedMaterial = skin;
            smr.updateWhenOffscreen = true;
        }

        int npc = LayerMask.NameToLayer(GameLayers.NpcName);

        if (npc >= 0)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = npc;
            }
        }

        /* Point the driver at the new Animator and the new clips' speeds. */

        XenoAnimatorDriver driver = rig.GetComponent<XenoAnimatorDriver>();

        if (driver != null)
        {
            SerializedObject so = new SerializedObject(driver);

            so.FindProperty("animator").objectReferenceValue = animator;

            SetSpeed(so, "walkClipReferenceSpeed", clips, "Walk");
            SetSpeed(so, "runClipReferenceSpeed", clips, "Run");
            SetSpeed(so, "prowlClipReferenceSpeed", clips, "Prowl");

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Bounds b = new Bounds(model.transform.position, Vector3.zero);

        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            b.Encapsulate(r.bounds);
        }

        report.Add("Model swapped onto " + rig.name + " - height " + b.size.y.ToString("F2") + " m");

        EditorSceneManager.MarkSceneDirty(rig.scene);

        EditorSceneManager.SaveScene(rig.scene);
    }


    /// <summary>
    /// The ground speed a locomotion clip was captured at, so the driver
    /// can scale playback to the agent's speed and the hands and feet do
    /// not skate.
    /// </summary>
    private static void SetSpeed(
        SerializedObject so, string property,
        Dictionary<string, AnimationClip> clips, string state)
    {
        SerializedProperty p = so.FindProperty(property);

        if (p == null || !clips.TryGetValue(state, out AnimationClip clip))
            return;

        float speed = MeasureContactSpeed(clip);

        if (speed < 0.1f)
        {
            report.Add(state + " clip: could not measure travel - reference speed left at " +
                       p.floatValue.ToString("F2"));

            return;
        }

        p.floatValue = speed;

        report.Add(state + " clip travels at " + speed.ToString("F2") + " m/s (measured from contact)");
    }


    /// <summary>
    /// How fast an In Place clip "really" moves. The root does not
    /// travel, so the speed is read off the limbs instead: whichever
    /// hand or foot is lowest is the one on the ground, and on the ground
    /// it slides backwards relative to the body at exactly the speed the
    /// body would be moving forwards. Hands count because the run is on
    /// all fours.
    /// </summary>
    private static float MeasureContactSpeed(AnimationClip clip)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        GameObject probe = Object.Instantiate(prefab);

        probe.hideFlags = HideFlags.HideAndDontSave;

        probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Animator animator = probe.GetComponent<Animator>();

        HumanBodyBones[] limbs =
        {
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand
        };

        const int samples = 90;

        float dt = clip.length / samples;

        float travelled = 0f, time = 0f;

        try
        {
            AnimationMode.StartAnimationMode();

            Transform previous = null;

            float previousZ = 0f;

            for (int i = 0; i <= samples; i++)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(probe, clip, i * dt);
                AnimationMode.EndSampling();

                Transform lowest = null;

                foreach (HumanBodyBones limb in limbs)
                {
                    Transform t = animator.GetBoneTransform(limb);

                    if (t != null && (lowest == null || t.position.y < lowest.position.y))
                        lowest = t;
                }

                if (lowest == null)
                    break;

                float z = probe.transform.InverseTransformPoint(lowest.position).z;

                /* Only count a frame where the same limb stayed planted. */

                if (lowest == previous)
                {
                    float dz = previousZ - z;

                    if (dz > 0f)
                    {
                        travelled += dz;
                        time += dt;
                    }
                }

                previous = lowest;
                previousZ = z;
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();

            Object.DestroyImmediate(probe);
        }

        return time > 0f ? travelled / time : 0f;
    }
}

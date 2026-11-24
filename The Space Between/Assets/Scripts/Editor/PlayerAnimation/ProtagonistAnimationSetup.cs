using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Builds everything the Protagonist character needs to replace the placeholder body:
    ///   1. Imports Protagonist.fbx (the Mixamo auto-rig) as a Humanoid with its own avatar.
    ///   2. Turns on Loop Pose for the looping Mixamo clips, so their seams do not pop.
    ///   3. Makes Protagonist.controller from animation_Trial.controller (all the Mixamo clips in one controller), with
    ///      the walk and run speeds matched to the movement. The fall is the plain Mixamo fall, with the same blends as
    ///      animation_Trial.controller. The wall run uses the same Running and Fast Run as the ground, with no fall in it.
    ///   4. Makes the Protagonist prefab: the rigged model with its Animator, CharacterFacing and SwapArmAim, and the
    ///      textured Head, Clothes and Hair materials when the textured mesh exists (grey CHR_Jacket otherwise).
    /// Running it again updates what is already there. animation_Trial.controller itself is never changed.
    /// </summary>
    public static class ProtagonistAnimationSetup
    {
        private const string MenuPath = "Tools/Player Animation/Build Protagonist Character";

        private const string ModelPath = "Assets/Animation/Mixamo/Protagonist.fbx";
        private const string MixamoFolder = "Assets/Animation/Mixamo";
        private const string SourceControllerPath = "Assets/Animation/Mixamo/animation_Trial.controller";
        private const string OutputFolder = "Assets/Animation/Protagonist";
        private const string ControllerPath = OutputFolder + "/Protagonist.controller";
        private const string PrefabFolder = "Assets/Prefabs/Characters";
        private const string PrefabPath = PrefabFolder + "/Protagonist.prefab";
        private const string MaterialPath = "Assets/Materials/Architecture/CHR_Jacket.mat"; // grey fallback

        // Mixamo merged the model's three materials into one, so the textured mesh is a copy of the rigged mesh split
        // back into Head, Clothes and Hair (same vertices, UVs and skinning), plus the jacket as a fourth part. Head,
        // Clothes and Hair hold the Substance textures; the jacket is a rough silver material made in Unity.
        private const string TexturedMeshPath = OutputFolder + "/Protagonist_Mesh_Textured.asset";
        private static readonly string[] TexturedMaterialPaths =
        {
            "Assets/Textures/Protagonist/Head/Protagonist_Head.mat",
            "Assets/Textures/Protagonist/Body/Protagonist_Body.mat",
            "Assets/Textures/Protagonist/Hair/Protagonist_Hair.mat",
            "Assets/Textures/Protagonist/Body/Protagonist_Jacket.mat",
        };

        // Clips that loop, by their name inside the Mixamo FBX files.
        private static readonly string[] LoopingClips = { "Idle", "Walk", "Run", "FastRun", "Fall" };

        //==================== WALK AND RUN SPEEDS =====================
        // Measured on the Protagonist from foot contact: the Mixamo Walking clip covers 1.57 m/s, Running 3.71 m/s and
        // Fast Run 4.74 m/s. The player moves at 6 and runs with Shift at 10 (PlaceholderPlayerController). Moving uses
        // Running; Shift uses Fast Run, a different clip with a deeper forward lean and a bigger arm swing, so the run is
        // its own movement and not the same one played faster.
        private const float WalkThreshold = 2.2f;   // walk only while speeding up or slowing down
        private const float WalkTimeScale = 1.35f;  // 2.1 m/s of foot travel at 2.2
        private const float JogThreshold = 6f;      // Move Speed
        private const float JogTimeScale = 1.4f;    // 5.2 m/s of foot travel at 6
        private const float SprintThreshold = 10f;  // Run Speed
        // 8.1 m/s of foot travel at 10, a 0.31 s stride cycle. Matching 10 exactly would need 2.1, about 8 steps a
        // second, which looks frantic; a little foot slip reads better side-on than that.
        private const float SprintTimeScale = 1.7f;
        // Fast Run puts the left foot furthest forward 0.06 of a cycle earlier than Running, so it is shifted to match,
        // and the legs never cross while the two blend.
        private const float SprintCycleOffset = 0.94f;

        //==================== BLENDS (seconds) =====================
        private const float IntoSlideSeconds = 0.25f;   // the hips drop about half a metre; faster reads as a pop

        [MenuItem(MenuPath)]
        public static void Build()
        {
            EnsureFolder(OutputFolder);
            EnsureFolder(PrefabFolder);

            ConfigureModel();
            ConfigureLoops();
            AnimatorController controller = BuildController();
            if (controller == null)
            {
                return;
            }

            BuildPrefab(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"Protagonist: model, {controller.name} and {PrefabPath} are ready.");
        }

        //==================== 1. MODEL =====================
        private static void ConfigureModel()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer == null)
            {
                Debug.LogWarning($"Protagonist: no model at {ModelPath}. Download it from Mixamo (FBX, T-pose, with skin) first.");
                return;
            }

            bool changed = importer.animationType != ModelImporterAnimationType.Human
                           || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                           || importer.importAnimation || importer.importCameras || importer.importLights;
            if (!changed)
            {
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }

        //==================== 2. LOOP POSE =====================
        private static void ConfigureLoops()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MixamoFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer == null || importer.clipAnimations.Length == 0)
                {
                    continue;
                }

                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                bool changed = false;
                foreach (ModelImporterClipAnimation clip in clips.Where(c => LoopingClips.Contains(c.name) && c.loopTime && !c.loopPose))
                {
                    clip.loopPose = true;
                    changed = true;
                }

                if (changed)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                }
            }
        }

        //==================== 3. CONTROLLER =====================
        private static AnimatorController BuildController()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null
                && !AssetDatabase.CopyAsset(SourceControllerPath, ControllerPath))
            {
                Debug.LogWarning($"Protagonist: could not copy {SourceControllerPath} to {ControllerPath}.");
                return null;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState locomotion = FindState(machine, "Locomotion");
            AnimatorState fall = FindState(machine, "Fall");
            if (locomotion == null || fall == null || !(locomotion.motion is BlendTree tree))
            {
                Debug.LogWarning($"Protagonist: {controller.name} needs a Locomotion blend tree and a Fall state.");
                return null;
            }

            // Locomotion: idle, a short walk, Running at the move speed, then Fast Run at the run speed.
            AnimationClip idle = FindMixamoClip("Idle");
            AnimationClip walk = FindMixamoClip("Walk");
            AnimationClip run = FindMixamoClip("Run");
            AnimationClip fastRun = FindMixamoClip("FastRun");
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                Child(idle, 0f, 1f),
                Child(walk, WalkThreshold, WalkTimeScale),
                Child(run, JogThreshold, JogTimeScale),
                Child(fastRun, SprintThreshold, SprintTimeScale, SprintCycleOffset),
            };
            EditorUtility.SetDirty(tree);
            BuildWallRun(machine, tree);

            // The fall is the plain Mixamo fall, blended in and out exactly as in animation_Trial.controller.
            fall.motion = FindMixamoClip("Fall");
            fall.speed = 1f;
            MatchFallBlends(machine, AssetDatabase.LoadAssetAtPath<AnimatorController>(SourceControllerPath));

            foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
            {
                if (transition.destinationState != null && transition.destinationState.name == "Slide")
                {
                    transition.hasFixedDuration = true;
                    transition.duration = IntoSlideSeconds;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        // Wall run: the same Running and Fast Run as on the ground, at the same speeds, so running up a wall looks like
        // running and not like a slow jog. Standing still on a wall keeps the running cycle; there is no Fall in this tree,
        // so the fall only plays once the player has let go of the wall. Traverse (the body turning along a curve) uses it too.
        private static void BuildWallRun(AnimatorStateMachine machine, BlendTree locomotion)
        {
            AnimatorState wallRun = FindState(machine, "WallRun");
            if (wallRun == null)
            {
                Debug.LogWarning("Protagonist: the controller has no WallRun state, so the wall run animation was not set up.");
                return;
            }

            // The tree copied from animation_Trial.controller lives inside this controller, so it is reused and renamed.
            var tree = wallRun.motion as BlendTree;
            if (tree == null || AssetDatabase.GetAssetPath(tree) != ControllerPath)
            {
                tree = new BlendTree { hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, ControllerPath);
            }

            tree.name = "Wall Run";
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            // Copied from the ground's running children, so the two always match, whatever they are tuned to.
            tree.children = locomotion.children.Where(child => child.threshold >= JogThreshold).ToArray();
            EditorUtility.SetDirty(tree);

            wallRun.motion = tree;
            wallRun.speed = 1f;

            AnimatorState traverse = FindState(machine, "Traverse");
            if (traverse != null)
            {
                traverse.motion = tree;
                traverse.speed = 1f;
            }
        }

        // Protagonist.controller started as a copy, so its states list their transitions in the same order as the source.
        private static void MatchFallBlends(AnimatorStateMachine machine, AnimatorController source)
        {
            if (source == null)
            {
                return;
            }

            AnimatorStateMachine sourceMachine = source.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states)
            {
                AnimatorState sourceState = FindState(sourceMachine, child.state.name);
                if (sourceState == null || sourceState.transitions.Length != child.state.transitions.Length)
                {
                    continue;
                }

                for (int i = 0; i < child.state.transitions.Length; i++)
                {
                    AnimatorStateTransition transition = child.state.transitions[i];
                    AnimatorStateTransition original = sourceState.transitions[i];
                    bool touchesFall = child.state.name == "Fall" || (transition.destinationState != null && transition.destinationState.name == "Fall");
                    if (touchesFall && original.destinationState != null && transition.destinationState != null
                        && original.destinationState.name == transition.destinationState.name)
                    {
                        transition.hasFixedDuration = original.hasFixedDuration;
                        transition.duration = original.duration;
                    }
                }
            }
        }

        private static ChildMotion Child(Motion motion, float threshold, float timeScale, float cycleOffset = 0f)
        {
            return new ChildMotion
            {
                motion = motion, threshold = threshold, timeScale = timeScale, cycleOffset = cycleOffset, directBlendParameter = "Speed"
            };
        }

        //==================== 4. PREFAB =====================
        private static void BuildPrefab(AnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                instance.name = "Protagonist";
                Animator animator = instance.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; // the PlaceholderPlayerController moves the player
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                if (instance.GetComponent<CharacterFacing>() == null)
                {
                    instance.AddComponent<CharacterFacing>();
                }

                if (instance.GetComponent<SwapArmAim>() == null)
                {
                    instance.AddComponent<SwapArmAim>();
                }

                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                var texturedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(TexturedMeshPath);
                Material[] texturedMaterials = TexturedMaterialPaths.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
                bool textured = texturedMesh != null && texturedMesh.subMeshCount == texturedMaterials.Length
                                && texturedMaterials.All(m => m != null);
                foreach (SkinnedMeshRenderer skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (textured)
                    {
                        skin.sharedMesh = texturedMesh;
                        skin.sharedMaterials = texturedMaterials;
                    }
                    else if (material != null)
                    {
                        skin.sharedMaterials = Enumerable.Repeat(material, skin.sharedMaterials.Length).ToArray();
                    }

                    skin.updateWhenOffscreen = true; // wide poses like the roll and wall jump are never culled at screen edges
                }

                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        //==================== HELPERS =====================
        private static AnimationClip FindMixamoClip(string clipName)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MixamoFolder }))
            {
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid))
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == clipName);
                if (clip != null)
                {
                    return clip;
                }
            }

            Debug.LogWarning($"Protagonist: no Mixamo clip named '{clipName}' in {MixamoFolder}.");
            return null;
        }

        private static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
        {
            return machine.states.Select(child => child.state).FirstOrDefault(state => state.name == stateName);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}

using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Adds the landing roll to the player's Animator controller once the Mixamo "Falling To Roll" clip is in the project.
    /// It sets the clip up for the Y Bot rig, then adds a Roll state that Fall enters on the HardLand trigger and that returns
    /// to Locomotion when it finishes. Running it again only updates what is already there.
    /// </summary>
    public static class LandingRollSetup
    {
        private const string MenuPath = "Tools/Player Animation/Add Landing Roll";
        private const string ControllerPath = "Assets/Animation/Mixamo/animation_Trial.controller";
        private const string ClipFolder = "Assets/Animation/Mixamo";
        private const string ClipSearch = "Falling To Roll t:Model";
        private const string AvatarSourcePath = "Assets/Animation/Mixamo/Y Bot@Breathing Idle.fbx";

        private const string RollState = "Roll";
        private const string ClipName = "Roll";
        private const string HardLand = "HardLand";

        // Matches the rest of the controller: a quick blend into an impact, a gentle one back out.
        private const float EnterSeconds = 0.1f;
        private const float LeaveSeconds = 0.2f;
        private const float FinishedAt = 0.85f;
        private const float FallingSpeed = -4f;

        /// <summary>Finds the roll clip in the Mixamo folder and adds it to the player's controller.</summary>
        [MenuItem(MenuPath)]
        public static void AddFromProject()
        {
            string[] found = AssetDatabase.FindAssets(ClipSearch, new[] { ClipFolder });
            if (found.Length == 0)
            {
                Debug.LogWarning($"Landing roll: no 'Falling To Roll' FBX in {ClipFolder}. Download it from Mixamo first.");
                return;
            }

            string clipPath = AssetDatabase.GUIDToAssetPath(found[0]);
            ConfigureImport(clipPath);

            AnimationClip clip = LoadClip(clipPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (clip == null || controller == null)
            {
                Debug.LogWarning($"Landing roll: could not load the clip in {clipPath} or the controller at {ControllerPath}.");
                return;
            }

            AddRollState(controller, clip);
            Debug.Log($"Landing roll: '{clip.name}' from {clipPath} is now the Roll state of {controller.name}.");
        }

        //==================== IMPORT =====================
        /// <summary>
        /// Humanoid with the Y Bot avatar, played once. Height and turning stay in the pose so the body drops and rolls over;
        /// any forward travel is left out of the pose, so the body stays on the player's capsule.
        /// </summary>
        public static void ConfigureImport(string clipPath)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(clipPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarSourcePath);

            // Keep a start frame trimmed by hand, so the falling part before the ground contact stays cut.
            ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            ModelImporterClipAnimation roll = clips[0];
            roll.name = ClipName;
            roll.loopTime = false;
            roll.lockRootRotation = true;
            roll.keepOriginalOrientation = true;
            roll.lockRootHeightY = true;
            roll.keepOriginalPositionY = true;
            roll.lockRootPositionXZ = false;
            roll.keepOriginalPositionXZ = true;
            importer.clipAnimations = new[] { roll };
            importer.SaveAndReimport();
        }

        private static AnimationClip LoadClip(string clipPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(clipPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview"));
        }

        //==================== CONTROLLER =====================
        /// <summary>Adds or updates the Roll state on the first layer of the controller, with its transitions.</summary>
        public static AnimatorState AddRollState(AnimatorController controller, AnimationClip clip)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState locomotion = FindState(machine, "Locomotion");
            AnimatorState fall = FindState(machine, "Fall");
            AnimatorState wallRun = FindState(machine, "WallRun");
            if (locomotion == null || fall == null || wallRun == null)
            {
                Debug.LogWarning($"Landing roll: {controller.name} needs Locomotion, Fall and WallRun states.");
                return null;
            }

            if (controller.parameters.All(parameter => parameter.name != HardLand))
            {
                controller.AddParameter(HardLand, AnimatorControllerParameterType.Trigger);
            }

            AnimatorState roll = FindState(machine, RollState) ?? machine.AddState(RollState, new Vector3(560f, 300f, 0f));
            roll.motion = clip;
            roll.writeDefaultValues = locomotion.writeDefaultValues;

            // The roll is only reached from Fall, never from Any State, so it cannot cut into a wall run, a swap or a slide.
            foreach (AnimatorStateTransition old in machine.anyStateTransitions.Where(transition => transition.destinationState == roll).ToArray())
            {
                machine.RemoveAnyStateTransition(old);
            }

            foreach (AnimatorStateTransition old in fall.transitions.Where(transition => transition.destinationState == roll).ToArray())
            {
                fall.RemoveTransition(old);
            }

            foreach (AnimatorStateTransition old in roll.transitions.ToArray())
            {
                roll.RemoveTransition(old);
            }

            // Right after Fall's wall run transition, so on the landing frame it wins over the normal landing and the plain
            // return to Locomotion, while touching a wall still starts the wall run as before.
            AnimatorStateTransition enter = fall.AddTransition(roll);
            Configure(enter, EnterSeconds);
            enter.AddCondition(AnimatorConditionMode.If, 0f, HardLand);
            AnimatorStateTransition[] others = fall.transitions.Where(transition => transition != enter).ToArray();
            fall.transitions = others.Where(transition => transition.destinationState == wallRun)
                .Append(enter)
                .Concat(others.Where(transition => transition.destinationState != wallRun))
                .ToArray();

            AnimatorStateTransition toWallRun = roll.AddTransition(wallRun);
            Configure(toWallRun, LeaveSeconds);
            toWallRun.AddCondition(AnimatorConditionMode.If, 0f, "IsWallRunning");

            // Rolling off an edge falls instead of finishing the roll in mid-air.
            AnimatorStateTransition toFall = roll.AddTransition(fall);
            Configure(toFall, LeaveSeconds);
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");
            toFall.AddCondition(AnimatorConditionMode.Less, FallingSpeed, "VerticalSpeed");

            AnimatorStateTransition finished = roll.AddTransition(locomotion);
            Configure(finished, LeaveSeconds);
            finished.hasExitTime = true;
            finished.exitTime = FinishedAt;

            // Only this controller is written, so other unsaved assets are left as they are.
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return roll;
        }

        private static void Configure(AnimatorStateTransition transition, float seconds)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = seconds;
            transition.offset = 0f;
        }

        private static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
        {
            return machine.states.Select(child => child.state).FirstOrDefault(state => state.name == stateName);
        }
    }
}

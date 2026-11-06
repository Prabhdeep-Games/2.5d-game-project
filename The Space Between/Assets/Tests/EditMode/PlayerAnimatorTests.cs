using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Checks the player's Animator controller: the Locomotion blend tree follows the movement's real speeds, and the landing
    /// and the roll are entered from Fall. Editing a blend tree's threshold range in the Inspector respaces its children
    /// evenly, which once left walking and running at 0.5 and 1, so this catches that. Reads assets only, opens no scene.
    /// </summary>
    public class PlayerAnimatorTests
    {
        private const string ControllerPath = "Assets/Animation/Mixamo/animation_Trial.controller";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";

        private static AnimatorStateMachine Machine()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.NotNull(controller, $"No controller at {ControllerPath}.");
            return controller.layers[0].stateMachine;
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name)
        {
            AnimatorState state = machine.states.Select(child => child.state).FirstOrDefault(candidate => candidate.name == name);
            Assert.NotNull(state, $"The controller needs a {name} state.");
            return state;
        }

        private static float PlayerSpeed(string field)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var serialized = new SerializedObject(prefab.GetComponent<PlaceholderPlayerController>());
            return serialized.FindProperty(field).floatValue;
        }

        [Test]
        public void Locomotion_IsA1DBlendTree_AtTheMovementsRealSpeeds()
        {
            AnimatorStateMachine machine = Machine();
            AnimatorState locomotion = State(machine, "Locomotion");
            Assert.AreEqual(locomotion, machine.defaultState, "Locomotion should be the default state.");

            var tree = locomotion.motion as BlendTree;
            Assert.NotNull(tree, "Locomotion should play a blend tree.");
            Assert.AreEqual(BlendTreeType.Simple1D, tree.blendType);
            Assert.AreEqual("Speed", tree.blendParameter);

            ChildMotion[] children = tree.children;
            Assert.AreEqual(3, children.Length, "Idle, Walk and Run.");
            Assert.AreEqual(0f, children[0].threshold, 0.01f, "Idle should sit at a standstill.");
            Assert.AreEqual(PlayerSpeed("moveSpeed"), children[1].threshold, 0.01f, "Walk should sit at the walking speed.");
            Assert.AreEqual(PlayerSpeed("runSpeed"), children[2].threshold, 0.01f, "Run should sit at the running speed.");
        }

        [Test]
        public void Landings_AreEnteredFromFall_RollFirst()
        {
            AnimatorStateMachine machine = Machine();
            AnimatorState fall = State(machine, "Fall");
            AnimatorState land = State(machine, "Land");
            AnimatorState roll = State(machine, "Roll");

            Assert.IsFalse(machine.anyStateTransitions.Any(transition => transition.destinationState == land || transition.destinationState == roll),
                "Landings should not come from Any State, where they can cut into other animations.");

            var order = fall.transitions.Select(transition => transition.destinationState).ToList();
            Assert.Less(order.IndexOf(roll), order.IndexOf(land), "The roll should be checked before the normal landing.");
            Assert.Less(order.IndexOf(land), order.IndexOf(State(machine, "Locomotion")), "Both landings should be checked before the plain return.");
            Assert.IsTrue(fall.transitions.First(transition => transition.destinationState == roll).conditions.Any(condition => condition.parameter == "HardLand"));

            Assert.IsTrue(roll.transitions.Any(transition => transition.destinationState.name == "Locomotion" && transition.hasExitTime),
                "The roll should finish back in Locomotion.");
        }
    }
}

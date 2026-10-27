using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class PlaceholderPlayerControllerTests : InputTestFixture
    {
        private GameObject playerObject;
        private PlaceholderPlayerController controller;
        private Keyboard keyboard;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();

            playerObject = new GameObject("Controller Test Player");
            playerObject.SetActive(false);
            playerObject.AddComponent<CharacterController>();
            controller = playerObject.AddComponent<PlaceholderPlayerController>();

            // Bindings normally come from Reset in the Editor, so a test sets its own.
            SetField("jumpAction", new InputAction("Jump", InputActionType.Button, "<Keyboard>/space"));
            SetField("moveAction", new InputAction("Move", InputActionType.Value));
            playerObject.SetActive(true);
        }

        public override void TearDown()
        {
            Object.DestroyImmediate(playerObject);
            base.TearDown();
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(PlaceholderPlayerController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, name);
            field.SetValue(controller, value);
        }

        private float VerticalSpeed()
        {
            FieldInfo field = typeof(PlaceholderPlayerController).GetField("verticalSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
            return (float)field.GetValue(controller);
        }

        private IEnumerator PressJumpOnce()
        {
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InTheAirWithoutAGrant_PressingJumpDoesNothing()
        {
            yield return null;
            yield return null;
            float before = VerticalSpeed();

            yield return PressJumpOnce();

            Assert.Less(VerticalSpeed(), before + 1f, "No jump should have been applied.");
            Assert.Less(VerticalSpeed(), 0f);
        }

        [UnityTest]
        public IEnumerator InTheAirWithAGrant_JumpWorksExactlyOnce()
        {
            yield return null;
            controller.GrantAirJump();

            Press(keyboard.spaceKey);
            yield return null;
            float afterFirstJump = VerticalSpeed();
            Release(keyboard.spaceKey);
            yield return null;

            Assert.Greater(afterFirstJump, 0f, "The granted jump should launch upwards.");

            // Let the jump fade so a second press is clearly distinguishable.
            for (int i = 0; i < 80 && VerticalSpeed() > -2f; i++)
            {
                yield return null;
            }

            float beforeSecond = VerticalSpeed();
            yield return PressJumpOnce();

            Assert.Less(VerticalSpeed(), beforeSecond + 1f, "The second press must not jump again.");
        }
    }
}

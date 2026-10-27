using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class WallRunTests : InputTestFixture
    {
        private const float RampAngle = 60f;
        private const float RampLength = 10f;
        private const float RampStartX = 4f;
        private const float WatchSeconds = 1.4f;

        private Keyboard keyboard;
        private GameObject playerObject;
        private PlaceholderPlayerController controller;
        private GameObject floor;
        private GameObject ramp;

        public override void Setup()
        {
            base.Setup();

            // Held keys must survive the editor window not having focus while the tests run.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            keyboard = InputSystem.AddDevice<Keyboard>();

            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "WallRun Test Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(80f, 1f, 10f);

            Physics.SyncTransforms();
        }

        public override void TearDown()
        {
            if (playerObject != null)
            {
                Object.DestroyImmediate(playerObject);
            }

            if (ramp != null)
            {
                Object.DestroyImmediate(ramp);
            }

            Object.DestroyImmediate(floor);
            base.TearDown();
        }

        private void BuildRamp(bool marked)
        {
            ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "WallRun Test Ramp";
            ramp.transform.localScale = new Vector3(RampLength, 0.5f, 6f);
            ramp.transform.rotation = Quaternion.Euler(0f, 0f, RampAngle);

            // The low end of the top face meets the floor at RampStartX.
            Vector3 along = new Vector3(Mathf.Cos(RampAngle * Mathf.Deg2Rad), Mathf.Sin(RampAngle * Mathf.Deg2Rad), 0f);
            Vector3 up = new Vector3(-Mathf.Sin(RampAngle * Mathf.Deg2Rad), Mathf.Cos(RampAngle * Mathf.Deg2Rad), 0f);
            Vector3 topCenter = new Vector3(RampStartX, 0f, 0f) + along * (RampLength * 0.5f);
            ramp.transform.position = topCenter - up * 0.25f;

            if (marked)
            {
                ramp.AddComponent<WallRunSurface>();
            }

            Physics.SyncTransforms();
        }

        private void BuildPlayer()
        {
            playerObject = new GameObject("WallRun Test Player");
            playerObject.SetActive(false);
            playerObject.transform.position = new Vector3(-2f, 1.001f, 0f);
            CharacterController cc = playerObject.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.5f;
            cc.slopeLimit = 45f;
            controller = playerObject.AddComponent<PlaceholderPlayerController>();

            InputAction move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            SetField("moveAction", move);
            SetField("jumpAction", new InputAction("Jump", InputActionType.Button, "<Keyboard>/space"));
            SetField("runAction", new InputAction("Run", InputActionType.Button, "<Keyboard>/leftShift"));
            playerObject.SetActive(true);
        }

        private void SetField(string name, object value)
        {
            System.Reflection.FieldInfo field = typeof(PlaceholderPlayerController).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field, name);
            field.SetValue(controller, value);
        }

        private string Diagnostics()
        {
            object Read(string n) => typeof(PlaceholderPlayerController).GetField(n, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
            InputAction moveAction = (InputAction)Read("moveAction");
            return $"dPressed={keyboard.dKey.isPressed} moveValue={moveAction.ReadValue<float>()} enabled={moveAction.enabled} controls={moveAction.controls.Count} pos={playerObject.transform.position} running={Read("isRunning")} contactNormal={Read("contactNormal")} contactTime={Read("contactTime")} now={Time.time} cooldown={Read("cooldownUntil")}";
        }

        // Two keys queued in the same frame can cancel each other in the test keyboard, so they are pressed a frame apart.
        private IEnumerator HoldKeys(bool run)
        {
            Press(keyboard.dKey);
            yield return null;

            if (run)
            {
                Press(keyboard.leftShiftKey);
                yield return null;
            }
        }

        // Holds the keys for a while and reports how high the player got and whether a wall run happened.
        private IEnumerator Watch(bool run, System.Action<float, bool> report)
        {
            yield return HoldKeys(run);

            float maxY = 0f;
            bool wallRan = false;
            float end = Time.time + WatchSeconds;
            while (Time.time < end)
            {
                yield return null;
                maxY = Mathf.Max(maxY, playerObject.transform.position.y);
                wallRan |= controller.IsWallRunning;
            }

            report(maxY, wallRan);
        }

        [UnityTest]
        public IEnumerator RunningIntoAMarkedRamp_ClimbsIt()
        {
            BuildRamp(marked: true);
            BuildPlayer();
            yield return null;

            float maxY = 0f;
            bool wallRan = false;
            yield return Watch(run: true, (y, ran) => { maxY = y; wallRan = ran; });

            Assert.IsTrue(wallRan, $"A wall run should have started. {Diagnostics()}");
            Assert.Greater(maxY, 4f, $"The player only reached {maxY:F2} units high.");
        }

        [UnityTest]
        public IEnumerator WalkingIntoAMarkedRamp_DoesNotClimbIt()
        {
            BuildRamp(marked: true);
            BuildPlayer();
            yield return null;

            float maxY = 0f;
            bool wallRan = false;
            yield return Watch(run: false, (y, ran) => { maxY = y; wallRan = ran; });

            Assert.IsFalse(wallRan, "Walking must not start a wall run.");
            Assert.Less(maxY, 2f);
        }

        [UnityTest]
        public IEnumerator RunningIntoAnUnmarkedRamp_DoesNotClimbIt()
        {
            BuildRamp(marked: false);
            BuildPlayer();
            yield return null;

            float maxY = 0f;
            bool wallRan = false;
            yield return Watch(run: true, (y, ran) => { maxY = y; wallRan = ran; });

            Assert.IsFalse(wallRan, "A surface without WallRunSurface is just a wall.");
            Assert.Less(maxY, 2f);
        }

        [UnityTest]
        public IEnumerator RunningIntoACurvedWall_FollowsTheCurveUpToTheTop()
        {
            ramp = new GameObject("WallRun Test Curve");
            ramp.transform.position = new Vector3(RampStartX, 0f, 0f);
            ramp.AddComponent<CurvedWall>();
            Physics.SyncTransforms();
            BuildPlayer();
            yield return null;

            float maxY = 0f;
            bool wallRan = false;
            yield return Watch(run: true, (y, ran) => { maxY = y; wallRan = ran; });

            Assert.IsTrue(wallRan, "A wall run should have started on the curve.");
            Assert.Greater(maxY, 5f, $"The player only reached {maxY:F2} units high on a curve that is 6 high.");
        }

        [UnityTest]
        public IEnumerator PressingJump_EndsTheWallRun()
        {
            BuildRamp(marked: true);
            BuildPlayer();
            yield return null;

            yield return HoldKeys(run: true);

            float end = Time.time + WatchSeconds;
            while (!controller.IsWallRunning && Time.time < end)
            {
                yield return null;
            }

            Assert.IsTrue(controller.IsWallRunning, "A wall run should have started.");

            Press(keyboard.spaceKey);
            yield return null;
            yield return null;

            Assert.IsFalse(controller.IsWallRunning);
        }

        [UnityTest]
        public IEnumerator ReleasingRun_EndsTheWallRun()
        {
            BuildRamp(marked: true);
            BuildPlayer();
            yield return null;

            yield return HoldKeys(run: true);

            float end = Time.time + WatchSeconds;
            while (!controller.IsWallRunning && Time.time < end)
            {
                yield return null;
            }

            Release(keyboard.leftShiftKey);
            yield return null;
            yield return null;

            Assert.IsFalse(controller.IsWallRunning);
        }
    }
}

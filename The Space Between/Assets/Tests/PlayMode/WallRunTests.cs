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
        private const float WallX = 4.5f;
        private const float WallHeight = 14f;
        private const float RunSpeed = 10f;
        private const float WatchSeconds = 1.4f;
        private const float FloorCenterY = 1.08f;
        private const float WallSlideSpeed = 2f;

        private Keyboard keyboard;
        private GameObject playerObject;
        private PlaceholderPlayerController controller;
        private Transform body;
        private GameObject floor;
        private GameObject surface;

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

            if (surface != null)
            {
                Object.DestroyImmediate(surface);
            }

            Object.DestroyImmediate(floor);
            base.TearDown();
        }

        //==================== SCENE BUILDING =====================
        private void BuildRamp(bool marked)
        {
            surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "WallRun Test Ramp";
            surface.transform.localScale = new Vector3(RampLength, 0.5f, 6f);
            surface.transform.rotation = Quaternion.Euler(0f, 0f, RampAngle);

            // The low end of the top face meets the floor at RampStartX.
            surface.transform.position = new Vector3(RampStartX, 0f, 0f) + RampAlong() * (RampLength * 0.5f) - RampNormal() * 0.25f;

            if (marked)
            {
                surface.AddComponent<WallRunSurface>();
            }

            Physics.SyncTransforms();
        }

        private void BuildVerticalWall()
        {
            surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "WallRun Test Wall";
            surface.transform.localScale = new Vector3(1f, WallHeight, 6f);
            surface.transform.position = new Vector3(WallX, WallHeight * 0.5f, 0f);
            surface.AddComponent<WallRunSurface>();
            Physics.SyncTransforms();
        }

        private static Vector3 RampAlong()
        {
            return new Vector3(Mathf.Cos(RampAngle * Mathf.Deg2Rad), Mathf.Sin(RampAngle * Mathf.Deg2Rad), 0f);
        }

        private static Vector3 RampNormal()
        {
            return new Vector3(-Mathf.Sin(RampAngle * Mathf.Deg2Rad), Mathf.Cos(RampAngle * Mathf.Deg2Rad), 0f);
        }

        private void BuildPlayer(float startX = -2f, bool withBody = false)
        {
            playerObject = new GameObject("WallRun Test Player");
            playerObject.SetActive(false);
            playerObject.transform.position = new Vector3(startX, 1.001f, 0f);
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
            SetField("runSpeed", RunSpeed);

            if (withBody)
            {
                body = new GameObject("Body").transform;
                body.SetParent(playerObject.transform, false);
                SetField("body", body);
            }

            playerObject.SetActive(true);
        }

        private void SetField(string name, object value)
        {
            System.Reflection.FieldInfo field = typeof(PlaceholderPlayerController).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field, name);
            field.SetValue(controller, value);
        }

        //==================== INPUT HELPERS =====================
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

        private IEnumerator TapJump()
        {
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            yield return null;
        }

        private IEnumerator WaitForWallRun()
        {
            float end = Time.time + WatchSeconds;
            while (!controller.IsWallRunning && Time.time < end)
            {
                yield return null;
            }

            Assert.IsTrue(controller.IsWallRunning, "A wall run should have started.");
        }

        // Holds the keys for a while and reports how high the player got and whether a wall run happened.
        private IEnumerator Watch(bool run, float seconds, System.Action<float, bool> report)
        {
            yield return HoldKeys(run);

            float maxY = 0f;
            bool wallRan = false;
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
                maxY = Mathf.Max(maxY, playerObject.transform.position.y);
                wallRan |= controller.IsWallRunning;
            }

            report(maxY, wallRan);
        }

        //==================== STARTING =====================
        [UnityTest]
        public IEnumerator RunningIntoAMarkedRamp_ClimbsIt()
        {
            BuildRamp(marked: true);
            BuildPlayer();
            yield return null;

            float maxY = 0f;
            bool wallRan = false;
            yield return Watch(run: true, WatchSeconds, (y, ran) => { maxY = y; wallRan = ran; });

            Assert.IsTrue(wallRan, "A wall run should have started.");
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
            yield return Watch(run: false, WatchSeconds, (y, ran) => { maxY = y; wallRan = ran; });

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
            yield return Watch(run: true, WatchSeconds, (y, ran) => { maxY = y; wallRan = ran; });

            Assert.IsFalse(wallRan, "A surface without WallRunSurface is just a wall.");
            Assert.Less(maxY, 2f);
        }

        [UnityTest]
        public IEnumerator PressingRunWhileStandingAgainstAWall_DoesNotStartAWallRun()
        {
            BuildVerticalWall();
            BuildPlayer(startX: WallX - 1.1f);
            yield return null;

            // Walk into the wall first so the player is standing against it, then press run.
            Press(keyboard.dKey);
            float walk = Time.time + 0.3f;
            while (Time.time < walk)
            {
                yield return null;
            }

            Press(keyboard.leftShiftKey);
            bool wallRan = false;
            float end = Time.time + 0.8f;
            while (Time.time < end)
            {
                yield return null;
                wallRan |= controller.IsWallRunning;
            }

            Assert.IsFalse(wallRan, "A wall run needs approach speed; standing still against a wall must not launch the player.");
        }

        //==================== MOMENTUM AND GRAVITY =====================
        [UnityTest]
        public IEnumerator EnteringFromTheGround_KeepsTheRunSpeed()
        {
            BuildRamp(marked: true);
            BuildPlayer();
            yield return null;

            yield return HoldKeys(run: true);
            yield return WaitForWallRun();

            Assert.Greater(controller.SurfaceSpeed, RunSpeed * 0.9f, "The ground speed should carry onto the surface instead of being replaced.");
        }

        [UnityTest]
        public IEnumerator HoldingTowardAVerticalWall_RunsUpItAtRunSpeed()
        {
            BuildVerticalWall();
            BuildPlayer();
            yield return null;

            yield return HoldKeys(run: true);
            yield return WaitForWallRun();

            float startY = playerObject.transform.position.y;
            float startTime = Time.time;
            float end = Time.time + 0.4f;
            while (Time.time < end && controller.IsWallRunning)
            {
                yield return null;
            }

            float climbSpeed = (playerObject.transform.position.y - startY) / (Time.time - startTime);
            Assert.AreEqual(RunSpeed, climbSpeed, 1f, "Holding toward the wall should run up it at run speed.");
        }

        [UnityTest]
        public IEnumerator ReleasingInput_SlidesSlowlyDownAndLandsOnTheFloor()
        {
            BuildVerticalWall();
            BuildPlayer();
            yield return null;

            yield return HoldKeys(run: true);
            yield return WaitForWallRun();
            float climb = Time.time + 0.4f;
            while (Time.time < climb)
            {
                yield return null;
            }

            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);

            // Give the run speed time to turn into the slow slide, then measure how fast the player sinks.
            float settle = Time.time + 0.5f;
            while (Time.time < settle)
            {
                yield return null;
            }

            Assert.IsTrue(controller.IsWallRunning, "Letting go should keep the player on the wall.");
            float fromY = playerObject.transform.position.y;
            float fromTime = Time.time;
            float measure = Time.time + 0.5f;
            while (Time.time < measure)
            {
                yield return null;
            }

            float sinkSpeed = (fromY - playerObject.transform.position.y) / (Time.time - fromTime);
            Assert.Less(sinkSpeed, WallSlideSpeed + 0.3f, "The player should slide slowly, not fall.");
            Assert.Greater(sinkSpeed, 0.5f, "With slide speed above 0 the player should sink.");

            float end = Time.time + 6f;
            while (controller.IsWallRunning && Time.time < end)
            {
                yield return null;
            }

            float land = Time.time + 0.5f;
            while (Time.time < land)
            {
                yield return null;
            }

            Assert.IsFalse(controller.IsWallRunning, "The slide should end at the floor.");
            Assert.AreEqual(FloorCenterY, playerObject.transform.position.y, 0.2f);
        }

        [UnityTest]
        public IEnumerator LettingGoOfRunOnAWall_SlidesInsteadOfRunning()
        {
            BuildVerticalWall();
            BuildPlayer();
            yield return null;

            yield return HoldKeys(run: true);
            yield return WaitForWallRun();
            float climb = Time.time + 0.3f;
            while (Time.time < climb)
            {
                yield return null;
            }

            // Keep holding toward the wall but let go of run.
            Release(keyboard.leftShiftKey);
            float settle = Time.time + 0.5f;
            while (Time.time < settle)
            {
                yield return null;
            }

            Assert.IsTrue(controller.IsWallRunning, "The player stays on the wall.");
            Assert.Less(controller.SurfaceSpeed, 0f, "Without run held the player should slide down, not keep running up.");
        }

        [UnityTest]
        public IEnumerator WallJumpWithNoOppositeWall_LaunchesAwayAndFalls()
        {
            BuildVerticalWall();
            BuildPlayer();
            yield return null;

            yield return HoldKeys(run: true);
            yield return WaitForWallRun();
            float climb = Time.time + 0.3f;
            while (Time.time < climb)
            {
                yield return null;
            }

            float wallJumpX = playerObject.transform.position.x;
            yield return TapJump();
            Assert.IsFalse(controller.IsWallRunning, "Jump should leave the wall.");
            Assert.IsTrue(controller.IsInWallJump);

            // Holding toward the old wall would steer back to it after the launch lock, so let go.
            Release(keyboard.dKey);
            yield return null;
            Release(keyboard.leftShiftKey);

            bool reattached = false;
            float end = Time.time + 3f;
            while (controller.IsInWallJump && Time.time < end)
            {
                reattached |= controller.IsWallRunning;
                yield return null;
            }

            Assert.IsFalse(reattached, "There is no wall to reach, so the player must not attach to anything.");
            Assert.Less(playerObject.transform.position.x, wallJumpX - 1f, "The jump should carry the player away from the wall.");
            Assert.AreEqual(FloorCenterY, playerObject.transform.position.y, 0.2f, "With nothing to reach the player falls to the floor.");
        }

        [UnityTest]
        public IEnumerator MaxSurfaceAngleBelowNinety_TurnsAVerticalWallIntoAPlainWall()
        {
            BuildVerticalWall();
            BuildPlayer();
            controller.MaxWallRunSurfaceAngle = 80f;
            yield return null;

            float maxY = 0f;
            bool wallRan = false;
            yield return Watch(run: true, 1f, (y, ran) => { maxY = y; wallRan = ran; });

            Assert.IsFalse(wallRan, "A 90 degree wall is steeper than the 80 degree limit.");
            Assert.Less(maxY, 2f);
        }

        [UnityTest]
        public IEnumerator LandingOnAMarkedRampWithoutRunning_SlidesDownToTheFloor()
        {
            BuildRamp(marked: true);

            // Drop the player just above the middle of the ramp with no keys held.
            Vector3 surfacePoint = new Vector3(RampStartX, 0f, 0f) + RampAlong() * (RampLength * 0.5f);
            BuildPlayer();
            CharacterController cc = playerObject.GetComponent<CharacterController>();
            cc.enabled = false;
            playerObject.transform.position = surfacePoint + RampNormal() * 1.1f + Vector3.up * 0.3f;
            cc.enabled = true;

            bool slid = false;
            float end = Time.time + 6f;
            while (Time.time < end)
            {
                yield return null;
                slid |= controller.IsWallRunning && controller.SurfaceSpeed < -1f;
                if (slid && !controller.IsWallRunning)
                {
                    break;
                }
            }

            float land = Time.time + 0.5f;
            while (Time.time < land)
            {
                yield return null;
            }

            Assert.IsTrue(slid, "The player should slide down the steep surface instead of sticking to it.");
            Assert.IsFalse(controller.IsWallRunning);
            Assert.AreEqual(FloorCenterY, playerObject.transform.position.y, 0.2f, "The slide should end on the floor.");
        }

        //==================== CURVES AND BODY =====================
        [UnityTest]
        public IEnumerator RunningIntoACurvedWall_FollowsTheCurveUpToTheTop()
        {
            surface = new GameObject("WallRun Test Curve");
            surface.transform.position = new Vector3(RampStartX, 0f, 0f);
            surface.AddComponent<CurvedWall>();
            Physics.SyncTransforms();
            BuildPlayer();
            yield return null;

            float maxY = 0f;
            bool wallRan = false;
            yield return Watch(run: true, WatchSeconds, (y, ran) => { maxY = y; wallRan = ran; });

            Assert.IsTrue(wallRan, "A wall run should have started on the curve.");
            Assert.Greater(maxY, 5f, $"The player only reached {maxY:F2} units high on a curve that is 6 high.");
        }

        [UnityTest]
        public IEnumerator Body_TurnsToTheSurfaceWithFeetOnIt_AndReturnsUpright()
        {
            BuildRamp(marked: true);
            BuildPlayer(withBody: true);
            yield return null;

            yield return HoldKeys(run: true);
            yield return WaitForWallRun();

            // Tests run at an uncapped frame rate, so the body is given real time to settle, not a frame count.
            float settle = Time.time + 0.4f;
            while (Time.time < settle)
            {
                yield return null;
            }

            Assert.IsTrue(controller.IsWallRunning);
            float lean = Vector3.Angle(body.up, RampNormal());
            Assert.Less(lean, 8f, $"The body should stand on the ramp, but is {lean:F1} degrees off its surface normal.");

            // The lowest point of the turned body should rest on the ramp surface, not sink into it or float.
            Vector3 feet = body.position - body.up * 1f;
            float feetHeight = Vector3.Dot(feet - new Vector3(RampStartX, 0f, 0f), RampNormal());
            Assert.AreEqual(0f, feetHeight, 0.15f, "The body's feet should touch the ramp surface.");

            Assert.AreEqual(Quaternion.identity, playerObject.transform.rotation, "Only the body turns; the controller stays upright.");

            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);
            float end = Time.time + 8f;
            while ((controller.IsWallRunning || Vector3.Angle(body.up, Vector3.up) > 1f) && Time.time < end)
            {
                yield return null;
            }

            Assert.Less(Vector3.Angle(body.up, Vector3.up), 1f, "Back on the floor the body should be upright again.");
        }
    }
}

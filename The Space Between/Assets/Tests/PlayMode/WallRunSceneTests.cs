using System.Collections;
using System.Text;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Drives the real Player through the stations in the Wall_Run_Test scene.
    /// The positions below match the scene layout, so moving a station means updating it here.
    /// Each test reports its measurements in the pass message.
    /// </summary>
    public class WallRunSceneTests : InputTestFixture
    {
        private const string SceneName = "Wall_Run_Test";
        private const float GroundCenterY = 0.87f;
        private const float RunSpeed = 10f;
        private const float WallSlideSpeed = 2f;

        // Opposite wall pairs: the inner face of each left and right wall.
        private static readonly float[] Gaps = { 3f, 5f, 7f, 10f };
        private static readonly float[] LeftFaces = { 75.5f, 90.5f, 108.5f, 128.5f };
        private static readonly float[] RightFaces = { 78.5f, 95.5f, 115.5f, 138.5f };

        // Other stations.
        private const float BeforeRamp60X = 12f;
        private const float LedgeStartX = 20.5f;
        private const float LedgeTopCenterY = 8.66f;
        private const float BeforeCurve6X = 42f;
        private const float BeforeCurve35X = 57f;

        // The player must stay this far inside the screen edges, as a fraction of the screen.
        private const float ScreenMargin = 0.05f;

        // The camera must stay out of colliders and above the ground by at least this much.
        private const float GroundTopY = -0.21f;
        private const float CameraProbeRadius = 0.2f;

        private Keyboard keyboard;
        private GameObject player;
        private PlaceholderPlayerController controller;
        private Transform body;
        private CameraWallRunFraming framing;
        private CinemachinePositionComposer composer;
        private Camera view;

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        // The scene must not stay loaded, or its geometry gets in the way of tests that build their own.
        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            Scene scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.isLoaded)
            {
                yield break;
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Wall_Run_Test"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        //==================== HELPERS =====================
        private IEnumerator LoadScene(float startX)
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;

            player = GameObject.Find("Player");
            Assert.NotNull(player, "Wall_Run_Test needs a Player.");
            controller = player.GetComponent<PlaceholderPlayerController>();
            body = player.transform.Find("Body");
            Assert.NotNull(body, "The Player needs its Body child for the wall run pose.");
            framing = Object.FindFirstObjectByType<CameraWallRunFraming>();
            Assert.NotNull(framing, "The CM Follow Camera needs CameraWallRunFraming.");
            composer = framing.GetComponent<CinemachinePositionComposer>();
            view = Camera.main;

            yield return Teleport(startX);
        }

        private IEnumerator Teleport(float x)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = new Vector3(x, GroundCenterY + 0.05f, player.transform.position.z);
            cc.enabled = true;
            Physics.SyncTransforms();

            float settle = Time.time + 0.2f;
            while (Time.time < settle)
            {
                yield return null;
            }
        }

        private IEnumerator Hold(KeyControl direction, bool run)
        {
            Press(direction);
            yield return null;
            if (run)
            {
                Press(keyboard.leftShiftKey);
                yield return null;
            }
        }

        private IEnumerator ReleaseAll()
        {
            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);
            yield return null;
            Release(keyboard.aKey);
            yield return null;
        }

        private IEnumerator TapJump()
        {
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
        }

        private IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
            }
        }

        private IEnumerator WaitForWallRun(float seconds)
        {
            float end = Time.time + seconds;
            while (!controller.IsWallRunning && Time.time < end)
            {
                yield return null;
            }
        }

        // How far the player sits from the nearest screen edge, as a fraction of the screen. Negative is off screen.
        private float ScreenEdgeDistance()
        {
            Vector3 v = view.WorldToViewportPoint(player.transform.position);
            return Mathf.Min(Mathf.Min(v.x, 1f - v.x), Mathf.Min(v.y, 1f - v.y));
        }

        // How far the camera is from the nearest collider below or around it. Negative means it is inside one or under the ground.
        private float CameraClearance()
        {
            Vector3 position = view.transform.position;
            if (Physics.CheckSphere(position, CameraProbeRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                return -1f;
            }

            return position.y - GroundTopY;
        }

        private float BodyLeanFrom(Vector3 normal)
        {
            return Vector3.Angle(body.up, normal);
        }

        //==================== 1, 2: GROUND ONTO A WALL =====================
        [UnityTest]
        public IEnumerator GroundOntoAWall_KeepsTheSpeedAndTheBodyStandsOnTheWall()
        {
            yield return LoadScene(RightFaces[1] - 2.5f);
            yield return Hold(keyboard.dKey, run: true);
            yield return WaitForWallRun(1f);
            Assert.IsTrue(controller.IsWallRunning, "Running into the wall should start a wall run.");
            float entrySpeed = controller.SurfaceSpeed;

            float fromY = player.transform.position.y;
            float fromTime = Time.time;
            yield return WaitSeconds(0.4f);
            float climbSpeed = (player.transform.position.y - fromY) / (Time.time - fromTime);
            float lean = BodyLeanFrom(Vector3.left);

            Assert.Greater(entrySpeed, RunSpeed * 0.9f, "The ground speed should carry onto the wall.");
            Assert.AreEqual(RunSpeed, climbSpeed, 1f, "Holding toward the wall runs up it at run speed.");
            Assert.Less(lean, 8f, "The body should stand on the wall, feet toward it.");
            Assert.Pass($"entrySpeed={entrySpeed:F2} climbSpeed={climbSpeed:F2} bodyLean={lean:F1}deg");
        }

        //==================== 3, 4, 8: WALL TO WALL =====================
        [UnityTest]
        public IEnumerator WallJumpBetweenOppositeWalls_AtEachGap()
        {
            yield return LoadScene(RightFaces[0] - 1.5f);
            StringBuilder report = new StringBuilder();
            bool[] reached = new bool[Gaps.Length];
            float worstScreenEdge = 1f;
            float minCameraClearance = float.MaxValue;

            for (int i = 0; i < Gaps.Length; i++)
            {
                yield return ReleaseAll();
                yield return Teleport(RightFaces[i] - 1.5f);
                yield return Hold(keyboard.dKey, run: true);
                yield return WaitForWallRun(1f);
                Assert.IsTrue(controller.IsWallRunning, $"Gap {Gaps[i]}: no wall run on the right wall.");
                yield return WaitSeconds(0.35f);

                // Jump, then hold toward the left wall, as a player would.
                float jumpY = player.transform.position.y;
                // Keys are changed a frame apart: the test keyboard can drop one of two changes made in the same frame.
                // Run stays held, because running on the new wall needs it.
                yield return TapJump();
                Release(keyboard.dKey);
                yield return null;
                Press(keyboard.aKey);

                float jumpTime = Time.time;
                float end = Time.time + 2.5f;
                while (!controller.IsWallRunning && controller.IsInWallJump && Time.time < end)
                {
                    worstScreenEdge = Mathf.Min(worstScreenEdge, ScreenEdgeDistance());
                    minCameraClearance = Mathf.Min(minCameraClearance, CameraClearance());
                    yield return null;
                }

                reached[i] = controller.IsWallRunning && player.transform.position.x < LeftFaces[i] + 1f;
                if (!reached[i])
                {
                    report.Append($"gap {Gaps[i]}: NOT reached (landed at x {player.transform.position.x:F1}); ");
                    continue;
                }

                float attachY = player.transform.position.y;
                float attachSpeed = controller.SurfaceSpeed;
                float flight = Time.time - jumpTime;

                // Holding toward the new wall should keep running up it without a pause.
                float fromY = player.transform.position.y;
                for (float t = Time.time + 0.3f; Time.time < t;)
                {
                    worstScreenEdge = Mathf.Min(worstScreenEdge, ScreenEdgeDistance());
                    yield return null;
                }

                bool stillRunning = controller.IsWallRunning;
                float climbed = player.transform.position.y - fromY;
                float lean = BodyLeanFrom(Vector3.right);
                report.Append($"gap {Gaps[i]}: reached in {flight:F2}s, jumped at y {jumpY:F1}, landed on wall at y {attachY:F1}, entry {attachSpeed:F1} u/s, climbed {climbed:F2} in 0.3s, bodyLean {lean:F1}deg; ");

                Assert.IsTrue(stillRunning, $"Gap {Gaps[i]}: the run should continue on the new wall. {report}");
                Assert.Greater(climbed, 1.5f, $"Gap {Gaps[i]}: the player should keep moving up the new wall. {report}");
                Assert.Less(lean, 10f, $"Gap {Gaps[i]}: the body should stand on the new wall. {report}");
            }

            report.Append($"closest to screen edge={worstScreenEdge:F2}, camera clearance={minCameraClearance:F2}");
            Assert.Greater(minCameraClearance, CameraProbeRadius, "The camera should stay out of colliders. " + report);
            Assert.IsTrue(reached[0] && reached[1] && reached[2], "Gaps 3, 5 and 7 should be crossed. " + report);
            Assert.Greater(worstScreenEdge, ScreenMargin, "The player went too near the screen edge. " + report);
            Assert.Pass(report.ToString());
        }

        // Holding one key the whole time (D, or A after the first wall), each jump must still reach the opposite wall.
        [UnityTest]
        public IEnumerator WallJumpChain_HoldingOneKey_AlwaysReachesTheOppositeWall([Values("d", "a")] string heldKey)
        {
            const int pair = 1;
            yield return LoadScene(RightFaces[pair] - 2.5f);
            yield return Hold(keyboard.dKey, run: true);
            yield return WaitForWallRun(1f);
            Assert.IsTrue(controller.IsWallRunning, "No wall run on the right wall.");

            if (heldKey == "a")
            {
                // Swap to A while on the right wall; either key keeps the run going.
                Release(keyboard.dKey);
                yield return null;
                Press(keyboard.aKey);
            }

            yield return WaitSeconds(0.3f);
            StringBuilder report = new StringBuilder($"holding {heldKey.ToUpper()}: ");

            // Right wall to left wall, then back to the right wall.
            for (int jump = 0; jump < 2; jump++)
            {
                bool towardLeft = jump == 0;
                float fromX = player.transform.position.x;
                yield return TapJump();

                float end = Time.time + 2f;
                while (!controller.IsWallRunning && Time.time < end)
                {
                    yield return null;
                }

                float x = player.transform.position.x;
                bool onOpposite = controller.IsWallRunning && (towardLeft ? x < LeftFaces[pair] + 1f : x > RightFaces[pair] - 1f);
                report.Append($"jump {jump + 1} from x {fromX:F1} to x {x:F1} ({(onOpposite ? "opposite wall" : "WRONG")}); ");
                Assert.IsTrue(onOpposite, "The jump should land on the opposite wall. " + report);

                float fromY = player.transform.position.y;
                yield return WaitSeconds(0.3f);
                float climbed = player.transform.position.y - fromY;
                report.Append($"climbed {climbed:F2}; ");
                Assert.IsTrue(controller.IsWallRunning, "The run should continue on the new wall. " + report);
                Assert.Greater(climbed, 1.5f, "The held key should keep the player running up the new wall. " + report);
            }

            Assert.Pass(report.ToString());
        }

        //==================== 5: RELEASING INPUT =====================
        [UnityTest]
        public IEnumerator ReleasingInputOnAWall_SlidesSlowly_ThenTheFloorEndsIt()
        {
            yield return LoadScene(RightFaces[1] - 2.5f);
            yield return Hold(keyboard.dKey, run: true);
            yield return WaitForWallRun(1f);
            yield return WaitSeconds(0.5f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.5f);

            float fromY = player.transform.position.y;
            float fromTime = Time.time;
            yield return WaitSeconds(0.6f);
            float sinkSpeed = (fromY - player.transform.position.y) / (Time.time - fromTime);

            Assert.IsTrue(controller.IsWallRunning, "Letting go must keep the player on the wall.");
            Assert.Less(sinkSpeed, WallSlideSpeed + 0.3f, "The player should slide slowly, not fall.");

            float end = Time.time + 6f;
            while (controller.IsWallRunning && Time.time < end)
            {
                yield return null;
            }

            yield return WaitSeconds(0.8f);
            Assert.IsFalse(controller.IsWallRunning);
            Assert.AreEqual(GroundCenterY, player.transform.position.y, 0.2f, "The slide ends on the floor.");
            Assert.Less(BodyLeanFrom(Vector3.up), 2f, "Back upright on the floor.");
            Assert.Pass($"sinkSpeed={sinkSpeed:F2} u/s");
        }

        //==================== 6: ANGLE SETTING =====================
        [UnityTest]
        public IEnumerator MaxSurfaceAngle_DecidesWhichSurfacesCanBeRun()
        {
            yield return LoadScene(RightFaces[1] - 2.5f);

            // 80 degrees: the vertical wall (90) is too steep, the 60 degree ramp is still fine.
            controller.MaxWallRunSurfaceAngle = 80f;
            yield return Hold(keyboard.dKey, run: true);
            yield return WaitForWallRun(1f);
            bool wallAt80 = controller.IsWallRunning;
            yield return ReleaseAll();

            yield return Teleport(BeforeRamp60X);
            yield return Hold(keyboard.dKey, run: true);
            yield return WaitForWallRun(1.5f);
            bool rampAt80 = controller.IsWallRunning;
            yield return ReleaseAll();

            // 100 degrees: the vertical wall runs again.
            controller.MaxWallRunSurfaceAngle = 100f;
            yield return WaitSeconds(0.5f);
            yield return Teleport(RightFaces[1] - 2.5f);
            yield return Hold(keyboard.dKey, run: true);
            yield return WaitForWallRun(1f);
            bool wallAt100 = controller.IsWallRunning;

            string result = $"vertical wall at 80={wallAt80}, 60 degree ramp at 80={rampAt80}, vertical wall at 100={wallAt100}";
            Assert.IsFalse(wallAt80, result);
            Assert.IsTrue(rampAt80, result);
            Assert.IsTrue(wallAt100, result);
            Assert.Pass(result);
        }

        //==================== 7, 9: DIFFERENT SURFACES AND BACK TO NORMAL =====================
        [UnityTest]
        public IEnumerator Ramp60_RunsUpOntoTheLedge_AndReturnsToNormalRunning()
        {
            yield return LoadScene(BeforeRamp60X);
            yield return Hold(keyboard.dKey, run: true);
            yield return WaitForWallRun(1.5f);
            Assert.IsTrue(controller.IsWallRunning, "The marked 60 degree ramp should start a wall run.");

            float maxWeight = 0f;
            float end = Time.time + 2f;
            while (player.transform.position.x < LedgeStartX + 1.5f && Time.time < end)
            {
                maxWeight = Mathf.Max(maxWeight, framing.FramingWeight);
                yield return null;
            }

            // Stop on the ledge before its far end and let the camera settle back.
            yield return ReleaseAll();
            yield return WaitSeconds(1.2f);

            Vector3 end2 = player.transform.position;
            string result = $"end={end2} bodyLean={BodyLeanFrom(Vector3.up):F1}deg maxFramingWeight={maxWeight:F2} framingWeightAfter={framing.FramingWeight:F2}";
            Assert.Greater(end2.x, LedgeStartX, result);
            Assert.AreEqual(LedgeTopCenterY, end2.y, 0.25f, "The player should be standing on the ledge. " + result);
            Assert.Less(BodyLeanFrom(Vector3.up), 1f, "Upright again. " + result);
            Assert.Greater(maxWeight, 0.3f, "The camera should have started to widen during the run. " + result);
            Assert.Less(framing.FramingWeight, 0.05f, "The camera should return to normal framing. " + result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator CurveIntoTheVerticalWall_RunsAllTheWayUp()
        {
            yield return LoadScene(BeforeCurve6X);

            // The wall can be resized in the scene, so its top is read from its collider rather than assumed.
            GameObject wall = GameObject.Find("Vertical Wall (marked)");
            Assert.NotNull(wall, "Wall_Run_Test needs the Vertical Wall (marked) above the radius 6 curve.");
            float wallTopY = wall.GetComponent<Collider>().bounds.max.y;

            yield return Hold(keyboard.dKey, run: true);

            float maxY = 0f;
            float maxLean = 0f;
            float end = Time.time + 3f;
            while (Time.time < end)
            {
                yield return null;
                maxY = Mathf.Max(maxY, player.transform.position.y);
                if (controller.IsWallRunning)
                {
                    maxLean = Mathf.Max(maxLean, BodyLeanFrom(Vector3.up));
                }
            }

            string result = $"maxY={maxY:F2} wallTop={wallTopY:F2} maxBodyTurn={maxLean:F1}deg";
            Assert.Greater(maxY, wallTopY, "The run should carry the player from the curve up the whole wall above it. " + result);
            Assert.Greater(maxLean, 80f, "The body should turn to the vertical part. " + result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator TightCurve_CarriesThePlayerOverTheTop()
        {
            yield return LoadScene(BeforeCurve35X);
            yield return Hold(keyboard.dKey, run: true);

            float maxY = 0f;
            bool wallRan = false;
            float end = Time.time + 1.5f;
            while (Time.time < end)
            {
                yield return null;
                maxY = Mathf.Max(maxY, player.transform.position.y);
                wallRan |= controller.IsWallRunning;
            }

            Assert.IsTrue(wallRan);
            Assert.Greater(maxY, 3.5f, $"maxY={maxY:F2}");
            Assert.Pass($"maxY={maxY:F2}");
        }

        //==================== 8: CAMERA =====================
        [UnityTest]
        public IEnumerator Camera_ZoomsOutDuringAWallRun_AndKeepsThePlayerOnScreen()
        {
            yield return LoadScene(RightFaces[1] - 2.5f);
            float baseDistance = composer.CameraDistance;

            yield return Hold(keyboard.dKey, run: true);
            yield return WaitForWallRun(1f);

            float worstEdge = 1f;
            float minClearance = CameraClearance();
            float maxDistance = baseDistance;
            float end = Time.time + 0.9f;
            while (Time.time < end && controller.IsWallRunning)
            {
                worstEdge = Mathf.Min(worstEdge, ScreenEdgeDistance());
                minClearance = Mathf.Min(minClearance, CameraClearance());
                maxDistance = Mathf.Max(maxDistance, composer.CameraDistance);
                yield return null;
            }

            // Let go, slide to the floor and give the camera time to settle back.
            yield return ReleaseAll();
            float slide = Time.time + 8f;
            while (controller.IsWallRunning && Time.time < slide)
            {
                worstEdge = Mathf.Min(worstEdge, ScreenEdgeDistance());
                minClearance = Mathf.Min(minClearance, CameraClearance());
                yield return null;
            }

            for (float settle = Time.time + 1f; Time.time < settle;)
            {
                minClearance = Mathf.Min(minClearance, CameraClearance());
                yield return null;
            }

            string result = $"baseDistance={baseDistance:F2} maxDistance={maxDistance:F2} distanceAfter={composer.CameraDistance:F2} closestToEdge={worstEdge:F2} cameraClearance={minClearance:F2}";
            Assert.Greater(minClearance, CameraProbeRadius, "The camera should stay out of colliders and above the ground. " + result);
            Assert.Greater(maxDistance, baseDistance + 1f, "The camera should pull back while wall running. " + result);
            Assert.AreEqual(baseDistance, composer.CameraDistance, 0.05f, "The camera should return to its normal distance. " + result);
            Assert.Greater(worstEdge, ScreenMargin, "The player should stay well on screen. " + result);
            Assert.Pass(result);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Plays the Integrated Traversal Route in Wall_Run_Test with the real Player, using the keyboard and the mouse as a
    /// player would. Each test reports its measurements in the pass message.
    /// The positions below match the route layout, so moving geometry means updating them here.
    /// </summary>
    public class TraversalRouteTests : InputTestFixture
    {
        private const string SceneName = "Wall_Run_Test";
        private const float StandingHeight = 1.08f;
        private const float SwapWaitLimitSeconds = 3f;
        private const float CameraProbeRadius = 0.2f;
        private const float RunSpeed = 10f;
        private const int AimFrames = 3;

        // Route layout.
        private const float StartX = -196f;
        private const float StartTop = 2f;
        private const float Barrier1X = -189.3f;
        private const float AfterBarrier1X = -186f;
        private const float CurveVerticalX = -174f;
        private const float CurveVerticalBottomY = 6f;
        private const float Ledge1Start = -173.4f;
        private const float Ledge1Top = 11f;
        private const float Ledge1Edge = -160f;
        private const float Entry2X = -171f;
        private const float WallEFace = -148.5f;
        private const float Ledge2Start = -147.5f;
        private const float Ledge2Top = 17.5f;
        private const float Entry3X = -145.5f;
        private const float ChuteGapStart = -132f;
        private const float ChuteGapEnd = -129f;
        private const float Entry4X = -126f;
        private const float Barrier2X = -118.6f;
        private const float F1Face = -116f;
        private const float F2Face = -121f;
        private const float CapBottomY = 23.5f;
        private const float CapTopY = 24.1f;
        private const float FinalTop = 29.5f;

        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject player;
        private PlaceholderPlayerController controller;
        private SwapAbility ability;
        private Swappable playerSwappable;
        private PlayerAnimationDriver animationDriver;
        private TraversalEffects effects;
        private Transform body;
        private Camera view;

        private readonly HashSet<string> statesSeen = new HashSet<string>();
        private float closestToScreenEdge;
        private int framesCameraInsideGeometry;
        private int framesTrailWithoutWallRun;
        private int framesSparksWithoutWallRun;

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
        }

        // The scene must not stay loaded, or its geometry gets in the way of tests that build their own.
        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.isLoaded)
            {
                yield break;
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Traversal Route"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        //==================== HELPERS =====================
        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;

            player = GameObject.Find("Player");
            Assert.NotNull(player, $"{SceneName} needs a Player.");
            controller = player.GetComponent<PlaceholderPlayerController>();
            ability = player.GetComponent<SwapAbility>();
            playerSwappable = player.GetComponent<Swappable>();
            animationDriver = player.GetComponent<PlayerAnimationDriver>();
            effects = player.GetComponent<TraversalEffects>();
            body = player.transform.Find("Body");
            Assert.NotNull(animationDriver, "The Player needs a PlayerAnimationDriver.");
            Assert.NotNull(effects, "The Player needs TraversalEffects.");
            view = Camera.main;

            statesSeen.Clear();
            closestToScreenEdge = 1f;
            framesCameraInsideGeometry = 0;
            framesTrailWithoutWallRun = 0;
            framesSparksWithoutWallRun = 0;
        }

        private static T Find<T>(string name) where T : Component
        {
            GameObject found = GameObject.Find(name);
            Assert.NotNull(found, $"The scene needs '{name}'.");
            T component = found.GetComponent<T>();
            Assert.NotNull(component, $"'{name}' needs a {typeof(T).Name}.");
            return component;
        }

        private IEnumerator Teleport(float x, float floorY)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = new Vector3(x, floorY + StandingHeight + 0.02f, player.transform.position.z);
            cc.enabled = true;
            Physics.SyncTransforms();
            yield return WaitSeconds(0.4f);
        }

        // Called every frame: the camera, the Animator state and whether the wall run effects match the wall run.
        private void Watch()
        {
            Vector3 v = view.WorldToViewportPoint(player.transform.position);
            closestToScreenEdge = Mathf.Min(closestToScreenEdge, Mathf.Min(Mathf.Min(v.x, 1f - v.x), Mathf.Min(v.y, 1f - v.y)));
            if (Physics.CheckSphere(view.transform.position, CameraProbeRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                framesCameraInsideGeometry++;
            }

            statesSeen.Add(animationDriver.CurrentState);

            bool wallRunVisible = controller.IsWallRunning && !playerSwappable.IsFrozen;
            if (Trail().emitting && !wallRunVisible)
            {
                framesTrailWithoutWallRun++;
            }

            if (Sparks().isEmitting && !wallRunVisible)
            {
                framesSparksWithoutWallRun++;
            }
        }

        private TrailRenderer Trail()
        {
            return body.Find("FX Wall Run Trail").GetComponent<TrailRenderer>();
        }

        private ParticleSystem Sparks()
        {
            return body.Find("FX Wall Run Sparks").GetComponent<ParticleSystem>();
        }

        private ParticleSystem Dust(string name)
        {
            return player.transform.Find(name).GetComponent<ParticleSystem>();
        }

        private IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
                Watch();
            }
        }

        private IEnumerator WaitUntil(System.Func<bool> condition, float limitSeconds = 6f)
        {
            float end = Time.time + limitSeconds;
            while (!condition() && Time.time < end)
            {
                yield return null;
                Watch();
            }
        }

        // Keys are changed a frame apart: the test keyboard can drop one of two changes made in the same frame.
        private IEnumerator HoldRun(KeyControl direction)
        {
            Press(direction);
            yield return null;
            Press(keyboard.leftShiftKey);
            yield return null;
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
            yield return null;
        }

        // Turns from one held direction to the other, keeping run held, as after a wall jump.
        private IEnumerator Turn(KeyControl from, KeyControl to)
        {
            Release(from);
            yield return null;
            Press(to);
            yield return null;
        }

        // Puts the cursor over the target for a couple of frames, clicks, and waits for the smooth swap to end.
        private IEnumerator AimAndClick(Swappable target)
        {
            for (int i = 0; i < AimFrames; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(target.Center));
                yield return null;
            }

            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;

            // Game time stops during a smooth swap, so this waits in real time.
            float start = Time.realtimeSinceStartup;
            while (ability.IsSwapping && Time.realtimeSinceStartup - start < SwapWaitLimitSeconds)
            {
                yield return null;
            }
        }

        private bool StandingOn(float floorY)
        {
            return controller.IsGrounded && Mathf.Abs(player.transform.position.y - (floorY + StandingHeight)) < 0.15f;
        }

        private float X => player.transform.position.x;

        private float Y => player.transform.position.y;

        // Runs right and jumps once the player is this close to the given x.
        private IEnumerator RunAndJumpAt(float jumpX)
        {
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= jumpX, 4f);
            yield return TapJump();
        }

        //==================== 1: HOLDING FORWARD IS NOT ENOUGH =====================
        [UnityTest]
        public IEnumerator HoldingForwardOnly_IsStoppedByTheFirstBarrier()
        {
            yield return LoadScene();
            yield return Teleport(StartX, StartTop);
            yield return HoldRun(keyboard.dKey);
            yield return WaitSeconds(3f);

            string result = $"stopped at x {X:F2}, barrier at {Barrier1X}, wallRunning={controller.IsWallRunning}";
            Assert.Less(X, Barrier1X, "Holding run and forward without jumping must not get past the first barrier. " + result);
            Assert.IsFalse(controller.IsWallRunning, "A barrier is never a wall run surface. " + result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator CapOnF1_EndsAWallRunThatDoesNotJump()
        {
            yield return LoadScene();
            yield return Teleport(Entry4X, Ledge2Top);
            yield return RunAndJumpAt(Barrier2X - 2.2f);
            yield return WaitUntil(() => controller.IsWallRunning, 1.5f);
            Assert.IsTrue(controller.IsWallRunning, "Jumping the barrier should land on wall F1.");

            float maxY = Y;
            yield return WaitUntil(() =>
            {
                maxY = Mathf.Max(maxY, Y);
                return !controller.IsWallRunning;
            }, 3f);
            yield return ReleaseAll();
            yield return WaitSeconds(1f);

            string result = $"highest point {maxY:F2} (cap underside {CapBottomY}), ended at {player.transform.position}";
            Assert.Less(maxY + 1f, CapBottomY + 0.1f, "The cap should stop the run below it. " + result);
            Assert.Less(Y, CapBottomY, "Without a wall jump the player drops back down. " + result);
            Assert.Pass(result);
        }

        //==================== 2: GROUND, CURVE, VERTICAL WALL =====================
        [UnityTest]
        public IEnumerator GroundIntoCurveIntoVerticalWall_OneContinuousRunOntoLedge1()
        {
            yield return LoadScene();
            yield return Teleport(AfterBarrier1X, StartTop);
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => controller.IsWallRunning, 2f);
            Assert.IsTrue(controller.IsWallRunning, "Running into the curve should start a wall run.");

            float entrySpeed = controller.SurfaceSpeed;
            float minSpeed = float.MaxValue;
            float maxTurn = 0f;
            bool traversingOnCurve = false;
            bool settledOnVertical = false;
            int breaks = 0;
            bool wasRunning = true;
            float startTime = Time.time;
            float end = Time.time + 3f;
            while (Time.time < end && !(StandingOn(Ledge1Top) && X > Ledge1Start))
            {
                yield return null;
                Watch();
                bool onShape = X < Ledge1Start && Y < Ledge1Top + StandingHeight;
                if (controller.IsWallRunning)
                {
                    if (Time.time - startTime > 0.15f)
                    {
                        minSpeed = Mathf.Min(minSpeed, controller.SurfaceSpeed);
                    }

                    maxTurn = Mathf.Max(maxTurn, Vector3.Angle(body.up, Vector3.up));
                    traversingOnCurve |= controller.IsTraversing && Y < CurveVerticalBottomY;
                    settledOnVertical |= !controller.IsTraversing && Y > CurveVerticalBottomY + 1f && X > CurveVerticalX - 1f;
                }

                if (!wasRunning && controller.IsWallRunning && onShape)
                {
                    breaks++;
                }

                wasRunning = controller.IsWallRunning;
            }

            // Let go before the far edge of the ledge.
            yield return ReleaseAll();
            yield return WaitSeconds(0.5f);
            string result = $"entry {entrySpeed:F1} u/s, slowest on the surface {minSpeed:F1} u/s, body turned {maxTurn:F1} deg, " +
                            $"re-attachments {breaks}, traversing on curve={traversingOnCurve}, settled on vertical={settledOnVertical}, end {player.transform.position}";
            Assert.Greater(entrySpeed, RunSpeed * 0.9f, "The ground speed should carry onto the curve. " + result);
            Assert.Greater(minSpeed, RunSpeed * 0.9f, "Speed should be kept through the curve into the vertical part. " + result);
            Assert.AreEqual(0, breaks, "The run should not drop off and re-attach at the join. " + result);
            Assert.Greater(maxTurn, 80f, "The body should turn to the vertical part. " + result);
            Assert.IsTrue(traversingOnCurve, "IsTraversing should be on while the surface turns. " + result);
            Assert.IsTrue(settledOnVertical, "IsTraversing should switch off once the body lines up with the vertical part. " + result);
            Assert.IsTrue(StandingOn(Ledge1Top) && X > Ledge1Start, "The run should carry the player over the top onto ledge 1. " + result);
            Assert.Pass(result);
        }

        //==================== 3: JUMP, SWAP, KEEP RUNNING =====================
        [UnityTest]
        public IEnumerator JumpWithoutSwap_FallsIntoThePit_AndRestartsOnLedge1()
        {
            yield return LoadScene();
            TraversalHazard pit = Find<TraversalHazard>("Pit 2 (swap gap)");
            yield return Teleport(-166f, Ledge1Top);
            yield return RunAndJumpAt(Ledge1Edge - 0.1f);

            bool reachedE = false;
            yield return WaitUntil(() =>
            {
                reachedE |= controller.IsWallRunning && X > WallEFace - 1.5f;
                return pit.Hits > 0;
            }, 4f);

            // Read straight away: run is still held, so the player starts moving again from the entry.
            Vector3 resetAt = player.transform.position;
            yield return ReleaseAll();
            yield return WaitSeconds(0.3f);

            string result = $"reached wall E={reachedE}, pit hits={pit.Hits}, sent back to {resetAt}";
            Assert.IsFalse(reachedE, "A jump alone must not reach wall E. " + result);
            Assert.AreEqual(1, pit.Hits, "The player should fall into the pit. " + result);
            Assert.AreEqual(Entry2X, resetAt.x, 0.3f, "The pit should send the player back to the start of ledge 1. " + result);
            Assert.IsTrue(StandingOn(Ledge1Top), "Back on ledge 1. " + result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator JumpThenSwapInMidAir_KeepsMomentum_AndRunsUpWallE()
        {
            yield return LoadScene();
            Swappable beacon = Find<Swappable>("Swap Beacon (gap)");
            yield return Teleport(-166f, Ledge1Top);
            yield return RunAndJumpAt(Ledge1Edge - 0.1f);
            yield return WaitSeconds(0.2f);

            // Velocity just before the click, from the movement of the last frame.
            Vector3 before = player.transform.position;
            yield return null;
            Vector3 velocityBefore = (player.transform.position - before) / Time.deltaTime;
            Vector3 swapFrom = player.transform.position;

            yield return AimAndClick(beacon);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, "The beacon should be in range during the jump.");
            float upAfterSwap = Vector3.Angle(body.up, Vector3.up);
            Vector3 after = player.transform.position;
            yield return null;
            Vector3 velocityAfter = (player.transform.position - after) / Time.deltaTime;

            yield return WaitUntil(() => controller.IsWallRunning, 1.5f);
            bool onE = controller.IsWallRunning && X > WallEFace - 1f;
            float attachY = Y;
            yield return WaitUntil(() => StandingOn(Ledge2Top) && X > Ledge2Start, 3f);

            string result = $"swapped at {swapFrom}, velocity before {velocityBefore}, after {velocityAfter}, body tilt after swap {upAfterSwap:F1} deg, " +
                            $"on wall E={onE} at y {attachY:F2}, end {player.transform.position}";
            Assert.Greater(velocityAfter.x, velocityBefore.x * 0.9f, "The swap should keep the forward speed. " + result);
            Assert.AreEqual(velocityBefore.y, velocityAfter.y, 3f, "The swap should keep the vertical speed. " + result);
            Assert.Less(upAfterSwap, 10f, "The body should still be upright after the swap. " + result);
            Assert.IsTrue(onE, "After the swap the player should reach and run up wall E. " + result);
            Assert.IsTrue(StandingOn(Ledge2Top) && X > Ledge2Start, "The run up wall E should end on ledge 2. " + result);
            Assert.Pass(result);
        }

        // A swap during a wall run keeps the run only where the new position is right against a runnable surface.
        [UnityTest]
        public IEnumerator SwapDuringAWallRun_ContinuesOnlyWhereASurfaceIsTouching()
        {
            yield return LoadScene();
            Swappable beacon = Find<Swappable>("Swap Beacon (gap)");
            StringBuilder report = new StringBuilder();

            // Onto wall F2 from the air, running left into it.
            IEnumerator OntoF2()
            {
                CharacterController cc = player.GetComponent<CharacterController>();
                cc.enabled = false;
                player.transform.position = new Vector3(F2Face + 2.5f, Ledge2Top + 6f, player.transform.position.z);
                cc.enabled = true;
                Physics.SyncTransforms();

                // A teleport is a fresh start, as after a room reset; otherwise the run in progress ends here and
                // the same-wall cooldown would stop the player grabbing F2 again straight away.
                controller.ResetMotion();
                yield return HoldRun(keyboard.aKey);
                yield return WaitUntil(() => controller.IsWallRunning, 1f);
                Assert.IsTrue(controller.IsWallRunning, $"The player should start a wall run on F2. At {player.transform.position}, grounded={controller.IsGrounded}, so far: {report}");
            }

            // Case 1: the beacon touches the same wall higher up. The run carries on there.
            // Letting go of run makes the player slide slowly while the camera settles, so the aim is steady.
            yield return OntoF2();
            Release(keyboard.leftShiftKey);
            yield return WaitSeconds(0.6f);
            Assert.IsTrue(controller.IsWallRunning, "Holding toward F2 without run should slide, still on the wall.");
            beacon.MoveTo(new Vector3(F2Face + 0.58f, Y + 3.5f, beacon.Position.z));
            Physics.SyncTransforms();
            yield return AimAndClick(beacon);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, "The swap beside the wall should be allowed.");
            yield return null;
            bool keptRunning = controller.IsWallRunning;
            Press(keyboard.leftShiftKey);
            float fromY = Y;
            yield return WaitSeconds(0.5f);
            float climbed = Y - fromY;
            report.Append($"beside the wall: still wall running={keptRunning}, climbed {climbed:F2} in 0.5 s after pressing run; ");
            Assert.IsTrue(keptRunning && controller.IsWallRunning, "Next to the same surface the wall run should continue. " + report);
            Assert.Greater(climbed, 2.5f, "And climb again once run is held. " + report);

            // Case 2: the beacon floats near the wall, not touching it. The run ends and the player is not pulled to the wall.
            // The first swap left the beacon on F2, where the player was; it is moved out of the way first.
            yield return ReleaseAll();
            beacon.MoveTo(new Vector3(F2Face + 2.5f, Ledge2Top + 14f, beacon.Position.z));
            Physics.SyncTransforms();
            yield return OntoF2();
            yield return ReleaseAll();
            yield return WaitSeconds(0.6f);
            Assert.IsTrue(controller.IsWallRunning, "Letting go should slide, still on the wall.");
            // Close enough that the surface probe still reaches the wall (1.4 from the body's middle), but with a
            // 0.7 gap: the player has not reached it, so the run must end rather than pull the player across.
            beacon.MoveTo(new Vector3(F2Face + 1.3f, Y + 2f, beacon.Position.z));
            Physics.SyncTransforms();
            yield return AimAndClick(beacon);
            yield return null;
            bool runAfterOpenAir = controller.IsWallRunning;
            float swapX = X;
            yield return WaitSeconds(0.3f);
            float drift = Mathf.Abs(X - swapX);
            report.Append($"near the wall, not touching: wall running after swap={runAfterOpenAir}, sideways drift {drift:F2} in 0.3 s");
            Assert.IsFalse(runAfterOpenAir, "Without a surface touching the body the wall run should end. " + report);
            Assert.Less(drift, 0.2f, "The player must not be pulled toward a wall it has not reached. " + report);
            Assert.Pass(report.ToString());
        }

        //==================== 4: FALLING OBSTACLES =====================
        [UnityTest]
        public IEnumerator FallingObstacles_DropOnSchedule_FallStraight_AndAreDestroyed()
        {
            yield return LoadScene();
            FallingObstacleSpawner spawner = Find<FallingObstacleSpawner>("Falling Obstacle Spawner");
            float sceneStart = Time.time;
            const int Cycles = 4;

            List<float> dropTimes = new List<float>();
            int lastDropped = spawner.Dropped;
            int maxActive = 0;
            FallingObstacle tracked = null;
            float trackedStartX = 0f;
            float trackedMaxDrift = 0f;
            float trackedLastY = 0f;

            float end = Time.time + spawner.InitialDelay + spawner.Interval * Cycles + 4f;
            while (Time.time < end)
            {
                yield return null;
                if (spawner.Dropped != lastDropped)
                {
                    lastDropped = spawner.Dropped;
                    dropTimes.Add(Time.time - sceneStart);
                }

                maxActive = Mathf.Max(maxActive, Object.FindObjectsByType<FallingObstacle>(FindObjectsSortMode.None).Length);

                if (tracked == null && dropTimes.Count == 1)
                {
                    tracked = Object.FindAnyObjectByType<FallingObstacle>();
                    trackedStartX = tracked.transform.position.x;
                }

                if (tracked != null)
                {
                    trackedMaxDrift = Mathf.Max(trackedMaxDrift, Mathf.Abs(tracked.transform.position.x - trackedStartX));
                    trackedLastY = tracked.transform.position.y;
                }
            }

            StringBuilder report = new StringBuilder("drops at ");
            for (int i = 0; i < dropTimes.Count; i++)
            {
                report.Append($"{dropTimes[i]:F2} ");
            }

            int remaining = Object.FindObjectsByType<FallingObstacle>(FindObjectsSortMode.None).Length;
            report.Append($"s; most at once {maxActive}; first obstacle drifted {trackedMaxDrift:F3} sideways, last seen at y {trackedLastY:F2}; " +
                          $"destroy height {spawner.DestroyHeight}; existing now {remaining}");

            Assert.GreaterOrEqual(dropTimes.Count, Cycles + 1, "Several cycles should have dropped. " + report);
            Assert.AreEqual(spawner.InitialDelay, dropTimes[0], 0.1f, "The first drop comes after the initial delay. " + report);
            for (int i = 1; i < dropTimes.Count; i++)
            {
                Assert.AreEqual(spawner.Interval, dropTimes[i] - dropTimes[i - 1], 0.05f, $"Drop {i + 1} should follow the interval. " + report);
            }

            Assert.Less(trackedMaxDrift, 0.001f, "An obstacle should fall straight down. " + report);
            Assert.IsTrue(tracked == null, "The first obstacle should have been destroyed. " + report);
            Assert.Less(trackedLastY, spawner.DestroyHeight + 0.5f, "It should only be destroyed at the destroy height. " + report);
            Assert.LessOrEqual(maxActive, 2, "Obstacles must not accumulate. " + report);
            Assert.Pass(report.ToString());
        }

        [UnityTest]
        public IEnumerator CrossingTheChute_BetweenDrops_IsSafe_ButIntoAnObstacle_SendsThePlayerBack()
        {
            yield return LoadScene();
            yield return Teleport(ChuteGapStart - 2.5f, Ledge2Top);

            FallingObstacle Lowest()
            {
                FallingObstacle lowest = null;
                foreach (FallingObstacle o in Object.FindObjectsByType<FallingObstacle>(FindObjectsSortMode.None))
                {
                    if (o.transform.position.y > Ledge2Top - 3f && (lowest == null || o.transform.position.y < lowest.transform.position.y))
                    {
                        lowest = o;
                    }
                }

                return lowest;
            }

            // Bad timing: start the jump when an obstacle is just above the route, so the jump rises into it.
            yield return WaitUntil(() => Lowest() != null && Lowest().transform.position.y < Ledge2Top + 6.2f && Lowest().transform.position.y > Ledge2Top + 5f, 6f);
            FallingObstacle obstacle = Lowest();
            Assert.NotNull(obstacle, "An obstacle should come down the chute.");
            TraversalHazard hazard = obstacle.GetComponent<TraversalHazard>();
            yield return RunAndJumpAt(ChuteGapStart - 1.2f);
            yield return WaitUntil(() => hazard == null || hazard.Hits > 0, 1.5f);
            int hits = hazard != null ? hazard.Hits : 0;
            Vector3 resetAt = player.transform.position;
            yield return ReleaseAll();
            yield return WaitSeconds(0.3f);
            string badTiming = $"bad timing: obstacle hits {hits}, player sent to {resetAt}";
            Assert.AreEqual(1, hits, "Jumping into a falling obstacle should count as a hit. " + badTiming);
            Assert.AreEqual(Entry3X, resetAt.x, 0.3f, "A hit sends the player back to the start of room 3. " + badTiming);

            // Good timing: wait until an obstacle has dropped below the route, then cross.
            yield return Teleport(ChuteGapStart - 2.5f, Ledge2Top);
            yield return WaitForDropToPass();
            yield return RunAndJumpAt(ChuteGapStart - 1.2f);
            yield return WaitUntil(() => StandingOn(Ledge2Top) && X > ChuteGapEnd + 0.5f, 2f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.3f);
            string result = badTiming + $"; good timing: across at {player.transform.position}";
            Assert.IsTrue(StandingOn(Ledge2Top) && X > ChuteGapEnd, "Crossing between drops should reach the far side. " + result);
            Assert.Pass(result);
        }

        //==================== 5: BARRIER, CHIMNEY, GOAL =====================
        [UnityTest]
        public IEnumerator BarrierThenChimney_WallJumpsUpToTheGoal()
        {
            yield return LoadScene();
            LevelGoal goal = Find<LevelGoal>("Route Goal");
            yield return Teleport(Entry4X, Ledge2Top);
            yield return ClimbFinalSection();

            string result = $"goal reached={goal.Reached}, end {player.transform.position}, states seen: {string.Join(", ", statesSeen)}";
            Assert.IsTrue(goal.Reached, "The chimney should lead to the goal. " + result);
            Assert.Pass(result);
        }

        // Barrier jump onto F1, wall jump to F2 before the cap, wall jump back to F1 above the cap, over the top.
        private IEnumerator ClimbFinalSection()
        {
            yield return RunAndJumpAt(Barrier2X - 2.2f);
            yield return WaitUntil(() => controller.IsWallRunning, 1.5f);
            Assert.IsTrue(controller.IsWallRunning && X > F1Face - 1f, $"Jumping the barrier should land on F1. At {player.transform.position}");

            yield return WaitUntil(() => Y > Ledge2Top + 3.2f, 1f);
            yield return TapJump();
            yield return Turn(keyboard.dKey, keyboard.aKey);
            yield return WaitUntil(() => controller.IsWallRunning, 1.5f);
            Assert.IsTrue(controller.IsWallRunning && X < F2Face + 1f, $"The wall jump should reach F2. At {player.transform.position}");

            yield return WaitUntil(() => Y > CapTopY + 2.5f, 1.5f);
            yield return TapJump();
            yield return Turn(keyboard.aKey, keyboard.dKey);
            yield return WaitUntil(() => controller.IsWallRunning, 1.5f);
            Assert.IsTrue(controller.IsWallRunning && X > F1Face - 1f && Y > CapTopY, $"The wall jump should reach F1 above the cap. At {player.transform.position}");

            yield return WaitUntil(() => StandingOn(FinalTop) && X > F1Face + 1f, 3f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.5f);
        }

        //==================== EFFECTS =====================
        [UnityTest]
        public IEnumerator Effects_FollowJumpLandingAndWallRun()
        {
            yield return LoadScene();
            yield return Teleport(StartX, StartTop);
            ParticleSystem jumpDust = Dust("FX Jump Dust");
            ParticleSystem landingDust = Dust("FX Landing Dust");

            // A ground jump over the first barrier, then the landing behind it.
            int jumpsBefore = effects.JumpBursts;
            yield return RunAndJumpAt(Barrier1X - 2.2f);
            yield return null;
            int jumpParticles = jumpDust.particleCount;
            float jumpDustHeight = jumpParticles > 0 ? DustCenter(jumpDust).y - (player.transform.position.y - StandingHeight) : float.NaN;
            yield return WaitUntil(() => controller.IsGrounded, 2f);
            yield return null;
            int landingParticles = landingDust.particleCount;
            float outward = OutwardShare(landingDust);
            bool trailWhileRunning = Trail().emitting;

            // Into the curve: sparks and trail only while the wall run lasts.
            yield return WaitUntil(() => controller.IsWallRunning, 2f);
            yield return null;
            bool sparksOnWall = Sparks().isEmitting;
            bool trailOnWall = Trail().emitting;
            yield return WaitUntil(() => !controller.IsWallRunning, 3f);
            yield return null;
            bool sparksAfter = Sparks().isEmitting;
            bool trailAfter = Trail().emitting;
            yield return ReleaseAll();
            yield return WaitSeconds(1f);

            string result = $"jump bursts {effects.JumpBursts - jumpsBefore} ({jumpParticles} particles, {jumpDustHeight:F2} above the feet), " +
                            $"landing particles {landingParticles} ({outward:P0} of their speed sideways), trail while running on the ground={trailWhileRunning}, " +
                            $"on the wall sparks={sparksOnWall} trail={trailOnWall}, after the wall sparks={sparksAfter} trail={trailAfter}, " +
                            $"frames with trail but no wall run={framesTrailWithoutWallRun}, with sparks but no wall run={framesSparksWithoutWallRun}";
            Assert.AreEqual(1, effects.JumpBursts - jumpsBefore, "One ground jump, one burst. " + result);
            Assert.Greater(jumpParticles, 0, "The jump should emit dust. " + result);
            Assert.Less(Mathf.Abs(jumpDustHeight), 0.5f, "Jump dust starts at the feet. " + result);
            Assert.Greater(landingParticles, 0, "Landing after the barrier jump should emit dust. " + result);
            Assert.Greater(outward, 0.8f, "Landing dust should push outward, not up. " + result);
            Assert.IsFalse(trailWhileRunning, "No trail during ordinary running. " + result);
            Assert.IsTrue(sparksOnWall && trailOnWall, "Sparks and trail during the wall run. " + result);
            Assert.IsFalse(sparksAfter || trailAfter, "Both stop when the wall run ends. " + result);
            Assert.AreEqual(0, framesTrailWithoutWallRun, "The trail must never emit without a wall run. " + result);
            Assert.AreEqual(0, framesSparksWithoutWallRun, "Sparks must never emit without a wall run. " + result);
            Assert.Pass(result);
        }

        private static Vector3 DustCenter(ParticleSystem system)
        {
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[system.particleCount];
            int count = system.GetParticles(particles);
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                sum += particles[i].position;
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        // How much of the particles' speed points sideways (along the ground) rather than up or down.
        private static float OutwardShare(ParticleSystem system)
        {
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[system.particleCount];
            int count = system.GetParticles(particles);
            float sideways = 0f;
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = particles[i].velocity;
                sideways += new Vector2(v.x, v.z).magnitude;
                total += v.magnitude;
            }

            return total > 0f ? sideways / total : 0f;
        }

        //==================== THE WHOLE ROUTE =====================
        [UnityTest]
        public IEnumerator FullRoute_WithKeyboardAndMouse_ReachesTheGoal()
        {
            yield return LoadScene();
            Swappable beacon = Find<Swappable>("Swap Beacon (gap)");
            LevelGoal goal = Find<LevelGoal>("Route Goal");
            float startTime = Time.time;
            StringBuilder report = new StringBuilder();

            // 1 and 2: run, jump the barrier, up the curve and the vertical wall, onto ledge 1.
            yield return Teleport(StartX, StartTop);
            yield return RunAndJumpAt(Barrier1X - 2.2f);
            yield return WaitUntil(() => StandingOn(Ledge1Top) && X > Ledge1Start + 3f, 5f);
            Assert.IsTrue(StandingOn(Ledge1Top), $"Should reach ledge 1. At {player.transform.position}");
            report.Append($"ledge 1 at {Time.time - startTime:F1} s; ");

            // 3: jump, swap in mid-air, run up wall E, onto ledge 2.
            yield return WaitUntil(() => X >= Ledge1Edge - 0.1f, 3f);
            yield return TapJump();
            yield return WaitSeconds(0.2f);
            yield return AimAndClick(beacon);
            yield return WaitUntil(() => StandingOn(Ledge2Top) && X > Ledge2Start, 3f);
            Assert.IsTrue(StandingOn(Ledge2Top), $"Should reach ledge 2. At {player.transform.position}");
            report.Append($"ledge 2 at {Time.time - startTime:F1} s; ");

            // 4: wait for a drop to pass below the route, then cross the chute gap.
            yield return ReleaseAll();
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= ChuteGapStart - 2.5f, 3f);
            yield return ReleaseAll();
            yield return WaitForDropToPass();
            yield return RunAndJumpAt(ChuteGapStart - 1.2f);
            yield return WaitUntil(() => StandingOn(Ledge2Top) && X > ChuteGapEnd + 0.5f, 2f);
            Assert.IsTrue(StandingOn(Ledge2Top) && X > ChuteGapEnd, $"Should cross the chute. At {player.transform.position}");
            report.Append($"chute crossed at {Time.time - startTime:F1} s; ");

            // 5: barrier, chimney, goal.
            yield return ReleaseAll();
            yield return ClimbFinalSection();
            report.Append($"goal at {Time.time - startTime:F1} s; ");

            report.Append($"animator states seen: {string.Join(", ", statesSeen)}; closest to screen edge {closestToScreenEdge:F2}; " +
                          $"camera frames inside geometry {framesCameraInsideGeometry}; jump bursts {effects.JumpBursts}, landing bursts {effects.LandingBursts}; " +
                          $"trail frames without wall run {framesTrailWithoutWallRun}");
            Assert.IsTrue(goal.Reached, "The route should end at the goal. " + report);
            foreach (string state in new[] { "Idle", "Run", "Jump", "Fall", "Land", "WallRun", "Traverse", "WallJump", "Swap" })
            {
                Assert.IsTrue(statesSeen.Contains(state), $"The Animator should have played {state}. " + report);
            }

            Assert.AreEqual(0, framesCameraInsideGeometry, "The camera should stay out of geometry. " + report);
            Assert.Greater(closestToScreenEdge, 0.05f, "The player should stay on screen. " + report);
            Assert.AreEqual(0, framesTrailWithoutWallRun, "The trail must only show during wall runs. " + report);
            Assert.Pass(report.ToString());
        }

        // Waits, as a player watching the chute would, until an obstacle falls below the route. The next one is then
        // the furthest away it gets, so crossing straight after is safe.
        private IEnumerator WaitForDropToPass()
        {
            const float BelowRouteY = Ledge2Top - 3f;
            FallingObstacle watched = null;
            yield return WaitUntil(() =>
            {
                if (watched == null)
                {
                    foreach (FallingObstacle o in Object.FindObjectsByType<FallingObstacle>(FindObjectsSortMode.None))
                    {
                        if (o.transform.position.y > BelowRouteY)
                        {
                            watched = o;
                        }
                    }

                    return false;
                }

                return watched.transform.position.y < BelowRouteY;
            }, 8f);
            Assert.IsTrue(watched != null && watched.transform.position.y < BelowRouteY, "An obstacle should fall past the route.");
        }
    }
}

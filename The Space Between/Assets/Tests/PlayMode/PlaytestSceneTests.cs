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
    /// Plays Playtest_Full with the real Player, using the keyboard and the mouse as a participant would, and checks that
    /// a session can be finished, recovered from and started again. Each test reports its measurements in the pass message.
    /// Whether new players understand the route is not something a test can answer.
    /// The positions below match the scene layout, so moving geometry means updating them here.
    /// </summary>
    public class PlaytestSceneTests : InputTestFixture
    {
        private const string SceneName = "Playtest_Full";
        private const float StandingHeight = 1.08f;
        private const float SwapWaitLimitSeconds = 3f;
        private const float CameraProbeRadius = 0.2f;
        private const float BlendSeconds = 1f;
        private const int AimFrames = 3;
        private const int SectionCount = 10;

        // The whole body is about 2% of the screen wide, so this keeps all of it in view.
        private const float MinimumScreenMargin = 0.03f;
        private const float SlowFrameSeconds = 0.05f;

        // Half-pipes, measured from their floor.
        private const float PipeRadius = 3f;
        private const float PipeRise = 14f;
        private const float CapBottom = 12.4f;
        private const float FirstJumpHeight = 8f;
        private const float SecondJumpHeight = 12.2f;

        // Scene layout, from left to right.
        private const float StartX = -106f;
        private const float Barrier1X = -99.3f;
        private const float LedgeAStart = -82f;
        private const float LedgeATop = 14f;
        private const float PanelX = -76f;
        private const float DoorAX = -71f;
        private const float PipeAX = -61f;
        private const float TowerATop = 18f;
        private const float ChamberEntryX = -29f;
        private const float ViewingPlateX = 2f;
        private const float BesideGlassX = 11f;
        private const float GlassX = 12f;
        private const float PlatformTop = 0.9f;
        private const float PuzzleEntryX = 35f;
        private const float PadX = 38f;
        private const float Glass2X = 41f;
        private const float DoorBX = 50f;
        private const float WatchedEntryX = 57f;
        private const float CameraX = 66f;
        private const float CameraSafeX = 63.4f;
        private const float DoorCX = 74f;
        private const float ShaftX = 82f;
        private const float ShaftEntryX = 80f;
        private const float ShaftRight = 86.5f;
        private const float WalkTop = 16.9f;
        private const float WalkEntryX = 88f;
        private const float Barrier2X = 90f;
        private const float GapStart = 96f;
        private const float GapEnd = 99f;
        private const float LiftX = 109.5f;
        private const float LiftEnd = 111f;
        private const float RunUpEntryX = 113f;
        private const float LedgeBStart = 132.6f;
        private const float LedgeBEdge = 146f;
        private const float LedgeBTop = 9.9f;
        private const float SwapEntryX = 134.6f;
        private const float LedgeCStart = 162.6f;
        private const float LedgeCTop = 16.4f;
        private const float FinalEntryX = 164.6f;
        private const float Barrier3X = 170f;
        private const float PipeBX = 180.2f;

        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject player;
        private PlaceholderPlayerController controller;
        private SwapAbility ability;
        private PlayerAnimationDriver animationDriver;
        private ObservationPoint point;
        private ObservationCameraController cameraController;
        private LevelGoal goal;
        private PlaytestSession session;
        private FallingObstacleSpawner spawner;
        private SecurityCamera securityCamera;
        private MovingPlatform lift;
        private Swappable housing;
        private Swappable housing2;
        private Swappable crate;
        private Swappable beacon;
        private AccessDoor doorA;
        private AccessDoor doorB;
        private AccessDoor doorC;
        private Camera view;

        private readonly HashSet<string> statesSeen = new HashSet<string>();
        private float closestToScreenEdge;
        private Vector3 closestAt;
        private int framesCameraInsideGeometry;
        private int framesWatched;
        private int slowFrames;
        private float longestFrame;
        private float frameTimeTotal;

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

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Playtest"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        //==================== HELPERS =====================
        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            FindSceneObjects();
        }

        private static T Find<T>(string name) where T : Component
        {
            GameObject found = GameObject.Find(name);
            Assert.NotNull(found, $"The scene needs '{name}'.");
            T component = found.GetComponent<T>();
            Assert.NotNull(component, $"'{name}' needs a {typeof(T).Name}.");
            return component;
        }

        private void FindSceneObjects()
        {
            player = GameObject.Find("Player");
            Assert.NotNull(player, $"{SceneName} needs a Player.");
            controller = player.GetComponent<PlaceholderPlayerController>();
            ability = player.GetComponent<SwapAbility>();
            animationDriver = player.GetComponent<PlayerAnimationDriver>();
            point = Object.FindAnyObjectByType<ObservationPoint>();
            cameraController = Object.FindAnyObjectByType<ObservationCameraController>();
            goal = Object.FindAnyObjectByType<LevelGoal>();
            session = Object.FindAnyObjectByType<PlaytestSession>();
            spawner = Object.FindAnyObjectByType<FallingObstacleSpawner>();
            securityCamera = Object.FindAnyObjectByType<SecurityCamera>();
            lift = Find<MovingPlatform>("Movable Platform");
            housing = Find<Swappable>("Swap Housing");
            housing2 = Find<Swappable>("Swap Housing 2");
            crate = Find<Swappable>("Swap Crate");
            beacon = Find<Swappable>("Swap Beacon (gap)");
            doorA = Find<AccessDoor>("Sealed Door (panel)");
            doorB = Find<AccessDoor>("Sealed Door (pad)");
            doorC = Find<AccessDoor>("Sealed Door (camera)");
            view = Camera.main;
        }

        private float X => player.transform.position.x;

        private float Y => player.transform.position.y;

        private bool StandingOn(float floorY)
        {
            return controller.IsGrounded && Mathf.Abs(Y - (floorY + StandingHeight)) < 0.15f;
        }

        private void Watch()
        {
            // The observation view frames the room on purpose, so the player's place in it is not checked here.
            if (!cameraController.IsBusy)
            {
                Vector3 v = view.WorldToViewportPoint(player.transform.position);
                float toEdge = Mathf.Min(Mathf.Min(v.x, 1f - v.x), Mathf.Min(v.y, 1f - v.y));
                if (toEdge < closestToScreenEdge)
                {
                    closestToScreenEdge = toEdge;
                    closestAt = player.transform.position;
                }
            }

            if (Physics.CheckSphere(view.transform.position, CameraProbeRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                framesCameraInsideGeometry++;
            }

            statesSeen.Add(animationDriver.CurrentState);
            framesWatched++;
            frameTimeTotal += Time.unscaledDeltaTime;
            longestFrame = Mathf.Max(longestFrame, Time.unscaledDeltaTime);
            if (Time.unscaledDeltaTime > SlowFrameSeconds)
            {
                slowFrames++;
            }
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

        private IEnumerator Tap(ButtonControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
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

        private IEnumerator Turn(KeyControl from, KeyControl to)
        {
            Release(from);
            yield return null;
            Press(to);
            yield return null;
        }

        private IEnumerator AimAndClick(Swappable target)
        {
            for (int i = 0; i < AimFrames; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(target.Center));
                yield return null;
            }

            yield return Tap(mouse.leftButton);

            // Game time stops during a smooth swap, so this waits in real time.
            float start = Time.realtimeSinceStartup;
            while (ability.IsSwapping && Time.realtimeSinceStartup - start < SwapWaitLimitSeconds)
            {
                yield return null;
            }
        }

        private IEnumerator Teleport(float x, float floorY)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = new Vector3(x, floorY + StandingHeight + 0.02f, 0f);
            cc.enabled = true;
            Physics.SyncTransforms();
            yield return WaitSeconds(0.6f);
        }

        private IEnumerator SectionKey(ButtonControl key)
        {
            yield return Tap(key);
            yield return WaitSeconds(0.4f);
        }

        // Waits, as a player watching the chute would, until an obstacle falls below the walkway. The next one is then
        // the furthest away it gets, so crossing straight after is safe.
        private IEnumerator WaitForDropToPass()
        {
            const float BelowWalkY = WalkTop - 3f;
            FallingObstacle watched = null;
            yield return WaitUntil(() =>
            {
                if (watched == null)
                {
                    foreach (FallingObstacle o in Object.FindObjectsByType<FallingObstacle>(FindObjectsSortMode.None))
                    {
                        if (o.transform.position.y > BelowWalkY)
                        {
                            watched = o;
                        }
                    }

                    return false;
                }

                return watched.transform.position.y < BelowWalkY;
            }, 8f);
            Assert.IsTrue(watched != null && watched.transform.position.y < BelowWalkY, "An obstacle should fall past the walkway.");
        }

        //==================== THE ROUTE, PIECE BY PIECE =====================
        private IEnumerator PlayArrivalAndWallRun()
        {
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= Barrier1X - 2.2f, 4f);
            yield return Tap(keyboard.spaceKey);
            yield return WaitUntil(() => StandingOn(LedgeATop) && X > LedgeAStart + 0.5f, 5f);
            Assert.IsTrue(StandingOn(LedgeATop), $"The curve should lead onto the ledge. At {player.transform.position}");
        }

        private IEnumerator PlayControlRoom()
        {
            yield return WaitUntil(() => X > PanelX - 1f, 3f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.3f);
            ControlPanel panel = Find<ControlPanel>("Control Panel");
            Assert.IsTrue(panel.PlayerInReach, $"The panel should be in reach. At {player.transform.position}");
            yield return Tap(keyboard.eKey);
            yield return WaitUntil(() => doorA.State == AccessDoor.DoorState.Open, 3f);
            Assert.AreEqual(AccessDoor.DoorState.Open, doorA.State, "E at the panel should open the door.");
        }

        // From the ledge, off the edge and into the far wall, wall jump to the near wall, back above the cap, over the top.
        private IEnumerator PlayHalfPipe(float center, float ledgeTop, float towerFar)
        {
            float floor = ledgeTop - PipeRise;
            float farFace = center + PipeRadius;
            yield return WaitUntil(() => controller.IsWallRunning && X > center, 4f);
            Assert.IsTrue(controller.IsWallRunning && X > center, $"The player should run up the far wall. At {player.transform.position}");
            yield return WaitUntil(() => Y > floor + FirstJumpHeight, 2f);
            yield return Tap(keyboard.spaceKey);
            yield return Turn(keyboard.dKey, keyboard.aKey);
            yield return WaitUntil(() => controller.IsWallRunning && X < center, 1.5f);
            Assert.IsTrue(controller.IsWallRunning && X < center, $"The wall jump should reach the near wall. At {player.transform.position}");
            yield return WaitUntil(() => Y > floor + SecondJumpHeight, 1.5f);
            yield return Tap(keyboard.spaceKey);
            yield return Turn(keyboard.aKey, keyboard.dKey);

            // A strong jump can also clear the far wall's top outright, which gets through just the same.
            yield return WaitUntil(() => (controller.IsWallRunning && X > center && Y > floor + CapBottom) || X > farFace + 1f, 1.5f);
            Assert.IsTrue((controller.IsWallRunning && X > center && Y > floor + CapBottom) || X > farFace + 1f, $"The second wall jump should pass above the cap. At {player.transform.position}");
            yield return WaitUntil(() => controller.IsGrounded && X > farFace + 1f, 3f);
            Assert.IsTrue(controller.IsGrounded && X > farFace + 1f, $"The half-pipe should end beyond the far wall. At {player.transform.position}, tower to x {towerFar}");
        }

        private IEnumerator PlayStairsAndCorridor()
        {
            yield return WaitUntil(() => X > ViewingPlateX - 1.5f, 8f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.5f);
        }

        private IEnumerator PlayObservation()
        {
            Assert.IsTrue(point.IsAvailable, $"The viewing plate should offer the observation. At {player.transform.position}");
            yield return Tap(keyboard.eKey);
            yield return WaitSeconds(BlendSeconds);
            Assert.IsTrue(point.IsObserving, "E should enter observation.");
            Assert.IsTrue(controller.ControlLocked);
            yield return WaitSeconds(0.5f);
            yield return Tap(keyboard.eKey);
            yield return WaitSeconds(BlendSeconds);
            Assert.IsFalse(cameraController.IsBusy);
            Assert.IsFalse(controller.ControlLocked, "Control should be back.");
        }

        private IEnumerator PlayChamberSwap()
        {
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X > BesideGlassX - 0.5f, 3f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.5f);
            yield return AimAndClick(housing);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The housing behind the glass should be in range. At {player.transform.position}");
            Assert.Greater(X, GlassX, "The swap should put the player beyond the glass.");
            yield return WaitSeconds(0.5f);
        }

        private IEnumerator PlayLeaveSomethingBehind()
        {
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X > PadX - 3.5f, 4f);
            Release(keyboard.leftShiftKey);
            yield return null;
            yield return WaitUntil(() => X > PadX - 0.1f, 2f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.6f);
            Assert.AreEqual(AccessDoor.DoorState.Open, doorB.State, $"Standing on the pad should open the door. At {player.transform.position}");
            yield return AimAndClick(housing2);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The second housing should be in range from the pad. At {player.transform.position}");
            Assert.Greater(X, Glass2X, "The swap should cross the second glass.");
            yield return WaitSeconds(0.8f);
            Assert.AreEqual(AccessDoor.DoorState.Open, doorB.State, "The housing left on the pad should hold the door open.");
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X > DoorBX + 1f, 3f);
            Assert.Greater(X, DoorBX, $"The open door should let the player through. At {player.transform.position}");
        }

        private IEnumerator PlayWatchedRoom()
        {
            yield return WaitUntil(() => X > WatchedEntryX + 1f, 3f);
            Release(keyboard.leftShiftKey);
            yield return null;
            yield return WaitUntil(() => X > CameraSafeX - 0.15f, 3f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.5f);
            Assert.IsFalse(securityCamera.IsAlarmed, $"The edge of the light should be safe. At {player.transform.position}");
            yield return AimAndClick(crate);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The crate should be in range across the light. At {player.transform.position}");
            Assert.Greater(X, CameraX, "The swap should cross the light.");
            yield return WaitSeconds(0.4f);
            Assert.IsFalse(securityCamera.IsAlarmed, $"The swap should cross the light unseen. At {player.transform.position}");
            Assert.AreEqual(AccessDoor.DoorState.Open, doorC.State, "The door should still be open.");
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X > DoorCX + 1f, 3f);
            Assert.Greater(X, DoorCX, $"The door should let the player through. At {player.transform.position}");
        }

        private IEnumerator PlayShaft()
        {
            Release(keyboard.leftShiftKey);
            yield return null;
            yield return WaitUntil(() => X > ShaftX - 0.2f, 3f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.6f);
            for (int i = 1; i <= 4; i++)
            {
                yield return AimAndClick(Find<Swappable>($"Shaft Beacon {i}"));
                Assert.AreEqual(SwapRejection.None, ability.LastResult, $"Shaft beacon {i} should be in range. At {player.transform.position}");
                yield return Tap(keyboard.spaceKey);
            }

            Press(keyboard.dKey);
            yield return WaitUntil(() => StandingOn(WalkTop) && X > ShaftRight - 0.5f, 3f);
            Assert.IsTrue(StandingOn(WalkTop), $"The climb should end on the walkway. At {player.transform.position}");
            Release(keyboard.dKey);
            yield return null;
        }

        private IEnumerator PlayObstaclesAndElevator()
        {
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= Barrier2X - 2.2f, 3f);
            yield return Tap(keyboard.spaceKey);
            yield return WaitUntil(() => X >= GapStart - 2.5f, 3f);
            yield return ReleaseAll();
            yield return WaitForDropToPass();
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= GapStart - 1.2f, 2f);
            yield return Tap(keyboard.spaceKey);
            yield return WaitUntil(() => StandingOn(WalkTop) && X > GapEnd + 0.5f, 2f);
            Assert.IsTrue(StandingOn(WalkTop) && X > GapEnd, $"Should cross the gap between drops. At {player.transform.position}. {FrameReport}");
            Release(keyboard.leftShiftKey);
            yield return null;
            yield return WaitUntil(() => X > LiftX - 0.2f, 3f);
            yield return ReleaseAll();
            yield return WaitUntil(() => lift.Progress >= 1f, 8f);
            Assert.GreaterOrEqual(lift.Progress, 1f, $"The pad should take the elevator down. Player at {player.transform.position}");
            yield return WaitSeconds(0.2f);
            Assert.IsTrue(StandingOn(PlatformTop), $"The player should ride down with the elevator. At {player.transform.position}");
        }

        private IEnumerator PlayCombined()
        {
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => StandingOn(LedgeBTop) && X > LedgeBStart + 0.5f, 6f);
            Assert.IsTrue(StandingOn(LedgeBTop), $"The second curve should lead onto its ledge. At {player.transform.position}");
            yield return WaitUntil(() => X >= LedgeBEdge - 0.1f, 3f);
            yield return Tap(keyboard.spaceKey);
            yield return WaitSeconds(0.2f);
            yield return AimAndClick(beacon);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The beacon should be in range during the jump. At {player.transform.position}");
            yield return WaitUntil(() => StandingOn(LedgeCTop) && X > LedgeCStart, 4f);
            Assert.IsTrue(StandingOn(LedgeCTop), $"The curve after the swap should lead onto the last ledge. At {player.transform.position}");
            yield return WaitUntil(() => X >= Barrier3X - 2.2f, 3f);
            yield return Tap(keyboard.spaceKey);
            yield return PlayHalfPipe(PipeBX, LedgeCTop, 199f);
            yield return WaitUntil(() => goal.Reached, 3f);
            yield return ReleaseAll();
        }

        private IEnumerator PlayWholeRoute(StringBuilder report)
        {
            float start = Time.time;
            yield return PlayArrivalAndWallRun();
            report.Append($"ledge {Time.time - start:F1} s; ");
            yield return PlayControlRoom();
            yield return HoldRun(keyboard.dKey);
            report.Append($"door A {Time.time - start:F1} s; ");
            yield return PlayHalfPipe(PipeAX, LedgeATop, -52f);
            report.Append($"half-pipe {Time.time - start:F1} s; ");
            yield return PlayStairsAndCorridor();
            yield return PlayObservation();
            report.Append($"observed {Time.time - start:F1} s; ");
            yield return PlayChamberSwap();
            report.Append($"chamber {Time.time - start:F1} s; ");
            yield return PlayLeaveSomethingBehind();
            report.Append($"door B {Time.time - start:F1} s; ");
            yield return PlayWatchedRoom();
            report.Append($"watched room {Time.time - start:F1} s; ");
            yield return PlayShaft();
            report.Append($"shaft {Time.time - start:F1} s; ");
            yield return PlayObstaclesAndElevator();
            report.Append($"elevator {Time.time - start:F1} s; ");
            yield return PlayCombined();
            report.Append($"goal {Time.time - start:F1} s; ");
        }

        private string FrameReport => $"average frame {frameTimeTotal / Mathf.Max(1, framesWatched) * 1000f:F1} ms, longest {longestFrame * 1000f:F0} ms, slower than {SlowFrameSeconds * 1000f:F0} ms: {slowFrames} of {framesWatched}";

        private void ResetWatch()
        {
            statesSeen.Clear();
            closestToScreenEdge = 1f;
            framesCameraInsideGeometry = 0;
            framesWatched = 0;
            slowFrames = 0;
            longestFrame = 0f;
            frameTimeTotal = 0f;
        }

        // What a fresh session has to look like, whatever happened before it.
        private void AssertCleanStart(string when)
        {
            string state = $"{when}: player {player.transform.position}, goal {goal.Reached}, dropped {spawner.Dropped}, timeScale {Time.timeScale}, " +
                           $"state {animationDriver.CurrentState}, doors {doorA.State} {doorB.State} {doorC.State}, alarm {securityCamera.IsAlarmed}, lift {lift.Progress}, " +
                           $"housing {housing.Position}, housing 2 {housing2.Position}, crate {crate.Position}, beacon {beacon.Position}";
            Assert.AreEqual(1f, Time.timeScale, "Game time must run. " + state);
            Assert.AreEqual(StartX, X, 0.3f, "The player must be at the start. " + state);
            Assert.AreEqual(StandingHeight, Y, 0.2f, "The player must stand on the floor. " + state);
            Assert.IsFalse(goal.Reached, "The goal must be unreached. " + state);
            Assert.IsFalse(controller.IsWallRunning, state);
            Assert.IsFalse(controller.ControlLocked, state);
            Assert.IsTrue(ability.enabled, state);
            Assert.IsFalse(ability.IsSwapping, state);
            Assert.IsFalse(point.IsObserving, state);
            Assert.IsFalse(cameraController.IsBusy, state);
            Assert.AreEqual(0, session.SectionKeysUsed, state);
            Assert.AreEqual(AccessDoor.DoorState.Closed, doorA.State, "The panel door must be closed. " + state);
            Assert.AreEqual(AccessDoor.DoorState.Closed, doorB.State, "The pad door must be closed. " + state);
            Assert.AreEqual(AccessDoor.DoorState.Open, doorC.State, "The watched room's door starts open. " + state);
            Assert.IsFalse(securityCamera.IsAlarmed, "No alarm may carry over. " + state);
            Assert.AreEqual(0f, lift.Progress, 0.01f, "The elevator must be at the top. " + state);
            Assert.Greater(housing.Position.x, GlassX, "The first housing must be behind its glass. " + state);
            Assert.Greater(housing2.Position.x, Glass2X, "The second housing must be behind its glass. " + state);
            Assert.Greater(crate.Position.x, CameraX, "The crate must be across the light. " + state);
            Assert.AreEqual(LedgeBEdge + 5.5f, beacon.Position.x, 0.01f, "The beacon must be over the gap. " + state);
            Assert.AreEqual(ShaftX, Find<Swappable>("Shaft Beacon 1").Position.x, 0.01f, "The shaft beacons must be in place. " + state);
            Assert.LessOrEqual(spawner.ActiveCount, 1, "Only the current schedule's obstacles may exist. " + state);
            Assert.IsFalse(player.transform.Find("Body/FX Wall Run Trail").GetComponent<TrailRenderer>().emitting, state);
            Assert.IsFalse(player.transform.Find("Body/FX Wall Run Sparks").GetComponent<ParticleSystem>().isEmitting, state);
        }

        private IEnumerator Restart()
        {
            yield return Tap(keyboard.f12Key);
            yield return null;
            yield return null;
            FindSceneObjects();
        }

        //==================== THE TWO STANDING REQUIREMENTS =====================
        // Every wall run starts from a curve, never a plain straight wall.
        [UnityTest]
        public IEnumerator EveryWallRunSurface_StartsFromACurvedWall()
        {
            yield return LoadScene();
            WallRunSurface[] surfaces = Object.FindObjectsByType<WallRunSurface>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(surfaces.Length, 6, "The route should have its curves.");
            foreach (WallRunSurface surface in surfaces)
            {
                Assert.NotNull(surface.GetComponent<CurvedWall>(), $"'{surface.name}' is runnable but is not a CurvedWall.");
            }

            Assert.Pass($"{surfaces.Length} runnable surfaces, all CurvedWall");
        }

        [UnityTest]
        public IEnumerator EveryEnvironmentAsset_IsInTheRoute()
        {
            yield return LoadScene();
            string[] names =
            {
                "Control Panel", "Pressure Pad", "Elevator Pad", "Sealed Door (panel)", "Sealed Door (pad)", "Sealed Door (camera)",
                "Security Camera", "Movable Platform", "Vertical Shaft", "Glass Partition", "Glass Partition 2", "Low Barrier 1",
                "Swap Crate", "Swap Housing", "Swap Housing 2", "Swap Beacon (gap)", "Shaft Beacon 4", "Falling Obstacle Spawner"
            };
            foreach (string name in names)
            {
                Assert.NotNull(GameObject.Find(name), $"The route should contain '{name}'.");
            }

            Assert.Pass($"{names.Length} objects found");
        }

        //==================== THE WHOLE SESSION =====================
        [UnityTest]
        public IEnumerator TwoSessionsInARow_EachReachTheEnd_WithACleanRestartBetween()
        {
            yield return LoadScene();
            yield return WaitSeconds(0.3f);
            AssertCleanStart("first start");
            ResetWatch();

            StringBuilder report = new StringBuilder("session 1: ");
            yield return PlayWholeRoute(report);
            Assert.IsTrue(goal.Reached, "The first session should reach the end. " + report);
            foreach (string state in new[] { "Idle", "Run", "Jump", "Fall", "Land", "WallRun", "Traverse", "WallJump", "Swap" })
            {
                Assert.IsTrue(statesSeen.Contains(state), $"The Animator should have played {state}. Seen: {string.Join(", ", statesSeen)}. " + report);
            }

            Assert.AreEqual(0, framesCameraInsideGeometry, "The camera should stay out of geometry. " + report);
            Assert.Greater(closestToScreenEdge, MinimumScreenMargin, $"The player should stay on screen. Closest at {closestAt}. {FrameReport}. " + report);
            report.Append($"closest to screen edge {closestToScreenEdge:F2} at {closestAt}; states {statesSeen.Count}; {FrameReport}. ");

            yield return Restart();
            yield return WaitSeconds(0.3f);
            AssertCleanStart("after restart");
            ResetWatch();

            report.Append("session 2: ");
            yield return PlayWholeRoute(report);
            report.Append(FrameReport);
            Assert.IsTrue(goal.Reached, "The second session should reach the end without any repair. " + report);
            Assert.AreEqual(0, framesCameraInsideGeometry, "The camera should stay out of geometry. " + report);
            Assert.Pass(report.ToString());
        }

        //==================== THE NEW ROOMS =====================
        [UnityTest]
        public IEnumerator SealedDoor_StopsThePlayer_UntilThePanelIsUsed()
        {
            yield return LoadScene();
            yield return SectionKey(keyboard.f2Key);
            yield return HoldRun(keyboard.dKey);
            yield return WaitSeconds(2f);
            yield return ReleaseAll();
            float stoppedAt = X;
            Assert.Less(stoppedAt, DoorAX, "The closed door must stop the player.");

            yield return Teleport(PanelX, LedgeATop);
            yield return Tap(keyboard.eKey);
            yield return WaitUntil(() => doorA.State == AccessDoor.DoorState.Open, 3f);
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X > DoorAX + 1f, 2f);
            yield return ReleaseAll();
            string result = $"stopped at x {stoppedAt:F2}, door at {DoorAX}, then through to x {X:F2}";
            Assert.Greater(X, DoorAX, result);
            Assert.Pass(result);
        }

        // The way in is a drop under a low hood, so even the best jump off the ledge must not clear the cap.
        [UnityTest]
        public IEnumerator HalfPipe_CannotBeSkipped_ByJumpingAcross()
        {
            yield return LoadScene();
            yield return Teleport(DoorAX + 1f, LedgeATop);
            float nearFace = PipeAX - PipeRadius;
            float farFace = PipeAX + PipeRadius;
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= nearFace + 0.2f, 2f);
            yield return Tap(keyboard.spaceKey);

            float highestOnFarWall = 0f;
            float end = Time.time + 3f;
            while (Time.time < end)
            {
                yield return null;
                if (controller.IsWallRunning && X > PipeAX)
                {
                    highestOnFarWall = Mathf.Max(highestOnFarWall, Y);
                }
            }

            yield return ReleaseAll();
            string result = $"highest on the far wall {highestOnFarWall:F2}, cap underside {CapBottom}, at {player.transform.position}";
            Assert.Less(X, farFace + 1f, "Jumping across must not reach the tower. " + result);
            Assert.Less(highestOnFarWall, CapBottom, "Without a wall jump the cap stops the run. " + result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator SecurityCamera_WalkingIntoTheLight_SealsTheDoor_AndRClearsIt()
        {
            yield return LoadScene();
            yield return SectionKey(keyboard.f5Key);
            Assert.AreEqual(WatchedEntryX, X, 0.3f, "F5 is the watched room.");
            Assert.AreEqual(AccessDoor.DoorState.Open, doorC.State);

            Press(keyboard.dKey);
            yield return WaitUntil(() => securityCamera.IsAlarmed, 4f);
            yield return ReleaseAll();
            Assert.IsTrue(securityCamera.IsAlarmed, $"Walking into the light should raise the alarm. At {player.transform.position}");
            float seenAt = X;
            yield return WaitUntil(() => doorC.State == AccessDoor.DoorState.Closed, 3f);
            Assert.AreEqual(AccessDoor.DoorState.Closed, doorC.State, "The alarm should seal the door.");

            yield return Tap(keyboard.rKey);
            yield return WaitUntil(() => doorC.State == AccessDoor.DoorState.Open, 3f);
            string result = $"seen at x {seenAt:F2}, after R player at x {X:F2}, alarm {securityCamera.IsAlarmed}, door {doorC.State}";
            Assert.IsFalse(securityCamera.IsAlarmed, "R should clear the alarm. " + result);
            Assert.AreEqual(AccessDoor.DoorState.Open, doorC.State, "R should reopen the door. " + result);
            Assert.AreEqual(WatchedEntryX, X, 0.3f, result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator Shaft_RPutsTheBeaconsBack_AfterAFailedClimb()
        {
            yield return LoadScene();
            yield return SectionKey(keyboard.f6Key);
            Assert.AreEqual(ShaftEntryX, X, 0.3f, "F6 is the shaft.");
            Swappable first = Find<Swappable>("Shaft Beacon 1");
            Vector3 startedAt = first.Position;

            yield return Teleport(ShaftX, PlatformTop);
            yield return AimAndClick(first);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, "The first beacon should be in range from the floor.");
            yield return WaitSeconds(1f);
            Assert.AreNotEqual(startedAt, first.Position, "The swap should have moved the beacon.");

            yield return Tap(keyboard.rKey);
            yield return WaitSeconds(0.3f);
            string result = $"beacon {first.Position}, started {startedAt}, player {player.transform.position}";
            Assert.AreEqual(startedAt.x, first.Position.x, 0.05f, result);
            Assert.AreEqual(startedAt.y, first.Position.y, 0.05f, result);
            Assert.AreEqual(ShaftEntryX, X, 0.3f, result);
            Assert.Pass(result);
        }

        //==================== RESTART FROM ANYTHING =====================
        [UnityTest]
        public IEnumerator Restart_DuringASwap_RestoresGameTimeAndTheStart()
        {
            yield return LoadScene();
            yield return Teleport(BesideGlassX, 0f);
            yield return WaitSeconds(0.3f);
            for (int i = 0; i < AimFrames; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(housing.Center));
                yield return null;
            }

            yield return Tap(mouse.leftButton);
            Assert.IsTrue(ability.IsSwapping, "The swap should be playing when the restart is pressed.");
            Assert.AreEqual(0f, Time.timeScale, "A smooth swap stops game time.");

            yield return Restart();
            yield return WaitSeconds(0.3f);
            AssertCleanStart("restart during a swap");
        }

        [UnityTest]
        public IEnumerator Restart_WhileObserving_RestoresControlAndTheCamera()
        {
            yield return LoadScene();
            yield return Teleport(ViewingPlateX, 0f);
            yield return Tap(keyboard.eKey);
            yield return WaitSeconds(0.2f);
            Assert.IsTrue(controller.ControlLocked, "Observation should have started.");

            yield return Restart();
            yield return WaitSeconds(0.3f);
            AssertCleanStart("restart while observing");
        }

        [UnityTest]
        public IEnumerator Restart_AfterAnAlarmAndTheElevator_RestoresThem()
        {
            yield return LoadScene();
            yield return Teleport(CameraX, PlatformTop);
            yield return WaitUntil(() => securityCamera.IsAlarmed, 2f);
            Assert.IsTrue(securityCamera.IsAlarmed, "Standing in the light should raise the alarm.");
            yield return Teleport(LiftX, WalkTop);
            yield return WaitUntil(() => lift.Progress > 0.3f, 3f);
            Assert.Greater(lift.Progress, 0.3f, "The elevator should be on its way down.");

            yield return Restart();
            yield return WaitSeconds(0.3f);
            AssertCleanStart("restart after an alarm and the elevator");
        }

        [UnityTest]
        public IEnumerator Restart_RestartsTheObstacleSchedule()
        {
            yield return LoadScene();
            yield return WaitSeconds(spawner.InitialDelay + spawner.Interval * 2f);
            int droppedBefore = spawner.Dropped;
            Assert.GreaterOrEqual(droppedBefore, 2, "Obstacles should have dropped by now.");

            yield return Restart();
            int droppedAfter = spawner.Dropped;
            int obstaclesAfter = Object.FindObjectsByType<FallingObstacle>(FindObjectsSortMode.None).Length;
            string result = $"dropped before {droppedBefore}, right after restart {droppedAfter}, obstacles right after {obstaclesAfter}";
            Assert.AreEqual(0, droppedAfter, "The count starts again. " + result);
            Assert.AreEqual(0, obstaclesAfter, "No obstacle survives a restart. " + result);
            Assert.Pass(result);
        }

        //==================== RECOVERING FROM FAILURE =====================
        [UnityTest]
        public IEnumerator FallingIntoTheChutePit_SendsThePlayerBackToTheRoomEntry()
        {
            yield return LoadScene();
            yield return SectionKey(keyboard.f7Key);
            Assert.AreEqual(WalkEntryX, X, 0.3f, "F7 is the falling obstacle section.");

            CharacterController cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = new Vector3((GapStart + GapEnd) * 0.5f, WalkTop + StandingHeight, 0f);
            cc.enabled = true;
            Physics.SyncTransforms();
            yield return WaitUntil(() => X < GapStart, 4f);
            yield return WaitSeconds(0.3f);

            string result = $"sent to {player.transform.position}";
            Assert.AreEqual(WalkEntryX, X, 0.3f, result);
            Assert.IsTrue(StandingOn(WalkTop), result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator JumpWithoutTheSwap_FallsIntoTheGap_AndTheBeaconAndPlayerGoBack()
        {
            yield return LoadScene();
            yield return SectionKey(keyboard.f9Key);
            Assert.AreEqual(SwapEntryX, X, 0.3f, "F9 is the jump and swap section.");

            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= LedgeBEdge - 0.1f, 3f);
            yield return Tap(keyboard.spaceKey);
            float furthest = X;
            yield return WaitUntil(() =>
            {
                furthest = Mathf.Max(furthest, X);
                return X < LedgeBEdge - 2f;
            }, 4f);
            Vector3 resetAt = player.transform.position;
            yield return ReleaseAll();
            yield return WaitSeconds(0.3f);

            string result = $"furthest x {furthest:F2}, edge {LedgeBEdge}, sent to {resetAt}";
            Assert.AreEqual(SwapEntryX, resetAt.x, 0.3f, "A jump alone must not cross the gap, and the pit sends the player back. " + result);
            Assert.AreEqual(LedgeBEdge + 5.5f, beacon.Position.x, 0.01f, result);
            Assert.IsTrue(StandingOn(LedgeBTop), result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator SwappingFromBesideThePad_LeavesTheDoorSealed_AndRRestoresTheRoom()
        {
            yield return LoadScene();
            yield return SectionKey(keyboard.f4Key);
            Assert.AreEqual(PuzzleEntryX, X, 0.3f, "F4 is the puzzle section.");

            yield return Teleport(PadX + 1.6f, PlatformTop);
            yield return WaitSeconds(0.3f);
            Vector3 housingStart = housing2.Position;
            yield return AimAndClick(housing2);
            Assert.AreEqual(SwapRejection.None, ability.LastResult);
            yield return WaitSeconds(0.8f);
            Assert.AreEqual(AccessDoor.DoorState.Closed, doorB.State, "A housing beside the pad must not open the door.");

            // The player is now beyond the glass with the door shut: stuck, until R.
            yield return Tap(keyboard.rKey);
            yield return WaitSeconds(0.5f);
            string result = $"player {player.transform.position}, housing {housing2.Position}, started {housingStart}";
            Assert.AreEqual(PuzzleEntryX, X, 0.3f, "R puts the player back. " + result);
            Assert.AreEqual(housingStart.x, housing2.Position.x, 0.05f, "R puts the housing back. " + result);
            Assert.Pass(result);
        }

        //==================== EVERY SECTION CAN BE REACHED =====================
        [UnityTest]
        public IEnumerator SectionKeys_ReachEverySection_WhereRAlsoWorks()
        {
            yield return LoadScene();
            float[] entries = { StartX, LedgeAStart + 2f, ChamberEntryX, PuzzleEntryX, WatchedEntryX, ShaftEntryX, WalkEntryX, RunUpEntryX, SwapEntryX, FinalEntryX };
            float[] floors = { 0f, LedgeATop, 0f, PlatformTop, PlatformTop, PlatformTop, WalkTop, PlatformTop, LedgeBTop, LedgeCTop };
            ButtonControl[] keys =
            {
                keyboard.f1Key, keyboard.f2Key, keyboard.f3Key, keyboard.f4Key, keyboard.f5Key,
                keyboard.f6Key, keyboard.f7Key, keyboard.f8Key, keyboard.f9Key, keyboard.f10Key
            };
            Assert.AreEqual(SectionCount, keys.Length);
            StringBuilder report = new StringBuilder();

            // Backwards too, as an observer might, so no entry depends on the one before it.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int n = 0; n < keys.Length; n++)
                {
                    int i = pass == 0 ? n : keys.Length - 1 - n;
                    yield return SectionKey(keys[i]);
                    Assert.AreEqual(entries[i], X, 0.3f, $"F{i + 1} should reach its section. At {player.transform.position}");
                    Assert.IsTrue(StandingOn(floors[i]), $"F{i + 1} should stand the player on the floor. At {player.transform.position}, grounded {controller.IsGrounded}");

                    // A short walk, then R returns to the same entry: there is a working room everywhere.
                    Press(keyboard.dKey);
                    yield return WaitSeconds(0.2f);
                    Release(keyboard.dKey);
                    yield return null;
                    yield return Tap(keyboard.rKey);
                    yield return WaitSeconds(0.1f);
                    Assert.AreEqual(entries[i], X, 0.3f, $"R should work in section {i + 1}. At {player.transform.position}");
                    report.Append($"F{i + 1} ");
                }
            }

            Assert.AreEqual(keys.Length * 2, session.SectionKeysUsed, "Every use is counted.");
            Assert.Pass(report.ToString());
        }
    }
}

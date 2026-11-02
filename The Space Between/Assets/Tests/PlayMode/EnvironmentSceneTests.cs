using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Plays Environment_Elements with the real Player, using the keyboard and the mouse as a player would.
    /// One test plays every bay and both combination rooms from the start; the others check that each element
    /// cannot be bypassed and that the wrong approach fails in the way the room is meant to teach.
    /// The positions below match the scene layout, so moving geometry means updating them here.
    /// </summary>
    public class EnvironmentSceneTests : InputTestFixture
    {
        private const string SceneName = "Environment_Elements";
        private const float StandingHeight = 1.08f;
        private const float SwapWaitLimitSeconds = 3f;
        private const float CameraProbeRadius = 0.2f;

        // Floors and the spots the layout was measured for.
        private const float FloorY = 0f;
        private const float LedgeY = 6f;
        private const float PitFloorY = -3f;
        private const float GlassX = 27.3f;
        private const float BarrierTopX = 43f;
        private const float StepThreeY = 4.5f;
        private const float PanelFourX = 58f;
        private const float ElevatorX = 76.5f;
        private const float CameraSixSafeX = 88f;
        private const float CameraSixLitX = 92f;
        private const float ShaftFloorX = 113f;
        private const float ShaftExitY = 16f;
        private const float PadEightX = 133.1f;
        private const float BesidePadEightX = 135.5f;
        private const float GlassEightX = 137.3f;
        private const float DoorEightX = 147f;
        private const float LidStandX = 157f;
        private const float GoalX = 188f;

        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject player;
        private SwapAbility ability;
        private CameraWallRunFraming framing;
        private Camera view;
        private float closestToScreenEdge;
        private int framesCameraInsideGeometry;

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

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Environment Elements"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        //==================== HELPERS =====================
        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;

            player = GameObject.Find("Player");
            Assert.NotNull(player, $"{SceneName} needs a Player.");
            ability = player.GetComponent<SwapAbility>();
            Assert.IsInstanceOf<SmoothSwapAbility>(ability, "The element scene is built for the smooth swap.");
            framing = Object.FindAnyObjectByType<CameraWallRunFraming>();
            view = Camera.main;
            closestToScreenEdge = 1f;
            framesCameraInsideGeometry = 0;
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
            player.transform.position = new Vector3(x, floorY + StandingHeight + 0.02f, 0f);
            cc.enabled = true;
            Physics.SyncTransforms();
            yield return WaitSeconds(0.4f);
        }

        private void Watch()
        {
            Vector3 v = view.WorldToViewportPoint(player.transform.position);
            closestToScreenEdge = Mathf.Min(closestToScreenEdge, Mathf.Min(Mathf.Min(v.x, 1f - v.x), Mathf.Min(v.y, 1f - v.y)));
            if (Physics.CheckSphere(view.transform.position, CameraProbeRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                framesCameraInsideGeometry++;
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

        // Walks until the player passes x, then lets go. Like a player, it jumps when something low blocks the way.
        private IEnumerator WalkTo(float x)
        {
            const float StuckSeconds = 0.15f;
            const float StuckDistance = 0.05f;

            float startSide = Mathf.Sign(x - player.transform.position.x);
            KeyControl key = startSide > 0f ? keyboard.dKey : keyboard.aKey;
            Press(key);
            float end = Time.time + 25f;
            float checkAt = Time.time + StuckSeconds;
            float lastX = player.transform.position.x;
            while (Mathf.Sign(x - player.transform.position.x) == startSide && Time.time < end)
            {
                yield return null;
                Watch();

                if (Time.time < checkAt)
                {
                    continue;
                }

                if (Mathf.Abs(player.transform.position.x - lastX) < StuckDistance)
                {
                    yield return Tap(keyboard.spaceKey);
                }

                lastX = player.transform.position.x;
                checkAt = Time.time + StuckSeconds;
            }

            Release(key);
            Assert.AreNotEqual(startSide, Mathf.Sign(x - player.transform.position.x), $"The player got stuck at {player.transform.position} walking to x {x}.");
            yield return WaitSeconds(0.25f);
        }

        private IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        // Puts the cursor over the target for a couple of frames, clicks, and waits for the smooth swap to end.
        private IEnumerator AimAndClick(Swappable target, float settleSeconds = 0.3f)
        {
            for (int i = 0; i < 2; i++)
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

            yield return WaitSeconds(settleSeconds);
        }

        private void AssertStandingOn(float floorY, string where)
        {
            Assert.AreEqual(floorY + StandingHeight, player.transform.position.y, 0.15f, $"The player should be standing on {where}.");
        }

        //==================== THE WHOLE GALLERY =====================
        [UnityTest]
        public IEnumerator FullPlaythrough_WithMouseAndKeyboard_ReachesTheEnd()
        {
            yield return LoadScene();

            // Bay 2: the glass stops the player, the swap goes through it.
            yield return WalkTo(25f);
            yield return AimAndClick(Find<Swappable>("Swap Crate B"));
            Assert.Greater(player.transform.position.x, GlassX, "Swapped through the glass partition.");

            // Bay 3: over the steps and the barrier. Bay 4: use the panel, the door opens.
            yield return WalkTo(PanelFourX);
            AccessDoor doorFour = Find<AccessDoor>("Sealed Door 4");
            Assert.IsTrue(doorFour.IsLocked);
            Assert.IsTrue(Find<ControlPanel>("Control Panel 4").PlayerInReach);
            yield return Tap(keyboard.eKey);
            yield return WaitUntil(() => doorFour.State == AccessDoor.DoorState.Open);
            Assert.AreEqual(AccessDoor.DoorState.Open, doorFour.State, "The panel opened the sealed door.");

            // Bay 5: the pad on the platform lifts the player to the ledge.
            yield return WalkTo(ElevatorX);
            MovingPlatform elevator = Find<MovingPlatform>("Movable Platform 5");
            yield return WaitUntil(() => elevator.Progress >= 1f);
            yield return WalkTo(81f);
            AssertStandingOn(LedgeY, "the ledge");

            // Bay 6: swap past the watched area instead of walking through it.
            SecurityCamera cameraSix = Find<SecurityCamera>("Security Camera 6");
            yield return WalkTo(CameraSixSafeX);
            Assert.IsFalse(cameraSix.IsAlarmed, "Standing at the edge of the light is not being seen.");
            yield return AimAndClick(Find<Swappable>("Swap Crate D"));
            Assert.IsFalse(cameraSix.IsAlarmed, "The swap crossed the watched area unseen.");
            yield return WalkTo(108f);

            // Bay 7: climb the shaft by swapping with floating beacons, jumping after each swap.
            yield return WalkTo(ShaftFloorX);
            yield return WaitSeconds(1f);
            Assert.GreaterOrEqual(framing.ZoneDistance, 6.9f, "The shaft's camera zone pulled the camera back.");
            for (int i = 1; i <= 4; i++)
            {
                yield return AimAndClick(Find<Swappable>($"Shaft Beacon {i}"), 0f);
                yield return Tap(keyboard.spaceKey);
            }

            Press(keyboard.dKey);
            yield return WaitSeconds(0.5f);
            Release(keyboard.dKey);
            yield return WaitSeconds(0.6f);
            AssertStandingOn(ShaftExitY, "the top of the shaft");
            yield return WalkTo(130f);

            // Combination 1: stand on the pad, swap through the glass, the crate holds the door open.
            yield return WalkTo(PadEightX);
            AccessDoor doorEight = Find<AccessDoor>("Sealed Door 8");
            Assert.IsFalse(doorEight.IsLocked, "Standing on the pad powers the door.");
            yield return AimAndClick(Find<Swappable>("Swap Crate E"));
            yield return WaitSeconds(1f);
            Assert.IsFalse(doorEight.IsLocked, "The crate left on the pad keeps the door powered.");
            yield return WalkTo(150f);

            // Combination 2: swap into the pit through the glass lid, switch the camera off, swap back out.
            yield return WalkTo(LidStandX);
            Swappable beacon = Find<Swappable>("Swap Beacon F");
            yield return AimAndClick(beacon);
            AssertStandingOn(PitFloorY, "the pit floor");
            SecurityCamera cameraNine = Find<SecurityCamera>("Security Camera 9");
            yield return Tap(keyboard.eKey);
            Assert.IsFalse(cameraNine.IsActive, "The panel in the pit switched the camera off.");
            yield return AimAndClick(beacon);
            AssertStandingOn(FloorY, "the glass lid");
            yield return WalkTo(GoalX);

            Assert.IsTrue(Find<LevelGoal>("Gallery Goal").Reached, "Reached the end of the gallery.");
            Assert.IsFalse(cameraSix.IsAlarmed);
            Assert.AreEqual(0, framesCameraInsideGeometry, "The camera never entered geometry.");
            Assert.Greater(closestToScreenEdge, 0.1f, "The player stayed well inside the screen.");
        }

        //==================== EACH ELEMENT CANNOT BE BYPASSED =====================
        [UnityTest]
        public IEnumerator EveryPartition_FollowsTheRulesOfItsKind()
        {
            yield return LoadScene();

            Partition[] partitions = Object.FindObjectsByType<Partition>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(partitions.Length, 4);
            foreach (Partition partition in partitions)
            {
                CollectionAssert.IsEmpty(partition.FindProblems(), partition.name);
            }
        }

        [UnityTest]
        public IEnumerator Bay3_TheCrateBehindTheBarrier_CannotBeSwapped()
        {
            yield return LoadScene();
            yield return Teleport(BarrierTopX, StepThreeY);
            Swappable crate = Find<Swappable>("Swap Crate C (behind the barrier)");
            Vector3 start = crate.Position;

            yield return AimAndClick(crate);

            Assert.AreEqual(SwapRejection.LineOfSightBlocked, ability.LastResult);
            Assert.AreEqual(start, crate.Position);
        }

        [UnityTest]
        public IEnumerator Bay4_TheSealedDoor_StopsThePlayerUntilThePanelIsUsed()
        {
            yield return LoadScene();
            yield return Teleport(61f, FloorY);
            Press(keyboard.dKey);
            yield return WaitSeconds(1f);
            Release(keyboard.dKey);

            Assert.Less(player.transform.position.x, 63f, "The sealed door blocks the way.");
        }

        [UnityTest]
        public IEnumerator Bay6_WalkingIntoTheLight_SealsTheDoor_AndOnlyTheResetClearsIt()
        {
            yield return LoadScene();
            yield return Teleport(85f, LedgeY);
            SecurityCamera securityCamera = Find<SecurityCamera>("Security Camera 6");
            AccessDoor door = Find<AccessDoor>("Sealed Door 6");

            yield return WalkTo(CameraSixLitX);
            Assert.IsTrue(securityCamera.IsAlarmed);
            Assert.IsTrue(door.IsLockedDown);
            StringAssert.Contains("Press R", securityCamera.AlarmText);

            yield return WalkTo(84f);
            yield return WaitSeconds(2.5f);
            Assert.IsTrue(securityCamera.IsAlarmed, "Waiting out of view does not open the door again.");
            Assert.AreNotEqual(AccessDoor.DoorState.Open, door.State);

            yield return Tap(keyboard.rKey);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open);
            Assert.IsFalse(securityCamera.IsAlarmed, "The room reset cleared the alarm.");
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State);
        }

        [UnityTest]
        public IEnumerator Combination1_SwappingFromBesideThePad_LeavesTheDoorSealed_AndRResets()
        {
            yield return LoadScene();
            yield return Teleport(BesidePadEightX, FloorY);
            AccessDoor door = Find<AccessDoor>("Sealed Door 8");
            Swappable crate = Find<Swappable>("Swap Crate E");

            yield return AimAndClick(crate);
            yield return WaitSeconds(1f);

            Assert.Greater(player.transform.position.x, GlassEightX, "The player crossed the glass.");
            Assert.IsTrue(door.IsLocked, "Nothing holds the pad, so the door stays sealed.");
            Press(keyboard.dKey);
            yield return WaitSeconds(1f);
            Release(keyboard.dKey);
            Assert.Less(player.transform.position.x, DoorEightX, "Stuck between the glass and the door.");

            yield return Tap(keyboard.rKey);
            yield return WaitSeconds(0.3f);
            Assert.Less(player.transform.position.x, GlassEightX, "R puts the player back at the entry.");
            Assert.AreEqual(140.5f, crate.Position.x, 0.05f, "R puts the crate back.");
        }

        [UnityTest]
        public IEnumerator Combination2_ThePanelSeenThroughTheLid_CannotBeUsedFromAbove()
        {
            yield return LoadScene();
            yield return Teleport(158.5f, FloorY);
            ControlPanel panel = Find<ControlPanel>("Control Panel 9 (in the pit)");

            yield return Tap(keyboard.eKey);

            Assert.IsFalse(panel.PlayerInReach);
            Assert.IsFalse(panel.IsOn);
            Assert.IsTrue(Find<SecurityCamera>("Security Camera 9").IsActive);
        }

        [UnityTest]
        public IEnumerator Combination2_WalkingThroughTheWatchedCorridor_SealsTheExit()
        {
            yield return LoadScene();
            yield return Teleport(162f, FloorY);
            AccessDoor door = Find<AccessDoor>("Sealed Door 9");

            yield return WalkTo(178f);
            yield return WaitSeconds(1.5f);

            Assert.IsTrue(Find<SecurityCamera>("Security Camera 9").IsAlarmed);
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State);
            Press(keyboard.dKey);
            yield return WaitSeconds(1f);
            Release(keyboard.dKey);
            Assert.Less(player.transform.position.x, 180f, "The sealed exit stops the player.");
        }
    }
}

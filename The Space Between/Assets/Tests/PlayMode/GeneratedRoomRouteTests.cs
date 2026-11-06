using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Plays through the chain of generated rooms saved in Room_Generator_Sandbox with the real Player, using the keyboard as a
    /// player would. This is the proof that the rooms the Room Generator makes can really be walked, jumped and wall run: the
    /// generator's own route check only reads its plan. Regenerating the sandbox's rooms with other values can change these results.
    /// </summary>
    public class GeneratedRoomRouteTests : InputTestFixture
    {
        private const string SceneName = "Room_Generator_Sandbox";
        private const float StandingHeight = 1.08f;
        private const float StandingTolerance = 0.2f;
        private const float RouteDepthTolerance = 0.05f;
        private const float PanelApproach = 1f;
        private const float JumpBeforeBarrier = 2.2f;
        private const float WalkLimitSeconds = 30f;
        private const float ClearOfWall = 0.5f;

        private Keyboard keyboard;
        private GameObject player;

        private float X => player.transform.position.x;

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.isLoaded)
            {
                yield break;
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Generated Rooms"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        //==================== HELPERS =====================
        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            player = GameObject.Find("Player");
            Assert.NotNull(player, $"{SceneName} needs a Player.");
        }

        private static List<T> SortedAlongRoute<T>() where T : Component
        {
            var found = new List<T>(Object.FindObjectsByType<T>(FindObjectsSortMode.None));
            found.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            return found;
        }

        private bool StandingOn(float floorY)
        {
            return Mathf.Abs(player.transform.position.y - (floorY + StandingHeight)) < StandingTolerance;
        }

        private IEnumerator WaitUntil(System.Func<bool> condition, float limitSeconds)
        {
            float end = Time.time + limitSeconds;
            while (!condition() && Time.time < end)
            {
                yield return null;
                Assert.AreEqual(0f, player.transform.position.z, RouteDepthTolerance, "The player left the route's plane.");
            }
        }

        private IEnumerator Tap(ButtonControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        private IEnumerator HoldRun()
        {
            Press(keyboard.dKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            yield return null;
        }

        private IEnumerator ReleaseRun()
        {
            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);
            yield return null;
        }

        private IEnumerator UsePanel(ControlPanel panel, AccessDoor door)
        {
            yield return HoldRun();
            yield return WaitUntil(() => X >= panel.transform.position.x - PanelApproach, WalkLimitSeconds);
            yield return ReleaseRun();
            yield return WaitUntil(() => panel.PlayerInReach, 1f);
            Assert.IsTrue(panel.PlayerInReach, $"'{panel.transform.root.name}': the panel should be in reach. At {player.transform.position}");
            yield return Tap(keyboard.eKey);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open, 3f);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State, $"'{panel.transform.root.name}': E at the panel should open its door.");
        }

        //==================== TESTS =====================
        [UnityTest]
        public IEnumerator EveryGeneratedRoom_CanBeCrossed_FromTheFirstEntranceToTheLastRoom()
        {
            yield return LoadScene();
            List<GeneratedRoom> rooms = SortedAlongRoute<GeneratedRoom>();
            List<ControlPanel> panels = SortedAlongRoute<ControlPanel>();
            List<AccessDoor> doors = SortedAlongRoute<AccessDoor>();
            List<CurvedWall> curves = SortedAlongRoute<CurvedWall>();
            Assert.AreEqual(2, panels.Count, "The sandbox should have the chamber's and the cell's panels.");
            Assert.AreEqual(2, doors.Count);
            Assert.AreEqual(1, curves.Count);

            float start = Time.time;

            // Corridor and chamber, then the first panel.
            yield return UsePanel(panels[0], doors[0]);

            // Through the first door, across the cell and through its threshold, to the second panel.
            yield return UsePanel(panels[1], doors[1]);

            // Across the hall, jumping its barrier.
            Partition barrier = null;
            foreach (Partition partition in Object.FindObjectsByType<Partition>(FindObjectsSortMode.None))
            {
                if (partition.Kind == Partition.PartitionKind.IndustrialBarrier)
                {
                    barrier = partition;
                }
            }

            Assert.NotNull(barrier, "The hall should have a low barrier.");
            float barrierX = barrier.transform.position.x;
            yield return HoldRun();
            yield return WaitUntil(() => X >= barrierX - JumpBeforeBarrier, WalkLimitSeconds);
            Assert.GreaterOrEqual(X, barrierX - JumpBeforeBarrier, $"Stopped before the barrier. At {player.transform.position}");
            yield return Tap(keyboard.spaceKey);
            yield return WaitUntil(() => X > barrierX + 1f, 3f);
            Assert.Greater(X, barrierX + 1f, $"Should clear the barrier. At {player.transform.position}");

            // The last room's position is the exit of the climb: its floor is the climb's ledge height.
            GeneratedRoom last = rooms[rooms.Count - 1];
            float ledgeY = last.transform.position.y;
            float lastStart = last.transform.position.x;

            // Up the curved wall as a wall run, along the ledge and through the exit.
            yield return WaitUntil(() => X > lastStart + ClearOfWall && StandingOn(ledgeY), WalkLimitSeconds);
            Assert.Greater(X, lastStart, $"Should reach the last room. At {player.transform.position}");
            Assert.IsTrue(StandingOn(ledgeY), $"Should be on the raised floor. At {player.transform.position}");
            float seconds = Time.time - start;
            yield return ReleaseRun();
            Assert.Pass($"Crossed {rooms.Count - 1} rooms in {seconds:0.0} s, ending at {player.transform.position}.");
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ChangingAValue_WhilePlaying_RebuildsTheRoom_AndItsDoorAndPanelStillWork()
        {
            yield return LoadScene();
            GeneratedRoom room = null;
            foreach (GeneratedRoom candidate in SortedAlongRoute<GeneratedRoom>())
            {
                if (candidate.Settings.Purpose == RoomPurpose.ObservationChamber)
                {
                    room = candidate;
                }
            }

            Assert.NotNull(room, "The sandbox should have an observation chamber.");
            const float NewHeight = 20f;
            var values = new UnityEditor.SerializedObject(room);
            values.FindProperty("settings.height").floatValue = NewHeight;
            values.FindProperty("settings.interiorThresholds").intValue = 1;
            values.ApplyModifiedProperties();
            yield return WaitUntil(() => room.BuiltSignature == room.CurrentSignature, 2f);
            Assert.AreEqual(room.CurrentSignature, room.BuiltSignature, "The room should rebuild while the game is playing.");

            Transform ceiling = room.transform.Find("Generated_Structure/Ceiling");
            Assert.AreEqual(NewHeight, ceiling.GetComponent<Collider>().bounds.min.y, 0.01f);
            Assert.NotNull(room.transform.Find("Generated_Openings/Threshold 1 Header"), "The new threshold should exist.");

            // The rebuilt door and panel must work from where they now stand, and the new threshold must be passable.
            ControlPanel panel = SortedAlongRoute<ControlPanel>()[0];
            AccessDoor door = SortedAlongRoute<AccessDoor>()[0];
            float doorX = door.transform.position.x;
            yield return UsePanel(panel, door);
            yield return HoldRun();
            yield return WaitUntil(() => X > doorX + 1f, 3f);
            yield return ReleaseRun();
            Assert.Greater(X, doorX + 1f, $"The player should get through the rebuilt door. At {player.transform.position}");
        }
#endif

        [UnityTest]
        public IEnumerator GeneratedPanelDoor_StopsThePlayer_UntilThePanelIsUsed()
        {
            yield return LoadScene();
            AccessDoor door = SortedAlongRoute<AccessDoor>()[0];
            float doorX = door.transform.position.x;
            yield return HoldRun();
            yield return WaitUntil(() => X > doorX + ClearOfWall, 9f);
            Assert.Less(X, doorX, $"The closed door should stop the player. At {player.transform.position}");
            yield return ReleaseRun();
        }

        [UnityTest]
        public IEnumerator WatchOnlyCamera_SeesThePlayer_WithoutChangingTheRoute()
        {
            yield return LoadScene();
            SecurityCamera securityCamera = SortedAlongRoute<SecurityCamera>()[0];
            float cameraX = securityCamera.transform.position.x;
            yield return HoldRun();
            bool saw = false;
            yield return WaitUntil(() =>
            {
                saw |= securityCamera.IsSeeing;
                return X > cameraX + 4f;
            }, WalkLimitSeconds);
            yield return ReleaseRun();
            Assert.IsTrue(saw, "The camera should have seen the player walk under it.");
            Assert.Greater(X, cameraX + 4f, "The route should carry on past the camera.");
        }
    }
}

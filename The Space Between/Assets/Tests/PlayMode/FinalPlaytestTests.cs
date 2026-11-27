using System.Collections;
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
    /// Plays Final_Playtest, the level built from eight Room Generator rooms, with the real Player and the keyboard
    /// (A/D move, Shift run, W jump, S slide, Space swaps toward the mouse pointer, E uses panels and observation points).
    /// Each section is reached with its F key, so a failure points at one room. Positions are relative to each room's own
    /// Room Reset entry, so they follow the rooms if the level is regenerated with the same values.
    /// </summary>
    public class FinalPlaytestTests : InputTestFixture
    {
        private const string SceneName = "Final_Playtest";
        private const string MenuScene = "MainMenu";
        private const string FirstLevel = "L00_Opening";
        private static readonly string[] ProgressKeys =
        {
            "TheSpaceBetween.Progress.Scene", "TheSpaceBetween.Progress.Level", "TheSpaceBetween.Progress.Room", "TheSpaceBetween.Progress.Flags"
        };
        private const float StandingHeight = 1.08f;
        private const int AimFrames = 3;
        private const float SwapWaitLimitSeconds = 3f;

        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject player;
        private PlaceholderPlayerController controller;
        private SwapAbility ability;
        private PlayerAnimationDriver animationDriver;
        private PlaytestSession session;
        private Camera view;
        private RoomReset[] sections;
        private TutorialGuide guide;
        private readonly System.Collections.Generic.List<string> hintsSeen = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.Dictionary<string, string> savedProgress = new System.Collections.Generic.Dictionary<string, string>();

        public override void Setup()
        {
            base.Setup();

            // The tests play through rooms and the menu, which saves progress; the developer's own save is put back after.
            savedProgress.Clear();
            foreach (string key in ProgressKeys)
            {
                if (PlayerPrefs.HasKey(key))
                {
                    savedProgress[key] = key.EndsWith("Level") || key.EndsWith("Room") ? "i" + PlayerPrefs.GetInt(key) : "s" + PlayerPrefs.GetString(key);
                }
            }
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
        }

        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            Time.timeScale = 1f;
            foreach (string key in ProgressKeys)
            {
                PlayerPrefs.DeleteKey(key);
                if (savedProgress.TryGetValue(key, out string value))
                {
                    if (value[0] == 'i')
                    {
                        PlayerPrefs.SetInt(key, int.Parse(value.Substring(1)));
                    }
                    else
                    {
                        PlayerPrefs.SetString(key, value.Substring(1));
                    }
                }
            }

            // Whatever the test ended in (the level, the menu, the first story level), unload it.
            var loaded = new System.Collections.Generic.List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                loaded.Add(SceneManager.GetSceneAt(i));
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Final Playtest"));
            foreach (Scene scene in loaded)
            {
                if (scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }
        }

        //==================== HELPERS =====================
        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            GatherReferences();
            yield return WaitSeconds(0.5f);
        }

        private void GatherReferences()
        {
            player = GameObject.Find("Player");
            Assert.NotNull(player, $"{SceneName} needs a Player.");
            controller = player.GetComponent<PlaceholderPlayerController>();
            ability = player.GetComponent<SwapAbility>();
            animationDriver = player.GetComponent<PlayerAnimationDriver>();
            session = Object.FindAnyObjectByType<PlaytestSession>();
            view = Camera.main;
            guide = Object.FindAnyObjectByType<TutorialGuide>();
            Assert.NotNull(guide, "The level needs its Tutorial Guide.");
            hintsSeen.Clear();
            // The session's sections are the rooms' Room Resets, in route order.
            // The rooms in the order of the F keys, as the Playtest Session lists them.
            var field = typeof(PlaytestSession).GetField("sections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            sections = (RoomReset[])field.GetValue(session);
            Assert.AreEqual(9, sections.Length, "Every room should be a section.");
            foreach (RoomReset section in sections)
            {
                Assert.NotNull(section, "A section has lost its Room Reset.");
            }
        }

        private static T Find<T>(string name) where T : Component
        {
            GameObject found = GameObject.Find(name);
            Assert.NotNull(found, $"The scene needs '{name}'.");
            T component = found.GetComponent<T>();
            Assert.NotNull(component, $"'{name}' needs a {typeof(T).Name}.");
            return component;
        }

        private float X => player.transform.position.x;

        private float Y => player.transform.position.y;

        private Vector3 Entry(int section) => sections[section].transform.Find("Entry Point").position;

        // Every wait notes the hint on screen, so a test can check which hints appeared and in what order.
        private void NoteHint()
        {
            string current = guide != null ? guide.CurrentHint : null;
            if (current != null && (hintsSeen.Count == 0 || hintsSeen[hintsSeen.Count - 1] != current))
            {
                hintsSeen.Add(current);
            }
        }

        private string HintsSeen => string.Join(" > ", hintsSeen);

        private IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
                NoteHint();
            }
        }

        // Real time, for waits while the game is paused and game time stands still.
        private static IEnumerator WaitReal(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitRealUntil(System.Func<bool> condition, float limitSeconds)
        {
            float end = Time.realtimeSinceStartup + limitSeconds;
            while (!condition() && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        private IEnumerator WaitUntil(System.Func<bool> condition, float limitSeconds)
        {
            float end = Time.time + limitSeconds;
            while (!condition() && Time.time < end)
            {
                yield return null;
                NoteHint();
            }
        }

        // Presses whatever the swap is bound to on the keyboard or mouse, so the tests follow a change of binding.
        private ButtonControl SwapButton()
        {
            foreach (InputBinding binding in ability.SwapAction.bindings)
            {
                if (binding.effectivePath == "<Mouse>/leftButton")
                {
                    return mouse.leftButton;
                }

                if (binding.effectivePath.StartsWith("<Keyboard>/"))
                {
                    return (ButtonControl)keyboard[binding.effectivePath.Substring("<Keyboard>/".Length)];
                }
            }

            Assert.Fail("The swap has no keyboard or mouse binding.");
            return null;
        }

        private void AssertHintSeenAndCleared(string hint)
        {
            Assert.Contains(hint, hintsSeen, $"The '{hint}' hint should have shown. Seen: {HintsSeen}");
            Assert.AreNotEqual(hint, guide.CurrentHint, $"The '{hint}' hint should be gone once it is done. Seen: {HintsSeen}");
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

        private IEnumerator WalkTo(float x, bool run = false)
        {
            Press(keyboard.dKey);
            yield return null;
            if (run)
            {
                Press(keyboard.leftShiftKey);
                yield return null;
            }

            yield return WaitUntil(() => X >= x, 8f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.3f);
        }

        private IEnumerator AimAndSwap(Swappable target)
        {
            for (int i = 0; i < AimFrames; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(target.Center));
                yield return null;
            }

            yield return Tap(SwapButton());
            float start = Time.realtimeSinceStartup;
            while (ability.IsGesturing && Time.realtimeSinceStartup - start < SwapWaitLimitSeconds)
            {
                yield return null;
            }

            while (ability.IsSwapping && Time.realtimeSinceStartup - start < SwapWaitLimitSeconds)
            {
                yield return null;
            }
        }

        // F1 to F8 put the player at a room's entry.
        private IEnumerator GoToSection(int index)
        {
            ButtonControl[] keys =
            {
                keyboard.f1Key, keyboard.f2Key, keyboard.f3Key, keyboard.f4Key,
                keyboard.f5Key, keyboard.f6Key, keyboard.f7Key, keyboard.f8Key, keyboard.f9Key
            };
            yield return Tap(keys[index]);
            yield return WaitSeconds(0.5f);
            Assert.AreEqual(Entry(index).x, X, 0.4f, $"F{index + 1} should reach room {index + 1}.");
        }

        private bool StandingAbove(float floorY) => controller.IsGrounded && Y > floorY + StandingHeight - 0.2f;

        //==================== EACH ROOM =====================
        [UnityTest]
        public IEnumerator Room1_Arrival_JumpTheBarriers_AndThePanelOpensTheDoor()
        {
            yield return LoadScene();

            // The designer moves the Player's start while building later rooms, so the test starts from room 1's entry.
            yield return GoToSection(0);
            float x0 = Entry(0).x;
            AccessDoor door = sections[0].GetComponentInParent<GeneratedRoom>().GetComponentInChildren<AccessDoor>();
            ControlPanel panel = sections[0].GetComponentInParent<GeneratedRoom>().GetComponentInChildren<ControlPanel>();
            yield return HoldRun();
            foreach (float barrier in new[] { 12f, 20f })
            {
                yield return WaitUntil(() => X >= x0 - 2.5f + barrier - 2.2f, 4f);
                yield return Tap(keyboard.wKey);
            }

            yield return WaitUntil(() => X >= panel.transform.position.x - 0.5f, 5f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.5f);
            Assert.Less(X, door.transform.position.x, "The closed door should stop the player.");
            Assert.IsTrue(panel.PlayerInReach, $"The panel should be in reach. At {player.transform.position}");
            yield return Tap(keyboard.eKey);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open, 3f);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State, "E should open the door.");
            yield return WalkTo(door.transform.position.x + 2f, true);
            Assert.Greater(X, door.transform.position.x, "The player should pass the door.");
            AssertHintSeenAndCleared("Barrier");
            Assert.Pass($"through the door at x {X:F1}");
        }

        [UnityTest]
        public IEnumerator Room2_Observation_ObserveThenSwapThroughTheGlass()
        {
            yield return LoadScene();
            yield return GoToSection(1);
            ObservationPoint point = Find<ObservationPoint>("Observation Point");
            yield return WalkTo(point.transform.position.x + 0.5f);
            yield return Tap(keyboard.eKey);
            yield return WaitSeconds(1.2f);
            Assert.IsTrue(point.IsObserving, $"E should start observing. At {player.transform.position}");
            yield return Tap(keyboard.eKey);
            yield return WaitSeconds(1f);
            Assert.IsFalse(point.IsObserving, "E again should return.");

            Swappable housing = Find<Swappable>("Swap Housing (behind glass)");
            float glassX = sections[1].GetComponentInParent<GeneratedRoom>().transform.position.x + 24f;
            yield return WalkTo(glassX - 1.4f);
            Assert.Less(X, glassX, "The glass should stop the player.");
            yield return AimAndSwap(housing);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The housing should be in range. At {player.transform.position}");
            Assert.Greater(X, glassX, "The swap should cross the glass.");
            AssertHintSeenAndCleared("Glass");
            Assert.Pass($"observed, then swapped to x {X:F1}");
        }

        [UnityTest]
        public IEnumerator Room3_WallRun_UpTheCurve_OntoTheLedge()
        {
            yield return LoadScene();
            yield return GoToSection(2);
            float floor = Entry(2).y - StandingHeight;
            bool ran = false;
            yield return HoldRun();
            float end = Time.time + 8f;
            while (Time.time < end && !(StandingAbove(floor + 12f) && X > Entry(3).x - 6f))
            {
                ran |= controller.IsWallRunning;
                yield return null;
            }

            yield return ReleaseAll();
            Assert.IsTrue(ran, "The curve should start a wall run.");
            Assert.IsTrue(StandingAbove(floor + 12f), $"The wall run should end on the ledge 12 up. At {player.transform.position}");
            AssertHintSeenAndCleared("Wall run");
            Assert.Pass($"on the ledge at {player.transform.position}");
        }

        [UnityTest]
        public IEnumerator Room4_WallJumpShaft_WallJumpsOutOfThePit()
        {
            yield return LoadScene();
            yield return GoToSection(3);
            GeneratedRoom room = sections[3].GetComponentInParent<GeneratedRoom>();
            float roomX = room.transform.position.x;
            float floor = room.transform.position.y;
            float center = roomX + (25.8f + 33f) * 0.5f;
            float pitFloor = floor - 14f;
            yield return HoldRun();
            yield return WaitUntil(() => controller.IsWallRunning && X > center, 6f);
            Assert.IsTrue(controller.IsWallRunning && X > center, $"The player should run up the far wall. At {player.transform.position}");
            yield return WaitUntil(() => Y > pitFloor + 8f, 2f);
            yield return Tap(keyboard.wKey);
            yield return Turn(keyboard.dKey, keyboard.aKey);
            yield return WaitUntil(() => controller.IsWallRunning && X < center, 1.5f);
            Assert.IsTrue(controller.IsWallRunning && X < center, $"The wall jump should reach the near wall. At {player.transform.position}");
            yield return WaitUntil(() => Y > pitFloor + 12.2f, 1.5f);
            yield return Tap(keyboard.wKey);
            yield return Turn(keyboard.aKey, keyboard.dKey);
            yield return WaitUntil(() => StandingAbove(floor + 4f) && X > center + 4f, 4f);
            yield return ReleaseAll();
            Assert.IsTrue(StandingAbove(floor + 4f), $"The climb should end on the exit floor 4 up. At {player.transform.position}");
            AssertHintSeenAndCleared("Wall jump");
            Assert.Pass($"out of the shaft at {player.transform.position}");
        }

        [UnityTest]
        public IEnumerator Room5_WatchedCorridor_SwapPastTheCamera_AndThePanelOpensTheDoor()
        {
            yield return LoadScene();
            yield return GoToSection(4);
            GeneratedRoom room = sections[4].GetComponentInParent<GeneratedRoom>();
            SecurityCamera securityCamera = room.GetComponentInChildren<SecurityCamera>();
            AccessDoor door = room.GetComponentInChildren<AccessDoor>();
            ControlPanel panel = room.GetComponentInChildren<ControlPanel>();
            Swappable crate = Find<Swappable>("Swap Crate (past the camera)");
            float cameraX = securityCamera.transform.position.x;

            yield return WalkTo(crate.Position.x - 7.5f);
            Assert.IsFalse(securityCamera.IsAlarmed, $"The camera should not see the player yet. At {player.transform.position}");
            yield return AimAndSwap(crate);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The crate should be in range. At {player.transform.position}");
            Assert.Greater(X, cameraX, "The swap should cross the camera's light.");
            yield return WaitSeconds(0.4f);
            Assert.IsFalse(securityCamera.IsAlarmed, $"The swap should pass unseen. At {player.transform.position}");

            yield return WalkTo(panel.transform.position.x - 0.5f);
            yield return Tap(keyboard.eKey);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open, 3f);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State, "E should open the door when unseen.");
            yield return WalkTo(door.transform.position.x + 2f);
            Assert.Greater(X, door.transform.position.x, "The player should pass the door.");
            AssertHintSeenAndCleared("Camera");
            Assert.Pass($"through at x {X:F1}");
        }

        [UnityTest]
        public IEnumerator Room5_WalkingIntoTheLight_SealsTheExit()
        {
            yield return LoadScene();
            yield return GoToSection(4);
            GeneratedRoom room = sections[4].GetComponentInParent<GeneratedRoom>();
            SecurityCamera securityCamera = room.GetComponentInChildren<SecurityCamera>();
            Press(keyboard.dKey);
            yield return WaitUntil(() => securityCamera.IsAlarmed, 4f);
            Release(keyboard.dKey);
            Assert.IsTrue(securityCamera.IsAlarmed, $"The camera should see a player walking under it. At {player.transform.position}");
            yield return WaitSeconds(0.3f);
            Assert.AreEqual("Seen", guide.CurrentHint, $"Being seen shows what to do. Seen: {HintsSeen}");
            Assert.Pass($"seen at x {X:F1}, camera at {securityCamera.transform.position.x:F1}; hints {HintsSeen}");
        }

        [UnityTest]
        public IEnumerator Room6_PressurePuzzle_TheCrateHoldsTheDoorOpen()
        {
            yield return LoadScene();
            yield return GoToSection(5);
            GeneratedRoom room = sections[5].GetComponentInParent<GeneratedRoom>();
            AccessDoor door = room.GetComponentInChildren<AccessDoor>();
            ControlPanel pad = Find<ControlPanel>("Pressure Pad (exit door)");
            Swappable crate = Find<Swappable>("Swap Crate (behind glass)");
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State, "The exit starts sealed.");
            yield return WalkTo(pad.transform.position.x - 0.1f);
            yield return WaitSeconds(0.6f);
            Assert.IsTrue(pad.IsOccupied, $"The player should stand on the pad. At {player.transform.position}");
            yield return AimAndSwap(crate);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The crate should be in range from the pad. At {player.transform.position}");
            yield return WaitSeconds(1f);
            Assert.IsTrue(pad.IsOccupied, "The crate should now weigh the pad down.");
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open, 3f);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State, "The crate on the pad should hold the door open.");
            yield return WalkTo(door.transform.position.x + 2f);
            Assert.Greater(X, door.transform.position.x, "The player should pass the door.");
            AssertHintSeenAndCleared("Pressure pad");
            Assert.Pass($"through at x {X:F1}");
        }

        [UnityTest]
        public IEnumerator Room7_ChaseRun_CrumblingBridgesAndTheSlide()
        {
            yield return LoadScene();
            yield return GoToSection(6);
            GeneratedRoom room = sections[6].GetComponentInParent<GeneratedRoom>();
            float roomX = room.transform.position.x;
            float floor = room.transform.position.y;
            float exitX = roomX + 48f;
            var report = new StringBuilder();
            bool slid = false;
            bool through = false;
            for (int attempt = 1; attempt <= 3 && !through; attempt++)
            {
                yield return HoldRun();
                yield return WaitUntil(() => X >= roomX + 20.5f, 5f);
                yield return Tap(keyboard.sKey);
                float end = Time.time + 6f;
                while (Time.time < end && X < exitX + 1f && X > roomX + 1f && Y > floor - 1f)
                {
                    slid |= controller.IsSliding;
                    yield return null;
                }

                yield return ReleaseAll();
                through = X >= exitX + 1f;
                report.Append($"attempt {attempt}: reached x {X:F1}; ");
                if (!through)
                {
                    yield return WaitSeconds(1f);
                    yield return GoToSection(6);
                }
            }

            Assert.IsTrue(slid, "The hood should need a slide. " + report);
            Assert.IsTrue(through, "The chase room should be crossable. " + report);
            Assert.Less(hintsSeen.IndexOf("Failing floor"), hintsSeen.LastIndexOf("Slide"), $"The floor hint comes first, then the slide near the hood. Seen: {HintsSeen}");
            AssertHintSeenAndCleared("Slide");
            AssertHintSeenAndCleared("Failing floor");
            Assert.Pass(report.ToString());
        }

        private static Transform Marker(GeneratedRoom room, string name)
        {
            Transform marker = room.transform.Find($"Tutorial Marker ({name})");
            Assert.NotNull(marker, $"{room.name} needs the marker '{name}'.");
            return marker;
        }

        [UnityTest]
        public IEnumerator Room8_Elevator_SwapOverTheGap_JumpTheBarrier_RideTheShuttle_AndTheElevatorUp()
        {
            yield return LoadScene();
            yield return GoToSection(7);
            GeneratedRoom room = sections[7].GetComponentInParent<GeneratedRoom>();
            float floor = room.transform.position.y;
            Swappable beacon = Find<Swappable>("Swap Beacon (over the gap)");
            MovingPlatform shuttle = Find<MovingPlatform>("Moving Platform (over the gap)");
            MovingPlatform lift = Find<MovingPlatform>("Elevator");
            Transform barrier = null;
            foreach (Transform t in room.GetComponentsInChildren<Transform>())
            {
                if (t.name == "Low Barrier")
                {
                    barrier = t;
                }
            }

            Assert.NotNull(barrier);
            var report = new StringBuilder();

            // Gap 1, with glass over it: jump, swap to the beacon in the air, land beyond.
            float gapStart = Marker(room, "first gap").position.x;
            float gapEnd = Marker(room, "past the first gap").position.x - 0.5f;
            yield return HoldRun();
            yield return WaitUntil(() => X >= gapStart - 1.2f, 5f);
            yield return Tap(keyboard.wKey);
            yield return WaitSeconds(0.15f);
            yield return AimAndSwap(beacon);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The beacon should be in range mid-jump. At {player.transform.position}");
            yield return WaitUntil(() => controller.IsGrounded, 2f);
            Assert.IsTrue(StandingAbove(floor) && X > gapEnd, $"The swap should carry the player over the gap. At {player.transform.position}");
            report.Append($"over gap 1 to x {X:F1}; ");

            // The low barrier, then gap 2: wait for the shuttle at the near side, ride it, step off.
            yield return HoldRun();
            yield return WaitUntil(() => X >= barrier.position.x - 2.2f, 4f);
            yield return Tap(keyboard.wKey);
            float gap2Start = shuttle.transform.position.x - 1.5f;
            yield return WaitUntil(() => X >= barrier.position.x + 1f, 3f);
            yield return ReleaseAll();
            yield return WalkTo(gap2Start - 1f);
            Assert.IsTrue(StandingAbove(floor), $"Over the barrier and before the second gap. At {player.transform.position}");
            yield return WaitUntil(() => shuttle.Progress <= 0.01f, 8f);
            yield return WalkTo(gap2Start + 1.2f);
            Assert.IsTrue(controller.IsGrounded && Y > floor, $"The player should stand on the shuttle. At {player.transform.position}");
            yield return WaitUntil(() => shuttle.Progress >= 0.99f, 8f);
            yield return WalkTo(lift.transform.position.x - 0.2f);
            Assert.IsTrue(X >= lift.transform.position.x - 1.5f && Y > floor + 0.9f, $"The shuttle should carry the player across to the elevator, not into the gap. At {player.transform.position}");
            report.Append($"shuttle to x {X:F1}; ");

            // The elevator: stand on its pad, ride up, step onto the ledge.
            yield return WaitUntil(() => lift.Progress >= 1f, 8f);
            Assert.GreaterOrEqual(lift.Progress, 1f, $"The pad should raise the elevator. At {player.transform.position}");
            yield return WalkTo(lift.transform.position.x + 3f);
            float ledgeY = Marker(room, "on the goal ledge").position.y;
            Assert.IsTrue(controller.IsGrounded && Y >= ledgeY, $"The elevator should lift the player onto the ledge. At {player.transform.position}");
            yield return WaitSeconds(0.6f);
            AssertHintSeenAndCleared("Mid-air swap");
            AssertHintSeenAndCleared("Elevator");
            report.Append($"on the ledge at {player.transform.position}");
            Assert.Pass(report.ToString());
        }

        [UnityTest]
        public IEnumerator Room9_DoubleJump_TheHintShows_AndJumpAfterASwapCrossesTheFirstGap()
        {
            yield return LoadScene();
            yield return GoToSection(8);
            GeneratedRoom room = sections[8].GetComponentInParent<GeneratedRoom>();
            float floor = room.transform.position.y;
            float gapStart = Marker(room, "first gap").position.x;
            float gapEnd = Marker(room, "past the first gap").position.x - 0.5f;
            Swappable beacon = null;
            foreach (Swappable candidate in room.GetComponentsInChildren<Swappable>())
            {
                if (candidate.Position.x > gapStart && candidate.Position.x < gapEnd)
                {
                    beacon = candidate;
                }
            }

            Assert.NotNull(beacon, "Room 9 needs a beacon over its first gap.");
            yield return WaitUntil(() => guide.CurrentHint == "Double Jump", 4f);
            Assert.AreEqual("Double Jump", guide.CurrentHint, $"The hint shows on the way to the first gap. At {player.transform.position}");
            yield return WaitSeconds(0.4f);
            StringAssert.Contains("Press W again after a swap", guide.CurrentHintText);

            // Jump at the edge, swap to the beacon in the air, then jump again from there.
            yield return HoldRun();
            yield return WaitUntil(() => X >= gapStart - 1.2f, 5f);
            yield return Tap(keyboard.wKey);
            yield return WaitSeconds(0.15f);
            yield return AimAndSwap(beacon);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The beacon should be in range mid-jump. At {player.transform.position}");
            yield return Tap(keyboard.wKey);
            yield return WaitUntil(() => controller.IsGrounded, 3f);
            yield return ReleaseAll();
            Assert.IsTrue(StandingAbove(floor) && X > gapEnd, $"The swap and the second jump should cross the gap. At {player.transform.position}");
            yield return WaitSeconds(0.6f);
            AssertHintSeenAndCleared("Double Jump");
            Assert.Pass($"over the first gap to {player.transform.position}; hints {HintsSeen}");
        }

        [UnityTest]
        public IEnumerator Goal_PlaysTheEnding_StillView_TitleThenEnd_ThenCreditsAndTheMenu()
        {
            yield return LoadScene();
            FacilityUI ui = FacilityUI.Current;
            Assert.NotNull(ui, "The level needs its Facility UI.");
            LevelGoal goal = Object.FindAnyObjectByType<LevelGoal>();
            CharacterController body = player.GetComponent<CharacterController>();
            body.enabled = false;
            player.transform.position = goal.transform.position;
            body.enabled = true;
            Physics.SyncTransforms();
            yield return WaitUntil(() => goal.Reached, 2f);
            yield return WaitSeconds(0.5f);
            Assert.IsTrue(goal.Reached, "The goal should register the player.");
            Assert.IsTrue(ui.IsEnding, "Reaching the goal starts the ending.");
            Assert.IsTrue(controller.ControlLocked && GameplayHold.Active, "The player is still during the ending.");
            Assert.IsFalse(guide.EndVisible, "The tutorial's own closing line gives way to the ending.");
            Assert.IsNull(ui.MessageText, "The view is held still before the title.");

            yield return WaitUntil(() => ui.MessageText == "THE SPACE BETWEEN", 6f);
            Assert.AreEqual("THE SPACE BETWEEN", ui.MessageText, "The title appears after the pause.");
            Assert.IsNull(ui.MessageSubtitle, "END comes after the title.");
            var brain = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineBrain>();
            Assert.AreEqual("CM Ending View", brain.ActiveVirtualCamera?.Name, "The still, wide ending view is live.");

            yield return WaitUntil(() => ui.MessageSubtitle == "END", 6f);
            Assert.AreEqual("END", ui.MessageSubtitle);
            Assert.IsNull(ui.TopScreen, "The credits wait until END has been read.");

            yield return WaitUntil(() => ui.TopScreen == "Credits", 10f);
            Assert.AreEqual("Credits", ui.TopScreen, "The credits follow the ending.");
            ui.Back();
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == MenuScene, 10f);
            yield return WaitUntil(() => FacilityUI.Current != null && FacilityUI.Current.TopScreen == "Main Menu" && !FacilityUI.Current.LoadingVisible, 5f);
            Assert.AreEqual(MenuScene, SceneManager.GetActiveScene().name, "Skipping the credits returns to the main menu.");
            Assert.IsFalse(GameplayHold.Active);
        }

        //==================== FACILITY UI =====================
        [UnityTest]
        public IEnumerator Pause_StopsTheGame_DimsTheView_AndResumesWhereItWas()
        {
            yield return LoadScene();
            yield return GoToSection(1);
            FacilityUI ui = FacilityUI.Current;
            yield return Tap(keyboard.escapeKey);
            Assert.IsTrue(ui.IsPaused, "Escape pauses.");
            Assert.AreEqual(0f, Time.timeScale, "Game time stops.");
            Assert.AreEqual("Pause", ui.TopScreen);
            CollectionAssert.AreEqual(new[] { "Resume", "Restart Checkpoint", "Settings", "Main Menu" }, ui.Options);
            Assert.AreEqual("Resume", ui.SelectedOption);

            Vector3 at = player.transform.position;
            Press(keyboard.dKey);
            yield return WaitReal(0.6f);
            Release(keyboard.dKey);
            yield return null;
            Assert.Less(Vector3.Distance(at, player.transform.position), 0.01f, "Nothing moves while paused.");
            Assert.Greater(ui.LookWeight, 0.9f, "The paused view is darkened and drained.");

            yield return Tap(keyboard.downArrowKey);
            Assert.AreEqual("Restart Checkpoint", ui.SelectedOption, "Down moves the selection.");
            yield return Tap(keyboard.upArrowKey);
            Assert.AreEqual("Resume", ui.SelectedOption);
            yield return Tap(keyboard.enterKey);
            Assert.IsFalse(ui.IsPaused, "Resume closes the menu.");
            Assert.AreEqual(1f, Time.timeScale, "Game time runs again.");
            Assert.IsNull(ui.TopScreen);
            Assert.IsFalse(GameplayHold.Active);
            yield return WaitReal(0.6f);
            Assert.Less(ui.LookWeight, 0.05f, "The view comes back.");
            yield return WalkTo(at.x + 2f);
            Assert.Greater(X, at.x + 1.5f, "The player moves again after Resume.");
        }

        [UnityTest]
        public IEnumerator Pause_Settings_ShowTheRealBindings_ApplyAtOnce_AndEscapeGoesBack()
        {
            yield return LoadScene();
            FacilityUI ui = FacilityUI.Current;
            yield return Tap(keyboard.escapeKey);
            Assert.IsTrue(ui.Choose("Settings"));
            yield return null;
            Assert.AreEqual("Settings", ui.TopScreen);

            var keys = TutorialGuide.InputDevice.KeyboardMouse;
            var pad = TutorialGuide.InputDevice.Gamepad;
            Assert.AreEqual(guide.KeyLabel("Move", keys).ToUpperInvariant(), ui.ControlText("Movement", false));
            Assert.AreEqual(guide.KeyLabel("Jump", keys).ToUpperInvariant(), ui.ControlText("Jump", false));
            Assert.AreEqual(guide.KeyLabel("Swap", keys).ToUpperInvariant(), ui.ControlText("Swap", false));
            Assert.AreEqual(guide.KeyLabel("Slide", keys).ToUpperInvariant(), ui.ControlText("Slide", false));
            Assert.AreEqual(guide.KeyLabel("Observe", keys).ToUpperInvariant(), ui.ControlText("Observe", false));
            Assert.AreEqual(guide.KeyLabel("Jump", pad).ToUpperInvariant(), ui.ControlText("Jump", true));
            StringAssert.StartsWith(guide.KeyLabel("Run", keys).ToUpperInvariant(), ui.ControlText("Wall Run", false));

            bool vSync = GameSettings.VSync;
            ui.StepSetting("VSync", 1);
            Assert.AreEqual(vSync ? 0 : 1, QualitySettings.vSyncCount, "VSync changes at once.");
            ui.StepSetting("VSync", 1);
            Assert.AreEqual(vSync ? 1 : 0, QualitySettings.vSyncCount);

            float master = GameSettings.Master;
            ui.StepSetting("Master Volume", -1);
            Assert.AreEqual(Mathf.Round((master - 0.05f) / 0.05f) * 0.05f, AudioListener.volume, 0.001f, "Master volume is the AudioListener volume.");
            ui.StepSetting("Master Volume", 1);
            Assert.AreEqual(master, AudioListener.volume, 0.001f);

            yield return Tap(keyboard.escapeKey);
            Assert.AreEqual("Pause", ui.TopScreen, "Escape goes back to the pause menu.");
            yield return Tap(keyboard.escapeKey);
            Assert.IsFalse(ui.IsPaused, "Escape again resumes.");
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator Pause_RestartCheckpoint_ReturnsToTheRoomEntry_WithoutTheFailureLine()
        {
            yield return LoadScene();
            yield return GoToSection(1);
            yield return WalkTo(Entry(1).x + 5f);
            Assert.Greater(X, Entry(1).x + 4f);
            FacilityUI ui = FacilityUI.Current;
            yield return Tap(keyboard.escapeKey);
            Assert.IsTrue(ui.Choose("Restart Checkpoint"));
            Assert.IsFalse(ui.IsPaused);
            yield return WaitRealUntil(() => !ui.IsReturning, 2f);
            yield return WaitSeconds(0.3f);
            Assert.AreEqual(Entry(1).x, X, 0.4f, "The player is back at the room's entry.");
            Assert.IsNull(ui.MessageText, "A restart is not a failure, so POSITION LOST does not show.");
            Assert.IsFalse(GameplayHold.Active);
            Assert.IsFalse(controller.ControlLocked);
        }

        [UnityTest]
        public IEnumerator Hazard_PositionLost_GoesDark_ShowsTheLine_ThenReturnsToTheRoom()
        {
            yield return LoadScene();
            FacilityUI ui = FacilityUI.Current;
            TraversalHazard hazard = null;
            foreach (TraversalHazard candidate in Object.FindObjectsByType<TraversalHazard>(FindObjectsSortMode.InstanceID))
            {
                if (hazard == null && candidate.Room != null && candidate.GetComponent<Collider>() != null && candidate.GetComponent<FallingObstacle>() == null)
                {
                    hazard = candidate;
                }
            }

            Assert.NotNull(hazard, "The level needs a hazard with a room.");
            Vector3 hazardAt = hazard.GetComponent<Collider>().bounds.center;
            CharacterController body = player.GetComponent<CharacterController>();
            body.enabled = false;
            player.transform.position = hazardAt;
            body.enabled = true;
            Physics.SyncTransforms();

            yield return WaitUntil(() => ui.IsReturning, 1f);
            Assert.IsTrue(ui.IsReturning, $"Touching '{hazard.name}' starts the return.");
            Assert.IsTrue(GameplayHold.Active && controller.ControlLocked, "Control is held during the return.");
            yield return WaitUntil(() => ui.MessageText == "POSITION LOST", 2f);
            Assert.AreEqual("POSITION LOST", ui.MessageText);
            yield return WaitUntil(() => !ui.IsReturning, 3f);
            Assert.IsFalse(ui.IsReturning, "The return ends on its own.");
            Assert.IsFalse(GameplayHold.Active);
            Assert.IsFalse(controller.ControlLocked, "Control comes back.");
            Assert.IsTrue(hazard.Room.Area.Contains(player.transform.position + Vector3.up), $"The player is back in '{hazard.Room.name}'. At {player.transform.position}");
            Assert.Greater(Vector3.Distance(player.transform.position, hazardAt), 2f, "The player is no longer on the hazard.");
            Assert.AreEqual(1, hazard.Hits, "One touch, one return.");
            yield return WaitSeconds(0.6f);
            Assert.IsNull(ui.MessageText, "Nothing stays on screen.");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator MainMenu_ContinueNeedsASave_NewGameStartsAtRoom1_AndTheMenuOffersContinueAfter()
        {
            GameProgress.Clear();
            yield return SceneManager.LoadSceneAsync(MenuScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            FacilityUI ui = FacilityUI.Current;
            Assert.NotNull(ui, "The menu needs its Facility UI.");
            Assert.AreEqual("Main Menu", ui.TopScreen);
            CollectionAssert.AreEqual(new[] { "Continue", "New Game", "Settings", "Credits", "Quit" }, ui.Options);
            Assert.IsFalse(ui.IsOptionEnabled("Continue"), "Continue is off without saved progress.");
            Assert.AreEqual("New Game", ui.SelectedOption, "The first option that can be chosen is selected.");

            Assert.IsTrue(ui.Choose("Credits"));
            Assert.AreEqual("Credits", ui.TopScreen);
            ui.Back();
            Assert.AreEqual("Main Menu", ui.TopScreen, "Credits close back to the menu.");

            Assert.IsTrue(ui.Choose("New Game"));
            yield return null;
            Assert.IsTrue(ui.LoadingVisible, "New Game shows the loading screen.");
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == FirstLevel, 10f);
            yield return WaitUntil(() => FacilityUI.Current != null && !FacilityUI.Current.LoadingVisible, 5f);
            Assert.AreEqual(FirstLevel, SceneManager.GetActiveScene().name, "New Game starts the first level.");
            var levelPlayer = Object.FindAnyObjectByType<PlaceholderPlayerController>();
            Assert.Less(levelPlayer.transform.position.x, 8f, "New Game starts in the first room of the first level.");
            yield return WaitSeconds(0.3f);
            Assert.AreEqual(FirstLevel, GameProgress.Scene, "Progress is saved from the first level.");
            Assert.AreEqual(0, GameProgress.Room, "Progress is saved from its first room.");

            // The opening cinematic cannot be paused; the pause menu is there once it hands control over.
            var opening = Object.FindAnyObjectByType<OpeningSequence>();
            yield return WaitUntil(() => opening.Finished, 60f);
            ui = FacilityUI.Current;
            yield return Tap(keyboard.escapeKey);
            Assert.IsTrue(ui.Choose("Main Menu"));
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == MenuScene, 10f);
            yield return WaitUntil(() => FacilityUI.Current != null && FacilityUI.Current.TopScreen == "Main Menu", 5f);
            Assert.AreEqual(1f, Time.timeScale, "Leaving from the pause menu starts game time again.");
            Assert.IsTrue(FacilityUI.Current.IsOptionEnabled("Continue"), "Continue is on once there is progress.");
        }

        //==================== TUTORIAL GUIDANCE =====================
        private static string Readable(string path)
        {
            switch (path)
            {
                case "<Mouse>/leftButton":
                    return "Left click";
                case "<Keyboard>/space":
                    return "Space";
                case "<Keyboard>/#(W)":
                case "<Keyboard>/w":
                    return "W";
                default:
                    return null;
            }
        }

        [UnityTest]
        public IEnumerator Tutorial_ControlsShowTheRealBindings_ForKeyboardAndGamepad()
        {
            yield return LoadScene();
            Assert.AreEqual(2, guide.SignCount, "A controls sign at the start and a swap sign before the glass.");
            string kb(string token) => guide.KeyLabel(token, TutorialGuide.InputDevice.KeyboardMouse);
            string pad(string token) => guide.KeyLabel(token, TutorialGuide.InputDevice.Gamepad);

            Assert.AreEqual("A / D", kb("Move"));
            Assert.AreEqual("W", kb("Jump"));
            Assert.AreEqual("Left Shift", kb("Run"));
            Assert.AreEqual("S", kb("Slide"));
            Assert.AreEqual("E", kb("Use"));
            Assert.AreEqual("R", kb("Restart"));
            Assert.AreEqual("Mouse", kb("Aim"));
            string swapPath = null;
            foreach (InputBinding binding in ability.SwapAction.bindings)
            {
                if (binding.effectivePath.StartsWith("<Keyboard>") || binding.effectivePath.StartsWith("<Mouse>"))
                {
                    swapPath = binding.effectivePath;
                    break;
                }
            }

            Assert.AreEqual(Readable(swapPath), kb("Swap"), $"The swap label follows its binding, {swapPath}.");

            Assert.AreEqual("Left stick", pad("Move"));
            Assert.AreEqual("A / Cross", pad("Jump"));
            Assert.AreEqual("L3 (left stick press)", pad("Run"));
            Assert.AreEqual("not set", pad("Slide"), "Slide has no gamepad binding yet, and the overview says so.");
            Assert.AreEqual("X / Square", pad("Swap"));
            Assert.AreEqual("Right stick", pad("Aim"));
            Assert.AreEqual("X / Square", pad("Use"));
            Assert.AreEqual("View / Select", pad("Restart"));

            // The signs in the level show the keys of the keyboard and mouse until a gamepad is used.
            string start = guide.SignText(0);
            string swapSign = guide.SignText(1);
            StringAssert.Contains("A / D  move", start);
            StringAssert.Contains("Left Shift  run", start);
            StringAssert.Contains("W  jump", start);
            StringAssert.Contains("E  use", start);
            StringAssert.Contains("R  restart the room", start);
            StringAssert.Contains("Mouse  aim", swapSign);
            StringAssert.Contains(kb("Swap") + "  swap places", swapSign);

            // Using a gamepad turns the signs to its buttons.
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            Press(gamepad.buttonNorth);
            yield return null;
            Release(gamepad.buttonNorth);
            yield return null;
            yield return null;
            Assert.AreEqual(TutorialGuide.InputDevice.Gamepad, guide.ActiveDevice);
            StringAssert.Contains("Left stick  move", guide.SignText(0));
            StringAssert.Contains("A / Cross  jump", guide.SignText(0));
            Press(keyboard.dKey);
            yield return null;
            Release(keyboard.dKey);
            yield return null;
            yield return null;
            Assert.AreEqual(TutorialGuide.InputDevice.KeyboardMouse, guide.ActiveDevice, "Back to the keyboard.");
            StringAssert.Contains("W  jump", guide.SignText(0));
            Assert.Pass(start.Replace((char)10, ' ') + " | " + swapSign);
        }

        [UnityTest]
        public IEnumerator Tutorial_OpensWithTheObjective_ThenTheFirstHint_AndTheControlsLeaveAfterTheFirstRoom()
        {
            yield return LoadScene();
            yield return GoToSection(0);
            Assert.AreEqual("Intro", guide.CurrentHint, "The objective comes first in the first room.");
            yield return WaitSeconds(0.5f);
            StringAssert.Contains("Reach the end of the facility", guide.CurrentHintText);
            yield return WaitSeconds(7f);
            Assert.IsNull(guide.CurrentHint, "The opening line leaves, and nothing else shows before the barrier.");

            yield return HoldRun();
            yield return WaitUntil(() => guide.CurrentHint == "Barrier", 3f);
            Assert.AreEqual("Barrier", guide.CurrentHint, $"Approaching the barrier shows its hint. At {player.transform.position}");
            yield return WaitSeconds(0.4f);
            StringAssert.Contains("W jumps over it", guide.CurrentHintText);
            yield return ReleaseAll();

            yield return GoToSection(1);
            yield return WaitSeconds(0.5f);
            Assert.IsNull(guide.CurrentHint, "Past the first room the opening line does not come back.");
            Assert.Pass(HintsSeen);
        }

        [UnityTest]
        public IEnumerator Tutorial_RestartingARoom_BringsItsHintBack_AndLeavesNothingStale()
        {
            yield return LoadScene();
            yield return GoToSection(1);
            Swappable housing = Find<Swappable>("Swap Housing (behind glass)");
            float glassX = sections[1].GetComponentInParent<GeneratedRoom>().transform.position.x + 24f;
            yield return WalkTo(glassX - 1.4f);
            yield return WaitUntil(() => guide.CurrentHint == "Glass", 6f);
            Assert.AreEqual("Glass", guide.CurrentHint, "Near the glass, its hint shows.");
            yield return AimAndSwap(housing);
            Assert.Greater(X, glassX, "Swapped through.");
            yield return WaitSeconds(0.5f);
            Assert.AreNotEqual("Glass", guide.CurrentHint, "Done once through.");

            yield return Tap(keyboard.rKey);
            yield return WaitSeconds(0.5f);
            Assert.AreNotEqual("Glass", guide.CurrentHint, "Back at the entry, far from the glass, nothing stale stays.");
            yield return WalkTo(glassX - 1.4f);
            yield return WaitUntil(() => guide.CurrentHint == "Glass", 3f);
            Assert.AreEqual("Glass", guide.CurrentHint, "After the restart the room teaches again.");
            Assert.Pass(HintsSeen);
        }

        [UnityTest]
        public IEnumerator Tutorial_PanelsStayOnScreen_AndOffThePlayer()
        {
            yield return LoadScene();
            yield return WaitSeconds(0.6f);
            var report = new StringBuilder($"screen {Screen.width}x{Screen.height}; ");
            Vector3 playerOnScreen = view.WorldToScreenPoint(player.transform.position);
            Assert.IsNull(guide.transform.Find("Tutorial UI/Controls"), "No controls box: the controls are signs in the level.");
            foreach (string panelName in new[] { "Hint" })
            {
                var panel = (RectTransform)guide.transform.Find("Tutorial UI/" + panelName);
                Assert.NotNull(panel, panelName);
                var corners = new Vector3[4];
                panel.GetWorldCorners(corners);
                var rect = new Rect(corners[0], corners[2] - corners[0]);
                report.Append($"{panelName} {rect}; ");
                Assert.GreaterOrEqual(rect.xMin, -0.5f, panelName + " stays on screen. " + report);
                Assert.LessOrEqual(rect.xMax, Screen.width + 0.5f, panelName + " stays on screen. " + report);
                Assert.LessOrEqual(rect.yMax, Screen.height + 0.5f, panelName + " stays on screen. " + report);
                Assert.IsFalse(rect.Contains(playerOnScreen), $"{panelName} must not cover the player at {playerOnScreen}. " + report);
                Assert.Less(rect.height, Screen.height * 0.6f, panelName + " stays compact. " + report);
            }

            Assert.Pass(report.ToString());
        }
    }
}

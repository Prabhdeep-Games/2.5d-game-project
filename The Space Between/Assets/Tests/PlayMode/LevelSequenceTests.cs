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
    /// The eight story levels: each loads on its own with the interface and a checkpoint per room, its exit leads to the
    /// next with the agreed loading line, each level's own beat works (the swap through glass, the door that opens by
    /// itself, the lit cell remembered for the exterior, the core going down), the slow-motion moments only play where
    /// they are authored and always give time back, and the drone strike at the very end leads into the ending.
    /// </summary>
    public class LevelSequenceTests : InputTestFixture
    {
        private const string Level00 = "L00_Opening";
        private const string Level01 = "L01_TestingChamber";
        private const string Level02 = "L02_ResearchFacility";
        private const string Level03 = "L03_ObservationSection";
        private const string Level04 = "L04_ContainmentWing";
        private const string Level05 = "L05_ContainmentBreach";
        private const string Level06 = "L06_CentralCore";
        private const string Level07 = "L07_Ascent";
        private const string Level08 = "L08_Exterior";

        // Each level, the one after it, its loading line, and whether the change is the pale (daylight) one.
        private static readonly (string scene, string next, string message, bool bright)[] Chain =
        {
            (Level01, Level02, "ACCESSING SECTOR", false),
            (Level02, Level03, "ACCESSING SECTOR", false),
            (Level03, Level04, "WHAT IS GOING ON?", false),
            (Level04, Level05, "", false),
            (Level05, Level06, "WHO WAS I?", false),
            (Level06, Level07, "RESTORING SYSTEMS", false),
            (Level07, Level08, "", true),
        };
        private static readonly string[] ProgressKeys =
        {
            "TheSpaceBetween.Progress.Scene", "TheSpaceBetween.Progress.Level", "TheSpaceBetween.Progress.Room", "TheSpaceBetween.Progress.Flags"
        };

        private readonly System.Collections.Generic.Dictionary<string, string> savedProgress = new System.Collections.Generic.Dictionary<string, string>();
        private Keyboard keyboard;
        private Mouse mouse;
        private PlaceholderPlayerController player;
        private SwapAbility ability;

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            savedProgress.Clear();
            foreach (string key in ProgressKeys)
            {
                if (PlayerPrefs.HasKey(key))
                {
                    savedProgress[key] = key.EndsWith("Level") || key.EndsWith("Room") ? "i" + PlayerPrefs.GetInt(key) : "s" + PlayerPrefs.GetString(key);
                }
            }
        }

        [UnityTearDown]
        public IEnumerator UnloadScenes()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
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

            var loaded = new System.Collections.Generic.List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                loaded.Add(SceneManager.GetSceneAt(i));
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Level Sequence"));
            foreach (Scene scene in loaded)
            {
                if (scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }
        }

        //==================== HELPERS =====================
        private IEnumerator Load(string scene)
        {
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            yield return null;
            Gather();
            yield return WaitReal(0.5f);
        }

        private void Gather()
        {
            player = Object.FindAnyObjectByType<PlaceholderPlayerController>();
            Assert.NotNull(player, "The level needs a player.");
            ability = player.GetComponent<SwapAbility>();
        }

        private float X => player.transform.position.x;

        // L01 opens with the First Gate cutscene; tests of the level itself start once it has handed control back.
        private IEnumerator WaitForTheGate()
        {
            FirstGateSequence gate = Object.FindAnyObjectByType<FirstGateSequence>();
            if (gate != null)
            {
                yield return WaitRealUntil(() => gate.Finished, 30f);
                Assert.IsTrue(gate.Finished, $"The First Gate hands control back. Stage {gate.Stage}");
                yield return WaitReal(0.3f);
            }
        }

        // Loads the opening with the player already standing in the corridor, as Continue would: the cinematic is skipped.
        private IEnumerator LoadOpeningPastTheAwakening()
        {
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> place = (scene, mode) =>
            {
                Transform stand = GameObject.Find("Stand Point").transform;
                var p = Object.FindAnyObjectByType<PlaceholderPlayerController>();
                CharacterController body = p.GetComponent<CharacterController>();
                body.enabled = false;
                p.transform.position = stand.position + new Vector3(0f, 0.97f, 0f);
                body.enabled = true;
            };
            SceneManager.sceneLoaded += place;
            yield return SceneManager.LoadSceneAsync(Level00, LoadSceneMode.Single);
            SceneManager.sceneLoaded -= place;
            yield return null;
            Gather();
            yield return WaitReal(0.6f);
            Assert.IsTrue(Object.FindAnyObjectByType<OpeningSequence>().Skipped, "Starting past the chamber skips the cinematic.");
        }

        private static string ActiveCamera()
        {
            var brain = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineBrain>();
            return brain != null && brain.ActiveVirtualCamera != null ? brain.ActiveVirtualCamera.Name : null;
        }

        // The start and end of the first gap in the floor after a point: where the floor gives way, and where it comes back.
        private static Vector2 GapAfter(float x)
        {
            float start = float.NaN;
            for (float at = x; at < x + 60f; at += 0.05f)
            {
                bool floor = FloorBelow(at, 3f) > -0.5f;
                if (float.IsNaN(start) && !floor)
                {
                    start = at;
                }
                else if (!float.IsNaN(start) && floor)
                {
                    return new Vector2(start, at);
                }
            }

            Assert.Fail($"No gap after x {x}.");
            return Vector2.zero;
        }

        // Moves towards a gap's edge, walking or running, and jumps as the feet reach it. True when the far side is reached.
        private IEnumerator JumpGap(Vector2 gap, bool run, System.Action<bool> result)
        {
            Teleport(new Vector3(gap.x - 7f, 1.0f, 0f));
            yield return WaitReal(0.5f);
            if (run)
            {
                yield return HoldRun();
            }
            else
            {
                Press(keyboard.dKey);
            }

            yield return WaitRealUntil(() => X >= gap.x - 0.3f, 4f);
            yield return Tap(keyboard.wKey);
            yield return WaitRealUntil(() => (player.IsGrounded && X > gap.x + 0.5f) || player.transform.position.y < -1.5f, 3f);
            yield return ReleaseAll();
            result(player.IsGrounded && X >= gap.y && player.transform.position.y > -0.5f);
        }

        private void Teleport(Vector3 at)
        {
            CharacterController body = player.GetComponent<CharacterController>();
            body.enabled = false;
            player.transform.position = at;
            body.enabled = true;
            Physics.SyncTransforms();
        }

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

        private IEnumerator Tap(ButtonControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        // One key per frame: two presses queued in the same frame would overwrite each other on the test keyboard.
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
            Release(keyboard.dKey);
            Release(keyboard.aKey);
            yield return null;
        }

        private IEnumerator WalkTo(float x)
        {
            Press(keyboard.dKey);
            yield return WaitRealUntil(() => X >= x, 8f);
            yield return ReleaseAll();
            yield return WaitReal(0.3f);
        }

        // Presses whatever the swap is bound to on the mouse or keyboard, so the test follows a change of binding.
        private ButtonControl SwapButton()
        {
            foreach (InputBinding binding in ability.SwapAction.bindings)
            {
                string path = binding.effectivePath;
                if (path == "<Mouse>/leftButton")
                {
                    return mouse.leftButton;
                }

                if (path.StartsWith("<Keyboard>/"))
                {
                    return (ButtonControl)keyboard[path.Substring("<Keyboard>/".Length)];
                }
            }

            Assert.Fail("The swap has no keyboard or mouse binding.");
            return null;
        }

        private string Describe()
        {
            return $"input {player.MoveAction.ReadValue<float>()}, grounded {player.IsGrounded}, locked {player.ControlLocked}, enabled {player.enabled}, "
                + $"frozen {player.GetComponent<Swappable>().IsFrozen}, hold {GameplayHold.Active}, timeScale {Time.timeScale}, velocity {player.Velocity}, "
                + $"keyboard D {keyboard.dKey.isPressed}, current keyboard is the test's {Keyboard.current == keyboard}";
        }

        private IEnumerator SwapWith(Swappable target)
        {
            Camera view = Camera.main;
            for (int i = 0; i < 3; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(target.Center));
                yield return null;
            }

            yield return Tap(SwapButton());
            yield return WaitRealUntil(() => !ability.IsGesturing && !ability.IsSwapping, 3f);
            yield return WaitReal(0.4f);
        }

        private static AccessDoor ExitDoorOf(Component inRoom)
        {
            foreach (AccessDoor door in inRoom.GetComponentInParent<GeneratedRoom>().GetComponentsInChildren<AccessDoor>(true))
            {
                if (door.name.Contains("Exit Door"))
                {
                    return door;
                }
            }

            Assert.Fail("The room has no exit door.");
            return null;
        }

        private static float FloorBelow(float x, float fromY)
        {
            return Physics.Raycast(new Vector3(x, fromY, 0f), Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : float.NaN;
        }

        //==================== LEVEL 1 =====================
        [UnityTest]
        public IEnumerator Level01_StartsAtTheFirstGate_WithItsRoomsAsCheckpoints()
        {
            yield return Load(Level01);
            Assert.NotNull(FacilityUI.Current, "Every level has the Facility UI.");
            PlaytestSession session = Object.FindAnyObjectByType<PlaytestSession>();
            Assert.AreEqual(4, session.Sections.Count, "Each room is a checkpoint, the gate room first.");
            Assert.IsTrue(session.Sections[0].Area.Contains(player.transform.position + Vector3.up), $"The player starts at the gate. At {player.transform.position}");
            yield return WaitForTheGate();
            Assert.IsTrue(player.IsGrounded && !player.ControlLocked, "The game opens standing, with control: the fall is only in the final cutscene.");
            Animator animator = player.GetComponentInChildren<Animator>();
            Assert.IsFalse(animator.GetCurrentAnimatorStateInfo(0).IsName("Knockdown"));
        }

        [UnityTest]
        public IEnumerator Level01_TheGlassIsPassedBySwappingWithTheHousingBeyondIt()
        {
            yield return Load(Level01);
            yield return WaitForTheGate();
            Swappable housing = GameObject.Find("Swap Housing (beyond the glass)").GetComponent<Swappable>();
            float glassX = housing.Position.x - 5f;
            yield return WalkTo(glassX - 2f);
            Assert.Less(X, glassX, "The glass stops the player.");
            Camera view = Camera.main;
            for (int i = 0; i < 3; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(housing.Center));
                yield return null;
            }

            yield return Tap(SwapButton());
            yield return WaitRealUntil(() => !ability.IsGesturing && !ability.IsSwapping, 3f);
            yield return WaitReal(0.4f);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The housing is in range from the glass. At {player.transform.position}");
            Assert.Greater(X, glassX, $"The swap carries the player through the glass. At {player.transform.position}");
        }

        [UnityTest]
        public IEnumerator Level01_TheExitLoadsTheNextLevel_OnceBehindTheLoadingScreen()
        {
            GameProgress.Clear();
            yield return Load(Level01);
            yield return WaitForTheGate();
            LevelExit exit = Object.FindAnyObjectByType<LevelExit>();
            Assert.AreEqual(Level02, exit.NextScene);
            Teleport(exit.transform.position - Vector3.up);
            yield return WaitRealUntil(() => FacilityUI.Current != null && FacilityUI.Current.IsLeaving, 2f);
            Assert.IsTrue(exit.Used, "The exit fires.");
            yield return WaitRealUntil(() => FacilityUI.Current.LoadingVisible, 1f);
            Assert.IsTrue(FacilityUI.Current.LoadingVisible, "The loading screen covers the change.");
            Assert.AreEqual("ACCESSING SECTOR", FacilityUI.Current.LoadingMessage);
            yield return WaitRealUntil(() => SceneManager.GetActiveScene().name == Level02, 15f);
            yield return WaitRealUntil(() => FacilityUI.Current != null && !FacilityUI.Current.LoadingVisible, 6f);
            Assert.AreEqual(Level02, SceneManager.GetActiveScene().name);
            Gather();
            PlaytestSession session = Object.FindAnyObjectByType<PlaytestSession>();
            Assert.IsTrue(session.Sections[0].Area.Contains(player.transform.position + Vector3.up), $"The next level starts in its first room. At {player.transform.position}");
            yield return WaitReal(0.3f);
            Assert.AreEqual(Level02, GameProgress.Scene, "Progress moves on to the new level.");
            Assert.IsFalse(GameplayHold.Active, "Control is back after the arrival.");
        }

        //==================== THE OPENING (L00) =====================
        [UnityTest, Timeout(150000)]
        public IEnumerator Opening_TheTransportAndTheAwakening_PlayWithoutControl_ThenHandOverWalkingOnly()
        {
            yield return Load(Level00);
            OpeningSequence opening = Object.FindAnyObjectByType<OpeningSequence>();
            OpeningControls controls = Object.FindAnyObjectByType<OpeningControls>();
            FacilityUI ui = FacilityUI.Current;
            Assert.IsFalse(opening.Skipped, "A new game plays the opening.");
            Assert.IsTrue(ui.IsCutscene, "No control during the transport.");
            Assert.Greater(ui.Darkness, 0.9f, "The game begins almost completely black.");
            Assert.IsFalse(player.MoveAction.enabled || player.JumpAction.enabled || player.RunAction.enabled || player.SlideAction.enabled, "Every key is held back.");
            Assert.AreEqual("CM Transport (side-on)", ActiveCamera(), "The side-on transport camera.");

            // The chambers travel at one steady speed, and the camera travels with them.
            Transform carriage = GameObject.Find("Carriage").transform;
            Camera view = Camera.main;
            yield return WaitRealUntil(() => opening.Stage == "Transport", 6f);
            float x0 = carriage.position.x, c0 = view.transform.position.x, t0 = Time.realtimeSinceStartup;
            yield return WaitReal(2f);
            float x1 = carriage.position.x, c1 = view.transform.position.x, t1 = Time.realtimeSinceStartup;
            yield return WaitReal(2f);
            float x2 = carriage.position.x, t2 = Time.realtimeSinceStartup;
            float speedA = (x1 - x0) / (t1 - t0), speedB = (x2 - x1) / (t2 - t1);
            Assert.AreEqual(1.6f, speedA, 0.15f, "The transport speed.");
            Assert.AreEqual(speedA, speedB, 0.15f, "The transport keeps a constant speed.");
            Assert.Greater(c1 - c0, 2f, "The camera follows the transport.");
            Assert.Less(ui.Darkness, 0.8f, "The silhouettes slowly become visible.");

            // The protagonist's chamber is lit for a moment.
            Light moment = GameObject.Find("Protagonist Light").GetComponent<Light>();
            float brightest = 0f;
            yield return WaitRealUntil(() => opening.Stage == "Protagonist", 12f);
            float until = Time.realtimeSinceStartup + 4.4f;
            while (Time.realtimeSinceStartup < until)
            {
                brightest = Mathf.Max(brightest, moment.intensity);
                yield return null;
            }

            Assert.Greater(brightest, 4f, "The protagonist's chamber becomes visible.");
            Assert.Less(moment.intensity, brightest * 0.5f, "Only briefly.");

            // Black, an impact, silence, the crash; glass falls; the transport is gone.
            yield return WaitRealUntil(() => opening.Stage == "Impact", 10f);
            Assert.Greater(ui.Darkness, 0.99f, "The impact is heard in complete black.");
            yield return WaitRealUntil(() => opening.Stage == "Crash", 3f);
            yield return null;
            Assert.IsFalse(GameObject.Find("Glass Shards").GetComponentInChildren<Rigidbody>().isKinematic, "The shards fall at the crash.");
            Assert.IsNull(GameObject.Find("Carriage"), "The transport is switched off once it has gone.");

            // The awakening: the emergency strip comes up on the chamber, seen from low down, and the keys still do nothing.
            yield return WaitRealUntil(() => opening.Stage == "Emergency Light", 5f);
            Assert.AreEqual("CM Chamber (low)", ActiveCamera(), "The low camera on the damaged chamber.");
            float startX = X;
            Press(keyboard.dKey);
            yield return WaitReal(1f);
            Release(keyboard.dKey);
            Assert.AreEqual(startX, X, 0.01f, "No control before the awakening ends.");
            yield return WaitRealUntil(() => opening.Finished, 30f);
            Assert.IsTrue(opening.Finished, $"The awakening ends. Stage {opening.Stage}");
            foreach (Rigidbody shard in GameObject.Find("Glass Shards").GetComponentsInChildren<Rigidbody>())
            {
                Assert.IsTrue(shard.isKinematic, "The shards have settled and no longer block anything.");
            }

            // Control: walking only, and the move prompt from the real bindings.
            Assert.IsFalse(ui.IsCutscene);
            Assert.IsTrue(controls.IsAllowed(OpeningControls.Ability.Move));
            Assert.IsFalse(controls.IsAllowed(OpeningControls.Ability.Run) || controls.IsAllowed(OpeningControls.Ability.Slide) || controls.IsAllowed(OpeningControls.Ability.Jump), "Only walking so far.");
            TutorialGuide guide = Object.FindAnyObjectByType<TutorialGuide>();
            yield return WaitRealUntil(() => guide.CurrentHintText != null, 2f);
            Assert.AreEqual("Move", guide.CurrentHint);
            StringAssert.Contains("A / D", guide.CurrentHintText);
            StringAssert.Contains("MOVE", guide.CurrentHintText);
            yield return WaitRealUntil(() => ActiveCamera() == "CM Follow Camera", 3f);
            Assert.AreEqual("CM Follow Camera", ActiveCamera(), "Back to the gameplay camera.");

            startX = X;
            Press(keyboard.dKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            yield return WaitReal(1.2f);
            Assert.IsFalse(player.IsRunning, "Running is not handed over yet.");
            yield return ReleaseAll();
            Assert.Greater(X - startX, 3f, $"The player walks. {Describe()}");
            yield return WaitReal(0.6f);
            Assert.AreNotEqual("Move", guide.CurrentHint, "The move prompt goes once movement is registered.");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Opening_FirstMovement_TeachesRunThenSlide_ThenJump_EachPromptGoneOnceDone()
        {
            yield return LoadOpeningPastTheAwakening();
            OpeningControls controls = Object.FindAnyObjectByType<OpeningControls>();
            TutorialGuide guide = Object.FindAnyObjectByType<TutorialGuide>();
            Transform runFrom = GameObject.Find("Run From").transform;
            Transform low = GameObject.Find("Low Opening").transform;

            // Running comes with the longer service passage.
            yield return WalkTo(runFrom.position.x + 0.5f);
            Assert.IsTrue(controls.IsAllowed(OpeningControls.Ability.Run));
            yield return WaitRealUntil(() => guide.CurrentHint == "Run" && guide.CurrentHintText != null, 2f);
            StringAssert.Contains("Left Shift", guide.CurrentHintText);
            StringAssert.Contains("RUN", guide.CurrentHintText);
            yield return HoldRun();
            yield return WaitReal(1f);
            Assert.IsTrue(player.IsRunning, $"The player runs. {Describe()}");

            // The low opening: the slide prompt comes up on the approach, and the opening stops anyone who does not slide.
            yield return WaitRealUntil(() => guide.CurrentHint == "Slide", 5f);
            Assert.AreEqual("Slide", guide.CurrentHint, "The run prompt has gone; the slide prompt is up.");
            StringAssert.Contains("S", guide.CurrentHintText);
            StringAssert.Contains("SLIDE WHILE RUNNING", guide.CurrentHintText);
            Assert.IsTrue(controls.IsAllowed(OpeningControls.Ability.Slide));
            yield return WaitRealUntil(() => X >= low.position.x + 1f, 4f);
            yield return WaitReal(0.5f);
            Assert.Less(X, low.position.x + 0.5f, "The low opening cannot be walked through.");

            // Back up, run at it and slide.
            yield return ReleaseAll();
            Teleport(new Vector3(low.position.x - 9f, 1f, 0f));
            yield return WaitReal(0.3f);
            yield return HoldRun();
            yield return WaitRealUntil(() => X >= low.position.x - 3f, 3f);
            yield return Tap(keyboard.sKey);
            yield return WaitRealUntil(() => player.IsSliding, 1f);
            Assert.IsTrue(player.IsSliding, $"The slide works with the existing controller. {Describe()}");
            yield return WaitRealUntil(() => X > low.position.x + 7f, 4f);
            Assert.Greater(X, low.position.x + 6f, $"The slide carries the player under the opening. {Describe()}");
            Assert.AreNotEqual("Slide", guide.CurrentHint, "The slide prompt goes once the slide is done.");

            // Jumping comes with the first gap.
            Transform jumpFrom = GameObject.Find("Jump From").transform;
            Assert.IsFalse(controls.IsAllowed(OpeningControls.Ability.Jump), "No jump before the first gap.");
            yield return WaitRealUntil(() => X >= jumpFrom.position.x + 0.5f, 10f);
            yield return ReleaseAll();
            Assert.IsTrue(controls.IsAllowed(OpeningControls.Ability.Jump));
            yield return WaitRealUntil(() => guide.CurrentHint == "Jump", 2f);
            StringAssert.Contains("W", guide.CurrentHintText);
            StringAssert.Contains("JUMP", guide.CurrentHintText);
            yield return Tap(keyboard.wKey);
            yield return WaitReal(0.6f);
            Assert.AreNotEqual("Jump", guide.CurrentHint, "The jump prompt goes once the player jumps.");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Opening_TheFirstGapTakesAJump_TheSecondARunningJump()
        {
            yield return LoadOpeningPastTheAwakening();
            Vector2 first = GapAfter(GameObject.Find("Jump From").transform.position.x);
            Vector2 second = GapAfter(first.y + 1f);
            bool made = false;
            yield return JumpGap(first, false, ok => made = ok);
            Assert.IsTrue(made, $"A walking jump clears the first gap ({first.y - first.x:0.0} m). {Describe()}");
            yield return WaitReal(2.5f);
            yield return JumpGap(second, false, ok => made = ok);
            Assert.IsFalse(made, $"A walking jump does not clear the second gap ({second.y - second.x:0.0} m). At {player.transform.position}");
            yield return WaitRealUntil(() => !FacilityUI.Current.IsReturning, 5f);
            yield return WaitReal(0.5f);
            yield return JumpGap(second, true, ok => made = ok);
            Assert.IsTrue(made, $"A running jump clears the second gap ({second.y - second.x:0.0} m). {Describe()}");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Opening_TheRun_TakesControl_TheCameraStops_CutsToBlack_ThenTheFirstGateClosesBehind()
        {
            GameProgress.Clear();
            yield return LoadOpeningPastTheAwakening();
            ScriptedRun run = Object.FindAnyObjectByType<ScriptedRun>();
            Teleport(new Vector3(run.transform.position.x - 6f, 1f, 0f));
            yield return WaitReal(0.4f);
            Press(keyboard.dKey);
            yield return WaitRealUntil(() => run.Started, 4f);
            Release(keyboard.dKey);
            Assert.IsTrue(run.Started, "Crossing the trigger starts the run.");
            Assert.IsTrue(FacilityUI.Current.IsCutscene && !player.enabled, "Control is taken.");

            // Pressing back does nothing: the protagonist keeps running on.
            float before = X;
            Press(keyboard.aKey);
            yield return WaitReal(0.8f);
            Release(keyboard.aKey);
            Assert.Greater(X - before, 4f, "The protagonist runs by themselves.");
            Assert.AreEqual("CM Run (follow)", ActiveCamera(), "The run camera follows.");

            // The camera stops and the protagonist runs out of the frame.
            yield return WaitRealUntil(() => run.Stage == "Empty Corridor", 15f);
            Assert.AreEqual("CM Run (still)", ActiveCamera(), "The camera has stopped.");
            Camera view = Camera.main;
            Assert.Greater(view.WorldToViewportPoint(player.transform.position).x, 1.05f, "The protagonist is out of view.");
            Vector3 cameraAt = view.transform.position;
            yield return WaitReal(1f);
            Assert.AreEqual(0f, Vector3.Distance(cameraAt, view.transform.position), 0.01f, "The camera holds on the empty corridor.");
            yield return WaitRealUntil(() => run.Stage == "Black", 4f);
            Assert.Greater(FacilityUI.Current.Darkness, 0.99f, "Cut to black.");

            // The next scene: the First Gate.
            yield return WaitRealUntil(() => SceneManager.GetActiveScene().name == Level01, 15f);
            Assert.AreEqual(Level01, SceneManager.GetActiveScene().name);
            yield return null;
            Gather();
            FirstGateSequence gate = Object.FindAnyObjectByType<FirstGateSequence>();
            AccessDoor door = GameObject.Find("First Gate").GetComponent<AccessDoor>();
            Transform threshold = GameObject.Find("Threshold").transform;
            Assert.AreEqual("CM First Gate", ActiveCamera(), "The scene opens on the gate.");
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State, "The gate is open as the protagonist comes in.");
            Assert.Less(X, threshold.position.x, "The protagonist starts outside the gate.");
            yield return WaitRealUntil(() => door.State != AccessDoor.DoorState.Open, 8f);
            Assert.GreaterOrEqual(gate.ClosedFromX, threshold.position.x, "The gate only closes once the protagonist is through.");
            yield return WaitRealUntil(() => gate.Stage == "Shut", 8f);
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State, "The gate shuts completely.");
            Assert.IsTrue(door.IsLocked, "It stays shut.");
            Assert.AreEqual("CM First Gate", ActiveCamera(), "The camera holds on the shut gate.");
            yield return WaitRealUntil(() => gate.Stage == "Intercom", 5f);
            Assert.AreEqual("Intercom", gate.Stage, "The intercom placeholder has its moment.");
            yield return WaitRealUntil(() => gate.Finished, 10f);
            yield return WaitRealUntil(() => ActiveCamera() == "CM Follow Camera", 4f);
            Assert.AreEqual("CM Follow Camera", ActiveCamera(), "Back to the gameplay camera.");
            Assert.IsTrue(player.enabled && !FacilityUI.Current.IsCutscene && !GameplayHold.Active, "Control is back.");
            float atStop = X;
            yield return WalkTo(atStop + 2f);
            Assert.Greater(X, atStop + 1.5f, $"Normal play resumes. {Describe()}");
            Assert.Greater(Object.FindAnyObjectByType<PlaytestSession>().Sections[0].transform.Find("Entry Point").position.x, door.transform.position.x, "A restart now puts the player inside the gate.");
            Assert.AreEqual(Level01, GameProgress.Scene);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Opening_RoomsBehindSwitchOff_ComeBackOnReturn_AndNeverFlickerAtABoundary()
        {
            yield return LoadOpeningPastTheAwakening();
            RoomActivation activation = Object.FindAnyObjectByType<RoomActivation>();
            GameObject chamber = activation.Rooms[0], corridor = activation.Rooms[1];
            int renderersBefore = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;

            // Past the middle of the service passage, the chamber and the corridor are behind the camera and switched off.
            Bounds passage = activation.BoundsOf(2);
            Teleport(new Vector3(passage.center.x + 2f, 1f, 0f));
            yield return WaitReal(2f);
            Assert.IsFalse(chamber.activeSelf || corridor.activeSelf, "Rooms left behind are switched off.");
            Assert.IsTrue(activation.Rooms[2].activeSelf && activation.Rooms[3].activeSelf, "The room in play and the one ahead stay on.");
            Assert.Less(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length, renderersBefore - 40, "Fewer active renderers.");
            Assert.IsTrue(player.IsGrounded, "The player stands on the room in play.");

            // Walking back (from this side of the low opening): the corridor is switched on again before the player reaches it.
            Bounds corridorBounds = activation.BoundsOf(1);
            Teleport(new Vector3(passage.min.x + 10f, 1f, 0f));
            yield return null;
            Assert.IsFalse(corridor.activeSelf, "Still off while the player is well into the passage.");
            Press(keyboard.aKey);
            yield return WaitRealUntil(() => corridor.activeSelf, 6f);
            Assert.IsTrue(corridor.activeSelf, "Coming back switches the room on again.");
            Assert.Greater(X, corridorBounds.max.x, "It is on before the player gets there.");
            yield return WaitRealUntil(() => X < corridorBounds.center.x, 8f);
            Release(keyboard.aKey);
            Assert.IsTrue(chamber.activeSelf, "Back in the corridor, the chamber behind is on again.");
            Assert.IsTrue(player.IsGrounded, "No missing floor on the way back.");

            // Put straight back into the chamber: it is there the same frame.
            Teleport(new Vector3(passage.center.x + 2f, 1f, 0f));
            yield return WaitReal(2f);
            Assert.IsFalse(chamber.activeSelf);
            Teleport(GameObject.Find("Stand Point").transform.position + new Vector3(-3f, 1f, 0f));
            yield return null;
            Assert.IsTrue(chamber.activeSelf, "A room the player is put into is switched on at once.");
            yield return WaitReal(0.5f);
            Assert.IsTrue(player.IsGrounded && player.transform.position.y > 0.5f, $"The player never falls through. At {player.transform.position}");

            // Moving back and forth over a boundary does not switch a room on and off over and over.
            Teleport(new Vector3(corridorBounds.center.x - 1f, 1f, 0f));
            yield return WaitReal(1f);
            int switches = activation.Switches;
            for (int i = 0; i < 4; i++)
            {
                Teleport(new Vector3(corridorBounds.center.x + 1.5f, 1f, 0f));
                yield return WaitReal(0.4f);
                Teleport(new Vector3(corridorBounds.center.x - 1.5f, 1f, 0f));
                yield return WaitReal(0.4f);
            }

            Assert.LessOrEqual(activation.Switches - switches, 1, "No repeated switching at a boundary.");
        }

        //==================== LEVEL 5 =====================
        [UnityTest]
        public IEnumerator Level05_OrdinaryJumpsNeverSlowTime()
        {
            yield return Load(Level05);
            SlowMotionMoment moment = Object.FindAnyObjectByType<SlowMotionMoment>();
            float startX = X;
            yield return HoldRun();
            yield return WaitReal(0.6f);
            float lowest = 1f;
            for (int jump = 0; jump < 3; jump++)
            {
                yield return Tap(keyboard.wKey);
                float end = Time.realtimeSinceStartup + 0.8f;
                while (Time.realtimeSinceStartup < end)
                {
                    lowest = Mathf.Min(lowest, Time.timeScale);
                    yield return null;
                }
            }

            float travelled = X - startX;
            yield return ReleaseAll();
            Assert.Greater(travelled, 8f, "The player really ran and jumped.");
            Assert.AreEqual(1f, lowest, "Jumps away from the authored ledge keep normal time.");
            Assert.AreEqual(0, moment.Attempts, "None of them counted as the authored jump.");
        }

        [UnityTest]
        public IEnumerator Level05_SlowMotionOnTheDropIntoTheShaft_FirstTryThenEveryOther_AndTimeAlwaysComesBack()
        {
            yield return Load(Level05);
            SlowMotionMoment moment = Object.FindAnyObjectByType<SlowMotionMoment>();
            Bounds zone = moment.GetComponent<BoxCollider>().bounds;
            float startX = zone.min.x - 7f;
            float floor = FloorBelow(startX, zone.max.y + 5f);
            Assert.IsFalse(float.IsNaN(floor), "There is a ledge to run along before the drop.");

            // First try: slow motion while in the air, then normal time again.
            Teleport(new Vector3(startX, floor + 1.1f, 0f));
            yield return WaitReal(0.3f);
            yield return HoldRun();
            yield return WaitRealUntil(() => SlowMotionMoment.AnyActive, 4f);
            Assert.IsTrue(SlowMotionMoment.AnyActive, $"Running off the ledge starts the moment. At {player.transform.position}; {Describe()}");
            yield return WaitReal(0.35f);
            yield return ReleaseAll();
            Assert.Less(Time.timeScale, 0.6f, "Time slows while airborne.");
            Assert.Less(Time.fixedDeltaTime, 0.015f, "Physics steps scale with it, so moving objects stay smooth.");
            yield return WaitRealUntil(() => !SlowMotionMoment.AnyActive, 6f);
            Assert.IsFalse(SlowMotionMoment.AnyActive, "The moment ends by itself.");
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "Normal time comes back.");
            Assert.AreEqual(0.02f, Time.fixedDeltaTime, 0.0001f, "The physics step comes back.");
            Assert.AreEqual(1, moment.Plays);

            // Second try: normal speed, so retries stay quick.
            Teleport(new Vector3(startX, floor + 1.1f, 0f));
            yield return WaitReal(0.3f);
            yield return HoldRun();
            float lowest = 1f;
            float end = Time.realtimeSinceStartup + 2.5f;
            while (Time.realtimeSinceStartup < end)
            {
                lowest = Mathf.Min(lowest, Time.timeScale);
                yield return null;
            }

            yield return ReleaseAll();
            Assert.AreEqual(2, moment.Attempts, "The second run off the ledge counts as an attempt.");
            Assert.AreEqual(1f, lowest, "The second attempt keeps normal time.");
        }

        [UnityTest]
        public IEnumerator Level05_CatchingTheFragment_LeadsToTheCore_WithWhoWasI()
        {
            yield return Load(Level05);
            LevelExit exit = Object.FindAnyObjectByType<LevelExit>();
            Assert.AreEqual(Level06, exit.NextScene);
            Teleport(exit.transform.position - Vector3.up * 0.5f);
            yield return WaitRealUntil(() => exit.Used, 4f);
            Assert.IsTrue(exit.Used, "The exit fires after its pause.");
            yield return WaitRealUntil(() => FacilityUI.Current.LoadingVisible, 1f);
            Assert.AreEqual("WHO WAS I?", FacilityUI.Current.LoadingMessage);
            yield return WaitRealUntil(() => SceneManager.GetActiveScene().name == Level06, 15f);
            yield return WaitRealUntil(() => FacilityUI.Current != null && !FacilityUI.Current.LoadingVisible, 6f);
            Assert.AreEqual(Level06, SceneManager.GetActiveScene().name);
            Assert.IsFalse(GameplayHold.Active);
        }

        //==================== EVERY LEVEL =====================
        [UnityTest]
        public IEnumerator EveryLevel_HasTheInterface_ACheckpointPerRoom_AndAnExitToTheNextInOrder()
        {
            for (int i = 0; i < Chain.Length; i++)
            {
                var link = Chain[i];
                yield return Load(link.scene);
                Assert.NotNull(FacilityUI.Current, $"{link.scene} has the Facility UI.");
                Assert.AreEqual(i + 1, FacilityUI.Current.LevelIndexOf(link.scene), $"{link.scene} is in its place in the level order, after the opening.");
                yield return WaitForTheGate();
                PlaytestSession session = Object.FindAnyObjectByType<PlaytestSession>();
                GeneratedRoom[] rooms = Object.FindObjectsByType<GeneratedRoom>(FindObjectsSortMode.None);
                Assert.GreaterOrEqual(session.Sections.Count, 3, $"{link.scene} has a checkpoint per room.");
                Assert.IsTrue(session.Sections[0].Area.Contains(player.transform.position + Vector3.up), $"{link.scene} starts in its first room. At {player.transform.position}");
                Assert.IsTrue(player.IsGrounded && !player.ControlLocked, $"{link.scene} opens standing, with control.");
                LevelExit exit = Object.FindAnyObjectByType<LevelExit>();
                Assert.NotNull(exit, $"{link.scene} has a way out.");
                Assert.AreEqual(link.next, exit.NextScene, $"{link.scene} leads to the next level.");
                Assert.AreEqual(link.message, exit.LoadingMessage, $"The loading line after {link.scene}.");
                Assert.AreEqual(link.bright, exit.BrightTransition, $"{link.scene}: only the step out into daylight is pale.");
                Assert.Greater(rooms.Length, 2, $"{link.scene} is built from generated rooms.");
            }
        }

        //==================== LEVEL 2 =====================
        [UnityTest]
        public IEnumerator Level02_TheTestRigIsPassedBySwappingThroughTheGlass()
        {
            yield return Load(Level02);
            Swappable housing = GameObject.Find("Swap Housing (test rig)").GetComponent<Swappable>();
            float glassX = housing.Position.x - 3.5f;
            yield return WalkTo(glassX - 1.5f);
            Assert.Less(X, glassX, "The glass stops the player.");
            yield return SwapWith(housing);
            Assert.AreEqual(SwapRejection.None, ability.LastResult, $"The rig's housing is in range from the glass. At {player.transform.position}");
            Assert.Greater(X, glassX, $"The swap carries the player through the glass. At {player.transform.position}");
        }

        //==================== LEVEL 3 =====================
        [UnityTest]
        public IEnumerator Level03_TheLastDoorOpensByItselfAsThePlayerApproaches()
        {
            yield return Load(Level03);
            DoorOpensItself opener = Object.FindAnyObjectByType<DoorOpensItself>();
            AccessDoor door = ExitDoorOf(opener);
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State, "The door starts sealed, with nothing to press.");
            Teleport(opener.transform.position + new Vector3(0f, -0.9f, 0f));
            yield return WaitRealUntil(() => door.State == AccessDoor.DoorState.Open, 5f);
            Assert.IsTrue(opener.Opened, "Arriving opens it.");
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State, "Someone let the player through.");
        }

        //==================== LEVEL 4 =====================
        [UnityTest]
        public IEnumerator Level04_LookingIntoTheLitCell_IsRememberedForTheExterior()
        {
            NarrativeFlags.ClearAll();
            yield return Load(Level04);
            ObservedFlag flag = Object.FindAnyObjectByType<ObservedFlag>();
            ObservationPoint point = flag.GetComponent<ObservationPoint>();
            Assert.AreEqual(NarrativeFlags.SawOccupiedCell, flag.Flag);
            Assert.IsFalse(NarrativeFlags.Get(NarrativeFlags.SawOccupiedCell), "Nothing is remembered before looking.");
            yield return WalkTo(point.transform.position.x + 0.3f);
            yield return Tap(keyboard.eKey);
            yield return WaitRealUntil(() => point.IsObserving, 2f);
            Assert.IsTrue(point.IsObserving, $"E looks into the lit cell. At {player.transform.position}");
            yield return WaitReal(0.3f);
            Assert.IsTrue(NarrativeFlags.Get(NarrativeFlags.SawOccupiedCell), "Seeing the occupant is remembered.");
            yield return Tap(keyboard.eKey);
            yield return WaitRealUntil(() => !point.IsObserving, 2f);
            Assert.IsFalse(point.IsObserving, "E returns.");
        }

        //==================== LEVEL 6 =====================
        [UnityTest]
        public IEnumerator Level06_DisplacingTheThreeAnchors_ShutsTheCoreDown_ThenTheExitOpens()
        {
            yield return Load(Level06);
            CoreShutdown core = Object.FindAnyObjectByType<CoreShutdown>();
            Assert.AreEqual(3, core.AnchorCount);
            AccessDoor door = ExitDoorOf(core);
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State, "The chamber is sealed while the core runs.");
            var anchors = new System.Collections.Generic.List<CoreAnchor>(Object.FindObjectsByType<CoreAnchor>(FindObjectsSortMode.None));
            anchors.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            Vector3 origin = core.GetComponentInParent<GeneratedRoom>().transform.position;

            // A: through the floor-to-ceiling glass. B: from the floor up onto the plinth. C: along the ledge from the lift side.
            Vector3[] standAt = { origin + new Vector3(12.8f, 1.1f, 0f), origin + new Vector3(24.5f, 1.1f, 0f), origin + new Vector3(51.4f, 11.1f, 0f) };
            for (int i = 0; i < 3; i++)
            {
                Teleport(standAt[i]);
                yield return WaitReal(0.6f);
                yield return SwapWith(anchors[i].Anchor);
                Assert.AreEqual(SwapRejection.None, ability.LastResult, $"Anchor {i + 1} can be swapped from where it is approached. At {player.transform.position}");
                yield return WaitReal(0.2f);
                Assert.IsTrue(anchors[i].Released, $"Swapping anchor {i + 1} out of its cradle displaces it. Anchor at {anchors[i].Anchor.Center}");
                if (i < 2)
                {
                    Assert.AreEqual("Running", core.Stage, "The core keeps running until every anchor is out.");
                    Assert.AreEqual(AccessDoor.DoorState.Closed, door.State);
                }
            }

            yield return WaitRealUntil(() => core.Stage != "Running", 2f);
            Assert.IsTrue(FacilityUI.Current.IsCutscene && GameplayHold.Active, "Control is held while the core goes down.");
            yield return WaitRealUntil(() => core.IsDown, 10f);
            Assert.IsTrue(core.IsDown, $"The shutdown finishes. Stage {core.Stage}");
            yield return WaitRealUntil(() => door.State == AccessDoor.DoorState.Open, 4f);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State, "The sealed exit opens.");
            Assert.IsFalse(GameplayHold.Active, "Control comes back.");
            Assert.IsFalse(FacilityUI.Current.IsCutscene);
        }

        //==================== LEVEL 7 =====================
        [UnityTest]
        public IEnumerator Level07_TheLeapIntoDaylight_IsSlowedOnTheFirstTry_AndTimeComesBack()
        {
            yield return Load(Level07);
            SlowMotionMoment moment = Object.FindAnyObjectByType<SlowMotionMoment>();
            Bounds zone = moment.GetComponent<BoxCollider>().bounds;
            float startX = zone.min.x - 7f;
            float floor = FloorBelow(startX, zone.max.y + 3f);
            Assert.IsFalse(float.IsNaN(floor), "There is floor to run along before the leap.");
            Teleport(new Vector3(startX, floor + 1.1f, 0f));
            yield return WaitReal(0.3f);
            float edge = zone.center.x;
            yield return HoldRun();
            yield return WaitRealUntil(() => X >= edge - 0.6f, 4f);
            yield return Tap(keyboard.wKey);
            yield return WaitRealUntil(() => SlowMotionMoment.AnyActive, 2f);
            Assert.IsTrue(SlowMotionMoment.AnyActive, $"Jumping from the edge at a run starts the moment. At {player.transform.position}; {Describe()}");
            yield return WaitReal(0.3f);
            Assert.Less(Time.timeScale, 0.6f, "Time slows over the gap.");
            yield return WaitRealUntil(() => !SlowMotionMoment.AnyActive, 6f);
            yield return ReleaseAll();
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "Normal time comes back.");
            Assert.AreEqual(0.02f, Time.fixedDeltaTime, 0.0001f, "The physics step comes back.");
            Assert.Greater(X, edge + 4.6f, $"The leap clears the gap at a run. At {player.transform.position}");
            Assert.Greater(player.transform.position.y, floor - 0.2f, $"It lands on the far side, not in the gap. At {player.transform.position}");
        }

        //==================== PLAYER SOUNDS =====================
        [UnityTest]
        public IEnumerator PlayerSounds_StepsFollowTheFeet_WalkThenRun_EveryJumpGrunts_WithoutRepeating_AndSlidesScuff()
        {
            yield return Load(Level02);
            Vector3 start = player.transform.position;
            PlayerSounds sounds = player.GetComponentInChildren<PlayerSounds>();
            Assert.NotNull(sounds, "The player prefab carries its sounds.");

            // Walking: steps at the animation's rhythm, from the walking set.
            int before = sounds.StepsPlayed;
            Press(keyboard.dKey);
            yield return WaitReal(0.4f);
            int walkStart = sounds.StepsPlayed;
            yield return WaitReal(1.2f);
            float walkRate = (sounds.StepsPlayed - walkStart) / 1.2f;
            string walkClip = sounds.LastClip != null ? sounds.LastClip.name : "none";

            // Running (Shift held): the running set.
            Press(keyboard.leftShiftKey);
            yield return WaitReal(0.4f);
            int runStart = sounds.StepsPlayed;
            yield return WaitReal(1.0f);
            float runRate = (sounds.StepsPlayed - runStart) / 1.0f;
            string runClip = sounds.LastClip != null ? sounds.LastClip.name : "none";
            yield return ReleaseAll();

            Assert.Greater(sounds.StepsPlayed, before + 4, "Moving makes steps.");
            Assert.That(walkRate, Is.InRange(1.2f, 6f), $"Walking steps come at a footstep rhythm, {walkRate:0.0} per second.");
            Assert.That(runRate, Is.InRange(1.5f, 7f), $"Running steps come at a footstep rhythm, {runRate:0.0} per second.");
            StringAssert.StartsWith("FS_Walk", walkClip, "Moving normally uses the walking steps.");
            StringAssert.StartsWith("FS_Run", runClip, "Holding run uses the running steps.");

            // Standing still: silence.
            yield return WaitReal(0.8f);
            int still = sounds.StepsPlayed;
            yield return WaitReal(1f);
            Assert.AreEqual(still, sounds.StepsPlayed, "Standing still makes no steps.");

            // Jumping: a grunt every time, one of the four, never the same twice in a row.
            var heard = new System.Collections.Generic.List<string>();
            for (int jump = 0; jump < 6; jump++)
            {
                int jumps = sounds.JumpsPlayed;
                yield return Tap(keyboard.wKey);
                yield return WaitRealUntil(() => sounds.JumpsPlayed > jumps, 1f);
                Assert.AreEqual(jumps + 1, sounds.JumpsPlayed, $"Jump {jump + 1} grunts.");
                heard.Add(sounds.LastClip.name);
                yield return WaitRealUntil(() => player.IsGrounded, 2f);
                yield return WaitReal(0.3f);
            }

            for (int i = 0; i < heard.Count; i++)
            {
                StringAssert.StartsWith("VOC_Jump", heard[i]);
                if (i > 0)
                {
                    Assert.AreNotEqual(heard[i - 1], heard[i], $"Two jumps in a row never use the same grunt: {string.Join(", ", heard)}");
                }
            }

            Assert.GreaterOrEqual(new System.Collections.Generic.HashSet<string>(heard).Count, 3, $"The grunts vary: {string.Join(", ", heard)}");
            Assert.Greater(sounds.LandingsPlayed, 0, "Landing from a jump makes a landing step.");

            // Sliding: a concrete scuff as the slide starts, gone soon after the slide ends.
            Teleport(start);
            yield return WaitReal(0.4f);
            yield return HoldRun();
            yield return WaitReal(0.8f);
            int slides = sounds.SlidesPlayed;
            yield return Tap(keyboard.sKey);
            yield return WaitRealUntil(() => sounds.SlidesPlayed > slides, 1f);
            Assert.AreEqual(slides + 1, sounds.SlidesPlayed, $"Sliding plays a scuff. {Describe()}");
            StringAssert.StartsWith("SFX_Slide", sounds.LastClip.name);
            Assert.IsTrue(sounds.IsSlideSounding, "The scuff sounds during the slide.");
            yield return WaitRealUntil(() => !player.IsSliding, 3f);
            yield return ReleaseAll();
            yield return WaitReal(1.2f);
            Assert.IsFalse(sounds.IsSlideSounding, "The scuff is over soon after the slide ends.");
        }

        //==================== LEVEL 8 =====================
        [UnityTest]
        public IEnumerator Level08_DroneStrike_TheRunnerIsHitAndFalls_ThenTheEndingTitle()
        {
            yield return Load(Level08);
            DroneStrikeSequence strike = Object.FindAnyObjectByType<DroneStrikeSequence>();
            Animator animator = player.GetComponentInChildren<Animator>();
            Assert.IsFalse(animator.GetCurrentAnimatorStateInfo(0).IsName("Knockdown"), "Nothing falls before the end.");
            Teleport(strike.transform.position + new Vector3(0f, -1.9f, 0f));
            yield return WaitRealUntil(() => strike.Started, 2f);
            Assert.IsTrue(strike.Started, "Reaching the end of the walkway starts the cutscene.");
            Assert.IsTrue(FacilityUI.Current.IsCutscene && GameplayHold.Active, "Control is taken.");
            float startX = X;
            yield return WaitRealUntil(() => strike.Stage == "Struck", 5f);
            Assert.AreEqual("Struck", strike.Stage);
            Assert.Greater(X, startX + 10f, $"The protagonist kept running until the strike. From {startX} to {X}");
            yield return WaitReal(0.3f);
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Knockdown") || animator.GetNextAnimatorStateInfo(0).IsName("Knockdown"), "The strike plays the fall.");
            yield return WaitRealUntil(() => FacilityUI.Current.MessageText == "THE SPACE BETWEEN", 12f);
            Assert.AreEqual("THE SPACE BETWEEN", FacilityUI.Current.MessageText, "The ending follows the fall.");
            Assert.IsTrue(FacilityUI.Current.IsEnding);
        }
    }
}

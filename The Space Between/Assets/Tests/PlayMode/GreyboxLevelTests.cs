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
    /// Plays Greybox_Level_01 with the real Player, using the keyboard and the mouse exactly as a player would:
    /// the cursor is put over a crate and the left button is clicked. One test plays the whole level from the
    /// start; the others check that each beat cannot be skipped and that the rejections teach the right rule.
    /// The positions below match the scene layout, so moving geometry means updating them here.
    /// </summary>
    public class GreyboxLevelTests : InputTestFixture
    {
        private const string SceneName = "Greybox_Level_01";
        private const float StandingHeight = 1.08f;     // Player pivot above the floor it stands on.
        private const float SwapWaitLimitSeconds = 3f;
        private const float ScreenMargin = 0.05f;
        private const float CameraProbeRadius = 0.2f;

        // Floors, and the spots the layout was measured for.
        private const float LowerFloorY = 0f;
        private const float LedgeY = 4.5f;
        private const float UpperFloorY = 12.5f;
        private const float ShelfY = 17f;
        private const float GoalPlatformY = 15f;
        private const float Beat2SwapX = 31f;
        private const float Beat3GlassX = 59.4f;
        private const float WallRunBlockTopY = 14.5f;
        private const float Beat4SwapX = 85.5f;
        private const float CrateEStartX = 91f;
        private const float Beat5ShelfSwapX = 91f;
        private const float Beat5GoalSwapX = 105f;
        private const float GoalX = 120f;

        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject player;
        private PlaceholderPlayerController controller;
        private SwapAbility ability;
        private SwapFeedback feedback;
        private Camera view;

        private float closestToScreenEdge;
        private float closestTargetToScreenEdge;
        private int framesCameraInsideGeometry;
        private readonly System.Text.StringBuilder targetEdges = new System.Text.StringBuilder();

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

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Greybox"));
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
            Assert.IsInstanceOf<SmoothSwapAbility>(ability, "The greybox level is built for the smooth swap.");
            feedback = player.GetComponent<SwapFeedback>();
            Assert.NotNull(feedback, "The Player needs SwapFeedback.");
            view = Camera.main;

            closestToScreenEdge = 1f;
            closestTargetToScreenEdge = 1f;
            framesCameraInsideGeometry = 0;
            targetEdges.Clear();
        }

        private static Swappable Crate(string name)
        {
            GameObject found = GameObject.Find(name);
            Assert.NotNull(found, $"The level needs '{name}'.");
            return found.GetComponent<Swappable>();
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

        // Records how readable the view is: the player inside the screen, and the camera outside all geometry.
        private void Watch()
        {
            closestToScreenEdge = Mathf.Min(closestToScreenEdge, EdgeDistance(player.transform.position));
            if (Physics.CheckSphere(view.transform.position, CameraProbeRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                framesCameraInsideGeometry++;
            }
        }

        private float EdgeDistance(Vector3 world)
        {
            Vector3 v = view.WorldToViewportPoint(world);
            return Mathf.Min(Mathf.Min(v.x, 1f - v.x), Mathf.Min(v.y, 1f - v.y));
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
                    Press(keyboard.spaceKey);
                    yield return null;
                    Release(keyboard.spaceKey);
                }

                lastX = player.transform.position.x;
                checkAt = Time.time + StuckSeconds;
            }

            Release(key);
            Assert.AreNotEqual(startSide, Mathf.Sign(x - player.transform.position.x), $"The player got stuck at x {player.transform.position.x:F2} walking to {x}.");
            yield return WaitSeconds(0.25f);
        }

        // Puts the cursor over the crate for a few frames, then clicks the left mouse button.
        private IEnumerator AimAndClick(Swappable target)
        {
            for (int i = 0; i < 4; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(target.Center));
                yield return null;
            }

            closestTargetToScreenEdge = Mathf.Min(closestTargetToScreenEdge, EdgeDistance(target.Center));
            targetEdges.Append($"{target.name} {EdgeDistance(target.Center):F2}; ");
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

            yield return WaitSeconds(0.3f);
        }

        private void AssertStandingOn(float floorY, string where)
        {
            Assert.AreEqual(floorY + StandingHeight, player.transform.position.y, 0.15f, $"The player should be standing on {where}.");
        }

        //==================== THE WHOLE LEVEL =====================
        [UnityTest]
        public IEnumerator FullPlaythrough_WithMouseAndKeyboard_ReachesTheGoal()
        {
            yield return LoadScene();
            LevelGoal goal = Object.FindFirstObjectByType<LevelGoal>();
            float start = Time.time;

            // Beat 2: step back far enough to see over the ledge edge, then swap up.
            yield return WalkTo(Beat2SwapX);
            yield return AimAndClick(Crate("Crate C (ledge edge)"));
            Assert.AreEqual(SwapRejection.None, ability.LastResult, feedback.LastMessage);
            AssertStandingOn(LedgeY, "the high ledge");

            // Beat 3: the crate on the glass shelf, seen through the glass partition.
            yield return WalkTo(Beat3GlassX);
            yield return AimAndClick(Crate("Crate V (on glass shelf)"));
            Assert.AreEqual(SwapRejection.None, ability.LastResult, feedback.LastMessage);
            Assert.Greater(player.transform.position.x, 60.3f, "The swap should carry the player past the glass.");

            // Beat 4: run up the violet wall, then swap with the crate beyond the tall glass.
            yield return WalkTo(68f);
            Press(keyboard.dKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            float runEnd = Time.time + 4f;
            while (player.transform.position.x < 85f && Time.time < runEnd)
            {
                yield return null;
                Watch();
            }

            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);
            yield return WaitSeconds(0.5f);
            AssertStandingOn(WallRunBlockTopY, "the top of the violet block");
            yield return WalkTo(Beat4SwapX);
            yield return AimAndClick(Crate("Crate E (beyond glass)"));
            Assert.AreEqual(SwapRejection.None, ability.LastResult, feedback.LastMessage);
            AssertStandingOn(UpperFloorY, "the upper floor beyond the glass");

            // Beat 5: up onto the shelf, then down through the glass onto the goal platform.
            yield return WalkTo(Beat5ShelfSwapX);
            yield return AimAndClick(Crate("Crate S (shelf edge)"));
            Assert.AreEqual(SwapRejection.None, ability.LastResult, feedback.LastMessage);
            AssertStandingOn(ShelfY, "the high shelf");

            yield return WalkTo(Beat5GoalSwapX);
            yield return AimAndClick(Crate("Crate G (goal platform)"));
            Assert.AreEqual(SwapRejection.None, ability.LastResult, feedback.LastMessage);
            AssertStandingOn(GoalPlatformY, "the goal platform");

            yield return WalkTo(GoalX);
            Assert.IsTrue(goal.Reached, "Walking onto the green marker should complete the level.");

            Assert.Greater(closestToScreenEdge, ScreenMargin, "The player left the safe area of the screen.");
            Assert.Greater(closestTargetToScreenEdge, ScreenMargin, $"Every crate swapped with should be clearly on screen when it is clicked ({targetEdges}).");
            Assert.AreEqual(0, framesCameraInsideGeometry, "The camera went inside geometry.");
            Assert.Pass($"Completed in {Time.time - start:F1} s of play. Closest the player came to the screen edge: {closestToScreenEdge:F2}. Closest a clicked crate came to the edge: {closestTargetToScreenEdge:F2} ({targetEdges}).");
        }

        //==================== EACH BEAT CANNOT BE SKIPPED =====================
        [UnityTest]
        public IEnumerator Beat2_TooCloseOrTooFar_IsRejected_AndJumpingCannotReachTheLedge()
        {
            yield return LoadScene();
            Swappable crate = Crate("Crate C (ledge edge)");

            yield return Teleport(34.5f, LowerFloorY);
            yield return AimAndClick(crate);
            Assert.AreEqual(SwapRejection.LineOfSightBlocked, ability.LastResult, "Right below the ledge its edge hides the crate.");
            Assert.IsTrue(feedback.IsShowingReason);

            yield return Teleport(27f, LowerFloorY);
            yield return AimAndClick(crate);
            Assert.AreEqual(SwapRejection.NoTargetInRange, ability.LastResult, "From further back the crate is out of range.");

            // Run at the ledge and jump repeatedly.
            yield return Teleport(28f, LowerFloorY);
            Press(keyboard.dKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            float highest = 0f;
            for (int i = 0; i < 6; i++)
            {
                Press(keyboard.spaceKey);
                yield return null;
                Release(keyboard.spaceKey);
                float end = Time.time + 0.5f;
                while (Time.time < end)
                {
                    yield return null;
                    highest = Mathf.Max(highest, player.transform.position.y);
                }
            }

            Assert.Less(highest, LedgeY + StandingHeight - 0.5f, "No jump may reach the top of the ledge.");
        }

        [UnityTest]
        public IEnumerator Beat3_CrateBehindTheSolidWall_IsRejected_AndTheGlassCannotBeCrossed()
        {
            yield return LoadScene();

            yield return Teleport(Beat3GlassX, LedgeY);
            yield return AimAndClick(Crate("Crate H (behind solid wall)"));
            Assert.AreEqual(SwapRejection.LineOfSightBlocked, ability.LastResult, "The low solid wall hides the crate even though it is on screen.");
            Assert.AreEqual(SwapRejectionText.Describe(SwapRejection.LineOfSightBlocked), feedback.LastMessage);

            Press(keyboard.dKey);
            for (int i = 0; i < 4; i++)
            {
                Press(keyboard.spaceKey);
                yield return null;
                Release(keyboard.spaceKey);
                yield return WaitSeconds(0.5f);
            }

            Assert.Less(player.transform.position.x, 60f, "The glass partition must stop the player.");
        }

        [UnityTest]
        public IEnumerator Beat4_FromTheGround_TheCrateBeyondTheGlassIsOutOfRange()
        {
            yield return LoadScene();

            yield return Teleport(79.4f, LedgeY);
            yield return AimAndClick(Crate("Crate E (beyond glass)"));
            Assert.AreNotEqual(SwapRejection.None, ability.LastResult, "From the ground the crate must be unreachable; only the top of the violet block is close enough.");
            Vector3 cratePosition = Crate("Crate E (beyond glass)").transform.position;
            Assert.AreEqual(CrateEStartX, cratePosition.x, 0.01f, "The crate must not have moved.");
        }

        [UnityTest]
        public IEnumerator Beat5_FromTheFloor_TheSolidBarrierHidesTheGoalCrate()
        {
            yield return LoadScene();

            yield return Teleport(107.3f, UpperFloorY);
            yield return AimAndClick(Crate("Crate G (goal platform)"));
            Assert.AreEqual(SwapRejection.LineOfSightBlocked, ability.LastResult, "Below the glass, the solid lower part of the barrier blocks the view.");
        }

        [UnityTest]
        public IEnumerator OnlyTheVioletBlockCanBeWallRun()
        {
            yield return LoadScene();

            WallRunSurface[] surfaces = Object.FindObjectsByType<WallRunSurface>(FindObjectsSortMode.None);
            Assert.AreEqual(1, surfaces.Length, "Wall running is kept to one chosen spot in this level.");
            Assert.AreEqual("Wall Run Block (violet face)", surfaces[0].name);
        }

        //==================== RESET =====================
        [UnityTest]
        public IEnumerator DroppingBackOffTheLedge_ThenPressingR_RestoresBeat2()
        {
            yield return LoadScene();
            Swappable crate = Crate("Crate C (ledge edge)");
            Vector3 crateStart = crate.transform.position;

            yield return WalkTo(Beat2SwapX);
            yield return AimAndClick(crate);
            AssertStandingOn(LedgeY, "the high ledge");

            // Walk back off the ledge: the crate is on the floor now, so beat 2 can no longer be solved.
            yield return WalkTo(33f);
            AssertStandingOn(LowerFloorY, "the floor again");

            Press(keyboard.rKey);
            yield return null;
            Release(keyboard.rKey);
            yield return WaitSeconds(0.3f);

            Assert.AreEqual(crateStart.x, crate.transform.position.x, 0.01f);
            Assert.AreEqual(crateStart.y, crate.transform.position.y, 0.05f);
            Assert.AreEqual(25f, player.transform.position.x, 0.05f, "The player should be back at the room's entry.");
        }
    }
}

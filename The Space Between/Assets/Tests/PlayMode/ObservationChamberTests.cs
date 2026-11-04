using System.Collections;
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
    /// Plays Observation_Chamber with the real Player, using the keyboard and the mouse as a player would. The tests
    /// check that observation mode enters, locks, restores and always lets the player out, and that the room can be
    /// finished through the glass. Whether it looks right is judged in the game view, not here.
    /// The positions below match the scene layout, so moving geometry means updating them here.
    /// </summary>
    public class ObservationChamberTests : InputTestFixture
    {
        private const string SceneName = "Observation_Chamber";
        private const float StandingHeight = 1.08f;
        private const float EntryX = -29f;
        private const float PointX = 2f;
        private const float OutOfRangeX = 7f;
        private const float GlassX = 12f;
        private const float BesideGlassX = 9.5f;
        private const float PlatformTop = 0.9f;
        private const float BlendSeconds = 1f;
        private const float TimeLimitSeconds = 3f;   // Well past the blend, so a slow frame cannot end observing before it is checked.
        private const float SwapWaitLimitSeconds = 3f;
        private const float CameraProbeRadius = 0.2f;

        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject player;
        private PlaceholderPlayerController controller;
        private SwapAbility ability;
        private ObservationPoint point;
        private ObservationCameraController cameraController;
        private CinemachineBrain brain;
        private CinemachineCamera followCamera;
        private CinemachineCamera observationCamera;
        private Camera view;

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

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Observation Chamber"));
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
            point = Object.FindAnyObjectByType<ObservationPoint>();
            cameraController = Object.FindAnyObjectByType<ObservationCameraController>();
            Assert.NotNull(point, "The chamber needs an ObservationPoint.");
            Assert.NotNull(cameraController, "The chamber needs an ObservationCameraController.");
            view = Camera.main;
            brain = view.GetComponent<CinemachineBrain>();
            followCamera = GameObject.Find("CM Follow Camera").GetComponent<CinemachineCamera>();
            observationCamera = cameraController.GetComponent<CinemachineCamera>();
        }

        private IEnumerator Teleport(float x, float floorY = 0f)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = new Vector3(x, floorY + StandingHeight + 0.02f, 0f);
            cc.enabled = true;
            Physics.SyncTransforms();
            followCamera.PreviousStateIsValid = false;
            yield return WaitSeconds(0.4f);
        }

        private IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
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

        private bool ObservationCameraIsLive => ReferenceEquals(brain.ActiveVirtualCamera, observationCamera);

        private bool FollowCameraIsLive => ReferenceEquals(brain.ActiveVirtualCamera, followCamera);

        private IEnumerator EnterObservation()
        {
            yield return Teleport(PointX);
            Assert.IsTrue(point.IsAvailable, "Standing on the viewing plate should make the point available.");
            yield return Tap(keyboard.eKey);
            yield return WaitSeconds(BlendSeconds);
            Assert.IsTrue(point.IsObserving, "Pressing E should enter observation.");
        }

        //==================== PROMPT =====================
        [UnityTest]
        public IEnumerator Prompt_ShowsOnlyInRange()
        {
            yield return LoadScene();

            yield return Teleport(OutOfRangeX);
            string far = point.PromptText;
            yield return Teleport(PointX);
            string near = point.PromptText;
            yield return Teleport(OutOfRangeX);
            string farAgain = point.PromptText;

            Assert.IsEmpty(far, "No prompt away from the point.");
            Assert.AreEqual("E - Observe", near, "The prompt shows within the radius.");
            Assert.IsEmpty(farAgain, "The prompt goes when the player leaves.");
        }

        [UnityTest]
        public IEnumerator PressingEOutOfRange_DoesNothing()
        {
            yield return LoadScene();
            yield return Teleport(OutOfRangeX);
            yield return Tap(keyboard.eKey);
            yield return WaitSeconds(0.3f);

            Assert.IsFalse(point.IsObserving);
            Assert.IsFalse(controller.ControlLocked);
            Assert.IsTrue(FollowCameraIsLive);
        }

        //==================== ENTERING AND LOCKING =====================
        [UnityTest]
        public IEnumerator Entering_MovesTheCamera_AndLocksEveryAction()
        {
            yield return LoadScene();
            yield return EnterObservation();

            Assert.IsTrue(ObservationCameraIsLive, "The observation camera should be live.");
            float anchorDistance = Vector3.Distance(view.transform.position, point.CameraAnchor.position);
            Assert.Less(anchorDistance, 0.1f, "The view should arrive at the camera anchor.");
            Assert.AreEqual("E - Return", cameraController.ExitPromptText, "The way out is shown.");
            Assert.IsEmpty(point.PromptText, "The entry prompt is hidden while observing.");

            // Try everything a player might press while observing.
            Vector3 before = player.transform.position;
            Press(keyboard.dKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            yield return WaitSeconds(0.3f);
            yield return Tap(keyboard.spaceKey);
            Set(mouse.position, (Vector2)view.WorldToScreenPoint(GameObject.Find("Swap Housing").transform.position));
            yield return null;
            yield return Tap(mouse.leftButton);
            yield return WaitSeconds(0.5f);
            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);
            yield return null;

            Vector3 after = player.transform.position;
            string result = $"before {before}, after {after}, grounded={controller.IsGrounded}, swapping={ability.IsSwapping}";
            Assert.AreEqual(before.x, after.x, 0.01f, "Moving must be locked. " + result);
            Assert.AreEqual(before.y, after.y, 0.05f, "Jumping must be locked and the player must not fall. " + result);
            Assert.IsFalse(controller.IsWallRunning, result);
            Assert.IsTrue(controller.IsGrounded, result);
            Assert.IsFalse(ability.enabled, "The swap must be off while observing.");
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator PressingEAgain_ReturnsTheCamera_AndRestoresControl()
        {
            yield return LoadScene();
            yield return EnterObservation();
            Vector3 observedAt = player.transform.position;

            yield return Tap(keyboard.eKey);
            Assert.IsFalse(point.IsObserving, "Pressing E again should leave.");
            Assert.IsTrue(controller.ControlLocked, "Control stays locked until the view is back, so nothing happens unseen.");

            // Pressing again during the blend back must not re-enter.
            yield return Tap(keyboard.eKey);
            Assert.IsFalse(point.IsObserving, "Re-entering during the blend back must be refused.");

            yield return WaitSeconds(BlendSeconds);
            Assert.IsFalse(cameraController.IsBusy);
            Assert.IsFalse(controller.ControlLocked, "Control should be back.");
            Assert.IsTrue(ability.enabled, "The swap should be back.");
            Assert.IsTrue(FollowCameraIsLive, "The gameplay camera should be live again.");
            Assert.AreEqual(observedAt.x, player.transform.position.x, 0.01f, "The player must not be moved by leaving.");
            Assert.IsEmpty(cameraController.ExitPromptText);

            float fromX = player.transform.position.x;
            Press(keyboard.aKey);
            yield return WaitSeconds(0.3f);
            Release(keyboard.aKey);
            yield return null;
            Assert.Less(player.transform.position.x, fromX - 0.5f, "The player should be able to move again.");
        }

        [UnityTest]
        public IEnumerator InTheAir_CannotStartObserving()
        {
            yield return LoadScene();
            yield return Teleport(PointX);
            yield return Tap(keyboard.spaceKey);
            yield return WaitSeconds(0.1f);
            Assert.IsFalse(controller.IsGrounded, "The player should be in the air.");
            yield return Tap(keyboard.eKey);

            Assert.IsFalse(point.IsObserving, "Observing only starts from the ground.");
            Assert.IsFalse(controller.ControlLocked);
        }

        //==================== NEVER TRAPPED =====================
        [UnityTest]
        public IEnumerator TimeLimit_EndsObservation_AndRestoresControl()
        {
            yield return LoadScene();
            typeof(ObservationPoint).GetField("maximumObservationDuration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(point, TimeLimitSeconds);
            yield return EnterObservation();

            yield return WaitSeconds(TimeLimitSeconds - BlendSeconds + 0.5f);
            Assert.IsFalse(point.IsObserving, "The time limit should end observing.");
            yield return WaitSeconds(BlendSeconds);
            Assert.IsFalse(controller.ControlLocked);
            Assert.IsTrue(FollowCameraIsLive);
        }

        [UnityTest]
        public IEnumerator RoomReset_WhileObserving_ReleasesThePlayer()
        {
            yield return LoadScene();
            yield return EnterObservation();

            yield return Tap(keyboard.rKey);
            yield return WaitSeconds(BlendSeconds);

            string result = $"player at {player.transform.position}, observing={point.IsObserving}, locked={controller.ControlLocked}";
            Assert.AreEqual(EntryX, player.transform.position.x, 0.3f, "R still resets the room. " + result);
            Assert.IsFalse(point.IsObserving, "Being moved away ends observing. " + result);
            Assert.IsFalse(controller.ControlLocked, "The player must not stay locked. " + result);
            Assert.IsTrue(FollowCameraIsLive, result);
            Assert.Pass(result);
        }

        //==================== THE ROOM =====================
        [UnityTest]
        public IEnumerator GlassBlocksWalking_AndTheSwapReachesTheExit()
        {
            yield return LoadScene();
            LevelGoal goal = Object.FindAnyObjectByType<LevelGoal>();
            Swappable housing = GameObject.Find("Swap Housing").GetComponent<Swappable>();
            SwapDisplacementEffect effect = player.GetComponent<SwapDisplacementEffect>();
            Assert.NotNull(effect, "The player needs the displacement effect.");

            // Walking into the glass stops the player.
            yield return Teleport(BesideGlassX);
            Press(keyboard.dKey);
            yield return WaitSeconds(1f);
            Release(keyboard.dKey);
            yield return null;
            float stoppedAt = player.transform.position.x;
            Assert.Less(stoppedAt, GlassX, "The glass must block walking.");

            // Aim through the glass and swap.
            yield return Teleport(BesideGlassX);
            Vector3 housingFrom = housing.Position;
            float sizeBefore = player.transform.localScale.x;
            for (int i = 0; i < 3; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(housing.Center));
                yield return null;
            }

            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(SwapRejection.None, ability.LastResult, "The housing behind the glass should be swappable.");
            bool effectPlayed = effect.IsPlaying;
            float smallest = sizeBefore;
            float start = Time.realtimeSinceStartup;
            while (ability.IsSwapping && Time.realtimeSinceStartup - start < SwapWaitLimitSeconds)
            {
                smallest = Mathf.Min(smallest, player.transform.localScale.x);
                effectPlayed |= effect.IsPlaying;
                yield return null;
            }

            Assert.IsTrue(effectPlayed, "The displacement effect should show during the swap.");
            Assert.AreEqual(sizeBefore, smallest, 0.001f, "In this room the bodies keep their size; the effect shows the swap.");
            Assert.Greater(player.transform.position.x, GlassX, "The swap should put the player beyond the glass.");
            Assert.Less(housing.Position.x, GlassX, "The housing should end up on the near side.");
            Assert.AreNotEqual(housingFrom, housing.Position);

            yield return WaitSeconds(0.6f);
            Assert.IsFalse(effect.IsPlaying, "The effect must end with the swap.");
            Assert.IsTrue(controller.IsGrounded, "The player should settle on the platform.");
            Assert.AreEqual(PlatformTop + StandingHeight, player.transform.position.y, 0.15f, "Standing on the platform.");

            // Walk to the bright opening.
            Press(keyboard.dKey);
            float end = Time.time + 4f;
            while (!goal.Reached && Time.time < end)
            {
                yield return null;
            }

            Release(keyboard.dKey);
            yield return null;
            string result = $"stopped by glass at x {stoppedAt:F2}, goal reached={goal.Reached} at {player.transform.position}";
            Assert.IsTrue(goal.Reached, "The exit should be reachable once beyond the glass. " + result);
            Assert.Pass(result);
        }

        [UnityTest]
        public IEnumerator WalkingTheWholeRoom_KeepsTheCameraOutOfGeometry_AndThePlayerInFrame()
        {
            yield return LoadScene();
            yield return Teleport(EntryX);
            int framesInside = 0;
            float closestToEdge = 1f;

            Press(keyboard.dKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            float end = Time.time + 5f;
            while (player.transform.position.x < BesideGlassX && Time.time < end)
            {
                yield return null;
                if (Physics.CheckSphere(view.transform.position, CameraProbeRadius, ~0, QueryTriggerInteraction.Ignore))
                {
                    framesInside++;
                }

                Vector3 v = view.WorldToViewportPoint(player.transform.position);
                closestToEdge = Mathf.Min(closestToEdge, Mathf.Min(Mathf.Min(v.x, 1f - v.x), Mathf.Min(v.y, 1f - v.y)));
            }

            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);
            yield return null;

            string result = $"reached x {player.transform.position.x:F1}, camera frames in geometry {framesInside}, closest to screen edge {closestToEdge:F2}";
            Assert.Greater(player.transform.position.x, BesideGlassX - 1f, "The floor should be clear from the corridor to the glass. " + result);
            Assert.AreEqual(0, framesInside, result);
            Assert.Greater(closestToEdge, 0.05f, "The player should stay in frame. " + result);
            Assert.Pass(result);
        }
    }
}

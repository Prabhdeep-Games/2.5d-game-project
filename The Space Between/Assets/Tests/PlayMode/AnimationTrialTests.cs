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
    /// Plays animation_Trial with the real Player and the Mixamo character, using the keyboard and the mouse as a player
    /// would: the slide, every way out of it, the arm aim, the fist timing, and the whole level. Each test reports its
    /// measurements in the pass message. The positions match the level layout, so moving geometry means updating them here.
    /// </summary>
    public class AnimationTrialTests : InputTestFixture
    {
        private const string SceneName = "animation_Trial";
        private const float StandingOffset = 1.08f;
        private const float BarrierX = 12f;
        private const float HoodStart = 24f;
        private const float HoodEnd = 31f;
        private const float CurveX = 46f;
        private const float LedgeStart = 50f;
        private const float LedgeTop = 14f;
        private const float GlassX = 58f;
        private const float PipeX = 72f;
        private const float PipeFarFace = 75f;
        private const float CapBottom = 12.4f;
        private const float FirstJumpHeight = 8f;
        private const float SecondJumpHeight = 12.2f;
        private const float GestureSeconds = 0.14f;
        private const float SmallestExitBlend = 0.19f;
        private const float StandingHeight = 2f;
        private const float SlidingHeight = 1f;
        private const float SwapWaitLimitSeconds = 3f;
        private const int AimFrames = 3;
        private const float HandOnLineDegrees = 12f;

        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject player;
        private PlaceholderPlayerController controller;
        private CharacterController capsule;
        private SmoothSwapAbility ability;
        private PlayerAnimationDriver driver;
        private SwapArmAim arm;
        private Animator animator;
        private Transform hips;
        private Camera view;
        private readonly HashSet<string> statesSeen = new HashSet<string>();
        private readonly List<string> slideExitBlends = new List<string>();
        private float smallestSlideExitBlend;
        private bool wasSliding;
        private bool awaitingExitBlend;
        private float previousHipHeight;
        private float previousVelocityX;
        private float fastestHipSpeed;
        private float biggestSlideExitVelocityStep;
        private int framesSinceSlideEnded;

        private float X => player.transform.position.x;

        private float Y => player.transform.position.y;

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
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

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Animation Trial"));
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
            capsule = player.GetComponent<CharacterController>();
            ability = player.GetComponent<SmoothSwapAbility>();
            driver = player.GetComponent<PlayerAnimationDriver>();
            arm = player.GetComponentInChildren<SwapArmAim>();
            Assert.NotNull(arm, "The character needs a SwapArmAim.");
            animator = arm.GetComponent<Animator>();
            Assert.IsTrue(animator.isHuman, "The character needs a Humanoid avatar.");
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            view = Camera.main;

            statesSeen.Clear();
            slideExitBlends.Clear();
            smallestSlideExitBlend = float.PositiveInfinity;
            wasSliding = false;
            awaitingExitBlend = false;
            previousHipHeight = HipHeight();
            previousVelocityX = 0f;
            fastestHipSpeed = 0f;
            biggestSlideExitVelocityStep = 0f;
            framesSinceSlideEnded = int.MaxValue;
        }

        private float HipHeight()
        {
            return hips.position.y - player.transform.position.y;
        }

        // Called every frame: the Animator state, and how smooth the way out of a slide is.
        private void Watch()
        {
            statesSeen.Add(driver.CurrentState);

            bool sliding = controller.IsSliding;
            if (wasSliding && !sliding)
            {
                awaitingExitBlend = true;
                framesSinceSlideEnded = 0;
            }
            else if (framesSinceSlideEnded < int.MaxValue)
            {
                framesSinceSlideEnded++;
            }

            wasSliding = sliding;

            // The Animator picks its transition on the frame after the parameter changes.
            if (awaitingExitBlend && animator.IsInTransition(0) && framesSinceSlideEnded >= 1)
            {
                AnimatorTransitionInfo info = animator.GetAnimatorTransitionInfo(0);
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                slideExitBlends.Add($"{NameOf(next)} {info.duration:F2} s");
                smallestSlideExitBlend = Mathf.Min(smallestSlideExitBlend, info.duration);
                awaitingExitBlend = false;
            }

            float velocityX = capsule.enabled ? capsule.velocity.x : previousVelocityX;
            if (framesSinceSlideEnded <= 3)
            {
                biggestSlideExitVelocityStep = Mathf.Max(biggestSlideExitVelocityStep, Mathf.Abs(velocityX - previousVelocityX));
            }

            previousVelocityX = velocityX;

            // How fast the hips move inside the model, from the moment a slide starts until shortly after it ends.
            float height = HipHeight();
            if (Time.deltaTime > 0f && (sliding || framesSinceSlideEnded <= 30))
            {
                fastestHipSpeed = Mathf.Max(fastestHipSpeed, Mathf.Abs(height - previousHipHeight) / Time.deltaTime);
            }

            previousHipHeight = height;
        }

        private static string NameOf(AnimatorStateInfo info)
        {
            foreach (string state in new[] { "Locomotion", "Jump", "Fall", "Land", "Slide", "WallRun", "Traverse", "WallJump" })
            {
                if (info.shortNameHash == Animator.StringToHash(state))
                {
                    return state;
                }
            }

            return "other";
        }

        private IEnumerator Teleport(float x, float floorY, float settleSeconds = 0.4f)
        {
            capsule.enabled = false;
            player.transform.position = new Vector3(x, floorY + StandingOffset, player.transform.position.z);
            capsule.enabled = true;
            Physics.SyncTransforms();
            yield return WaitSeconds(settleSeconds);
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

        private IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        private IEnumerator Turn(KeyControl from, KeyControl to)
        {
            Release(from);
            yield return null;
            Press(to);
            yield return null;
        }

        private IEnumerator AimAt(Swappable target)
        {
            for (int i = 0; i < AimFrames; i++)
            {
                Set(mouse.position, (Vector2)view.WorldToScreenPoint(target.Center));
                yield return null;
                Watch();
            }
        }

        private IEnumerator Click()
        {
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
        }

        private static T Find<T>(string name) where T : Component
        {
            GameObject found = GameObject.Find(name);
            Assert.NotNull(found, $"The scene needs '{name}'.");
            T component = found.GetComponent<T>();
            Assert.NotNull(component, $"'{name}' needs a {typeof(T).Name}.");
            return component;
        }

        private bool StandingOn(float floorY)
        {
            return controller.IsGrounded && Mathf.Abs(Y - (floorY + StandingOffset)) < 0.15f;
        }

        // Runs up to the given x and starts a slide there.
        private IEnumerator RunAndSlideAt(float slideX)
        {
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= slideX, 5f);
            Press(keyboard.cKey);
            yield return null;
            Watch();
            Release(keyboard.cKey);
            yield return null;
            Watch();
        }

        //==================== SLIDE =====================
        [UnityTest]
        public IEnumerator PressingSlideFromAStandstill_DoesNothing()
        {
            yield return LoadScene();
            yield return Teleport(2f, 0f);

            yield return Tap(keyboard.cKey);
            yield return WaitSeconds(0.1f);

            Assert.IsFalse(controller.IsSliding, "Standing still should not start a slide.");
            Assert.AreEqual(StandingHeight, capsule.height, 0.01f);
        }

        [UnityTest]
        public IEnumerator Slide_ShrinksTheCapsule_KeepsTheFeet_ThenFadesAndStandsUp()
        {
            yield return LoadScene();
            yield return Teleport(14f, 0f);
            float feetBefore = capsule.bounds.min.y;

            yield return RunAndSlideAt(18f);
            Assert.IsTrue(controller.IsSliding, "Pressing C while running should start a slide.");
            Assert.AreEqual(SlidingHeight, capsule.height, 0.01f, "The capsule should shrink.");
            Assert.AreEqual(feetBefore, capsule.bounds.min.y, 0.05f, "The feet should stay on the floor.");
            yield return ReleaseAll();

            float startX = X;
            float startedAt = Time.time;
            yield return WaitUntil(() => !controller.IsSliding, 3f);
            float seconds = Time.time - startedAt;

            Assert.IsFalse(controller.IsSliding, "The slide should end by itself.");
            Assert.AreEqual(StandingHeight, capsule.height, 0.01f, "The capsule should be back to full height.");
            Assert.Greater(X - startX, 2f, "The slide should carry the player forward.");
            Assert.Pass($"slid {X - startX:F1} units in {seconds:F2} s, exits {string.Join(", ", slideExitBlends)}");
        }

        [UnityTest]
        public IEnumerator HoodBlocksAStandingPlayer_ButASlideGoesUnderIt()
        {
            yield return LoadScene();
            yield return Teleport(17f, 0f);

            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => X >= HoodStart, 2f);
            yield return WaitSeconds(0.3f);
            Assert.Less(X, HoodStart, "A standing player should not fit under the hood.");
            yield return ReleaseAll();

            yield return Teleport(14f, 0f);
            yield return RunAndSlideAt(HoodStart - 3f);
            yield return WaitUntil(() => X > HoodEnd + 1f, 4f);
            Assert.Greater(X, HoodEnd, "A slide should pass under the hood.");
            yield return ReleaseAll();
        }

        [UnityTest]
        public IEnumerator UnderTheHood_TheSlideCannotEnd_JumpIsIgnored_AndTheKeysChooseTheWayOut()
        {
            yield return LoadScene();
            yield return Teleport(14f, 0f);
            yield return RunAndSlideAt(HoodStart - 1.5f);
            yield return ReleaseAll();

            // A slide from here runs out of speed well before the far end, so it must keep going at its end speed.
            yield return WaitUntil(() => X > HoodStart + 1f, 2f);
            yield return Tap(keyboard.spaceKey);
            yield return null;
            Assert.IsTrue(controller.IsSliding, "Jump should be ignored while there is no room to stand.");
            Assert.AreEqual(SlidingHeight, capsule.height, 0.01f);

            yield return WaitUntil(() => X > HoodEnd + 0.8f || !controller.IsSliding, 6f);
            Assert.Greater(X, HoodEnd, "With no keys held the slide should carry on out from under the hood.");
            yield return WaitUntil(() => !controller.IsSliding, 1f);
            Assert.IsFalse(controller.IsSliding, "The player should stand up after the hood.");
            Assert.AreEqual(StandingHeight, capsule.height, 0.01f);
        }

        [UnityTest]
        public IEnumerator SlideIntoRun_DoesNotSnapTheSpeed()
        {
            yield return LoadScene();
            yield return Teleport(2f, 0f);
            yield return RunAndSlideAt(6f);
            yield return WaitUntil(() => !controller.IsSliding, 3f);
            yield return WaitSeconds(0.1f);

            // Full run speed is 10, and the slide ends at 3. A snap would be a step of about 7 in one frame.
            Assert.Less(biggestSlideExitVelocityStep, 2f, "Leaving a slide should not snap the speed.");
            Assert.Pass($"biggest speed step in one frame leaving the slide: {biggestSlideExitVelocityStep:F2} u/s");
        }

        [UnityTest]
        public IEnumerator SlideIntoJump_KeepsTheMomentum()
        {
            yield return LoadScene();
            yield return Teleport(14f, 0f);
            yield return RunAndSlideAt(16f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.1f);
            Assert.IsTrue(controller.IsSliding);

            float speedBefore = Mathf.Abs(capsule.velocity.x);
            yield return Tap(keyboard.spaceKey);
            yield return null;

            Assert.IsFalse(controller.IsSliding, "Jump should end the slide when there is room.");
            Assert.Greater(Y, StandingOffset, "The player should be rising.");
            Assert.Greater(Mathf.Abs(capsule.velocity.x), speedBefore * 0.7f, "The jump should keep the slide's speed.");
        }

        [UnityTest]
        public IEnumerator SlideIntoTheCurve_WithRunHeld_BecomesAWallRun()
        {
            yield return LoadScene();
            yield return Teleport(CurveX - 12f, 0f);
            yield return RunAndSlideAt(CurveX - 3f);
            Assert.IsTrue(controller.IsSliding, "The slide should have started.");

            yield return WaitUntil(() => controller.IsWallRunning, 2f);
            Assert.IsTrue(controller.IsWallRunning, "Sliding into the curve with run held should start a wall run.");
            Assert.AreEqual(StandingHeight, capsule.height, 0.01f, "A wall run needs the full capsule.");

            yield return WaitUntil(() => StandingOn(LedgeTop), 3f);
            Assert.IsTrue(StandingOn(LedgeTop), $"The run should reach the ledge. At {player.transform.position}");
            yield return ReleaseAll();
        }

        [UnityTest]
        public IEnumerator SlidePressedJustBeforeLanding_StartsOnLanding()
        {
            yield return LoadScene();
            yield return Teleport(14f, 0f);
            yield return HoldRun(keyboard.dKey);
            yield return WaitSeconds(0.2f);
            yield return Tap(keyboard.spaceKey);
            yield return WaitUntil(() => !controller.IsGrounded, 0.5f);
            yield return WaitUntil(() => Y < StandingOffset + 0.6f && capsule.velocity.y < 0f, 1f);

            yield return Tap(keyboard.cKey);
            yield return WaitUntil(() => controller.IsGrounded, 0.5f);
            yield return WaitSeconds(0.05f);

            Assert.IsTrue(controller.IsSliding, "A slide pressed just before landing should start on landing.");
            yield return ReleaseAll();
        }

        [UnityTest]
        public IEnumerator EveryWayOutOfASlide_BlendsSmoothly()
        {
            yield return LoadScene();

            // To standing.
            yield return Teleport(14f, 0f);
            yield return RunAndSlideAt(16f);
            yield return ReleaseAll();
            yield return WaitUntil(() => !controller.IsSliding, 3f);
            yield return WaitSeconds(0.2f);
            yield return null;

            // To running.
            yield return Teleport(14f, 0f);
            yield return RunAndSlideAt(16f);
            yield return WaitUntil(() => !controller.IsSliding, 3f);
            yield return WaitSeconds(0.2f);
            yield return ReleaseAll();

            // To a fall, off the far edge of the ledge into the half-pipe.
            yield return Teleport(64f, LedgeTop);
            yield return RunAndSlideAt(66f);
            yield return WaitUntil(() => !controller.IsSliding, 3f);
            yield return WaitSeconds(0.2f);
            yield return ReleaseAll();

            Assert.GreaterOrEqual(slideExitBlends.Count, 3, $"Each exit should have blended. Saw: {string.Join(", ", slideExitBlends)}");
            Assert.GreaterOrEqual(smallestSlideExitBlend, SmallestExitBlend, $"Every exit should blend gently. Saw: {string.Join(", ", slideExitBlends)}");

            // Under this the hips travel the full depth of the slide in about a tenth of a second, which reads as a pop.
            Assert.Less(fastestHipSpeed, 7f, "The hips should never jump.");
            Assert.Pass($"exits: {string.Join(", ", slideExitBlends)}; fastest hips {fastestHipSpeed:F1} u/s");
        }

        //==================== THE ARM AND THE FIST =====================
        [UnityTest]
        public IEnumerator Arm_PointsAtTheTarget_AndDropsWhenAimedAway()
        {
            yield return LoadScene();
            yield return Teleport(GlassX - 3f, LedgeTop);
            Swappable crate = Find<Swappable>("Swap Crate");

            yield return AimAt(crate);
            yield return WaitSeconds(0.35f);
            yield return AimAt(crate);

            Assert.AreEqual(crate, ability.Target, "The crate should be the aimed target.");
            Assert.AreEqual(1f, arm.ArmWeight, 0.01f, "The arm should be fully raised.");

            Vector3 armLine = arm.Hand.position - arm.UpperArm.position;
            Vector3 targetLine = crate.Center - arm.UpperArm.position;
            float offBy = Vector3.Angle(armLine, targetLine);
            Assert.Less(offBy, HandOnLineDegrees, "The right arm should point at the crate.");

            Set(mouse.position, (Vector2)view.WorldToScreenPoint(new Vector3(GlassX - 3f, LedgeTop + 20f, 0f)));
            yield return WaitSeconds(0.5f);
            Assert.IsNull(ability.Target, "Aiming at nothing should clear the target.");
            Assert.AreEqual(0f, arm.ArmWeight, 0.01f, "The arm should drop.");
            Assert.Pass($"arm was {offBy:F1} degrees off the line to the crate");
        }

        [UnityTest]
        public IEnumerator Swap_HappensWhenTheFistFinishesClosing()
        {
            yield return LoadScene();
            yield return Teleport(GlassX - 3f, LedgeTop);
            Swappable crate = Find<Swappable>("Swap Crate");
            Vector3 startedAt = player.transform.position;
            float fistAtSwap = -1f;
            float gestureMeasured = -1f;
            float pressedAt = -1f;
            ability.Swapped += _ =>
            {
                fistAtSwap = ability.GestureProgress;
                gestureMeasured = Time.time - pressedAt;
            };

            yield return AimAt(crate);
            yield return WaitSeconds(0.25f);
            yield return AimAt(crate);
            Assert.AreEqual(0f, arm.FistWeight, 0.01f, "The hand should be open while only aiming.");

            pressedAt = Time.time;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);

            Assert.IsTrue(ability.IsGesturing, "A valid press should start the gesture.");
            Assert.AreEqual(startedAt.x, player.transform.position.x, 0.01f, "The swap should wait for the fist.");

            float fistMidway = -1f;
            float realStart = Time.realtimeSinceStartup;
            while (ability.IsGesturing && Time.realtimeSinceStartup - realStart < SwapWaitLimitSeconds)
            {
                yield return null;
                if (ability.GestureProgress > 0.2f && ability.GestureProgress < 0.8f && fistMidway < 0f)
                {
                    fistMidway = arm.FistWeight;
                }
            }

            Assert.AreNotEqual(-1f, gestureMeasured, "The swap should have happened.");
            Assert.AreEqual(GestureSeconds, gestureMeasured, 0.06f, "The swap should come when the gesture time is up.");
            yield return null;
            Assert.AreEqual(1f, arm.FistWeight, 0.01f, "The fist should be fully closed as the swap plays.");
            Assert.AreEqual(1f, animator.GetLayerWeight(animator.GetLayerIndex("Right Hand Fist")), 0.01f, "The fist layer should follow.");

            realStart = Time.realtimeSinceStartup;
            while (ability.IsSwapping && Time.realtimeSinceStartup - realStart < SwapWaitLimitSeconds)
            {
                yield return null;
            }

            Assert.Greater(player.transform.position.x, GlassX, "The player should have crossed the glass.");
            yield return WaitSeconds(0.4f);
            Assert.AreEqual(0f, arm.FistWeight, 0.01f, "The hand should open after the swap.");
            Assert.Pass($"fist mid-gesture {fistMidway:F2}, swap {gestureMeasured:F3} s after the press");
        }

        [UnityTest]
        public IEnumerator PressWithNoTarget_IsRejectedAtOnce_WithoutAGesture()
        {
            yield return LoadScene();
            yield return Teleport(10f, 0f);
            Set(mouse.position, (Vector2)view.WorldToScreenPoint(new Vector3(5f, 20f, 0f)));
            yield return null;
            yield return null;

            bool rejected = false;
            ability.SwapRejected += _ => rejected = true;
            yield return Click();

            Assert.IsTrue(rejected, "A press with no target should be rejected straight away.");
            Assert.IsFalse(ability.IsGesturing);
            Assert.AreEqual(0f, arm.FistWeight, 0.01f, "The hand should not close on nothing.");
        }

        [UnityTest]
        public IEnumerator TargetLeavingRangeDuringTheGesture_RejectsTheSwap()
        {
            yield return LoadScene();
            yield return Teleport(GlassX - 3f, LedgeTop);
            Swappable crate = Find<Swappable>("Swap Crate");
            yield return AimAt(crate);

            bool swapped = false;
            ability.Swapped += _ => swapped = true;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            Assert.IsTrue(ability.IsGesturing);

            crate.MoveTo(crate.Position + Vector3.right * 30f);
            Physics.SyncTransforms();
            yield return WaitSeconds(GestureSeconds + 0.1f);

            Assert.IsFalse(swapped, "The swap should be rejected when the target has gone.");
            Assert.AreEqual(SwapRejection.NoTargetInRange, ability.LastResult);
            Assert.Less(X, GlassX, "The player should stay put.");
        }

        //==================== THE WHOLE LEVEL =====================
        [UnityTest]
        public IEnumerator FullLevel_WithKeyboardAndMouse_PlaysEveryAnimation_AndReachesTheGoal()
        {
            yield return LoadScene();
            LevelGoal goal = Find<LevelGoal>("Trial Goal");
            Swappable crate = Find<Swappable>("Swap Crate");
            float start = Time.time;
            StringBuilder report = new StringBuilder();

            // Stand, walk, run, and jump the barrier.
            yield return WaitSeconds(0.4f);
            Press(keyboard.dKey);
            yield return WaitSeconds(0.5f);
            Press(keyboard.leftShiftKey);
            yield return WaitUntil(() => X >= BarrierX - 2.2f, 4f);
            yield return Tap(keyboard.spaceKey);
            yield return WaitUntil(() => X > BarrierX + 1f && controller.IsGrounded, 3f);
            Assert.Greater(X, BarrierX, $"The player should clear the barrier. At {player.transform.position}");
            report.Append($"barrier {Time.time - start:F1} s; ");

            // Slide under the hood.
            yield return WaitUntil(() => X >= HoodStart - 3f, 3f);
            yield return Tap(keyboard.cKey);
            Assert.IsTrue(controller.IsSliding, "The slide should start before the hood.");
            yield return WaitUntil(() => X > HoodEnd + 1f, 4f);
            Assert.Greater(X, HoodEnd, "The slide should pass the hood.");
            report.Append($"hood {Time.time - start:F1} s; ");

            // Run into the curve and up onto the ledge.
            yield return WaitUntil(() => controller.IsWallRunning, 4f);
            Assert.IsTrue(controller.IsWallRunning, $"The curve should start a wall run. At {player.transform.position}");
            yield return WaitUntil(() => StandingOn(LedgeTop) && X > LedgeStart + 1f, 4f);
            Assert.IsTrue(StandingOn(LedgeTop), $"The wall run should reach the ledge. At {player.transform.position}");
            report.Append($"ledge {Time.time - start:F1} s; ");

            // Stop at the glass, aim, close the fist, swap across.
            yield return WaitUntil(() => X >= GlassX - 4f, 2f);
            yield return ReleaseAll();
            yield return WaitSeconds(0.3f);
            yield return AimAt(crate);
            yield return WaitSeconds(0.25f);
            yield return AimAt(crate);
            yield return Click();
            float realStart = Time.realtimeSinceStartup;
            while ((ability.IsGesturing || ability.IsSwapping) && Time.realtimeSinceStartup - realStart < SwapWaitLimitSeconds)
            {
                yield return null;
            }

            Assert.Greater(X, GlassX, $"The swap should cross the glass. At {player.transform.position}");
            report.Append($"swap {Time.time - start:F1} s; ");

            // Drop into the half-pipe, land hard, wall jump out and over.
            Set(mouse.position, new Vector2(0f, 0f));
            yield return HoldRun(keyboard.dKey);
            yield return WaitUntil(() => controller.IsWallRunning && X > PipeX, 5f);
            Assert.IsTrue(controller.IsWallRunning && X > PipeX, $"The player should run up the far wall. At {player.transform.position}");
            yield return WaitUntil(() => Y > FirstJumpHeight, 2f);
            yield return Tap(keyboard.spaceKey);
            yield return Turn(keyboard.dKey, keyboard.aKey);
            yield return WaitUntil(() => controller.IsWallRunning && X < PipeX, 1.5f);
            Assert.IsTrue(controller.IsWallRunning && X < PipeX, $"The wall jump should reach the near wall. At {player.transform.position}");
            yield return WaitUntil(() => Y > SecondJumpHeight, 1.5f);
            yield return Tap(keyboard.spaceKey);
            yield return Turn(keyboard.aKey, keyboard.dKey);
            yield return WaitUntil(() => (controller.IsWallRunning && X > PipeX && Y > CapBottom) || X > PipeFarFace + 1f, 1.5f);
            yield return WaitUntil(() => goal.Reached, 5f);

            report.Append($"goal {Time.time - start:F1} s; states seen: {string.Join(", ", statesSeen)}; slide exits: {string.Join(", ", slideExitBlends)}");
            Assert.IsTrue(goal.Reached, $"The level should end at the goal. At {player.transform.position}. {report}");
            yield return ReleaseAll();

            foreach (string state in new[] { "Locomotion", "Jump", "Fall", "Land", "Slide", "WallRun", "Traverse", "WallJump" })
            {
                Assert.IsTrue(statesSeen.Contains(state), $"The Animator should have played {state}. {report}");
            }

            Assert.Pass(report.ToString());
        }
    }
}

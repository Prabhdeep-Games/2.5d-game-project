using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Checks that running, jumping, wall running, landing and swapping work together on one player
    /// built like the real one (Character Controller, capsule, Swappable, controller and a swap ability).
    /// Each test watches every frame that the capsule never ends up inside solid geometry.
    /// </summary>
    public class MovementSwapIntegrationTests : InputTestFixture
    {
        private const float FloorCenterY = 1.08f;
        private const float RunSpeed = 10f;
        private const float WallX = 4.5f;
        private const float WallHeight = 14f;
        private const float SwapWaitLimitSeconds = 3f;
        private const float OverlapSkin = 0.06f;

        private Keyboard keyboard;
        private GameObject root;
        private GameObject playerObject;
        private PlaceholderPlayerController controller;
        private CharacterController characterController;
        private SwapAbility ability;
        private string clipping;

        public override void Setup()
        {
            base.Setup();

            // Held keys must survive the editor window not having focus while the tests run.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();

            root = new GameObject("Integration Test Rig");
            Block("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 10f));
            clipping = null;
        }

        public override void TearDown()
        {
            // A failed smooth swap must never leave game time stopped for the next test.
            Time.timeScale = 1f;
            Object.DestroyImmediate(root);
            base.TearDown();
        }

        //==================== RIG =====================
        private GameObject Block(string name, Vector3 center, Vector3 size, bool runnable = false)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(root.transform, false);
            block.transform.position = center;
            block.transform.localScale = size;
            if (runnable)
            {
                block.AddComponent<WallRunSurface>();
            }

            Physics.SyncTransforms();
            return block;
        }

        private void BuildMarkedWall()
        {
            Block("Marked Wall", new Vector3(WallX, WallHeight * 0.5f, 0f), new Vector3(1f, WallHeight, 6f), runnable: true);
        }

        // A one unit crate. Without a Rigidbody it hangs where it is put, which keeps swap tests exact.
        private Swappable Crate(Vector3 center, bool falls = false)
        {
            GameObject crate = Block("Crate", center, Vector3.one);
            if (falls)
            {
                crate.AddComponent<Rigidbody>();
            }

            return crate.AddComponent<Swappable>();
        }

        private void BuildPlayer(Vector3 position, bool smoothSwap)
        {
            playerObject = new GameObject("Integration Player");
            playerObject.SetActive(false);
            playerObject.transform.SetParent(root.transform, false);
            playerObject.transform.position = position;

            characterController = playerObject.AddComponent<CharacterController>();
            characterController.height = 2f;
            characterController.radius = 0.5f;
            characterController.slopeLimit = 45f;
            CapsuleCollider capsule = playerObject.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.5f;
            playerObject.AddComponent<Swappable>();

            controller = playerObject.AddComponent<PlaceholderPlayerController>();
            InputAction move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            SetField(controller, "moveAction", move);
            SetField(controller, "jumpAction", new InputAction("Jump", InputActionType.Button, "<Keyboard>/space"));
            SetField(controller, "runAction", new InputAction("Run", InputActionType.Button, "<Keyboard>/leftShift"));
            SetField(controller, "runSpeed", RunSpeed);

            // Nearest keeps target choice out of these tests; aiming has its own tests.
            ability = smoothSwap ? playerObject.AddComponent<SmoothSwapAbility>() : playerObject.AddComponent<SwapAbility>();
            SetField(ability, "aimMode", SwapAimMode.Nearest);

            playerObject.SetActive(true);
            Physics.SyncTransforms();
        }

        private static void SetField(object target, string fieldName, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(fieldName, flags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"{target.GetType().Name} has no field named '{fieldName}'.");
        }

        private float VerticalSpeed()
        {
            FieldInfo field = typeof(PlaceholderPlayerController).GetField("verticalSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
            return (float)field.GetValue(controller);
        }

        //==================== WATCHING =====================
        // Remembers the first frame in which the capsule overlaps something solid that is not the player.
        private void CheckForClipping()
        {
            if (clipping != null || playerObject == null || !characterController.enabled)
            {
                return;
            }

            Vector3 center = playerObject.transform.position + characterController.center;
            float half = characterController.height * 0.5f - characterController.radius;
            float radius = characterController.radius - OverlapSkin;
            Collider[] hits = Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                if (!hit.transform.IsChildOf(playerObject.transform))
                {
                    clipping = $"{hit.name} at player position {playerObject.transform.position}";
                    return;
                }
            }
        }

        private IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
                CheckForClipping();
            }
        }

        // Game time stops during a smooth swap, so this waits in real time.
        private IEnumerator WaitForSwapToEnd()
        {
            float start = Time.realtimeSinceStartup;
            while (ability.IsSwapping && Time.realtimeSinceStartup - start < SwapWaitLimitSeconds)
            {
                yield return null;
            }

            Assert.IsFalse(ability.IsSwapping, "The swap should have finished.");
        }

        private IEnumerator WaitForWallRun()
        {
            float end = Time.time + 1.5f;
            while (!controller.IsWallRunning && Time.time < end)
            {
                yield return null;
                CheckForClipping();
            }

            Assert.IsTrue(controller.IsWallRunning, "A wall run should have started.");
        }

        // Two keys changed in the same frame can cancel each other in the test keyboard, so they change a frame apart.
        private IEnumerator HoldRunRight()
        {
            Press(keyboard.dKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            yield return null;
        }

        private IEnumerator TapJump()
        {
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            yield return null;
        }

        private void AssertNeverClipped()
        {
            Assert.IsNull(clipping, $"The player's capsule ended up inside {clipping}.");
        }

        //==================== MOVEMENT TRANSITIONS =====================
        [UnityTest]
        public IEnumerator RunJumpLand_KeepsTheRunSpeedWithoutAStall()
        {
            BuildPlayer(new Vector3(-30f, 1.001f, 0f), smoothSwap: false);
            yield return null;
            yield return HoldRunRight();
            yield return WaitSeconds(0.4f);

            yield return TapJump();
            float slowest = float.MaxValue;
            bool leftTheGround = false;
            float lastX = playerObject.transform.position.x;
            float end = Time.time + 1.4f;
            while (Time.time < end)
            {
                yield return null;
                CheckForClipping();
                leftTheGround |= playerObject.transform.position.y > FloorCenterY + 1f;
                float speed = (playerObject.transform.position.x - lastX) / Time.deltaTime;
                lastX = playerObject.transform.position.x;
                slowest = Mathf.Min(slowest, speed);
            }

            Assert.IsTrue(leftTheGround, "The jump should lift the player off the floor.");
            Assert.AreEqual(FloorCenterY, playerObject.transform.position.y, 0.15f, "The player should be back on the floor.");
            Assert.Greater(slowest, RunSpeed * 0.9f, "Running speed should hold through the take off, the air and the landing.");
            AssertNeverClipped();
        }

        [UnityTest]
        public IEnumerator WallRunIntoACeiling_DropsThePlayerInsteadOfPinningThem()
        {
            BuildMarkedWall();
            Block("Ceiling", new Vector3(-6f, 6.5f, 0f), new Vector3(20f, 1f, 6f));
            BuildPlayer(new Vector3(-2f, 1.001f, 0f), smoothSwap: false);
            yield return null;

            yield return HoldRunRight();
            yield return WaitForWallRun();
            yield return WaitSeconds(1.5f);

            Assert.IsFalse(controller.IsWallRunning, "Running into the ceiling should end the run, not hold the player against it.");
            Assert.AreEqual(FloorCenterY, playerObject.transform.position.y, 0.2f, "The player should have dropped back to the floor.");
            AssertNeverClipped();
        }

        //==================== SWAPPING WHILE MOVING =====================
        [UnityTest]
        public IEnumerator InstantSwapWhileFalling_StopsTheFallAtTheNewSpot()
        {
            BuildPlayer(new Vector3(0f, 8f, 0f), smoothSwap: false);
            Crate(new Vector3(5f, 6f, 0f));
            yield return WaitSeconds(0.2f);
            Assert.Less(VerticalSpeed(), -3f, "The player should be falling before the swap.");

            Assert.IsTrue(ability.TrySwap());
            Assert.AreEqual(0f, VerticalSpeed(), 0.001f, "An instant swap removes all momentum.");
            Assert.AreEqual(5f, playerObject.transform.position.x, 0.01f);
            AssertNeverClipped();
        }

        [UnityTest]
        public IEnumerator SmoothSwapWhileFalling_KeepsTheFallAndGivesAnAirJump()
        {
            BuildPlayer(new Vector3(0f, 8f, 0f), smoothSwap: true);
            Crate(new Vector3(5f, 6f, 0f));
            yield return WaitSeconds(0.2f);
            float fallBefore = VerticalSpeed();
            Assert.Less(fallBefore, -3f, "The player should be falling before the swap.");

            Assert.IsTrue(ability.TrySwap());
            yield return WaitForSwapToEnd();

            Assert.AreEqual(5f, playerObject.transform.position.x, 0.01f);
            Assert.AreEqual(fallBefore, VerticalSpeed(), 1f, "A smooth swap keeps the fall.");

            float heightBeforeJump = playerObject.transform.position.y;
            yield return TapJump();
            yield return WaitSeconds(0.15f);
            Assert.Greater(playerObject.transform.position.y, heightBeforeJump, "The extra air jump should lift the player.");
            AssertNeverClipped();
        }

        [UnityTest]
        public IEnumerator SmoothSwapDuringAWallRun_EndsTheRunAndLandsSafely()
        {
            BuildMarkedWall();
            Swappable crate = Crate(new Vector3(-2f, 4f, 0f));
            BuildPlayer(new Vector3(-1f, 1.001f, 0f), smoothSwap: true);
            yield return null;

            yield return HoldRunRight();
            yield return WaitForWallRun();
            yield return WaitSeconds(0.15f);

            Assert.IsTrue(ability.TrySwap());
            yield return WaitForSwapToEnd();
            yield return WaitSeconds(0.05f);

            Assert.IsFalse(controller.IsWallRunning, "Away from the wall the run must end.");
            Assert.Less(playerObject.transform.position.x, -1f, "The player should be where the crate was.");
            Assert.Greater(crate.transform.position.x, 3f, "The crate should be where the player was, beside the wall.");

            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);
            yield return WaitSeconds(1.5f);
            Assert.AreEqual(FloorCenterY, playerObject.transform.position.y, 0.2f, "The player should land on the floor.");
            AssertNeverClipped();
        }

        [UnityTest]
        public IEnumerator SmoothSwapDuringAWallJump_GivesMovementControlBack()
        {
            BuildMarkedWall();
            Crate(new Vector3(-2f, 6f, 0f));
            BuildPlayer(new Vector3(-1f, 1.001f, 0f), smoothSwap: true);
            yield return null;

            yield return HoldRunRight();
            yield return WaitForWallRun();
            yield return WaitSeconds(0.2f);
            yield return TapJump();
            Assert.IsTrue(controller.IsInWallJump);

            Assert.IsTrue(ability.TrySwap());
            yield return WaitForSwapToEnd();

            // The controller notices the swap on its first update afterwards, before it moves again.
            yield return null;
            Assert.IsFalse(controller.IsInWallJump, "After a swap the player is somewhere else, so the wall jump is over.");

            // D is still held: with control back, the player should turn toward it within a short time.
            float xAfterSwap = playerObject.transform.position.x;
            yield return WaitSeconds(0.6f);
            Assert.Greater(playerObject.transform.position.x, xAfterSwap, "Holding D should move the player right again.");
            AssertNeverClipped();
        }

        [UnityTest]
        public IEnumerator InstantSwapDuringAWallRun_EndsTheRunWithNoMomentum()
        {
            BuildMarkedWall();
            Crate(new Vector3(-2f, 4f, 0f));
            BuildPlayer(new Vector3(-1f, 1.001f, 0f), smoothSwap: false);
            yield return null;

            yield return HoldRunRight();
            yield return WaitForWallRun();
            yield return WaitSeconds(0.15f);

            Assert.IsTrue(ability.TrySwap());
            Assert.IsFalse(controller.IsWallRunning);
            Assert.AreEqual(0f, VerticalSpeed(), 0.001f);
            yield return WaitSeconds(0.1f);
            Assert.IsFalse(controller.IsWallRunning, "The run must not restart away from the wall.");
            AssertNeverClipped();
        }

        //==================== SWAP EDGE CASES =====================
        [UnityTest]
        public IEnumerator FallingTarget_IsSwappedFromWhereItIsAtThatMoment()
        {
            BuildPlayer(new Vector3(0f, 1.001f, 0f), smoothSwap: false);
            Swappable crate = Crate(new Vector3(5f, 7f, 0f), falls: true);
            yield return WaitSeconds(0.4f);

            Vector3 crateAnchor = crate.Anchor;
            Assert.Less(crateAnchor.y, 6f, "The crate should have fallen a little before the swap.");
            Assert.IsTrue(ability.TrySwap());

            Vector3 feet = playerObject.GetComponent<Swappable>().Anchor;
            Assert.AreEqual(crateAnchor.x, feet.x, 0.01f);
            Assert.AreEqual(crateAnchor.y, feet.y, 0.01f, "The player's feet go where the crate's bottom was at the moment of the swap.");
            yield return WaitSeconds(0.1f);
            AssertNeverClipped();
        }

        [UnityTest]
        public IEnumerator StandingOnTheTarget_IsRejectedAndNothingMoves()
        {
            Swappable crate = Crate(new Vector3(0f, 0.5f, 0f));
            BuildPlayer(new Vector3(0f, 2.05f, 0f), smoothSwap: false);
            yield return WaitSeconds(0.2f);
            Vector3 playerBefore = playerObject.transform.position;

            Assert.IsFalse(ability.TrySwap());
            Assert.AreEqual(SwapRejection.DestinationBlocked, ability.LastResult);
            Assert.AreEqual(playerBefore, playerObject.transform.position);
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), crate.transform.position);
        }

        [UnityTest]
        public IEnumerator TargetUnderACeilingTooLowForThePlayer_IsRejected()
        {
            // The crate fits under the 1.6 high gap; the 2 unit tall player would not.
            Crate(new Vector3(5f, 0.5f, 0f));
            Block("Low Ceiling", new Vector3(5f, 2.1f, 0f), new Vector3(3f, 1f, 6f));
            BuildPlayer(new Vector3(0f, 1.001f, 0f), smoothSwap: true);
            yield return WaitSeconds(0.1f);
            Vector3 playerBefore = playerObject.transform.position;

            Assert.IsFalse(ability.TrySwap());
            Assert.AreEqual(SwapRejection.DestinationBlocked, ability.LastResult);
            Assert.AreEqual(1f, Time.timeScale, "A rejected smooth swap must not stop time.");
            Assert.AreEqual(playerBefore, playerObject.transform.position);
        }
    }
}

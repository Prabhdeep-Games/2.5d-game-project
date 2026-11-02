using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Checks the reusable environment elements on their own and in the combinations the puzzles rely on:
    /// sealed doors, movable platforms, control panels, security cameras, partitions and camera zones.
    /// The player is built like the real one (Character Controller, capsule, Swappable, controller, swap ability).
    /// </summary>
    public class EnvironmentElementTests : InputTestFixture
    {
        private const float FloorCenterY = 1.08f;
        private const float WaitLimitSeconds = 4f;
        private const float PositionTolerance = 0.15f;
        private const int IgnoreRaycastLayer = 2;
        private const int UiLayer = 5;

        private static readonly int GlassLayer = LayerMask.NameToLayer("Glass");
        private static readonly LayerMask SeeThroughGlass = ~((1 << 3) | (1 << IgnoreRaycastLayer) | (1 << UiLayer));
        private static readonly LayerMask ReachBlockedByGlass = ~((1 << IgnoreRaycastLayer) | (1 << UiLayer));

        private Keyboard keyboard;
        private GameObject root;
        private GameObject playerObject;
        private Swappable playerSwappable;
        private SwapAbility ability;

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();

            playerObject = null;
            root = new GameObject("Environment Test Rig");
            Block("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(300f, 1f, 10f));
        }

        public override void TearDown()
        {
            Time.timeScale = 1f;
            Object.DestroyImmediate(root);
            base.TearDown();
        }

        //==================== RIG =====================
        private GameObject Block(string name, Vector3 center, Vector3 size, int layer = 0)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.layer = layer;
            block.transform.SetParent(root.transform, false);
            block.transform.position = center;
            block.transform.localScale = size;
            Physics.SyncTransforms();
            return block;
        }

        private GameObject Glass(string name, Vector3 center, Vector3 size)
        {
            return Block(name, center, size, GlassLayer);
        }

        // A one unit crate without a Rigidbody, so it stays exactly where it is put.
        private Swappable Crate(Vector3 center)
        {
            return Block("Crate", center, Vector3.one).AddComponent<Swappable>();
        }

        private void BuildPlayer(Vector3 position)
        {
            playerObject = new GameObject("Environment Player");
            playerObject.SetActive(false);
            playerObject.transform.SetParent(root.transform, false);
            playerObject.transform.position = position;

            CharacterController characterController = playerObject.AddComponent<CharacterController>();
            characterController.height = 2f;
            characterController.radius = 0.5f;
            characterController.slopeLimit = 45f;
            CapsuleCollider capsule = playerObject.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.5f;
            playerSwappable = playerObject.AddComponent<Swappable>();

            PlaceholderPlayerController controller = playerObject.AddComponent<PlaceholderPlayerController>();
            InputAction move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            SetField(controller, "moveAction", move);
            SetField(controller, "jumpAction", new InputAction("Jump", InputActionType.Button, "<Keyboard>/space"));
            SetField(controller, "runAction", new InputAction("Run", InputActionType.Button, "<Keyboard>/leftShift"));

            ability = playerObject.AddComponent<SwapAbility>();
            SetField(ability, "sightBlockers", SeeThroughGlass);

            playerObject.SetActive(true);
            Physics.SyncTransforms();
        }

        private AccessDoor Door(Vector3 center, bool startsLocked)
        {
            GameObject go = Block("Door", center, new Vector3(1f, 3f, 4f));
            go.SetActive(false);
            AccessDoor door = go.AddComponent<AccessDoor>();
            SetField(door, "startsLocked", startsLocked);
            SetField(door, "openOffset", new Vector3(0f, 3f, 0f));
            SetField(door, "travelTime", 0.2f);
            go.SetActive(true);
            return door;
        }

        private MovingPlatform Platform(Vector3 center, Vector3 endOffset, MovingPlatform.PlatformMode mode = MovingPlatform.PlatformMode.GoToEndWhileActive)
        {
            GameObject go = Block("Platform", center, new Vector3(3f, 0.5f, 4f));
            go.SetActive(false);
            MovingPlatform platform = go.AddComponent<MovingPlatform>();
            SetField(platform, "endOffset", endOffset);
            SetField(platform, "speed", 4f);
            SetField(platform, "mode", mode);
            SetField(platform, "pauseAtEnds", 0.1f);
            go.SetActive(true);
            return platform;
        }

        private ControlPanel Panel(Vector3 position, ControlPanel.PanelActivation activation, ControlPanel.PanelResponse response, params Mechanism[] targets)
        {
            var go = new GameObject("Panel");
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;
            ControlPanel panel = go.AddComponent<ControlPanel>();
            SetField(panel, "interactAction", new InputAction("Interact", InputActionType.Button, "<Keyboard>/e"));
            SetField(panel, "activation", activation);
            SetField(panel, "response", response);
            SetField(panel, "reachBlockers", ReachBlockedByGlass);
            SetField(panel, "releaseDelay", 0.3f);
            SetField(panel, "targets", new System.Collections.Generic.List<Mechanism>(targets));
            if (playerObject != null)
            {
                SetField(panel, "player", playerObject.transform);
            }

            go.SetActive(true);
            return panel;
        }

        private SecurityCamera CreateCamera(Vector3 position, float releaseDelay, params Mechanism[] locksDown)
        {
            return CreateCamera(position, releaseDelay, false, locksDown);
        }

        private SecurityCamera CreateCamera(Vector3 position, float releaseDelay, bool holdUntilCleared, params Mechanism[] locksDown)
        {
            var go = new GameObject("Security Camera");
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;
            SecurityCamera securityCamera = go.AddComponent<SecurityCamera>();
            SetField(securityCamera, "viewAngle", 60f);
            SetField(securityCamera, "range", 12f);
            SetField(securityCamera, "sightBlockers", SeeThroughGlass);
            SetField(securityCamera, "releaseDelay", releaseDelay);
            SetField(securityCamera, "holdUntilCleared", holdUntilCleared);
            SetField(securityCamera, "locksDown", new System.Collections.Generic.List<Mechanism>(locksDown));
            if (playerObject != null)
            {
                SetField(securityCamera, "watched", new System.Collections.Generic.List<Transform> { playerObject.transform });
            }

            go.SetActive(true);
            return securityCamera;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = target.GetType(); type != null; type = type.BaseType)
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

        private static IEnumerator WaitUntil(Func<bool> condition, float limit = WaitLimitSeconds)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition() && Time.realtimeSinceStartup - start < limit)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private IEnumerator Tap(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        //==================== SEALED DOOR =====================
        [UnityTest]
        public IEnumerator ClosingDoor_WaitsWhileThePlayerStandsInTheDoorway()
        {
            AccessDoor door = Door(new Vector3(5f, 1.5f, 0f), startsLocked: false);
            door.Open();
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open);
            BuildPlayer(new Vector3(5f, FloorCenterY, 0f));
            yield return null;

            door.Lock();
            yield return WaitSeconds(0.6f);

            Assert.AreEqual(AccessDoor.DoorState.Closing, door.State, "The door must not finish closing onto the player.");
            Assert.IsTrue(door.IsObstructed);
            float doorBottom = door.GetComponent<Collider>().bounds.min.y;
            float playerTop = playerObject.GetComponent<CharacterController>().bounds.max.y;
            Assert.GreaterOrEqual(doorBottom, playerTop - 0.05f, "The door stopped above the player's head.");

            playerSwappable.MoveTo(new Vector3(10f, FloorCenterY, 0f));
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Closed);
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State, "Once the doorway is clear the door closes.");
        }

        [UnityTest]
        public IEnumerator Lockdown_ClosesAnUnlockedDoor_AndItReopensWhenReleased()
        {
            AccessDoor door = Door(new Vector3(5f, 1.5f, 0f), startsLocked: false);
            door.Open();
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open);

            door.SetLockdown(root, true);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Closed);
            door.Open();
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State, "A door in lockdown refuses to open.");
            Assert.IsFalse(door.IsLocked, "Lockdown does not change the lock.");

            door.SetLockdown(root, false);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State);
        }

        [Test]
        public void TwoLockdownSources_BothHaveToReleaseTheMechanism()
        {
            AccessDoor door = Door(new Vector3(5f, 1.5f, 0f), startsLocked: false);
            GameObject first = new GameObject("First camera");
            GameObject second = new GameObject("Second camera");
            first.transform.SetParent(root.transform);
            second.transform.SetParent(root.transform);

            door.SetLockdown(first, true);
            door.SetLockdown(second, true);
            door.SetLockdown(first, false);
            Assert.IsTrue(door.IsLockedDown);
            Assert.IsFalse(door.IsActive);

            door.SetLockdown(second, false);
            Assert.IsFalse(door.IsLockedDown);
            Assert.IsTrue(door.IsActive);
        }

        //==================== MOVABLE PLATFORM =====================
        [UnityTest]
        public IEnumerator Platform_CarriesThePlayerUpAndBackDown()
        {
            MovingPlatform platform = Platform(new Vector3(10f, 0.25f, 0f), new Vector3(0f, 4f, 0f));
            BuildPlayer(new Vector3(10f, 0.5f + FloorCenterY, 0f));
            yield return WaitSeconds(0.3f);

            platform.SetPowered(true);
            yield return WaitUntil(() => platform.Progress >= 1f);
            yield return WaitSeconds(0.3f);
            Assert.AreEqual(4.5f + FloorCenterY, playerObject.transform.position.y, PositionTolerance, "The player rode up with the platform.");

            platform.SetPowered(false);
            yield return WaitUntil(() => platform.Progress <= 0f);
            yield return WaitSeconds(0.3f);
            Assert.AreEqual(0.5f + FloorCenterY, playerObject.transform.position.y, PositionTolerance, "The player rode back down.");
        }

        [UnityTest]
        public IEnumerator DescendingPlatform_WaitsInsteadOfCrushingACrate()
        {
            MovingPlatform platform = Platform(new Vector3(20f, 4.25f, 0f), new Vector3(0f, -4f, 0f));
            Swappable crate = Crate(new Vector3(20f, 0.5f, 0f));
            platform.SetPowered(true);

            yield return WaitSeconds(1.5f);

            Assert.IsTrue(platform.IsObstructed);
            Assert.Less(platform.Progress, 1f);
            Assert.GreaterOrEqual(platform.GetComponent<Collider>().bounds.min.y, 1f - 0.05f, "The platform stopped on top of the crate.");
            Assert.AreEqual(new Vector3(20f, 0.5f, 0f), crate.transform.position);
        }

        [UnityTest]
        public IEnumerator RisingPlatform_StopsBeforeLiftingTheRiderIntoACeiling()
        {
            MovingPlatform platform = Platform(new Vector3(30f, 0.25f, 0f), new Vector3(0f, 6f, 0f));
            Block("Ceiling", new Vector3(30f, 4.5f, 0f), new Vector3(1f, 1f, 4f));
            BuildPlayer(new Vector3(30f, 0.5f + FloorCenterY, 0f));
            yield return WaitSeconds(0.3f);

            platform.SetPowered(true);
            yield return WaitSeconds(2f);

            float playerTop = playerObject.GetComponent<CharacterController>().bounds.max.y;
            Assert.IsTrue(platform.IsObstructed, $"progress {platform.Progress:F2}, riders {platform.RiderCount}, player {playerObject.transform.position}, top {playerTop:F2}");
            Assert.LessOrEqual(playerTop, 4f + 0.05f, "The player's head stopped under the ceiling.");
        }

        [UnityTest]
        public IEnumerator LoopingPlatform_ShuttlesBetweenBothEnds()
        {
            MovingPlatform platform = Platform(new Vector3(40f, 0.25f, 0f), new Vector3(5f, 0f, 0f), MovingPlatform.PlatformMode.LoopWhileActive);
            platform.SetPowered(true);

            yield return WaitUntil(() => platform.Progress >= 1f);
            Assert.AreEqual(1f, platform.Progress, 0.001f);
            yield return WaitUntil(() => platform.Progress <= 0f);
            Assert.AreEqual(0f, platform.Progress, 0.001f);

            platform.SetPowered(false);
            yield return WaitSeconds(0.3f);
            float stoppedAt = platform.Progress;
            yield return WaitSeconds(0.3f);
            Assert.AreEqual(stoppedAt, platform.Progress, 0.001f, "Unpowered, a looping platform stops where it is.");
        }

        //==================== CONTROL PANEL =====================
        [UnityTest]
        public IEnumerator InteractPanel_PressingE_UnlocksTheDoor()
        {
            BuildPlayer(new Vector3(51f, FloorCenterY, 0f));
            AccessDoor door = Door(new Vector3(58f, 1.5f, 0f), startsLocked: true);
            ControlPanel panel = Panel(new Vector3(50f, 1f, 0f), ControlPanel.PanelActivation.Interact, ControlPanel.PanelResponse.Toggle, door);
            yield return null;
            yield return null;

            Assert.IsTrue(panel.PlayerInReach);
            Assert.AreEqual("E  use", panel.PromptText);

            yield return Tap(keyboard.eKey);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open);

            Assert.IsTrue(panel.IsOn);
            Assert.IsFalse(door.IsLocked);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State);
        }

        [UnityTest]
        public IEnumerator InteractPanel_SeenThroughGlass_IsOutOfReach()
        {
            BuildPlayer(new Vector3(51.5f, FloorCenterY, 0f));
            Glass("Glass", new Vector3(50.8f, 2f, 0f), new Vector3(0.3f, 4f, 4f));
            AccessDoor door = Door(new Vector3(58f, 1.5f, 0f), startsLocked: true);
            ControlPanel panel = Panel(new Vector3(50f, 1f, 0f), ControlPanel.PanelActivation.Interact, ControlPanel.PanelResponse.Toggle, door);
            yield return null;
            yield return null;

            Assert.IsFalse(panel.PlayerInReach);
            Assert.AreEqual("Out of reach", panel.PromptText);

            yield return Tap(keyboard.eKey);
            yield return WaitSeconds(0.3f);

            Assert.IsFalse(panel.IsOn);
            Assert.IsTrue(door.IsLocked);
        }

        [UnityTest]
        public IEnumerator TwoPanelsInReach_OnlyTheNearerOneReacts()
        {
            BuildPlayer(new Vector3(51f, FloorCenterY, 0f));
            AccessDoor nearDoor = Door(new Vector3(58f, 1.5f, 0f), startsLocked: true);
            AccessDoor farDoor = Door(new Vector3(62f, 1.5f, 0f), startsLocked: true);
            ControlPanel near = Panel(new Vector3(51.8f, 1f, 0f), ControlPanel.PanelActivation.Interact, ControlPanel.PanelResponse.Toggle, nearDoor);
            ControlPanel far = Panel(new Vector3(50f, 1f, 0f), ControlPanel.PanelActivation.Interact, ControlPanel.PanelResponse.Toggle, farDoor);
            yield return null;
            yield return null;
            Assert.IsTrue(near.PlayerInReach && far.PlayerInReach, "Both panels are in reach.");

            yield return Tap(keyboard.eKey);

            Assert.IsTrue(near.IsOn);
            Assert.IsFalse(far.IsOn);
        }

        [UnityTest]
        public IEnumerator HoldPad_IsOnOnlyWhileACrateStandsOnIt()
        {
            AccessDoor door = Door(new Vector3(68f, 1.5f, 0f), startsLocked: true);
            ControlPanel panel = Panel(new Vector3(60f, 0f, 0f), ControlPanel.PanelActivation.Occupied, ControlPanel.PanelResponse.Hold, door);
            Swappable crate = Crate(new Vector3(60f, 0.5f, 0f));
            yield return null;
            yield return null;
            Assert.IsTrue(panel.IsOn);
            Assert.IsFalse(door.IsLocked);

            crate.MoveTo(new Vector3(64f, 0.5f, 0f));
            Physics.SyncTransforms();
            yield return WaitSeconds(0.1f);
            Assert.IsTrue(panel.IsOn, "Inside the grace period the panel stays on.");

            yield return WaitSeconds(0.4f);
            Assert.IsFalse(panel.IsOn);
            Assert.IsTrue(door.IsLocked);
        }

        [UnityTest]
        public IEnumerator LatchPad_StaysOnAfterTheCrateLeaves()
        {
            AccessDoor door = Door(new Vector3(68f, 1.5f, 0f), startsLocked: true);
            ControlPanel panel = Panel(new Vector3(60f, 0f, 0f), ControlPanel.PanelActivation.Occupied, ControlPanel.PanelResponse.Latch, door);
            Swappable crate = Crate(new Vector3(60f, 0.5f, 0f));
            yield return null;
            yield return null;

            crate.MoveTo(new Vector3(64f, 0.5f, 0f));
            Physics.SyncTransforms();
            yield return WaitSeconds(0.6f);

            Assert.IsTrue(panel.IsOn);
            Assert.IsFalse(door.IsLocked);
        }

        [UnityTest]
        public IEnumerator Panel_SwitchesEachTargetAwayFromItsStartingPower()
        {
            AccessDoor door = Door(new Vector3(68f, 1.5f, 0f), startsLocked: true);
            SecurityCamera securityCamera = CreateCamera(new Vector3(70f, 8f, 0f), 0.5f);
            ControlPanel panel = Panel(new Vector3(60f, 1f, 0f), ControlPanel.PanelActivation.Interact, ControlPanel.PanelResponse.Toggle, door, securityCamera);
            yield return null;

            panel.SetOn(true);
            Assert.IsTrue(door.IsPowered, "The locked door is unlocked.");
            Assert.IsFalse(securityCamera.IsPowered, "The camera that started on is switched off.");

            panel.SetOn(false);
            Assert.IsFalse(door.IsPowered);
            Assert.IsTrue(securityCamera.IsPowered);
        }

        //==================== COMBINATION: SWAP AND PAD =====================
        [UnityTest]
        public IEnumerator SwappingFromAHoldPad_LeavesTheCrateOnIt_SoTheDoorStaysOpen()
        {
            // The pad holds the exit door open. The crate is beyond a glass partition. Swapping from the pad puts
            // the player beyond the glass and leaves the crate on the pad, holding the door open.
            BuildPlayer(new Vector3(80f, FloorCenterY, 0f));
            AccessDoor door = Door(new Vector3(90f, 1.5f, 0f), startsLocked: true);
            Panel(new Vector3(80f, 0f, 0f), ControlPanel.PanelActivation.Occupied, ControlPanel.PanelResponse.Hold, door);
            Glass("Glass Partition", new Vector3(83f, 3f, 0f), new Vector3(0.3f, 6f, 4f));
            Swappable crate = Crate(new Vector3(86f, 0.5f, 0f));
            yield return WaitSeconds(0.3f);
            Assert.IsFalse(door.IsLocked, "Standing on the pad powers the door.");

            ability.SetAimDirection(crate.Center - playerSwappable.Center);
            bool swapped = ability.TrySwap();
            Assert.IsTrue(swapped, SwapRejectionText.Describe(ability.LastResult));
            yield return WaitSeconds(1f);

            Assert.Greater(playerObject.transform.position.x, 83f, "The player is beyond the glass.");
            Assert.AreEqual(80f, crate.transform.position.x, 0.01f, "The crate took the player's place on the pad.");
            Assert.IsFalse(door.IsLocked, "The crate keeps the pad down, so the door stays powered.");
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State);
        }

        //==================== SECURITY CAMERA =====================
        [UnityTest]
        public IEnumerator Camera_SeesThePlayerThroughGlass_AndLocksDownTheDoor()
        {
            BuildPlayer(new Vector3(100f, FloorCenterY, 0f));
            AccessDoor door = Door(new Vector3(106f, 1.5f, 0f), startsLocked: false);
            Glass("Glass Ceiling", new Vector3(100f, 5f, 0f), new Vector3(6f, 0.3f, 4f));
            SecurityCamera securityCamera = CreateCamera(new Vector3(100f, 9f, 0f), 0.5f, door);
            yield return null;
            yield return null;

            Assert.IsTrue(securityCamera.IsSeeing);
            Assert.IsTrue(securityCamera.IsAlarmed);
            Assert.IsTrue(door.IsLockedDown);
            Assert.IsFalse(door.IsLocked, "Lockdown is not the same as locking.");
        }

        [UnityTest]
        public IEnumerator Camera_ASolidCeilingHidesThePlayer()
        {
            BuildPlayer(new Vector3(100f, FloorCenterY, 0f));
            AccessDoor door = Door(new Vector3(106f, 1.5f, 0f), startsLocked: false);
            Block("Solid Ceiling", new Vector3(100f, 5f, 0f), new Vector3(6f, 0.3f, 4f));
            SecurityCamera securityCamera = CreateCamera(new Vector3(100f, 9f, 0f), 0.5f, door);
            yield return null;
            yield return null;

            Assert.IsFalse(securityCamera.IsSeeing);
            Assert.IsFalse(door.IsLockedDown);
        }

        [UnityTest]
        public IEnumerator Camera_ACrateBetweenItAndThePlayerHidesThem()
        {
            BuildPlayer(new Vector3(100f, FloorCenterY, 0f));
            Crate(new Vector3(100f, 5f, 0f));
            SecurityCamera securityCamera = CreateCamera(new Vector3(100f, 9f, 0f), 0.5f);
            yield return null;
            yield return null;

            Assert.IsFalse(securityCamera.IsSeeing);
        }

        [UnityTest]
        public IEnumerator Camera_DoesNotSeeOutsideItsCone()
        {
            // Looking straight down with a 60 degree cone, the edge at floor level is about 4.6 units to the side.
            BuildPlayer(new Vector3(107f, FloorCenterY, 0f));
            SecurityCamera securityCamera = CreateCamera(new Vector3(100f, 9f, 0f), 0.5f);
            yield return null;
            yield return null;

            Assert.IsFalse(securityCamera.IsSeeing);
        }

        [UnityTest]
        public IEnumerator Camera_ReleasesTheLockdownAfterTheDelay_AndTheDoorReopens()
        {
            BuildPlayer(new Vector3(100f, FloorCenterY, 0f));
            AccessDoor door = Door(new Vector3(112f, 1.5f, 0f), startsLocked: false);
            door.Open();
            SecurityCamera securityCamera = CreateCamera(new Vector3(100f, 9f, 0f), 0.6f, door);
            yield return null;
            yield return null;
            Assert.IsTrue(securityCamera.IsAlarmed);

            playerSwappable.MoveTo(new Vector3(120f, FloorCenterY, 0f));
            Physics.SyncTransforms();
            yield return WaitSeconds(0.3f);
            Assert.IsFalse(securityCamera.IsSeeing);
            Assert.IsTrue(securityCamera.IsAlarmed, "The alarm holds for the release delay.");

            yield return WaitSeconds(0.5f);
            Assert.IsFalse(securityCamera.IsAlarmed);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Open);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State);
        }

        [UnityTest]
        public IEnumerator HeldAlarm_StaysAfterThePlayerLeaves_UntilItIsCleared()
        {
            BuildPlayer(new Vector3(100f, FloorCenterY, 0f));
            AccessDoor door = Door(new Vector3(112f, 1.5f, 0f), startsLocked: false);
            SecurityCamera securityCamera = CreateCamera(new Vector3(100f, 9f, 0f), 0.2f, true, door);
            yield return null;
            yield return null;
            Assert.IsTrue(securityCamera.IsAlarmed);
            Assert.AreEqual("Spotted", securityCamera.AlarmText);

            securityCamera.ClearAlarm();
            Assert.IsTrue(securityCamera.IsAlarmed, "Clearing does nothing while the player is still in view.");

            playerSwappable.MoveTo(new Vector3(120f, FloorCenterY, 0f));
            Physics.SyncTransforms();
            yield return WaitSeconds(0.6f);
            Assert.IsTrue(securityCamera.IsAlarmed, "A held alarm ignores the release delay.");
            Assert.IsTrue(door.IsLockedDown);

            securityCamera.ClearAlarm();
            yield return null;
            Assert.IsFalse(securityCamera.IsAlarmed);
            Assert.IsFalse(door.IsLockedDown);
            Assert.AreEqual(string.Empty, securityCamera.AlarmText);
        }

        [UnityTest]
        public IEnumerator DoorThatStartsOpen_IsOpenAndSealsUnderLockdown()
        {
            GameObject go = Block("Open Door", new Vector3(5f, 1.5f, 0f), new Vector3(1f, 3f, 4f));
            go.SetActive(false);
            AccessDoor door = go.AddComponent<AccessDoor>();
            SetField(door, "startsLocked", false);
            SetField(door, "startsOpen", true);
            SetField(door, "openOffset", new Vector3(0f, 3f, 0f));
            SetField(door, "travelTime", 0.2f);
            go.SetActive(true);

            Assert.AreEqual(AccessDoor.DoorState.Open, door.State);
            Assert.AreEqual(4.5f, door.transform.position.y, 0.001f);

            door.SetLockdown(root, true);
            yield return WaitUntil(() => door.State == AccessDoor.DoorState.Closed);
            Assert.AreEqual(1.5f, door.transform.position.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator SwitchingTheCameraOffWithAPanel_EndsTheLockdownAtOnce()
        {
            BuildPlayer(new Vector3(100f, FloorCenterY, 0f));
            AccessDoor door = Door(new Vector3(106f, 1.5f, 0f), startsLocked: false);
            SecurityCamera securityCamera = CreateCamera(new Vector3(100f, 9f, 0f), 5f, door);
            ControlPanel panel = Panel(new Vector3(99f, 1f, 0f), ControlPanel.PanelActivation.Interact, ControlPanel.PanelResponse.Toggle, securityCamera);
            yield return null;
            yield return null;
            Assert.IsTrue(door.IsLockedDown);

            yield return Tap(keyboard.eKey);

            Assert.IsTrue(panel.IsOn);
            Assert.IsFalse(securityCamera.IsActive);
            Assert.IsFalse(securityCamera.IsAlarmed);
            Assert.IsFalse(door.IsLockedDown, "No release delay when the camera is switched off.");
        }

        //==================== PARTITIONS AND ZONES =====================
        [Test]
        public void Partition_ReportsEveryBrokenRule()
        {
            GameObject barrier = Block("Barrier", new Vector3(130f, 2f, 0f), new Vector3(1f, 4f, 4f));
            barrier.AddComponent<WallRunSurface>();
            Partition barrierPartition = barrier.AddComponent<Partition>();
            SetField(barrierPartition, "kind", Partition.PartitionKind.IndustrialBarrier);

            GameObject glass = Block("Glass", new Vector3(134f, 2f, 0f), new Vector3(0.3f, 4f, 4f));
            Partition glassPartition = glass.AddComponent<Partition>();
            SetField(glassPartition, "kind", Partition.PartitionKind.Glass);
            glass.layer = 0;

            StringAssert.Contains("wall run", string.Join(" ", barrierPartition.FindProblems()));
            StringAssert.Contains("Glass layer", string.Join(" ", glassPartition.FindProblems()));

            glass.layer = GlassLayer;
            CollectionAssert.IsEmpty(glassPartition.FindProblems());
            Assert.IsTrue(glassPartition.PassesSight);
            Assert.IsFalse(barrierPartition.PassesSight);
        }

        [Test]
        public void CameraZone_GivesTheLargestExtraDistanceOfTheZonesAroundAPoint()
        {
            CameraZone shaft = Zone(new Vector3(140f, 10f, 0f), new Vector3(10f, 20f, 4f), 6f);
            Zone(new Vector3(140f, 15f, 0f), new Vector3(10f, 4f, 4f), 9f);

            Assert.AreEqual(6f, CameraZone.ExtraDistanceAt(new Vector3(140f, 5f, 0f)));
            Assert.AreEqual(9f, CameraZone.ExtraDistanceAt(new Vector3(140f, 15f, 0f)));
            Assert.AreEqual(0f, CameraZone.ExtraDistanceAt(new Vector3(160f, 5f, 0f)));
            Assert.AreEqual(6f, shaft.ExtraDistance);
        }

        private CameraZone Zone(Vector3 center, Vector3 size, float extra)
        {
            var go = new GameObject("Camera Zone");
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            CameraZone zone = go.AddComponent<CameraZone>();
            SetField(zone, "extraDistance", extra);
            go.SetActive(true);
            Physics.SyncTransforms();
            return zone;
        }
    }
}

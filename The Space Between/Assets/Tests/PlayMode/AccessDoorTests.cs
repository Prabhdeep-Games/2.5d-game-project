using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class AccessDoorTests : PlayModeTestBase
    {
        private const float TestTravelTime = 0.05f;
        private const float WaitLimitSeconds = 3f;

        private static readonly Vector3 TestOffset = new Vector3(0f, 3f, 0f);

        private AccessDoor CreateDoor(bool startsLocked, GameEvent unlockEvent = null, bool opensWhenUnlocked = true, UnityEvent rejected = null)
        {
            AccessDoor door = CreateInactive<AccessDoor>("Door Test");
            door.transform.localPosition = Vector3.zero;
            SetField(door, "startsLocked", startsLocked);
            SetField(door, "unlockEvent", unlockEvent);
            SetField(door, "opensWhenUnlocked", opensWhenUnlocked);
            SetField(door, "openOffset", TestOffset);
            SetField(door, "travelTime", TestTravelTime);
            if (rejected != null)
            {
                SetField(door, "openRejectedEvent", rejected);
            }

            door.gameObject.SetActive(true);
            return door;
        }

        private static IEnumerator WaitForState(AccessDoor door, AccessDoor.DoorState wanted)
        {
            float waited = 0f;
            while (door.State != wanted && waited < WaitLimitSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        [Test]
        public void StartsLockedAndClosed()
        {
            AccessDoor door = CreateDoor(startsLocked: true);

            Assert.IsTrue(door.IsLocked);
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State);
        }

        [Test]
        public void LockedDoor_RefusesToOpenAndRaisesTheRejectedEvent()
        {
            int rejections = 0;
            var rejected = new UnityEvent();
            rejected.AddListener(() => rejections++);
            AccessDoor door = CreateDoor(startsLocked: true, rejected: rejected);

            door.Open();

            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State);
            Assert.AreEqual(1, rejections);
        }

        [UnityTest]
        public IEnumerator Unlock_OpensTheDoorToItsOffset()
        {
            AccessDoor door = CreateDoor(startsLocked: true);

            door.Unlock();
            yield return WaitForState(door, AccessDoor.DoorState.Open);

            Assert.IsFalse(door.IsLocked);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State);
            Assert.AreEqual(TestOffset.y, door.transform.localPosition.y, 0.001f);
        }

        [Test]
        public void Unlock_WithOpensWhenUnlockedOff_UnlocksButStaysClosed()
        {
            AccessDoor door = CreateDoor(startsLocked: true, opensWhenUnlocked: false);

            door.Unlock();

            Assert.IsFalse(door.IsLocked);
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State);
        }

        [UnityTest]
        public IEnumerator RaisingTheUnlockEvent_UnlocksTheDoor()
        {
            GameEvent unlock = Track(ScriptableObject.CreateInstance<GameEvent>());
            AccessDoor door = CreateDoor(startsLocked: true, unlockEvent: unlock);

            unlock.Raise();
            yield return WaitForState(door, AccessDoor.DoorState.Open);

            Assert.IsFalse(door.IsLocked);
            Assert.AreEqual(AccessDoor.DoorState.Open, door.State);
        }

        [UnityTest]
        public IEnumerator Close_ReturnsTheDoorToItsStartingPosition()
        {
            AccessDoor door = CreateDoor(startsLocked: false);
            door.Open();
            yield return WaitForState(door, AccessDoor.DoorState.Open);

            door.Close();
            yield return WaitForState(door, AccessDoor.DoorState.Closed);

            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State);
            Assert.AreEqual(0f, door.transform.localPosition.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator Lock_ClosesAndLocksAnOpenDoor()
        {
            AccessDoor door = CreateDoor(startsLocked: false);
            door.Open();
            yield return WaitForState(door, AccessDoor.DoorState.Open);

            door.Lock();
            yield return WaitForState(door, AccessDoor.DoorState.Closed);

            Assert.IsTrue(door.IsLocked);
            Assert.AreEqual(AccessDoor.DoorState.Closed, door.State);
        }
    }
}

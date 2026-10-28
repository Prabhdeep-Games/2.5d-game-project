using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class RoomResetTests : PlayModeTestBase
    {
        private Swappable player;
        private Swappable crate;
        private RoomReset room;
        private Transform entry;

        private IEnumerator BuildRoom()
        {
            GameObject playerObject = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            playerObject.name = "Reset Player Test";
            playerObject.transform.position = new Vector3(1f, 0.5f, 0f);
            player = playerObject.AddComponent<Swappable>();

            GameObject crateObject = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            crateObject.name = "Reset Crate Test";
            crateObject.transform.position = new Vector3(4f, 0.5f, 0f);
            crate = crateObject.AddComponent<Swappable>();

            entry = Track(new GameObject("Reset Entry Test")).transform;
            entry.position = new Vector3(-3f, 0f, 0f);

            room = CreateInactive<RoomReset>("Reset Room Test");
            BoxCollider box = room.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(20f, 10f, 4f);
            SetField(room, "player", player);
            SetField(room, "entryPoint", entry);
            room.gameObject.SetActive(true);
            Physics.SyncTransforms();

            // Start collects the objects in the room.
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reset_PutsObjectsBackAndThePlayerAtTheEntry()
        {
            yield return BuildRoom();

            crate.MoveTo(new Vector3(8f, 3f, 0f));
            crate.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            player.MoveTo(new Vector3(6f, 0.5f, 0f));

            room.ResetRoom();

            Assert.AreEqual(new Vector3(4f, 0.5f, 0f), crate.transform.position);
            Assert.AreEqual(Quaternion.identity, crate.transform.rotation);
            Assert.AreEqual(entry.position.x, player.Anchor.x, 0.001f);
            Assert.AreEqual(entry.position.y, player.Anchor.y, 0.001f, "The entry point marks where the player's feet go.");
        }

        [UnityTest]
        public IEnumerator Reset_LeavesObjectsOutsideTheRoomAlone()
        {
            GameObject outsideObject = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            outsideObject.transform.position = new Vector3(40f, 0.5f, 0f);
            Swappable outside = outsideObject.AddComponent<Swappable>();
            yield return BuildRoom();

            outside.MoveTo(new Vector3(45f, 0.5f, 0f));
            room.ResetRoom();

            Assert.AreEqual(45f, outside.transform.position.x, 0.001f);
        }
    }
}

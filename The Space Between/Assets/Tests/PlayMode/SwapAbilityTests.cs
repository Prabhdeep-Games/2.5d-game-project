using NUnit.Framework;
using UnityEngine;

namespace TheSpaceBetween.Tests
{
    public class SwapAbilityTests : PlayModeTestBase
    {
        private const int GlassLayer = 1;

        private SwapAbility CreatePlayer(Vector3 position, bool glassDoesNotBlock = false)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Player Test";
            go.transform.position = position;
            go.SetActive(false);

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            SwapAbility ability = go.AddComponent<SwapAbility>();
            if (glassDoesNotBlock)
            {
                SetField(ability, "sightBlockers", (LayerMask)~(1 << GlassLayer));
            }

            go.SetActive(true);
            Physics.SyncTransforms();
            return ability;
        }

        private Swappable CreateTarget(Vector3 position, float size = 1f)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Target Test";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;
            Swappable swappable = go.AddComponent<Swappable>();
            Physics.SyncTransforms();
            return swappable;
        }

        private GameObject CreateSolid(Vector3 position, Vector3 scale, int layer = 0)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Solid Test";
            go.transform.position = position;
            go.transform.localScale = scale;
            go.layer = layer;
            Physics.SyncTransforms();
            return go;
        }

        [Test]
        public void ValidSwap_ExchangesPositions()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f));

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped);
            Assert.AreEqual(SwapRejection.None, player.LastResult);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), player.transform.position);
            Assert.AreEqual(Vector3.zero, target.transform.position);
        }

        [Test]
        public void ValidSwap_RemovesMomentumFromTheRigidbody()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            CreateTarget(new Vector3(5f, 0f, 0f));
            Rigidbody body = player.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(3f, 7f, 0f);
            body.angularVelocity = new Vector3(0f, 2f, 0f);

            player.TrySwap();

            Assert.AreEqual(Vector3.zero, body.linearVelocity);
            Assert.AreEqual(Vector3.zero, body.angularVelocity);
        }

        [Test]
        public void ValidSwap_RaisesSwappedWithTheOtherObject()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f));
            Swappable reported = null;
            player.Swapped += other => reported = other;

            player.TrySwap();

            Assert.AreSame(target, reported);
        }

        [Test]
        public void TargetBeyondRange_IsRejectedAndNothingMoves()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(20f, 0f, 0f));
            SwapRejection reported = SwapRejection.None;
            player.SwapRejected += reason => reported = reason;

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.NoTargetInRange, reported);
            Assert.AreEqual(Vector3.zero, player.transform.position);
            Assert.AreEqual(new Vector3(20f, 0f, 0f), target.transform.position);
        }

        [Test]
        public void WallBetween_BlocksTheSwap()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f));
            CreateSolid(new Vector3(2.5f, 0f, 0f), new Vector3(0.2f, 3f, 3f));

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.LineOfSightBlocked, player.LastResult);
            Assert.AreEqual(Vector3.zero, player.transform.position);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), target.transform.position);
        }

        [Test]
        public void GlassBetween_AllowsTheSwap()
        {
            SwapAbility player = CreatePlayer(Vector3.zero, glassDoesNotBlock: true);
            CreateTarget(new Vector3(5f, 0f, 0f));
            CreateSolid(new Vector3(2.5f, 0f, 0f), new Vector3(0.2f, 3f, 3f), GlassLayer);

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), player.transform.position);
        }

        [Test]
        public void TargetThatDoesNotFitAtTheOtherPosition_IsRejected()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f), size: 3f);
            // Sits just above the player. The big target would overlap it after moving to the player's spot.
            CreateSolid(new Vector3(0f, 1.4f, 0f), Vector3.one);

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.DestinationBlocked, player.LastResult);
            Assert.AreEqual(Vector3.zero, player.transform.position);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), target.transform.position);
        }

        [Test]
        public void LockedTarget_IsRejected()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f));
            target.CanSwap = false;

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.TargetLocked, player.LastResult);
        }

        [Test]
        public void NearestValidTargetIsChosen()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable far = CreateTarget(new Vector3(6f, 0f, 0f));
            Swappable near = CreateTarget(new Vector3(0f, 0f, 3f));

            player.TrySwap();

            Assert.AreEqual(new Vector3(0f, 0f, 3f), player.transform.position);
            Assert.AreEqual(Vector3.zero, near.transform.position);
            Assert.AreEqual(new Vector3(6f, 0f, 0f), far.transform.position);
        }

        [Test]
        public void WhenTheNearestIsLocked_TheNextValidTargetIsUsed()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable far = CreateTarget(new Vector3(6f, 0f, 0f));
            Swappable near = CreateTarget(new Vector3(0f, 0f, 3f));
            near.CanSwap = false;

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped);
            Assert.AreEqual(new Vector3(6f, 0f, 0f), player.transform.position);
            Assert.AreEqual(Vector3.zero, far.transform.position);
            Assert.AreEqual(new Vector3(0f, 0f, 3f), near.transform.position);
        }

        [Test]
        public void TallCharacterControllerPlayer_SwapsWithAShortCrateStandingOnTheFloor()
        {
            CreateSolid(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 10f));

            GameObject go = Track(new GameObject("Capsule Player Test"));
            go.SetActive(false);
            go.transform.position = new Vector3(0f, 1.001f, 0f);
            var controller = go.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            SwapAbility player = go.AddComponent<SwapAbility>();
            go.SetActive(true);

            Swappable crate = CreateTarget(new Vector3(5f, 0.5f, 0f));
            Physics.SyncTransforms();

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped, SwapRejectionText.Describe(player.LastResult));
            Assert.AreEqual(5f, player.transform.position.x, 0.01f);
            Assert.AreEqual(1.001f, player.transform.position.y, 0.01f, "Player keeps its feet on the floor.");
            Assert.AreEqual(0.5f, crate.transform.position.y, 0.01f, "Crate keeps resting on the floor.");
        }

        [Test]
        public void SwappingTwice_ReturnsBothBodiesToTheStart()
        {
            SwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f));

            player.TrySwap();
            bool secondSwap = player.TrySwap();

            Assert.IsTrue(secondSwap);
            Assert.AreEqual(Vector3.zero, player.transform.position);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), target.transform.position);
        }
    }
}

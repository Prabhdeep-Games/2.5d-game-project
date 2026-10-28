using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    /// <summary>A stand-in for a mover that faces a chosen direction.</summary>
    public class FacingStub : MonoBehaviour, IFacingSource
    {
        public Vector3 Value = Vector3.right;

        public Vector3 Facing => Value;
    }

    public class SwapTargetingTests : PlayModeTestBase
    {
        private const int GlassLayer = 1;

        private SwapAbility CreatePlayer(SwapAimMode mode = SwapAimMode.Pointer, bool withFacing = false)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Targeting Player";
            go.transform.position = Vector3.zero;
            go.SetActive(false);

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            if (withFacing)
            {
                go.AddComponent<FacingStub>();
            }

            SwapAbility ability = go.AddComponent<SwapAbility>();
            SetField(ability, "aimMode", mode);
            SetField(ability, "sightBlockers", (LayerMask)~(1 << GlassLayer));
            go.SetActive(true);
            Physics.SyncTransforms();
            return ability;
        }

        private Swappable CreateTarget(Vector3 position, string targetName = "Target")
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = targetName;
            go.transform.position = position;
            Swappable swappable = go.AddComponent<Swappable>();
            Physics.SyncTransforms();
            return swappable;
        }

        private GameObject CreateSolid(Vector3 position, Vector3 scale, int layer = 0)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Solid";
            go.transform.position = position;
            go.transform.localScale = scale;
            go.layer = layer;
            Physics.SyncTransforms();
            return go;
        }

        [Test]
        public void AimingAtOneOfTwoObjects_SwapsOnlyWithTheAimedOne()
        {
            SwapAbility player = CreatePlayer();
            Swappable nearer = CreateTarget(new Vector3(0f, 3f, 0f), "Nearer, above");
            Swappable aimed = CreateTarget(new Vector3(6f, 0f, 0f), "Aimed, to the right");
            player.SetAimDirection(Vector3.right);

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped, SwapRejectionText.Describe(player.LastResult));
            Assert.AreEqual(new Vector3(6f, 0f, 0f), player.transform.position);
            Assert.AreEqual(Vector3.zero, aimed.transform.position);
            Assert.AreEqual(new Vector3(0f, 3f, 0f), nearer.transform.position, "The nearer object was not aimed at.");
        }

        [Test]
        public void ObjectBehindTheAimedOne_IsNeverSwapped()
        {
            SwapAbility player = CreatePlayer();
            Swappable front = CreateTarget(new Vector3(3f, 0f, 0f), "Front");
            Swappable back = CreateTarget(new Vector3(6f, 0f, 0f), "Back");
            player.SetAimDirection(Vector3.right);

            player.TrySwap();

            Assert.AreEqual(new Vector3(3f, 0f, 0f), player.transform.position, "The player takes the front crate's place.");
            Assert.AreEqual(Vector3.zero, front.transform.position);
            Assert.AreEqual(new Vector3(6f, 0f, 0f), back.transform.position, "The crate behind did not move.");
        }

        [Test]
        public void RejectedAimedObject_DoesNotFallThroughToTheOneBehind()
        {
            SwapAbility player = CreatePlayer();
            Swappable front = CreateTarget(new Vector3(3f, 0f, 0f), "Locked front");
            Swappable back = CreateTarget(new Vector3(6f, 0f, 0f), "Valid back");
            SetField(front, "canSwap", false);
            player.SetAimDirection(Vector3.right);

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.TargetLocked, player.LastResult);
            Assert.AreSame(front, player.Target);
            Assert.AreEqual(Vector3.zero, player.transform.position);
            Assert.AreEqual(new Vector3(6f, 0f, 0f), back.transform.position);
        }

        [Test]
        public void WallBetweenPlayerAndObject_MeansNoTarget()
        {
            SwapAbility player = CreatePlayer();
            CreateSolid(new Vector3(3f, 0f, 0f), new Vector3(1f, 5f, 5f));
            Swappable behindWall = CreateTarget(new Vector3(6f, 0f, 0f), "Behind the wall");
            player.SetAimDirection(Vector3.right);

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.LineOfSightBlocked, player.LastResult);
            Assert.IsNull(player.Target);
            Assert.AreEqual(new Vector3(6f, 0f, 0f), behindWall.transform.position);
        }

        [Test]
        public void GlassBetweenPlayerAndObject_DoesNotBlockTheAim()
        {
            SwapAbility player = CreatePlayer();
            CreateSolid(new Vector3(3f, 0f, 0f), new Vector3(0.2f, 5f, 5f), GlassLayer);
            CreateTarget(new Vector3(6f, 0f, 0f), "Behind the glass");
            player.SetAimDirection(Vector3.right);

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped, SwapRejectionText.Describe(player.LastResult));
            Assert.AreEqual(new Vector3(6f, 0f, 0f), player.transform.position);
        }

        [Test]
        public void CrateOnALedgeEdge_CanBeAimedAtFromBelow_WhenTheCentreLineClearsTheCorner()
        {
            // The aim passes 0.3 units above the ledge corner: inside the aim's thickness, but a clear view.
            SwapAbility player = CreatePlayer();
            CreateSolid(new Vector3(7f, -2f, 0f), new Vector3(6f, 6f, 5f));
            Swappable crate = CreateTarget(new Vector3(4.5f, 1.5f, 0f), "On the ledge edge");
            player.SetAimDirection(crate.Center - player.transform.position);

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped, SwapRejectionText.Describe(player.LastResult));
            Assert.AreEqual(new Vector3(4.5f, 1.5f, 0f), player.transform.position);
        }

        [Test]
        public void AimingAwayFromEveryObject_MeansNoTarget()
        {
            SwapAbility player = CreatePlayer();
            CreateTarget(new Vector3(4f, 0f, 0f));
            player.SetAimDirection(Vector3.left);

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.NoTargetInRange, player.LastResult);
            Assert.IsNull(player.Target);
        }

        [Test]
        public void ObjectBeyondRange_IsNotAimedAt()
        {
            SwapAbility player = CreatePlayer();
            CreateTarget(new Vector3(12f, 0f, 0f));
            player.SetAimDirection(Vector3.right);

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.NoTargetInRange, player.LastResult);
        }

        [Test]
        public void DiagonalAim_FindsAnObjectUpAndToTheRight()
        {
            SwapAbility player = CreatePlayer();
            Swappable high = CreateTarget(new Vector3(4f, 4f, 0f), "Up and right");
            player.SetAimDirection(new Vector3(1f, 1f, 0f));

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped, SwapRejectionText.Describe(player.LastResult));
            Assert.AreEqual(Vector3.zero, high.transform.position);
        }

        [Test]
        public void DiagonalObject_IsNotSelectedWhenAimingStraightAcross()
        {
            SwapAbility player = CreatePlayer();
            CreateTarget(new Vector3(4f, 4f, 0f), "Up and right");
            player.SetAimDirection(Vector3.right);

            bool swapped = player.TrySwap();

            Assert.IsFalse(swapped);
            Assert.AreEqual(SwapRejection.NoTargetInRange, player.LastResult);
        }

        [Test]
        public void FacingMode_UsesTheDirectionThePlayerFaces()
        {
            SwapAbility player = CreatePlayer(SwapAimMode.Facing, withFacing: true);
            Swappable right = CreateTarget(new Vector3(4f, 0f, 0f), "Right");
            Swappable left = CreateTarget(new Vector3(-6f, 0f, 0f), "Left");
            player.GetComponent<FacingStub>().Value = Vector3.left;

            bool swapped = player.TrySwap();

            Assert.IsTrue(swapped, SwapRejectionText.Describe(player.LastResult));
            Assert.AreEqual(new Vector3(-6f, 0f, 0f), player.transform.position);
            Assert.AreEqual(Vector3.zero, left.transform.position);
            Assert.AreEqual(new Vector3(4f, 0f, 0f), right.transform.position, "The object behind the player was not chosen.");
        }

        [UnityTest]
        public IEnumerator Indicator_StraightStyleDrawsTwoPointsToTheAimedTarget()
        {
            SwapAbility player = CreatePlayer();
            CreateTarget(new Vector3(5f, 0f, 0f));
            SwapTargetIndicator indicator = AddIndicator(player, SwapIndicatorStyle.Straight);

            yield return null;
            yield return null;

            LineRenderer line = indicator.GetComponent<LineRenderer>();
            Assert.IsTrue(line.enabled);
            Assert.AreEqual(2, line.positionCount);
            Assert.AreEqual(5f, line.GetPosition(1).x, 0.01f);
        }

        [UnityTest]
        public IEnumerator Indicator_WobblyStyleStartsAndEndsOnTheSameTwoPointsAsStraight()
        {
            SwapAbility player = CreatePlayer();
            CreateTarget(new Vector3(5f, 0f, 0f));
            SwapTargetIndicator indicator = AddIndicator(player, SwapIndicatorStyle.Wobbly);

            yield return null;
            yield return null;

            LineRenderer line = indicator.GetComponent<LineRenderer>();
            Assert.Greater(line.positionCount, 2);
            Assert.AreEqual(0f, line.GetPosition(0).x, 0.01f);
            Assert.AreEqual(5f, line.GetPosition(line.positionCount - 1).x, 0.01f);
            Assert.AreEqual(0f, line.GetPosition(line.positionCount - 1).y, 0.01f, "The wave is pinned at the target.");
        }

        [UnityTest]
        public IEnumerator Indicator_PointsAtTheSameObjectTheSwapWouldUse()
        {
            SwapAbility player = CreatePlayer();
            CreateTarget(new Vector3(3f, 0f, 0f), "Front");
            CreateTarget(new Vector3(6f, 0f, 0f), "Back");
            SwapTargetIndicator indicator = AddIndicator(player, SwapIndicatorStyle.Straight);

            yield return null;
            yield return null;

            LineRenderer line = indicator.GetComponent<LineRenderer>();
            Assert.AreEqual(player.Target.Center.x, line.GetPosition(1).x, 0.01f);
            Assert.AreEqual("Front", player.Target.name);
        }

        private SwapTargetIndicator AddIndicator(SwapAbility player, SwapIndicatorStyle style)
        {
            player.gameObject.SetActive(false);
            SwapTargetIndicator indicator = player.gameObject.AddComponent<SwapTargetIndicator>();
            indicator.Style = style;
            player.gameObject.SetActive(true);
            return indicator;
        }
    }
}

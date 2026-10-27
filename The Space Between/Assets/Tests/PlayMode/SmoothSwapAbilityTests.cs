using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    /// <summary>Records how many extra jumps it was given.</summary>
    public class AirJumpRecorder : MonoBehaviour, IAirJumpGrantable
    {
        public int Granted;

        public void GrantAirJump() => Granted++;
    }

    public class SmoothSwapAbilityTests : PlayModeTestBase
    {
        private const float WaitLimitSeconds = 3f;

        private SmoothSwapAbility CreatePlayer(Vector3 position)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Smooth Player Test";
            go.transform.position = position;
            go.SetActive(false);
            go.AddComponent<AirJumpRecorder>();
            SmoothSwapAbility ability = go.AddComponent<SmoothSwapAbility>();
            go.SetActive(true);
            Physics.SyncTransforms();
            return ability;
        }

        private Swappable CreateTarget(Vector3 position, bool dynamicBody = false)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Smooth Target Test";
            go.transform.position = position;
            if (dynamicBody)
            {
                Rigidbody body = go.AddComponent<Rigidbody>();
                body.useGravity = false;
            }

            Swappable swappable = go.AddComponent<Swappable>();
            Physics.SyncTransforms();
            return swappable;
        }

        private static IEnumerator WaitForSwapToEnd(SmoothSwapAbility ability)
        {
            float start = Time.realtimeSinceStartup;
            while (ability.IsSwapping && Time.realtimeSinceStartup - start < WaitLimitSeconds)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Swap_StopsGameTimeWhilePlayingAndResumesAfterwards()
        {
            SmoothSwapAbility player = CreatePlayer(Vector3.zero);
            CreateTarget(new Vector3(5f, 0f, 0f));

            player.TrySwap();

            Assert.IsTrue(player.IsSwapping);
            Assert.AreEqual(0f, Time.timeScale);

            yield return WaitForSwapToEnd(player);

            Assert.IsFalse(player.IsSwapping);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator Swap_ShrinksBothBodiesThenRestoresTheirSize()
        {
            SmoothSwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f));
            float smallestSeen = 1f;

            player.TrySwap();
            float start = Time.realtimeSinceStartup;
            while (player.IsSwapping && Time.realtimeSinceStartup - start < WaitLimitSeconds)
            {
                smallestSeen = Mathf.Min(smallestSeen, player.transform.localScale.x, target.transform.localScale.x);
                yield return null;
            }

            Assert.Less(smallestSeen, 0.2f, "Both bodies should get small during the swap.");
            Assert.AreEqual(Vector3.one, player.transform.localScale);
            Assert.AreEqual(Vector3.one, target.transform.localScale);
        }

        [UnityTest]
        public IEnumerator Swap_ExchangesPositionsAfterTheAnimation()
        {
            SmoothSwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f));

            player.TrySwap();
            yield return WaitForSwapToEnd(player);

            Assert.AreEqual(5f, player.transform.position.x, 0.001f);
            Assert.AreEqual(0f, target.transform.position.x, 0.001f);
        }

        [UnityTest]
        public IEnumerator Swap_KeepsTheTargetsMomentum()
        {
            SmoothSwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f), dynamicBody: true);
            Rigidbody body = target.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(0f, 4f, 0f);

            player.TrySwap();
            yield return WaitForSwapToEnd(player);

            Assert.IsFalse(body.isKinematic);
            Assert.AreEqual(4f, body.linearVelocity.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator Swap_IgnoresNewAttemptsWhileAnimating()
        {
            SmoothSwapAbility player = CreatePlayer(Vector3.zero);
            CreateTarget(new Vector3(5f, 0f, 0f));

            player.TrySwap();
            bool second = player.TrySwap();

            Assert.IsFalse(second);
            yield return WaitForSwapToEnd(player);
        }

        [UnityTest]
        public IEnumerator PlayerLandingInTheAir_GetsOneExtraJump()
        {
            SmoothSwapAbility player = CreatePlayer(new Vector3(0f, 0.5f, 0f));
            CreateTarget(new Vector3(5f, 6f, 0f));
            AirJumpRecorder recorder = player.GetComponent<AirJumpRecorder>();

            player.TrySwap();
            yield return WaitForSwapToEnd(player);

            Assert.AreEqual(1, recorder.Granted);
        }

        [UnityTest]
        public IEnumerator PlayerLandingOnTheGround_GetsNoExtraJump()
        {
            GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(40f, 1f, 10f);

            SmoothSwapAbility player = CreatePlayer(new Vector3(0f, 0.5f, 0f));
            CreateTarget(new Vector3(5f, 0.5f, 0f));
            AirJumpRecorder recorder = player.GetComponent<AirJumpRecorder>();
            Physics.SyncTransforms();

            player.TrySwap();
            yield return WaitForSwapToEnd(player);

            Assert.AreEqual(0, recorder.Granted);
        }

        [UnityTest]
        public IEnumerator CharacterControllerPlayer_LandsExactlyOnTheTargetsStandingPoint()
        {
            GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(40f, 1f, 10f);

            GameObject go = Track(new GameObject("Capsule Smooth Player Test"));
            go.SetActive(false);
            go.transform.position = new Vector3(0f, 1.001f, 0f);
            var controller = go.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.5f;
            SmoothSwapAbility player = go.AddComponent<SmoothSwapAbility>();
            go.SetActive(true);

            Swappable crate = CreateTarget(new Vector3(5f, 0.5f, 0f));
            Physics.SyncTransforms();

            player.TrySwap();
            yield return WaitForSwapToEnd(player);

            Assert.AreEqual(5f, player.transform.position.x, 0.001f);
            Assert.AreEqual(0f, crate.transform.position.x, 0.001f);
        }

        [UnityTest]
        public IEnumerator DisablingTheAbilityMidSwap_RestoresTimeAndSize()
        {
            SmoothSwapAbility player = CreatePlayer(Vector3.zero);
            Swappable target = CreateTarget(new Vector3(5f, 0f, 0f), dynamicBody: true);

            player.TrySwap();
            yield return null;
            player.enabled = false;

            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(Vector3.one, player.transform.localScale);
            Assert.AreEqual(Vector3.one, target.transform.localScale);
            Assert.IsFalse(target.GetComponent<Rigidbody>().isKinematic);
            Assert.IsFalse(target.IsFrozen);
        }
    }
}

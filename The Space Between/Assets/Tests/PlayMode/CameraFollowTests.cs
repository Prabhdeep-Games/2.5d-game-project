using System.Collections;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    /// <summary>Moves an object at a constant speed so camera tests have something steady to follow.</summary>
    public class ConstantMover : MonoBehaviour
    {
        public Vector3 Velocity;

        private void Update()
        {
            transform.position += Velocity * Time.deltaTime;
        }
    }

    public class CameraFollowTests : PlayModeTestBase
    {
        private const float PlayerSpeed = 6f;
        private const float SettleSeconds = 2f;
        private const int SampleFrames = 60;

        private Transform player;
        private Camera cam;
        private SwapAbility ability;

        private void BuildRig(bool cutOnSwap)
        {
            GameObject playerObject = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            playerObject.name = "Camera Test Player";
            playerObject.SetActive(false);
            playerObject.AddComponent<ConstantMover>().Velocity = Vector3.right * PlayerSpeed;
            ability = playerObject.AddComponent<SwapAbility>();
            SetField(ability, "aimMode", SwapAimMode.Nearest);
            playerObject.SetActive(true);
            player = playerObject.transform;

            GameObject camObject = Track(new GameObject("Camera Test Main"));
            camObject.transform.position = new Vector3(0f, 1f, -8.5f);
            cam = camObject.AddComponent<Camera>();
            camObject.AddComponent<CinemachineBrain>();

            GameObject vcamObject = Track(new GameObject("Camera Test Virtual"));
            vcamObject.SetActive(false);
            vcamObject.transform.position = camObject.transform.position;
            CinemachineCamera vcam = vcamObject.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = player;
            CinemachinePositionComposer composer = vcamObject.AddComponent<CinemachinePositionComposer>();
            composer.CameraDistance = 8.5f;
            composer.Damping = new Vector3(0.6f, 0.5f, 0f);
            CameraSwapResponse response = vcamObject.AddComponent<CameraSwapResponse>();
            SetField(response, "ability", ability);
            SetField(response, "cutOnSwap", cutOnSwap);
            vcamObject.SetActive(true);
        }

        private float Offset()
        {
            return cam.transform.position.x - player.position.x;
        }

        private IEnumerator Settle()
        {
            float end = Time.time + SettleSeconds;
            while (Time.time < end)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator FollowingASteadyMover_KeepsAConstantDistanceWithoutJitter()
        {
            BuildRig(cutOnSwap: true);
            yield return Settle();

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < SampleFrames; i++)
            {
                yield return null;
                float offset = Offset();
                min = Mathf.Min(min, offset);
                max = Mathf.Max(max, offset);
            }

            Assert.Less(max - min, 0.05f, $"Camera offset varied by {max - min:F3} units while following steady movement.");
        }

        [UnityTest]
        public IEnumerator CutOnSwap_KeepsThePlayerWhereTheyWereOnScreen()
        {
            BuildRig(cutOnSwap: true);
            yield return Settle();
            float before = Offset();

            CreateCrate(player.position + Vector3.right * 6f);
            Assert.IsTrue(ability.TrySwap());
            yield return null;
            yield return null;

            Assert.AreEqual(before, Offset(), 0.4f, "With Cut on Swap the camera moved with the player.");
        }

        [UnityTest]
        public IEnumerator GlideOnSwap_SweepsTheCameraAcrossAfterwards()
        {
            BuildRig(cutOnSwap: false);
            yield return Settle();
            float before = Offset();

            CreateCrate(player.position + Vector3.right * 6f);
            Assert.IsTrue(ability.TrySwap());
            yield return null;
            yield return null;

            Assert.Less(Offset(), before - 3f, "Without a cut the camera should still be behind the player's new position.");
        }

        private Swappable CreateCrate(Vector3 position)
        {
            GameObject crate = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            crate.name = "Camera Test Crate";
            crate.transform.position = position;
            Swappable swappable = crate.AddComponent<Swappable>();
            Physics.SyncTransforms();
            return swappable;
        }
    }
}

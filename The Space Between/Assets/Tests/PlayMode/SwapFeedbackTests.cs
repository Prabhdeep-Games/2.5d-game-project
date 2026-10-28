using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class SwapFeedbackTests : PlayModeTestBase
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const float WaitLimitSeconds = 3f;

        private SwapAbility ability;
        private SwapFeedback feedback;

        private void CreatePlayer(bool smooth = false)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Feedback Player Test";
            MakeGrey(go);
            go.SetActive(false);
            ability = smooth ? go.AddComponent<SmoothSwapAbility>() : go.AddComponent<SwapAbility>();
            SetField(ability, "aimMode", SwapAimMode.Nearest);
            feedback = go.AddComponent<SwapFeedback>();
            SetField(feedback, "pulseSpeed", 0f);
            go.SetActive(true);
            Physics.SyncTransforms();
        }

        private Swappable CreateCrate(Vector3 position)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = "Feedback Crate Test";
            go.transform.position = position;
            MakeGrey(go);
            Swappable swappable = go.AddComponent<Swappable>();
            Physics.SyncTransforms();
            return swappable;
        }

        // A grey material, so a white flash and both tints can be told apart from the object's own colour.
        private void MakeGrey(GameObject go)
        {
            Material grey = Track(new Material(go.GetComponent<Renderer>().sharedMaterial));
            grey.SetColor(BaseColorId, Color.gray);
            go.GetComponent<Renderer>().sharedMaterial = grey;
        }

        // The colour the feedback has put on the object, or null when it shows its own material colour.
        private static Color? ShownTint(Component item)
        {
            Renderer target = item.GetComponent<Renderer>();
            if (!target.HasPropertyBlock())
            {
                return null;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            return block.isEmpty ? (Color?)null : block.GetColor(BaseColorId);
        }

        private static Color OwnColor(Component item)
        {
            return item.GetComponent<Renderer>().sharedMaterial.GetColor(BaseColorId);
        }

        [UnityTest]
        public IEnumerator ObjectsInRange_Glow_AndObjectsOutOfRangeDoNot()
        {
            CreatePlayer();
            Swappable near = CreateCrate(new Vector3(0f, 5f, 0f));
            Swappable aimed = CreateCrate(new Vector3(3f, 0f, 0f));
            Swappable far = CreateCrate(new Vector3(30f, 0f, 0f));
            yield return null;

            Assert.AreEqual(2, feedback.ObjectsInRange);
            Assert.IsNotNull(ShownTint(near), "An object within range should glow.");
            Assert.IsNotNull(ShownTint(aimed));
            Assert.IsNull(ShownTint(far), "An object out of range should look normal.");
        }

        [UnityTest]
        public IEnumerator AimedObject_ShowsValidOrRejectedColour()
        {
            CreatePlayer();
            Swappable crate = CreateCrate(new Vector3(3f, 0f, 0f));
            yield return null;
            yield return null;
            Color valid = ShownTint(crate).Value;

            // A test resumes before LateUpdate, so the new tint shows one frame later.
            crate.CanSwap = false;
            yield return null;
            yield return null;
            Color rejected = ShownTint(crate).Value;

            Assert.Greater(valid.g, valid.r, "A target that would work leans green.");
            Assert.Greater(rejected.r, rejected.g, "A target that would be rejected leans red.");
        }

        [UnityTest]
        public IEnumerator RejectedSwap_ShowsTheReasonAboveTheTarget_ThenHidesIt()
        {
            CreatePlayer();
            Swappable crate = CreateCrate(new Vector3(3f, 0f, 0f));
            crate.CanSwap = false;
            SetField(feedback, "reasonSeconds", 0.3f);
            yield return null;

            Assert.IsFalse(ability.TrySwap());
            yield return null;

            Assert.IsTrue(feedback.IsShowingReason);
            Assert.AreEqual(SwapRejectionText.Describe(SwapRejection.TargetLocked), feedback.LastMessage);

            float end = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
            }

            Assert.IsFalse(feedback.IsShowingReason, "The reason should disappear after its time.");
        }

        [UnityTest]
        public IEnumerator NothingAimedAt_StillExplainsTheRejection()
        {
            CreatePlayer();
            yield return null;

            Assert.IsFalse(ability.TrySwap());
            yield return null;

            Assert.IsTrue(feedback.IsShowingReason);
            Assert.AreEqual(SwapRejectionText.Describe(SwapRejection.NoTargetInRange), feedback.LastMessage);
        }

        [UnityTest]
        public IEnumerator InstantSwap_FlashesBothBodies()
        {
            CreatePlayer();
            Swappable crate = CreateCrate(new Vector3(3f, 0f, 0f));
            yield return null;

            Assert.IsTrue(ability.TrySwap());
            yield return null;

            Color crateTint = ShownTint(crate).Value;
            Color playerTint = ShownTint(ability).Value;
            Assert.Greater(crateTint.grayscale, OwnColor(crate).grayscale, "The crate should flash brighter.");
            Assert.Greater(playerTint.grayscale, OwnColor(ability).grayscale, "The player should flash brighter.");
        }

        [UnityTest]
        public IEnumerator SmoothSwap_FlashesOnlyWhenTheAnimationEnds()
        {
            CreatePlayer(smooth: true);
            CreateCrate(new Vector3(3f, 0f, 0f));
            yield return null;

            Assert.IsTrue(ability.TrySwap());
            yield return null;
            Assert.IsNull(ShownTint(ability), "No flash while the bodies are still moving.");

            float start = Time.realtimeSinceStartup;
            while (ability.IsSwapping && Time.realtimeSinceStartup - start < WaitLimitSeconds)
            {
                yield return null;
            }

            yield return null;
            Assert.IsNotNull(ShownTint(ability), "The flash should play once the swap has finished.");
        }

        [UnityTest]
        public IEnumerator DisablingTheFeedback_RestoresEveryMaterialColour()
        {
            CreatePlayer();
            Swappable crate = CreateCrate(new Vector3(3f, 0f, 0f));
            yield return null;
            Assert.IsNotNull(ShownTint(crate));

            feedback.enabled = false;

            Assert.IsNull(ShownTint(crate));
        }
    }
}

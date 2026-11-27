using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The Central Core going down, once every anchor is displaced. A short camera-only sequence (about six seconds):
    /// control is held, the view settles on the core, its lights fail one by one, the room drops to emergency light,
    /// and the sealed exit opens. No dialogue and no animation of the protagonist.
    /// </summary>
    public sealed class CoreShutdown : MonoBehaviour
    {
        [Header("Core")]
        [Tooltip("Every anchor that must be displaced.")]
        [SerializeField] private List<CoreAnchor> anchors = new List<CoreAnchor>();

        [Tooltip("Powered when the core is down, such as the chamber's sealed exit door.")]
        [SerializeField] private List<Mechanism> exitDoors = new List<Mechanism>();

        [Header("Sequence")]
        [Tooltip("The still view of the core held during the shutdown. Off until then.")]
        [SerializeField] private CinemachineCamera coreView;

        [Tooltip("The core's own light strips, switched off one after another.")]
        [SerializeField] private List<Renderer> coreLights = new List<Renderer>();

        [Tooltip("Room lights that fade down to the given fraction.")]
        [SerializeField] private List<Light> roomLights = new List<Light>();

        [Tooltip("How much of the room light is left after the shutdown.")]
        [SerializeField, Range(0f, 1f)] private float remainingLight = 0.25f;

        [Tooltip("Switched on when the power fails: the emergency lighting.")]
        [SerializeField] private GameObject emergencyLights;

        [Tooltip("Real seconds before the view moves to the core.")]
        [SerializeField, Min(0f)] private float settleSeconds = 0.6f;

        [Tooltip("Real seconds over which the lights fail.")]
        [SerializeField, Min(0.1f)] private float failSeconds = 3f;

        [Tooltip("Real seconds held in emergency light before control returns.")]
        [SerializeField, Min(0f)] private float holdSeconds = 2.2f;

        [ReadOnly, SerializeField] private string stage = "Running";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private bool started;

        public string Stage => stage;

        public bool IsDown => stage == "Down";

        public int Displaced
        {
            get
            {
                int count = 0;
                foreach (CoreAnchor anchor in anchors)
                {
                    if (anchor != null && anchor.Released)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int AnchorCount => anchors.Count;

        private void Awake()
        {
            if (coreView != null)
            {
                coreView.enabled = false;
            }

            if (emergencyLights != null)
            {
                emergencyLights.SetActive(false);
            }
        }

        private void Update()
        {
            if (started || anchors.Count == 0 || Displaced < anchors.Count)
            {
                return;
            }

            FacilityUI ui = FacilityUI.Current;
            if (ui != null && (ui.IsReturning || ui.IsPaused || ui.IsLeaving || ui.IsEnding))
            {
                return;
            }

            started = true;
            StartCoroutine(Play(ui));
        }

        private IEnumerator Play(FacilityUI ui)
        {
            stage = "Settling";
            if (ui != null)
            {
                ui.SetCutscene(true);
            }

            yield return new WaitForSecondsRealtime(settleSeconds);
            if (coreView != null)
            {
                coreView.enabled = true;
                coreView.Prioritize();
            }

            stage = "Failing";
            var start = new float[roomLights.Count];
            for (int i = 0; i < roomLights.Count; i++)
            {
                start[i] = roomLights[i] != null ? roomLights[i].intensity : 0f;
            }

            int switchedOff = 0;
            float began = Time.unscaledTime;
            while (Time.unscaledTime - began < failSeconds)
            {
                float t = (Time.unscaledTime - began) / failSeconds;
                for (int i = 0; i < roomLights.Count; i++)
                {
                    if (roomLights[i] != null)
                    {
                        roomLights[i].intensity = Mathf.Lerp(start[i], start[i] * remainingLight, t);
                    }
                }

                int due = Mathf.FloorToInt(t * (coreLights.Count + 1));
                while (switchedOff < due && switchedOff < coreLights.Count)
                {
                    SwitchOff(coreLights[switchedOff++]);
                }

                yield return null;
            }

            while (switchedOff < coreLights.Count)
            {
                SwitchOff(coreLights[switchedOff++]);
            }

            if (emergencyLights != null)
            {
                emergencyLights.SetActive(true);
            }

            foreach (Mechanism door in exitDoors)
            {
                if (door != null)
                {
                    door.SetPowered(true);
                }
            }

            stage = "Emergency";
            yield return new WaitForSecondsRealtime(holdSeconds);
            if (coreView != null)
            {
                coreView.enabled = false;
            }

            if (ui != null)
            {
                ui.SetCutscene(false);
            }

            stage = "Down";
        }

        private static void SwitchOff(Renderer strip)
        {
            if (strip == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            strip.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(0.145f, 0.176f, 0.196f));
            block.SetColor(EmissionColorId, Color.black);
            strip.SetPropertyBlock(block);
        }
    }
}

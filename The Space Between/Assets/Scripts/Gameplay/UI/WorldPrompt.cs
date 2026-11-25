using UnityEngine;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// A short prompt floating in the world beside an object, such as "E  OBSERVE". It fades in and out instead of
    /// popping, on real time. Shared by the viewing plate and the control panels, which decide when it shows.
    /// </summary>
    internal sealed class WorldPrompt
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";
        private const float FadeSeconds = 0.25f;

        private readonly TextMesh text;
        private readonly Color color;
        private bool visible;
        private float alpha;

        public WorldPrompt(Transform parent, string objectName, Vector3 localPosition, int fontSize, float size, Color color, Font font, TextAnchor anchor)
        {
            var holder = new GameObject(objectName);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            holder.layer = LayerMask.NameToLayer("Ignore Raycast");

            text = holder.AddComponent<TextMesh>();
            Font used = font != null ? font : Resources.GetBuiltinResource<Font>(BuiltinFontName);
            text.font = used;
            holder.GetComponent<MeshRenderer>().sharedMaterial = used.material;
            text.fontSize = fontSize;
            text.characterSize = size;
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            this.color = color;
            text.color = Transparent();
            holder.SetActive(false);
        }

        /// <summary>Whether the prompt is meant to be up (it may still be fading in).</summary>
        public bool Visible => visible;

        public string Text
        {
            get => text != null ? text.text : string.Empty;
            set
            {
                if (text != null && text.text != value)
                {
                    text.text = value;
                }
            }
        }

        public void Show(bool show)
        {
            visible = show;
            if (show && text != null)
            {
                text.gameObject.SetActive(true);
            }
        }

        public void HideNow()
        {
            visible = false;
            alpha = 0f;
            if (text != null)
            {
                text.color = Transparent();
                text.gameObject.SetActive(false);
            }
        }

        /// <summary>Call once a frame.</summary>
        public void Tick()
        {
            if (text == null)
            {
                return;
            }

            alpha = Mathf.MoveTowards(alpha, visible ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            Color c = color;
            c.a *= alpha;
            text.color = c;
            if (!visible && alpha <= 0f && text.gameObject.activeSelf)
            {
                text.gameObject.SetActive(false);
            }
        }

        /// <summary>Replaces "{token}" in a prompt with the action's real key on the device used last, such as "E".</summary>
        public static string Fill(string prompt, string token, InputAction action)
        {
            string tag = "{" + token + "}";
            if (string.IsNullOrEmpty(prompt) || !prompt.Contains(tag))
            {
                return prompt;
            }

            return prompt.Replace(tag, TutorialGuide.Label(action, TutorialGuide.LastDevice).ToUpperInvariant());
        }

        private Color Transparent()
        {
            Color c = color;
            c.a = 0f;
            return c;
        }
    }
}

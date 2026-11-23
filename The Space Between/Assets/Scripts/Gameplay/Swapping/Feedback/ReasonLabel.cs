using System;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// A line of text in the world that explains a rejected swap, then fades out.
    ///
    /// The label is a separate root object, not a child of the player, so it stays where the rejection happened while
    /// the player moves on. Timing uses real time, because a Shrink swap stops game time.
    /// </summary>
    internal sealed class ReasonLabel : IDisposable
    {
        private const int FontSize = 64;
        private const float FadeSeconds = 0.3f;
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private readonly TextMesh text;
        private float visibleUntil = float.NegativeInfinity;

        public bool IsVisible => text != null && text.gameObject.activeSelf;

        public ReasonLabel(string ownerName, float size)
        {
            GameObject holder = new GameObject($"{ownerName} Swap Reason");
            text = holder.AddComponent<TextMesh>();

            Font font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            text.font = font;
            holder.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = FontSize;
            text.characterSize = size;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            holder.SetActive(false);
        }

        public void Show(string message, Vector3 position, float seconds)
        {
            text.transform.position = position;
            text.text = message;
            visibleUntil = Time.unscaledTime + seconds;
        }

        public void Hide()
        {
            visibleUntil = float.NegativeInfinity;
            if (text != null)
            {
                text.gameObject.SetActive(false);
            }
        }

        /// <summary>Call once a frame. Shows the label while its time lasts and fades it out over the last moment.</summary>
        public void Tick(Color color)
        {
            float left = visibleUntil - Time.unscaledTime;
            bool visible = left > 0f;
            text.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            color.a = Mathf.Clamp01(left / FadeSeconds);
            text.color = color;
        }

        public void Dispose()
        {
            if (text != null)
            {
                UnityEngine.Object.Destroy(text.gameObject);
            }
        }
    }
}

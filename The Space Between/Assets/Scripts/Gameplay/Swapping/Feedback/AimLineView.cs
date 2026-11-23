using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheSpaceBetween
{
    /// <summary>
    /// Draws the aim line of a swap: straight, or with a gentle travelling wave.
    ///
    /// Owns a child object with a LineRenderer, created at runtime so the player's inspector stays clean. Knows nothing
    /// about the swap rules; the owner decides where the line goes and what colour it is.
    /// </summary>
    internal sealed class AimLineView : IDisposable
    {
        private const float FullTurn = Mathf.PI * 2f;
        private const int StraightPointCount = 2;
        private const string HolderName = "Swap Aim Line";
        private const string FallbackShaderName = "Sprites/Default";

        /// <summary>The shape of a wobbly line.</summary>
        public readonly struct Wave
        {
            public readonly int Points;
            public readonly float Amplitude;
            public readonly float Wavelength;
            public readonly float Speed;
            public readonly bool PinEnds;

            public Wave(int points, float amplitude, float wavelength, float speed, bool pinEnds)
            {
                Points = points;
                Amplitude = amplitude;
                Wavelength = wavelength;
                Speed = speed;
                PinEnds = pinEnds;
            }
        }

        private readonly LineRenderer line;
        private readonly Material fallbackMaterial;
        private Vector3[] points = new Vector3[StraightPointCount];

        /// <summary>The LineRenderer that draws the aim.</summary>
        public LineRenderer Line => line;

        public AimLineView(Transform parent, Material material)
        {
            GameObject holder = new GameObject(HolderName) { layer = parent.gameObject.layer };
            holder.transform.SetParent(parent, false);

            line = holder.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = StraightPointCount;
            line.enabled = false;

            if (material == null)
            {
                Shader shader = Shader.Find(FallbackShaderName);
                if (shader != null)
                {
                    fallbackMaterial = new Material(shader) { name = HolderName + " (fallback)" };
                    material = fallbackMaterial;
                }
            }

            line.sharedMaterial = material;
        }

        public void Hide()
        {
            line.enabled = false;
        }

        public void ShowStraight(Vector3 start, Vector3 end, Color color, float width)
        {
            Style(color, width);
            line.positionCount = StraightPointCount;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        // The wave runs sideways within the game plane, so the side camera sees it. Time is real time, because a
        // Shrink swap stops game time.
        public void ShowWobbly(Vector3 start, Vector3 end, Color color, float width, Wave wave)
        {
            Style(color, width);

            int count = Mathf.Max(wave.Points, StraightPointCount);
            if (points.Length != count)
            {
                points = new Vector3[count];
            }

            Vector3 along = end - start;
            float length = along.magnitude;
            Vector3 direction = length > 0.0001f ? along / length : Vector3.right;
            Vector3 sideways = new Vector3(-direction.y, direction.x, 0f);
            float phase = Time.unscaledTime * wave.Speed * FullTurn;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float height = Mathf.Sin(t * length / wave.Wavelength * FullTurn - phase);
                float pin = wave.PinEnds ? Mathf.Sin(t * Mathf.PI) : 1f;
                points[i] = start + along * t + sideways * (height * pin * wave.Amplitude);
            }

            line.positionCount = count;
            line.SetPositions(points);
        }

        public void Dispose()
        {
            if (fallbackMaterial != null)
            {
                UnityEngine.Object.Destroy(fallbackMaterial);
            }
        }

        private void Style(Color color, float width)
        {
            line.enabled = true;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
        }
    }
}

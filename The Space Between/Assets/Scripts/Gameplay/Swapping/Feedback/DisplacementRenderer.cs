using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheSpaceBetween
{
    /// <summary>
    /// Draws the displacement of a swap: each body splits into two pale silhouettes that drift apart, peak when the
    /// bodies trade places, and close up again on arrival.
    ///
    /// The silhouettes are the bodies' own meshes, every submesh of them, so it works on any swappable object,
    /// including a character with several materials. Nothing is added to the scene: the meshes are drawn directly
    /// each frame. Timing uses real time, because a Shrink swap stops game time.
    /// </summary>
    internal sealed class DisplacementRenderer : IDisposable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>How the silhouettes look at the middle of the swap.</summary>
        public readonly struct Look
        {
            public readonly Material Material;
            public readonly Color Color;
            public readonly float Opacity;
            public readonly float Separation;
            public readonly float Scale;

            public Look(Material material, Color color, float opacity, float separation, float scale)
            {
                Material = material;
                Color = color;
                Opacity = opacity;
                Separation = separation;
                Scale = scale;
            }
        }

        private struct Silhouette
        {
            public Mesh Mesh;
            public Transform Transform;
            public int Layer;
        }

        private readonly List<Silhouette> silhouettes = new List<Silhouette>();
        private readonly Dictionary<SkinnedMeshRenderer, Mesh> bakedMeshes = new Dictionary<SkinnedMeshRenderer, Mesh>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Swappable first;
        private Swappable second;
        private float startTime;
        private float seconds;

        public bool IsPlaying { get; private set; }

        /// <summary>Captures both bodies as they are now and starts the effect.</summary>
        public void Begin(Swappable self, Swappable other, float duration)
        {
            first = self;
            second = other;
            silhouettes.Clear();
            Collect(self);
            Collect(other);

            seconds = Mathf.Max(duration, 0.0001f);
            startTime = Time.unscaledTime;
            IsPlaying = true;
        }

        public void Stop()
        {
            IsPlaying = false;
        }

        /// <summary>Call once a frame. Draws the silhouettes and stops by itself when the time is up.</summary>
        public void Draw(Look look)
        {
            if (!IsPlaying)
            {
                return;
            }

            float progress = (Time.unscaledTime - startTime) / seconds;
            if (progress >= 1f || first == null || second == null || look.Material == null)
            {
                IsPlaying = false;
                return;
            }

            // Rises to full at the middle, when the bodies trade places, and falls back to nothing.
            float strength = Mathf.Sin(progress * Mathf.PI);
            Vector3 offset = SeparationDirection() * (look.Separation * strength);

            Color tint = look.Color;
            tint.a = look.Opacity * strength;
            block.SetColor(BaseColorId, tint);

            RenderParams parameters = new RenderParams(look.Material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                matProps = block
            };

            Matrix4x4 enlarge = Matrix4x4.Scale(Vector3.one * look.Scale);
            foreach (Silhouette silhouette in silhouettes)
            {
                if (silhouette.Transform == null || silhouette.Mesh == null)
                {
                    continue;
                }

                parameters.layer = silhouette.Layer;
                Matrix4x4 matrix = silhouette.Transform.localToWorldMatrix * enlarge;
                DrawMesh(parameters, silhouette.Mesh, Matrix4x4.Translate(offset) * matrix);
                DrawMesh(parameters, silhouette.Mesh, Matrix4x4.Translate(-offset) * matrix);
            }
        }

        public void Dispose()
        {
            foreach (Mesh baked in bakedMeshes.Values)
            {
                UnityEngine.Object.Destroy(baked);
            }

            bakedMeshes.Clear();
            silhouettes.Clear();
        }

        // Along the line between the two bodies, flat in the game plane.
        private Vector3 SeparationDirection()
        {
            Vector3 line = second.Center - first.Center;
            line.z = 0f;
            return line.sqrMagnitude > 0.0001f ? line.normalized : Vector3.right;
        }

        // Only what can be seen: the swap needs the body's shape, not its lines or labels.
        private void Collect(Swappable body)
        {
            if (body == null)
            {
                return;
            }

            foreach (MeshRenderer meshRenderer in body.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                bool visibleMesh = filter != null && filter.sharedMesh != null && meshRenderer.enabled;
                if (visibleMesh && meshRenderer.GetComponent<TextMesh>() == null)
                {
                    Add(filter.sharedMesh, meshRenderer);
                }
            }

            foreach (SkinnedMeshRenderer skinned in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skinned.enabled)
                {
                    Add(Bake(skinned), skinned);
                }
            }
        }

        // An animated body is captured once, in the pose it has as the swap starts. Game time stops then, so it holds.
        // Baked without the transform's scale, because the silhouette is drawn with the transform's matrix.
        private Mesh Bake(SkinnedMeshRenderer skinned)
        {
            if (!bakedMeshes.TryGetValue(skinned, out Mesh baked))
            {
                baked = new Mesh { name = skinned.name + " (swap silhouette)" };
                bakedMeshes.Add(skinned, baked);
            }

            skinned.BakeMesh(baked, false);
            return baked;
        }

        private void Add(Mesh mesh, Renderer source)
        {
            silhouettes.Add(new Silhouette { Mesh = mesh, Transform = source.transform, Layer = source.gameObject.layer });
        }

        // Every submesh, so a body with several materials (head, clothes, hair, jacket) is outlined as a whole.
        private static void DrawMesh(RenderParams parameters, Mesh mesh, Matrix4x4 matrix)
        {
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                Graphics.RenderMesh(parameters, mesh, submesh, matrix);
            }
        }
    }
}

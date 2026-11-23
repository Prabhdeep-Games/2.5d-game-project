using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Tints swappable objects without touching their materials.
    ///
    /// Each frame the owner calls <see cref="BeginFrame"/>, asks for the tints it wants, then calls <see cref="Apply"/>.
    /// Objects that were tinted last frame but not asked for again get their own colours back. A later request for the
    /// same object wins, so the owner asks for the most important tint last.
    ///
    /// Colours are set with MaterialPropertyBlocks. A renderer with several materials (a character) is tinted per
    /// material, so every part keeps its own base colour.
    /// </summary>
    internal sealed class TintPainter
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        private struct Tint
        {
            public Color Color;
            public float Amount;
        }

        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly List<Material> materialBuffer = new List<Material>();
        private readonly Dictionary<Swappable, Renderer[]> renderers = new Dictionary<Swappable, Renderer[]>();
        private readonly Dictionary<Swappable, Tint> wanted = new Dictionary<Swappable, Tint>();
        private readonly List<Swappable> tinted = new List<Swappable>();

        public void BeginFrame()
        {
            wanted.Clear();
        }

        /// <summary>Blends the object's colours toward <paramref name="color"/> by <paramref name="amount"/> (0 to 1).</summary>
        public void Request(Swappable item, Color color, float amount)
        {
            if (item != null)
            {
                wanted[item] = new Tint { Color = color, Amount = amount };
            }
        }

        /// <summary>A flash that starts at full strength at <paramref name="startTime"/> and fades out over <paramref name="seconds"/>.</summary>
        public void RequestFlash(Swappable item, Color color, float startTime, float seconds)
        {
            float amount = 1f - (Time.unscaledTime - startTime) / seconds;
            if (amount > 0f)
            {
                Request(item, color, amount);
            }
        }

        public void Apply()
        {
            for (int i = tinted.Count - 1; i >= 0; i--)
            {
                if (tinted[i] == null || !wanted.ContainsKey(tinted[i]))
                {
                    Paint(tinted[i], null);
                    tinted.RemoveAt(i);
                }
            }

            foreach (KeyValuePair<Swappable, Tint> entry in wanted)
            {
                Paint(entry.Key, entry.Value);
                if (!tinted.Contains(entry.Key))
                {
                    tinted.Add(entry.Key);
                }
            }
        }

        /// <summary>Gives every tinted object its own colours back.</summary>
        public void ClearAll()
        {
            foreach (Swappable item in tinted)
            {
                Paint(item, null);
            }

            tinted.Clear();
            wanted.Clear();
        }

        // No tint puts the material colours back.
        private void Paint(Swappable item, Tint? tint)
        {
            if (item == null)
            {
                return;
            }

            foreach (Renderer target in RenderersOf(item))
            {
                if (target == null)
                {
                    continue;
                }

                target.GetSharedMaterials(materialBuffer);
                if (materialBuffer.Count <= 1)
                {
                    PaintWholeRenderer(target, materialBuffer.Count == 1 ? materialBuffer[0] : null, tint);
                }
                else
                {
                    for (int index = 0; index < materialBuffer.Count; index++)
                    {
                        PaintMaterial(target, index, materialBuffer[index], tint);
                    }
                }
            }
        }

        private void PaintWholeRenderer(Renderer target, Material material, Tint? tint)
        {
            if (tint == null || material == null)
            {
                target.SetPropertyBlock(null);
                return;
            }

            target.GetPropertyBlock(block);
            SetBlendedColor(material, tint.Value);
            target.SetPropertyBlock(block);
        }

        private void PaintMaterial(Renderer target, int index, Material material, Tint? tint)
        {
            if (tint == null || material == null)
            {
                target.SetPropertyBlock(null, index);
                return;
            }

            target.GetPropertyBlock(block, index);
            SetBlendedColor(material, tint.Value);
            target.SetPropertyBlock(block, index);
        }

        private void SetBlendedColor(Material material, Tint tint)
        {
            int colorId = material.HasProperty(BaseColorId) ? BaseColorId : LegacyColorId;
            Color own = material.HasProperty(colorId) ? material.GetColor(colorId) : Color.white;
            block.SetColor(colorId, Color.Lerp(own, tint.Color, tint.Amount));
        }

        // The visible meshes of an object. Lines and text are left alone.
        private Renderer[] RenderersOf(Swappable item)
        {
            if (renderers.TryGetValue(item, out Renderer[] found))
            {
                return found;
            }

            List<Renderer> meshes = new List<Renderer>();
            foreach (Renderer candidate in item.GetComponentsInChildren<Renderer>())
            {
                bool isMesh = candidate is MeshRenderer || candidate is SkinnedMeshRenderer;
                if (isMesh && candidate.GetComponent<TextMesh>() == null)
                {
                    meshes.Add(candidate);
                }
            }

            found = meshes.ToArray();
            renderers[item] = found;
            return found;
        }
    }
}

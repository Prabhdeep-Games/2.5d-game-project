using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Draws a visible cable from a control panel or security camera to every mechanism it drives, so the player
    /// can see which panel opens which door. Each cable runs level from the source, then straight up or down to
    /// the mechanism, slightly behind the play plane. Where it passes through a wall it is hidden, like a
    /// conduit in the building.
    ///
    /// A plain helper, owned by the component that draws the cables.
    /// </summary>
    public class PowerWires
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly List<LineRenderer> lines = new List<LineRenderer>();

        // Created on first use: a MaterialPropertyBlock may not be created while the owning component is constructed.
        private MaterialPropertyBlock block;

        /// <summary>Creates one cable per target as children of the source. Targets that are missing are skipped.</summary>
        public void Build(Transform source, IList<Mechanism> targets, Material material, float width, float depth)
        {
            Clear();
            if (material == null)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] == null)
                {
                    continue;
                }

                var holder = new GameObject($"Wire to {targets[i].name}");
                holder.transform.SetParent(source, false);
                holder.layer = source.gameObject.layer;

                LineRenderer line = holder.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = true;
                line.widthMultiplier = width;
                line.numCapVertices = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;

                Vector3 from = source.position;
                Vector3 to = targets[i].transform.position;
                line.positionCount = 3;
                line.SetPosition(0, new Vector3(from.x, from.y, depth));
                line.SetPosition(1, new Vector3(to.x, from.y, depth));
                line.SetPosition(2, new Vector3(to.x, to.y, depth));
                lines.Add(line);
            }
        }

        /// <summary>Colours every cable, for example green while the panel is on.</summary>
        public void SetColor(Color color)
        {
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null)
                {
                    continue;
                }

                lines[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                lines[i].SetPropertyBlock(block);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != null)
                {
                    Object.Destroy(lines[i].gameObject);
                }
            }

            lines.Clear();
        }
    }
}

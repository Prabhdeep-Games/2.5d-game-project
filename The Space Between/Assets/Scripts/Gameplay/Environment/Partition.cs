using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Marks a wall piece as one of the two fixed partitions and keeps it following the rules for its kind,
    /// so a level designer cannot accidentally build one that behaves differently from how it looks.
    ///
    ///   Glass partition:     blocks movement, but not sight. The swap, the aim and security cameras all see
    ///                        through it. It sits on the Glass layer. Visibility without access.
    ///   Industrial barrier:  blocks movement, sight and therefore swaps. It sits on the Default layer and is
    ///                        never a wall run surface. Physical restriction.
    ///
    /// The layer is set automatically in the editor. Neither kind may be swapped or wall run.
    /// </summary>
    [DisallowMultipleComponent]
    public class Partition : MonoBehaviour
    {
        /// <summary>The two fixed partitions.</summary>
        public enum PartitionKind
        {
            Glass,
            IndustrialBarrier
        }

        private const string GlassLayerName = "Glass";
        private const int DefaultLayer = 0;

        //==================== KIND =====================
        [Header("Kind")]
        [Tooltip("Glass: blocks movement, not sight. Industrial Barrier: blocks movement and sight.")]
        [SerializeField] private PartitionKind kind = PartitionKind.Glass;

        public PartitionKind Kind => kind;

        /// <summary>True when this partition lets sight (and so swaps and cameras) through.</summary>
        public bool PassesSight => kind == PartitionKind.Glass;

        /// <summary>The layer this kind belongs on.</summary>
        public int RequiredLayer => kind == PartitionKind.Glass ? LayerMask.NameToLayer(GlassLayerName) : DefaultLayer;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            foreach (string problem in FindProblems())
            {
                Debug.LogWarning($"[{nameof(Partition)}] '{name}': {problem}", this);
            }
        }

        private void OnValidate()
        {
            int layer = RequiredLayer;
            if (layer >= 0 && gameObject.layer != layer)
            {
                gameObject.layer = layer;
            }
        }

        /// <summary>Every way this object breaks the rules of its kind. Empty when it is set up correctly.</summary>
        public List<string> FindProblems()
        {
            var problems = new List<string>();
            int layer = RequiredLayer;
            if (layer < 0)
            {
                problems.Add($"the project has no '{GlassLayerName}' layer.");
            }
            else if (gameObject.layer != layer)
            {
                problems.Add($"a {kind} must be on the {LayerMask.LayerToName(layer)} layer.");
            }

            if (GetComponentInParent<WallRunSurface>() != null)
            {
                problems.Add("a partition must never be a wall run surface. Remove the WallRunSurface.");
            }

            if (GetComponentInParent<Swappable>() != null)
            {
                problems.Add("a partition must never be swappable. Remove the Swappable.");
            }

            Collider solid = GetComponent<Collider>();
            if (solid == null || solid.isTrigger)
            {
                problems.Add("a partition needs a solid (non-trigger) collider to block movement.");
            }

            return problems;
        }
    }
}

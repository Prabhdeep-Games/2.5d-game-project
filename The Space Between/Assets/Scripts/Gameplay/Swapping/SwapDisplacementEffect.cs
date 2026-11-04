using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Shows a swap as a brief disruption of space, not as a burst of energy: while the swap plays, each body separates
    /// into two closely aligned pale silhouettes, one to each side along the line between the bodies, which then close
    /// up again once the bodies have traded places.
    ///
    /// Nothing is created in the scene. The silhouettes are the bodies' own meshes, drawn for the length of the swap,
    /// so the effect works on any swappable object without setup. It is presentation only and never affects the swap.
    ///
    /// Put this on the player next to the SwapAbility. Pair it with a Smallest Scale of 1 on the smooth swap, so the
    /// bodies stay at full size. All timing uses real time, because a smooth swap stops game time.
    /// </summary>
    public class SwapDisplacementEffect : MonoBehaviour
    {
        private const float InstantSwapSeconds = 0.25f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private struct Silhouette
        {
            public Mesh Mesh;
            public Transform Transform;
            public int Layer;
        }

        //==================== TARGET =====================
        [Header("Target")]
        [Tooltip("The ability to show the swap for. Empty means the SwapAbility on this object.")]
        [SerializeField] private SwapAbility ability;

        //==================== LOOK =====================
        [Header("Look")]
        [Tooltip("Material for the silhouettes. Use an unlit, transparent material that draws back faces only, so they read as a contour around the body.")]
        [SerializeField] private Material contourMaterial;

        [Tooltip("Colour of the silhouettes. The swap's own colour; keep it the same as the swap feedback.")]
        [SerializeField] private Color color = new Color(0.66f, 0.86f, 0.88f, 1f);

        [Tooltip("How far each silhouette separates from the body at the middle of the swap, in world units. Keep it small, so the destination stays readable.")]
        [SerializeField, Range(0f, 1f)] private float separation = 0.22f;

        [Tooltip("How much larger than the body the silhouettes are drawn. A little larger shows as a thin contour.")]
        [SerializeField, Range(1f, 1.3f)] private float contourScale = 1.06f;

        [Tooltip("How opaque the silhouettes are at the middle of the swap.")]
        [SerializeField, Range(0f, 1f)] private float peakOpacity = 0.75f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Whether the effect is showing right now.")]
        [ReadOnly, SerializeField] private bool isPlaying;

        private readonly List<Silhouette> silhouettes = new List<Silhouette>();
        private Swappable self;
        private Swappable other;
        private MaterialPropertyBlock block;
        private float startTime;
        private float duration;

        public bool IsPlaying => isPlaying;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            if (ability == null)
            {
                ability = GetComponent<SwapAbility>();
            }

            self = GetComponent<Swappable>();
            block = new MaterialPropertyBlock();

            if (contourMaterial == null)
            {
                Debug.LogWarning($"[{nameof(SwapDisplacementEffect)}] '{name}' has no Contour Material, so swaps show no effect.", this);
            }
        }

        private void OnEnable()
        {
            if (ability != null)
            {
                ability.Swapped += OnSwapped;
            }
        }

        private void OnDisable()
        {
            if (ability != null)
            {
                ability.Swapped -= OnSwapped;
            }

            isPlaying = false;
        }

        // Drawn every frame the effect lasts. LateUpdate also runs while game time is stopped.
        private void LateUpdate()
        {
            if (!isPlaying)
            {
                return;
            }

            float progress = (Time.unscaledTime - startTime) / duration;
            if (progress >= 1f || other == null)
            {
                isPlaying = false;
                return;
            }

            // Rises to full at the middle, when the bodies trade places, and falls back to nothing.
            float strength = Mathf.Sin(progress * Mathf.PI);

            Vector3 line = other.Center - self.Center;
            line.z = 0f;
            Vector3 offset = (line.sqrMagnitude > 0.0001f ? line.normalized : Vector3.right) * (separation * strength);

            Color tint = color;
            tint.a = peakOpacity * strength;
            block.SetColor(BaseColorId, tint);

            Matrix4x4 enlarge = Matrix4x4.Scale(Vector3.one * contourScale);
            for (int i = 0; i < silhouettes.Count; i++)
            {
                Silhouette silhouette = silhouettes[i];
                if (silhouette.Transform == null)
                {
                    continue;
                }

                Matrix4x4 matrix = silhouette.Transform.localToWorldMatrix * enlarge;
                Draw(silhouette, Matrix4x4.Translate(offset) * matrix);
                Draw(silhouette, Matrix4x4.Translate(-offset) * matrix);
            }
        }

        //==================== EFFECT =====================
        private void OnSwapped(Swappable target)
        {
            if (contourMaterial == null)
            {
                return;
            }

            other = target;
            silhouettes.Clear();
            Collect(self);
            Collect(target);

            SmoothSwapAbility smooth = ability as SmoothSwapAbility;
            duration = smooth != null ? smooth.TotalDuration : InstantSwapSeconds;
            startTime = Time.unscaledTime;
            isPlaying = true;
        }

        // Only what can be seen: the swap needs the body's shape, not its effects or labels.
        private void Collect(Swappable body)
        {
            foreach (MeshRenderer meshRenderer in body.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && meshRenderer.enabled)
                {
                    silhouettes.Add(new Silhouette { Mesh = filter.sharedMesh, Transform = meshRenderer.transform, Layer = meshRenderer.gameObject.layer });
                }
            }
        }

        private void Draw(Silhouette silhouette, Matrix4x4 matrix)
        {
            Graphics.DrawMesh(silhouette.Mesh, matrix, contourMaterial, silhouette.Layer, null, 0, block, false, false);
        }
    }
}

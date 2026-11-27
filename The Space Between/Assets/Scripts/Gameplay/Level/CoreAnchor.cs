using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// One of the Central Core's anchors: a swap housing held in a cradle. The anchor counts as displaced the moment the
    /// housing leaves the cradle's Box Collider, which in practice means the player swapped with it. It stays displaced
    /// afterwards, even if a room restart puts the housing back, so progress on the core is never lost to a fall.
    /// The collider should be a trigger on the Ignore Raycast layer, around the housing's resting place.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CoreAnchor : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("The housing held in this cradle.")]
        [SerializeField] private Swappable anchor;

        [Tooltip("Lit while the anchor holds, dark once it is displaced. Optional.")]
        [SerializeField] private Renderer indicator;

        [Tooltip("The indicator's colour while the anchor holds: the restrained security amber.")]
        [SerializeField] private Color holdingColor = new Color(0.827f, 0.631f, 0.302f);

        [Tooltip("The indicator's colour once the anchor is displaced.")]
        [SerializeField] private Color releasedColor = new Color(0.145f, 0.176f, 0.196f);

        [ReadOnly, SerializeField] private bool released;

        private BoxCollider cradle;
        private MaterialPropertyBlock block;

        public bool Released => released;

        public Swappable Anchor => anchor;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            cradle = GetComponent<BoxCollider>();
            Show(holdingColor, 3f);
        }

        private void Update()
        {
            if (released || anchor == null)
            {
                return;
            }

            if (!cradle.bounds.Contains(anchor.Center))
            {
                released = true;
                Show(releasedColor, 0f);
            }
        }

        private void Show(Color color, float glow)
        {
            if (indicator == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            indicator.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(EmissionColorId, color * glow);
            indicator.SetPropertyBlock(block);
        }
    }
}

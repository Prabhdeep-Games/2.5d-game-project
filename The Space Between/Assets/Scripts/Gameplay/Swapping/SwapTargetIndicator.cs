using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>How the line to the target is drawn.</summary>
    public enum SwapIndicatorStyle
    {
        Straight,
        Wobbly
    }

    /// <summary>
    /// Draws a line from the player to the object a SwapAbility is aiming at, so the player can see
    /// what a swap would affect. It only displays the choice the ability already made, so the line
    /// can never disagree with the swap.
    ///
    /// Two styles share the same targeting: a straight line, and a line that wobbles gently.
    /// Switch between them with Style to compare which reads better in play.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class SwapTargetIndicator : MonoBehaviour
    {
        private const int StraightPointCount = 2;
        private const float FullTurn = Mathf.PI * 2f;

        //==================== TARGET =====================
        [Header("Target")]
        [Tooltip("The ability to display. Empty means the SwapAbility on this object or a parent.")]
        [SerializeField] private SwapAbility ability;

        //==================== STYLE =====================
        [Header("Style")]
        [Tooltip("Straight: a plain line. Wobbly: a line with a gentle moving wave.")]
        [SerializeField] private SwapIndicatorStyle style = SwapIndicatorStyle.Straight;

        //==================== APPEARANCE =====================
        [Header("Appearance")]
        [Tooltip("Line thickness in world units.")]
        [SerializeField, Min(0.005f)] private float width = 0.06f;

        [Tooltip("Colour when a swap with the target would work.")]
        [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.5f, 0.9f);

        [Tooltip("Colour when the target is aimed at but a swap would be rejected.")]
        [SerializeField] private Color rejectedColor = new Color(1f, 0.35f, 0.3f, 0.9f);

        [Tooltip("Colour of the aim line when nothing is aimed at.")]
        [SerializeField] private Color noTargetColor = new Color(1f, 1f, 1f, 0.25f);

        [Tooltip("Also draw a faint line along the aim when nothing is aimed at.")]
        [SerializeField] private bool showAimWithoutTarget = true;

        [Tooltip("Material for the line. Use an unlit material that supports vertex colours. Empty creates a basic one.")]
        [SerializeField] private Material lineMaterial;

        //==================== WOBBLE =====================
        [Header("Wobble (Wobbly style only)")]
        [Tooltip("Points along the line. More points make a smoother wave.")]
        [SerializeField, Range(4, 64)] private int wobblePoints = 28;

        [Tooltip("How far the wave strays from the straight line, in world units.")]
        [SerializeField, Min(0f)] private float wobbleAmplitude = 0.14f;

        [Tooltip("Distance covered by one full wave, in world units.")]
        [SerializeField, Min(0.1f)] private float wobbleWavelength = 1.6f;

        [Tooltip("How fast the wave travels, in waves per second.")]
        [SerializeField, Min(0f)] private float wobbleSpeed = 1.2f;

        [Tooltip("Shrink the wave to nothing at both ends so the line still touches the player and the target.")]
        [SerializeField] private bool pinEnds = true;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Draw a gizmo at the line's end point while selected.")]
        [SerializeField] private bool drawGizmos = true;

        private LineRenderer line;
        private Vector3[] points = new Vector3[StraightPointCount];

        public SwapIndicatorStyle Style
        {
            get => style;
            set => style = value;
        }

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            if (ability == null)
            {
                ability = GetComponentInParent<SwapAbility>();
            }

            if (lineMaterial != null)
            {
                line.sharedMaterial = lineMaterial;
            }
        }

        private void OnDisable()
        {
            if (line != null)
            {
                line.enabled = false;
            }
        }

        // Runs after the ability has updated its target for the frame.
        private void LateUpdate()
        {
            if (ability == null || !ability.isActiveAndEnabled)
            {
                line.enabled = false;
                return;
            }

            Swappable target = ability.Target;
            bool hasTarget = target != null;
            if ((!hasTarget && !showAimWithoutTarget) || ability.IsSwapping)
            {
                line.enabled = false;
                return;
            }

            Vector3 start = ability.Origin;
            Vector3 end = hasTarget ? target.Center : ability.AimEndPoint;
            Color color = !hasTarget ? noTargetColor : ability.TargetStatus == SwapRejection.None ? validColor : rejectedColor;

            line.enabled = true;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;

            if (style == SwapIndicatorStyle.Straight)
            {
                SetStraight(start, end);
            }
            else
            {
                SetWobbly(start, end);
            }
        }

        //==================== DRAWING =====================
        private void SetStraight(Vector3 start, Vector3 end)
        {
            line.positionCount = StraightPointCount;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        private void SetWobbly(Vector3 start, Vector3 end)
        {
            int count = Mathf.Max(wobblePoints, StraightPointCount);
            if (points.Length != count)
            {
                points = new Vector3[count];
            }

            Vector3 along = end - start;
            float length = along.magnitude;
            Vector3 direction = length > 0.0001f ? along / length : Vector3.right;

            // Sideways within the game plane, so the wave is seen from the side camera.
            Vector3 sideways = new Vector3(-direction.y, direction.x, 0f);
            float phase = Time.unscaledTime * wobbleSpeed * FullTurn;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float wave = Mathf.Sin(t * length / wobbleWavelength * FullTurn - phase);
                float pin = pinEnds ? Mathf.Sin(t * Mathf.PI) : 1f;
                points[i] = start + along * t + sideways * (wave * pin * wobbleAmplitude);
            }

            line.positionCount = count;
            line.SetPositions(points);
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying || ability == null)
            {
                return;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(ability.AimEndPoint, width * 2f);
        }
    }
}

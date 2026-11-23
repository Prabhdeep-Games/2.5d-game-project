using UnityEngine;

namespace TheSpaceBetween
{
    //================================================================================================================
    // SWAP FEEDBACK
    // Everything the player sees and hears about a swap. It only displays what the SwapAbility has already decided,
    // so it can never disagree with the swap, and it never changes the outcome of one.
    //================================================================================================================

    /// <summary>
    /// Makes every swap readable, whichever motion the SwapAbility uses:
    /// <list type="bullet">
    /// <item><b>Aim line</b>: a line from the player to the aimed object (or along the aim), straight or wobbly, in the
    /// valid or rejected colour. Drawn by <see cref="AimLineView"/>.</item>
    /// <item><b>Glow</b>: every object that could be swapped pulses gently while in range; the aimed one takes the
    /// valid or rejected colour. Painted by <see cref="TintPainter"/>, so no material is changed or copied.</item>
    /// <item><b>Rejection</b>: the reason appears above the target (<see cref="ReasonLabel"/>) and the target flashes.</item>
    /// <item><b>Success</b>: both bodies flash when they arrive.</item>
    /// <item><b>Displacement</b>: while the swap plays, each body splits into two pale silhouettes that close up again
    /// once the bodies have traded places. Drawn by <see cref="DisplacementRenderer"/>.</item>
    /// <item><b>Sound</b>: optional clips for success and rejection.</item>
    /// </list>
    /// This component holds the settings and decides what to show; the helpers only draw. They are plain C# classes,
    /// so the Inspector still shows this one component. All timing uses real time, because a Shrink swap stops game time.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("The Space Between/Swap/Swap Feedback")]
    public sealed class SwapFeedback : MonoBehaviour
    {
        private const float FullTurn = Mathf.PI * 2f;
        private const float DimmestPulse = 0.4f;
        private const float RejectFlashLength = 2f;      // The rejected flash lasts this many times the success flash.
        private const float InstantDisplacementSeconds = 0.25f;

        //==================== SOURCE =====================
        [InspectorSection("Source")]
        [Tooltip("The ability to show. Empty means the SwapAbility on this object or a parent.")]
        [SerializeField] private SwapAbility ability;

        //==================== COLOURS =====================
        [InspectorSection("Colours")]
        [Tooltip("When a swap with the aimed object would work. Used by the aim line and the glow.")]
        [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.5f, 1f);

        [Tooltip("When the aimed object would be rejected. Used by the aim line, the glow, the flash and the reason text.")]
        [SerializeField] private Color rejectedColor = new Color(1f, 0.35f, 0.3f, 1f);

        //==================== AIM LINE =====================
        [InspectorSection("Aim Line")]
        [Tooltip("Draw a line from the player to the aimed object.")]
        [SerializeField] private bool showAimLine = true;

        [Tooltip("Straight: a plain line. Wobbly: a line with a gentle moving wave.")]
        [ShowIf(nameof(showAimLine))]
        [SerializeField] private SwapIndicatorStyle lineStyle = SwapIndicatorStyle.Straight;

        [Tooltip("Line thickness in world units.")]
        [ShowIf(nameof(showAimLine))]
        [SerializeField, Min(0.005f)] private float lineWidth = 0.06f;

        [Tooltip("Opacity of the line, applied to the valid and rejected colours.")]
        [ShowIf(nameof(showAimLine))]
        [SerializeField, Range(0f, 1f)] private float lineOpacity = 0.9f;

        [Tooltip("Material for the line. Use an unlit material that supports vertex colours. Empty uses a basic sprite material.")]
        [ShowIf(nameof(showAimLine))]
        [SerializeField] private Material lineMaterial;

        [Tooltip("Also draw a faint line along the aim when nothing is aimed at.")]
        [ShowIf(nameof(showAimLine))]
        [SerializeField] private bool showLineWithoutTarget = true;

        [Tooltip("Colour of the line when nothing is aimed at.")]
        [ShowIf(nameof(showLineWithoutTarget))]
        [SerializeField] private Color noTargetLineColor = new Color(1f, 1f, 1f, 0.25f);

        [Tooltip("Points along a wobbly line. More points make a smoother wave.")]
        [ShowIf(nameof(lineStyle), SwapIndicatorStyle.Wobbly)]
        [SerializeField, Range(4, 64)] private int wobblePoints = 28;

        [Tooltip("How far the wave strays from the straight line, in world units.")]
        [ShowIf(nameof(lineStyle), SwapIndicatorStyle.Wobbly)]
        [SerializeField, Min(0f)] private float wobbleAmplitude = 0.14f;

        [Tooltip("Distance covered by one full wave, in world units.")]
        [ShowIf(nameof(lineStyle), SwapIndicatorStyle.Wobbly)]
        [SerializeField, Min(0.1f)] private float wobbleWavelength = 1.6f;

        [Tooltip("How fast the wave travels, in waves per second.")]
        [ShowIf(nameof(lineStyle), SwapIndicatorStyle.Wobbly)]
        [SerializeField, Min(0f)] private float wobbleSpeed = 1.2f;

        [Tooltip("Shrink the wave to nothing at both ends so the line still touches the player and the target.")]
        [ShowIf(nameof(lineStyle), SwapIndicatorStyle.Wobbly)]
        [SerializeField] private bool pinWobbleEnds = true;

        //==================== GLOW =====================
        [InspectorSection("Glow")]
        [Tooltip("Make every object that could be swapped with glow while it is within range.")]
        [SerializeField] private bool highlightInRange = true;

        [Tooltip("Glow colour of objects in range that are not aimed at.")]
        [ShowIf(nameof(highlightInRange))]
        [SerializeField] private Color inRangeColor = new Color(0.35f, 0.85f, 1f, 1f);

        [Tooltip("How strongly objects in range are tinted at the brightest point of the pulse. 0 is no tint, 1 is fully the glow colour.")]
        [ShowIf(nameof(highlightInRange))]
        [SerializeField, Range(0f, 1f)] private float inRangeStrength = 0.35f;

        [Tooltip("Pulses per second of the in range glow. 0 holds it steady.")]
        [ShowIf(nameof(highlightInRange))]
        [SerializeField, Min(0f)] private float pulseSpeed = 1.2f;

        [Tooltip("How strongly the aimed object is tinted in the valid or rejected colour.")]
        [SerializeField, Range(0f, 1f)] private float aimedStrength = 0.6f;

        //==================== REJECTION =====================
        [InspectorSection("Rejection")]
        [Tooltip("Show the reason for a rejected swap as text in the world.")]
        [SerializeField] private bool showReason = true;

        [Tooltip("Seconds the reason stays visible.")]
        [ShowIf(nameof(showReason))]
        [SerializeField, Min(0.1f)] private float reasonSeconds = 1.8f;

        [Tooltip("Where the reason appears, relative to the target or to the end of the aim.")]
        [ShowIf(nameof(showReason))]
        [SerializeField] private Vector3 reasonOffset = new Vector3(0f, 1.3f, 0f);

        [Tooltip("Size of the reason text. The level signs use 0.12.")]
        [ShowIf(nameof(showReason))]
        [SerializeField, Min(0.01f)] private float reasonSize = 0.1f;

        //==================== SUCCESS =====================
        [InspectorSection("Success")]
        [Tooltip("Colour both bodies flash when a swap completes.")]
        [SerializeField] private Color flashColor = Color.white;

        [Tooltip("Seconds the flash takes to fade out. A rejected flash lasts twice as long.")]
        [SerializeField, Min(0.01f)] private float flashSeconds = 0.35f;

        //==================== DISPLACEMENT =====================
        [InspectorSection("Displacement")]
        [Tooltip("While the swap plays, each body splits into two pale silhouettes that close up again on arrival.")]
        [SerializeField] private bool showDisplacement = true;

        [Tooltip("Material for the silhouettes: unlit, transparent, drawing back faces only, so they read as a contour around the body. Empty shows no silhouettes.")]
        [ShowIf(nameof(showDisplacement))]
        [SerializeField] private Material silhouetteMaterial;

        [Tooltip("Colour of the silhouettes. The swap's own colour.")]
        [ShowIf(nameof(showDisplacement))]
        [SerializeField] private Color silhouetteColor = new Color(0.66f, 0.86f, 0.88f, 1f);

        [Tooltip("How far each silhouette separates from the body at the middle of the swap, in world units. Keep it small, so the destination stays readable.")]
        [ShowIf(nameof(showDisplacement))]
        [SerializeField, Range(0f, 1f)] private float silhouetteSeparation = 0.22f;

        [Tooltip("How much larger than the body the silhouettes are drawn. A little larger shows as a thin contour.")]
        [ShowIf(nameof(showDisplacement))]
        [SerializeField, Range(1f, 1.3f)] private float silhouetteScale = 1.06f;

        [Tooltip("How opaque the silhouettes are at the middle of the swap.")]
        [ShowIf(nameof(showDisplacement))]
        [SerializeField, Range(0f, 1f)] private float silhouetteOpacity = 0.75f;

        //==================== SOUND =====================
        [InspectorSection("Sound", false)]
        [Tooltip("Plays the clips below. Empty means no sound.")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Played when a swap completes.")]
        [SerializeField] private AudioClip swapClip;

        [Tooltip("Played when a swap is rejected.")]
        [SerializeField] private AudioClip rejectClip;

        //==================== DEBUG =====================
        [InspectorSection("Debug", false)]
        [Tooltip("Draw a gizmo at the end of the aim while selected.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("The last feedback shown.")]
        [ReadOnly, SerializeField] private string lastMessage = "none yet";

        [Tooltip("How many objects are glowing because they are within range.")]
        [ReadOnly, SerializeField] private int objectsInRange;

        [Tooltip("Whether the displacement silhouettes are showing right now.")]
        [ReadOnly, SerializeField] private bool isDisplacing;

        // The helpers that draw. Plain C# objects, owned and driven by this component.
        private AimLineView aimLine;
        private TintPainter tints;
        private ReasonLabel reason;
        private DisplacementRenderer displacement;

        private Swappable self;
        private Swappable swappedWith;
        private float swapTime = float.NegativeInfinity;
        private Swappable rejectedTarget;
        private float rejectTime = float.NegativeInfinity;

        //==================== OUTPUTS =====================
        /// <summary>The text of the last rejection reason or success shown.</summary>
        public string LastMessage => lastMessage;

        /// <summary>Whether the rejection reason is on screen right now.</summary>
        public bool IsShowingReason => reason != null && reason.IsVisible;

        /// <summary>How many objects glow because they are within range.</summary>
        public int ObjectsInRange => objectsInRange;

        /// <summary>Whether the displacement silhouettes are showing right now.</summary>
        public bool IsDisplacing => displacement != null && displacement.IsPlaying;

        /// <summary>The runtime line that draws the aim. Null outside Play mode.</summary>
        public LineRenderer AimLine => aimLine != null ? aimLine.Line : null;

        public SwapIndicatorStyle LineStyle
        {
            get => lineStyle;
            set => lineStyle = value;
        }

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            if (ability == null)
            {
                ability = GetComponentInParent<SwapAbility>();
            }

            self = GetComponent<Swappable>();
            aimLine = new AimLineView(transform, lineMaterial);
            tints = new TintPainter();
            reason = new ReasonLabel(name, reasonSize);
            displacement = new DisplacementRenderer();
        }

        private void OnEnable()
        {
            if (ability == null)
            {
                Debug.LogWarning($"[{nameof(SwapFeedback)}] No SwapAbility found on '{name}'. Assign one.", this);
                return;
            }

            ability.Swapped += OnSwapAccepted;
            ability.SwapCompleted += OnSwapCompleted;
            ability.SwapRejected += OnSwapRejected;
        }

        private void OnDisable()
        {
            if (ability != null)
            {
                ability.Swapped -= OnSwapAccepted;
                ability.SwapCompleted -= OnSwapCompleted;
                ability.SwapRejected -= OnSwapRejected;
            }

            tints.ClearAll();
            aimLine.Hide();
            reason.Hide();
            displacement.Stop();
            isDisplacing = false;
        }

        private void OnDestroy()
        {
            aimLine?.Dispose();
            reason?.Dispose();
            displacement?.Dispose();
        }

        // Runs after the ability has chosen its target for the frame. LateUpdate also runs while game time is stopped.
        private void LateUpdate()
        {
            if (ability == null)
            {
                return;
            }

            UpdateAimLine();
            UpdateTints();
            reason.Tick(rejectedColor);
            displacement.Draw(SilhouetteLook());
            isDisplacing = displacement.IsPlaying;
        }

        //==================== EVENTS =====================
        private void OnSwapAccepted(Swappable other)
        {
            if (!showDisplacement || silhouetteMaterial == null || other == null)
            {
                return;
            }

            float seconds = ability.TotalDuration > 0f ? ability.TotalDuration : InstantDisplacementSeconds;
            displacement.Begin(self, other, seconds);
            isDisplacing = true;
        }

        private void OnSwapCompleted(Swappable other)
        {
            swappedWith = other;
            swapTime = Time.unscaledTime;
            rejectedTarget = null;
            reason.Hide();
            lastMessage = SwapRejectionText.Describe(SwapRejection.None);
            Play(swapClip);
        }

        private void OnSwapRejected(SwapRejection rejection)
        {
            rejectedTarget = ability.Target;
            rejectTime = Time.unscaledTime;
            lastMessage = SwapRejectionText.Describe(rejection);
            Play(rejectClip);

            if (showReason)
            {
                Vector3 anchor = rejectedTarget != null ? rejectedTarget.Center : ability.AimEndPoint;
                reason.Show(lastMessage, anchor + reasonOffset, reasonSeconds);
            }
        }

        private void Play(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        //==================== AIM LINE =====================
        private void UpdateAimLine()
        {
            Swappable target = ability.Target;
            bool hasTarget = target != null;
            bool visible = showAimLine && ability.isActiveAndEnabled && !ability.IsSwapping && (hasTarget || showLineWithoutTarget);
            if (!visible)
            {
                aimLine.Hide();
                return;
            }

            Vector3 start = ability.Origin;
            Vector3 end = hasTarget ? target.Center : ability.AimEndPoint;
            Color color = hasTarget ? WithOpacity(StatusColor(), lineOpacity) : noTargetLineColor;

            if (lineStyle == SwapIndicatorStyle.Straight)
            {
                aimLine.ShowStraight(start, end, color, lineWidth);
            }
            else
            {
                aimLine.ShowWobbly(start, end, color, lineWidth, WaveShape());
            }
        }

        //==================== GLOW AND FLASHES =====================
        // Asked for from least to most important, because a later request for the same object wins.
        private void UpdateTints()
        {
            tints.BeginFrame();
            objectsInRange = 0;

            if (!ability.IsSwapping)
            {
                RequestInRangeGlow();
                tints.Request(ability.Target, StatusColor(), aimedStrength);
            }

            tints.RequestFlash(rejectedTarget, rejectedColor, rejectTime, flashSeconds * RejectFlashLength);
            tints.RequestFlash(self, flashColor, swapTime, flashSeconds);
            tints.RequestFlash(swappedWith, flashColor, swapTime, flashSeconds);
            tints.Apply();
        }

        private void RequestInRangeGlow()
        {
            if (!highlightInRange)
            {
                return;
            }

            float pulse = pulseSpeed > 0f ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * FullTurn) : 1f;
            float amount = inRangeStrength * Mathf.Lerp(DimmestPulse, 1f, pulse);
            Vector3 origin = ability.Origin;

            // A for loop, because a foreach over the read-only list would allocate every frame.
            var all = Swappable.Active;
            for (int i = 0; i < all.Count; i++)
            {
                Swappable candidate = all[i];
                if (candidate != self && candidate.CanSwap && Vector3.Distance(origin, candidate.Center) <= ability.Range)
                {
                    tints.Request(candidate, inRangeColor, amount);
                    objectsInRange++;
                }
            }
        }

        //==================== SETTINGS FOR THE HELPERS =====================
        private AimLineView.Wave WaveShape()
        {
            return new AimLineView.Wave(wobblePoints, wobbleAmplitude, wobbleWavelength, wobbleSpeed, pinWobbleEnds);
        }

        private DisplacementRenderer.Look SilhouetteLook()
        {
            return new DisplacementRenderer.Look(silhouetteMaterial, silhouetteColor, silhouetteOpacity, silhouetteSeparation, silhouetteScale);
        }

        private Color StatusColor()
        {
            return ability.TargetStatus == SwapRejection.None ? validColor : rejectedColor;
        }

        private static Color WithOpacity(Color color, float opacity)
        {
            color.a = opacity;
            return color;
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying || ability == null)
            {
                return;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(ability.AimEndPoint, lineWidth * 2f);
        }
    }
}

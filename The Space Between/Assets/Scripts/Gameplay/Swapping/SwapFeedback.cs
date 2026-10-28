using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Makes every swap readable in the same three ways, whichever kind of swap the player has:
    ///   Compatible  Every object that can be swapped glows gently while it is within range. The aimed one
    ///               glows in the indicator's colours: green when the swap would work, red when it would not.
    ///   Rejected    The reason appears above the target, or at the end of the aim when nothing is aimed at,
    ///               and the target flashes red.
    ///   Succeeded   Both bodies flash when they arrive. For a smooth swap that is when the animation ends.
    /// Optional sounds play for success and rejection when clips are assigned.
    ///
    /// Put this on the player next to the SwapAbility. Colours are set with a MaterialPropertyBlock, so no
    /// material is changed or copied. All timing uses real time, so it also runs while a smooth swap stops time.
    /// </summary>
    public class SwapFeedback : MonoBehaviour
    {
        private const float FullTurn = Mathf.PI * 2f;
        private const float DimmestPulse = 0.4f;
        private const float RejectFlashLength = 2f;   // The rejected flash lasts this many times the success flash.
        private const float ReasonFadeSeconds = 0.3f;
        private const int ReasonFontSize = 64;
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        private struct Tint
        {
            public Color Color;
            public float Amount;
        }

        //==================== TARGET =====================
        [Header("Target")]
        [Tooltip("The ability to show feedback for. Empty means the SwapAbility on this object.")]
        [SerializeField] private SwapAbility ability;

        //==================== COMPATIBLE OBJECTS =====================
        [Header("Compatible Objects")]
        [Tooltip("Make every object that could be swapped with glow while it is within range.")]
        [SerializeField] private bool highlightInRange = true;

        [Tooltip("Glow colour of objects in range that are not aimed at.")]
        [SerializeField] private Color inRangeColor = new Color(0.35f, 0.85f, 1f, 1f);

        [Tooltip("How strongly objects in range are tinted at the brightest point of the pulse. 0 is no tint, 1 is fully the glow colour.")]
        [SerializeField, Range(0f, 1f)] private float inRangeStrength = 0.35f;

        [Tooltip("Pulses per second of the in range glow. 0 holds it steady.")]
        [SerializeField, Min(0f)] private float pulseSpeed = 1.2f;

        //==================== AIMED OBJECT =====================
        [Header("Aimed Object")]
        [Tooltip("Tint of the aimed object when a swap would work. Keep it the same as the line indicator's valid colour.")]
        [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.5f, 1f);

        [Tooltip("Tint of the aimed object when a swap would be rejected. Keep it the same as the line indicator's rejected colour.")]
        [SerializeField] private Color rejectedColor = new Color(1f, 0.35f, 0.3f, 1f);

        [Tooltip("How strongly the aimed object is tinted.")]
        [SerializeField, Range(0f, 1f)] private float aimedStrength = 0.6f;

        //==================== REJECTION =====================
        [Header("Rejection")]
        [Tooltip("Show the reason for a rejected swap as text in the world.")]
        [SerializeField] private bool showReason = true;

        [Tooltip("Seconds the reason stays visible.")]
        [SerializeField, Min(0.1f)] private float reasonSeconds = 1.8f;

        [Tooltip("Where the reason appears, relative to the target or to the end of the aim.")]
        [SerializeField] private Vector3 reasonOffset = new Vector3(0f, 1.3f, 0f);

        [Tooltip("Size of the reason text. The level signs use 0.12.")]
        [SerializeField, Min(0.01f)] private float reasonSize = 0.1f;

        //==================== SUCCESS =====================
        [Header("Success")]
        [Tooltip("Colour both bodies flash when a swap completes.")]
        [SerializeField] private Color flashColor = Color.white;

        [Tooltip("Seconds the flash takes to fade out.")]
        [SerializeField, Min(0.01f)] private float flashSeconds = 0.35f;

        //==================== SOUND =====================
        [Header("Sound (optional)")]
        [Tooltip("Plays the clips below. Empty means no sound.")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Played when a swap completes.")]
        [SerializeField] private AudioClip swapClip;

        [Tooltip("Played when a swap is rejected.")]
        [SerializeField] private AudioClip rejectClip;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("The last feedback shown.")]
        [ReadOnly, SerializeField] private string lastMessage = "none yet";

        [Tooltip("How many objects are glowing because they are within range.")]
        [ReadOnly, SerializeField] private int objectsInRange;

        private Swappable self;
        private SmoothSwapAbility smoothAbility;
        private MaterialPropertyBlock block;
        private TextMesh reasonText;
        private float reasonUntil = float.NegativeInfinity;

        private Swappable swappedWith;
        private float swapTime = float.NegativeInfinity;
        private Swappable rejectedTarget;
        private float rejectTime = float.NegativeInfinity;

        private readonly Dictionary<Swappable, Renderer[]> renderers = new Dictionary<Swappable, Renderer[]>();
        private readonly Dictionary<Swappable, Tint> wanted = new Dictionary<Swappable, Tint>();
        private readonly List<Swappable> tinted = new List<Swappable>();

        /// <summary>The text of the last rejection reason or success shown.</summary>
        public string LastMessage => lastMessage;

        /// <summary>Whether the rejection reason is on screen right now.</summary>
        public bool IsShowingReason => reasonText != null && reasonText.gameObject.activeSelf;

        /// <summary>How many objects glow because they are within range.</summary>
        public int ObjectsInRange => objectsInRange;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            if (ability == null)
            {
                ability = GetComponent<SwapAbility>();
            }

            self = GetComponent<Swappable>();
            block = new MaterialPropertyBlock();
            reasonText = CreateReasonText();
        }

        private void OnEnable()
        {
            if (ability == null)
            {
                Debug.LogWarning($"[{nameof(SwapFeedback)}] No SwapAbility found on '{name}'. Assign one.", this);
                return;
            }

            // A smooth swap has only finished when its animation ends.
            smoothAbility = ability as SmoothSwapAbility;
            if (smoothAbility != null)
            {
                smoothAbility.SwapCompleted += OnSwapSucceeded;
            }
            else
            {
                ability.Swapped += OnSwapSucceeded;
            }

            ability.SwapRejected += OnSwapRejected;
        }

        private void OnDisable()
        {
            if (ability != null)
            {
                if (smoothAbility != null)
                {
                    smoothAbility.SwapCompleted -= OnSwapSucceeded;
                }
                else
                {
                    ability.Swapped -= OnSwapSucceeded;
                }

                ability.SwapRejected -= OnSwapRejected;
            }

            ClearAllTints();
            if (reasonText != null)
            {
                reasonText.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (reasonText != null)
            {
                Destroy(reasonText.gameObject);
            }
        }

        // Runs after the ability has chosen its target for the frame.
        private void LateUpdate()
        {
            if (ability == null)
            {
                return;
            }

            wanted.Clear();
            if (!ability.IsSwapping)
            {
                AddRangeAndAimTints();
            }

            AddFlash(rejectedTarget, null, rejectedColor, rejectTime, flashSeconds * RejectFlashLength);
            AddFlash(self, swappedWith, flashColor, swapTime, flashSeconds);
            ApplyTints();
            UpdateReasonText();
        }

        //==================== EVENTS =====================
        private void OnSwapSucceeded(Swappable other)
        {
            swappedWith = other;
            swapTime = Time.unscaledTime;
            rejectedTarget = null;
            reasonUntil = float.NegativeInfinity;
            lastMessage = SwapRejectionText.Describe(SwapRejection.None);
            Play(swapClip);
        }

        private void OnSwapRejected(SwapRejection reason)
        {
            rejectedTarget = ability.Target;
            rejectTime = Time.unscaledTime;
            lastMessage = SwapRejectionText.Describe(reason);
            Play(rejectClip);

            if (!showReason)
            {
                return;
            }

            Vector3 anchor = rejectedTarget != null ? rejectedTarget.Center : ability.AimEndPoint;
            reasonText.transform.position = anchor + reasonOffset;
            reasonText.text = lastMessage;
            reasonUntil = Time.unscaledTime + reasonSeconds;
        }

        private void Play(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        //==================== TINTS =====================
        private void AddRangeAndAimTints()
        {
            objectsInRange = 0;
            if (highlightInRange)
            {
                float pulse = pulseSpeed > 0f ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * FullTurn) : 1f;
                float amount = inRangeStrength * Mathf.Lerp(DimmestPulse, 1f, pulse);
                Vector3 origin = ability.Origin;

                IReadOnlyList<Swappable> all = Swappable.Active;
                for (int i = 0; i < all.Count; i++)
                {
                    Swappable candidate = all[i];
                    if (candidate == self || !candidate.CanSwap || Vector3.Distance(origin, candidate.Center) > ability.Range)
                    {
                        continue;
                    }

                    wanted[candidate] = new Tint { Color = inRangeColor, Amount = amount };
                    objectsInRange++;
                }
            }

            Swappable target = ability.Target;
            if (target != null)
            {
                Color color = ability.TargetStatus == SwapRejection.None ? validColor : rejectedColor;
                wanted[target] = new Tint { Color = color, Amount = aimedStrength };
            }
        }

        // A flash starts at full strength and fades out over its length. It wins over any other tint.
        private void AddFlash(Swappable first, Swappable second, Color color, float startTime, float length)
        {
            float amount = 1f - (Time.unscaledTime - startTime) / length;
            if (amount <= 0f)
            {
                return;
            }

            Tint tint = new Tint { Color = color, Amount = amount };
            if (first != null)
            {
                wanted[first] = tint;
            }

            if (second != null)
            {
                wanted[second] = tint;
            }
        }

        private void ApplyTints()
        {
            for (int i = tinted.Count - 1; i >= 0; i--)
            {
                if (tinted[i] == null || !wanted.ContainsKey(tinted[i]))
                {
                    SetTint(tinted[i], null);
                    tinted.RemoveAt(i);
                }
            }

            foreach (KeyValuePair<Swappable, Tint> entry in wanted)
            {
                SetTint(entry.Key, entry.Value);
                if (!tinted.Contains(entry.Key))
                {
                    tinted.Add(entry.Key);
                }
            }
        }

        private void ClearAllTints()
        {
            foreach (Swappable item in tinted)
            {
                SetTint(item, null);
            }

            tinted.Clear();
        }

        // Blends each renderer's own material colour toward the tint. No tint puts the material colour back.
        private void SetTint(Swappable item, Tint? tint)
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

                if (tint == null || target.sharedMaterial == null)
                {
                    target.SetPropertyBlock(null);
                    continue;
                }

                int colorId = target.sharedMaterial.HasProperty(BaseColorId) ? BaseColorId : LegacyColorId;
                Color own = target.sharedMaterial.HasProperty(colorId) ? target.sharedMaterial.GetColor(colorId) : Color.white;

                target.GetPropertyBlock(block);
                block.SetColor(colorId, Color.Lerp(own, tint.Value.Color, tint.Value.Amount));
                target.SetPropertyBlock(block);
            }
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

        //==================== REASON TEXT =====================
        private TextMesh CreateReasonText()
        {
            GameObject holder = new GameObject($"{name} Swap Reason");
            TextMesh text = holder.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            text.font = font;
            holder.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = ReasonFontSize;
            text.characterSize = reasonSize;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            holder.SetActive(false);
            return text;
        }

        private void UpdateReasonText()
        {
            float left = reasonUntil - Time.unscaledTime;
            bool visible = left > 0f;
            reasonText.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            Color color = rejectedColor;
            color.a = Mathf.Clamp01(left / ReasonFadeSeconds);
            reasonText.color = color;
        }
    }
}

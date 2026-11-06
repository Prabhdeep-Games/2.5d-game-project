using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// The infrastructure that governs access: a panel that switches doors, platforms and cameras.
    ///
    /// Activation:
    ///   Interact: the player presses the interact action while standing close to the panel. Glass and walls
    ///             between them block the reach, so a panel seen through glass cannot be used from there.
    ///   Occupied: a pad in front of the panel reacts to whatever stands on it, the player or any swappable
    ///             object. A swap leaves an object where the player stood, so a crate can hold a pad down.
    ///
    /// Response:
    ///   Toggle: every activation switches the panel on or off.
    ///   Hold:   on only while the pad is occupied, with a short grace period (Occupied panels only;
    ///           an Interact panel treats it as Toggle).
    ///   Latch:  the first activation switches it on for good.
    ///
    /// While the panel is on, every linked mechanism is switched away from the power it started with: an
    /// unpowered door is powered, a powered camera is switched off. Switching the panel off restores them.
    /// </summary>
    public class ControlPanel : MonoBehaviour
    {
        /// <summary>What the panel reacts to.</summary>
        public enum PanelActivation
        {
            Interact,
            Occupied
        }

        /// <summary>How an activation changes the panel.</summary>
        public enum PanelResponse
        {
            Toggle,
            Hold,
            Latch
        }

        private const int BufferSize = 16;
        private const int PromptFontSize = 64;
        private const string BuiltinFontName = "LegacyRuntime.ttf";
        private const string UsePrompt = "E  use";
        private const string BlockedPrompt = "Out of reach";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly List<ControlPanel> panels = new List<ControlPanel>();

        //==================== INPUT =====================
        [Header("Input")]
        [Tooltip("Uses the nearest panel in reach. Reads E and the gamepad west button.")]
        [SerializeField] private InputAction interactAction = new InputAction("Interact", InputActionType.Button);

        //==================== ACTIVATION =====================
        [Header("Activation")]
        [Tooltip("Interact: the player presses the interact action nearby. Occupied: the pad reacts to the player or an object standing on it.")]
        [SerializeField] private PanelActivation activation = PanelActivation.Interact;

        [Tooltip("Toggle: each activation switches on or off. Hold: on only while the pad is occupied. Latch: stays on after the first activation.")]
        [SerializeField] private PanelResponse response = PanelResponse.Toggle;

        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private Transform player;

        [Tooltip("Furthest distance from the player's centre to the panel at which it can be used, in world units.")]
        [SerializeField, Min(0.1f)] private float interactRange = 2f;

        [Tooltip("Layers that block the reach between the player and the panel. Keep Glass in it: seeing a panel is not the same as reaching it.")]
        [SerializeField] private LayerMask reachBlockers = ~0;

        //==================== PAD =====================
        [Header("Pad (Occupied only)")]
        [Tooltip("Centre of the pad zone, relative to this object.")]
        [SerializeField] private Vector3 padCenter = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Size of the pad zone in world units. Anything movable reaching into it counts as standing on the pad.")]
        [SerializeField] private Vector3 padSize = new Vector3(1.6f, 1f, 2f);

        [Tooltip("Seconds a Hold panel stays on after the pad empties, so a body passing through a swap does not flicker it.")]
        [SerializeField, Min(0f)] private float releaseDelay = 0.3f;

        //==================== TARGETS =====================
        [Header("Targets")]
        [Tooltip("Doors, platforms and cameras this panel switches.")]
        [SerializeField] private List<Mechanism> targets = new List<Mechanism>();

        //==================== FEEDBACK =====================
        [Header("Feedback")]
        [Tooltip("Optional light on the panel. Red while off, green while on.")]
        [SerializeField] private Renderer indicator;

        [Tooltip("Indicator and cable colour while the panel is on.")]
        [SerializeField] private Color onColor = new Color(0.3f, 1f, 0.5f);

        [Tooltip("Indicator and cable colour while the panel is off.")]
        [SerializeField] private Color offColor = new Color(1f, 0.3f, 0.25f);

        [Tooltip("Show a prompt above an Interact panel when the player is close: how to use it, or that glass or a wall is in the way.")]
        [SerializeField] private bool showPrompt = true;

        [Tooltip("Where the prompt appears, relative to the panel.")]
        [SerializeField] private Vector3 promptOffset = new Vector3(0f, 1.3f, 0f);

        [Tooltip("Size of the prompt text in world units per font pixel.")]
        [SerializeField, Min(0.01f)] private float promptSize = 0.08f;

        [Tooltip("Draw a cable from the panel to each target, so the player can see what it controls.")]
        [SerializeField] private bool showWires = true;

        [Tooltip("Material for the cables. Use an unlit material; its colour is set while playing.")]
        [SerializeField] private Material wireMaterial;

        [Tooltip("Cable thickness in world units.")]
        [SerializeField, Min(0.01f)] private float wireWidth = 0.08f;

        [Tooltip("World Z of the cables. Slightly behind the player (z 0), so they read as part of the building.")]
        [SerializeField] private float wireDepth = 1.2f;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked when the panel switches on.")]
        [SerializeField] private UnityEvent switchedOnEvent;

        [Tooltip("Invoked when the panel switches off.")]
        [SerializeField] private UnityEvent switchedOffEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write every switch to the Console.")]
        [SerializeField] private bool logChanges;

        [Tooltip("Whether the panel is on.")]
        [ReadOnly, SerializeField] private bool isOn;

        [Tooltip("Interact panels: whether the player is close enough with nothing in the way.")]
        [ReadOnly, SerializeField] private bool playerInReach;

        [Tooltip("Occupied panels: whether something stands on the pad.")]
        [ReadOnly, SerializeField] private bool isOccupied;

        private readonly Collider[] buffer = new Collider[BufferSize];
        private readonly RaycastHit[] reachHits = new RaycastHit[BufferSize];
        private readonly List<bool> startPower = new List<bool>();
        private readonly PowerWires wires = new PowerWires();
        private MaterialPropertyBlock block;
        private TextMesh prompt;
        private Collider playerCollider;
        private float lastOccupiedTime = float.NegativeInfinity;
        private bool wasOccupied;

        //==================== OUTPUTS =====================
        public bool IsOn => isOn;

        public bool PlayerInReach => playerInReach;

        public bool IsOccupied => isOccupied;

        public PanelActivation Activation => activation;

        /// <summary>The text the prompt shows right now, or empty when it is hidden.</summary>
        public string PromptText => prompt != null && prompt.gameObject.activeSelf ? prompt.text : string.Empty;

        //==================== PUBLIC API =====================
        /// <summary>Activates the panel as if the player had used it. Respects the response setting.</summary>
        public void Activate()
        {
            if (response == PanelResponse.Latch)
            {
                SetOn(true);
            }
            else
            {
                SetOn(!isOn);
            }
        }

        /// <summary>Switches the panel on or off directly.</summary>
        public void SetOn(bool on)
        {
            if (isOn == on)
            {
                return;
            }

            isOn = on;
            CaptureStartPower();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].SetPowered(on ? !startPower[i] : startPower[i]);
                }
            }

            ShowState();
            if (logChanges)
            {
                Debug.Log($"[{nameof(ControlPanel)}] '{name}' switched {(on ? "on" : "off")}.", this);
            }

            if (on)
            {
                switchedOnEvent?.Invoke();
            }
            else
            {
                switchedOffEvent?.Invoke();
            }
        }

        /// <summary>
        /// True when the player is within range and nothing on the reach blocker layers lies between them.
        /// The reason is "Out of reach" when the player is close but something is in the way.
        /// </summary>
        public bool CanReach(out bool inRange)
        {
            inRange = false;
            if (player == null)
            {
                return false;
            }

            Vector3 from = PlayerCenter();
            Vector3 to = transform.position;
            float distance = Vector3.Distance(from, to);
            inRange = distance <= interactRange;
            if (!inRange)
            {
                return false;
            }

            int count = Physics.RaycastNonAlloc(from, (to - from).normalized, reachHits, distance, reachBlockers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = reachHits[i].transform;
                if (!hit.IsChildOf(transform) && !hit.IsChildOf(player))
                {
                    return false;
                }
            }

            return true;
        }

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            interactAction.AddBinding("<Gamepad>/buttonWest");
            reachBlockers = ~LayerMask.GetMask("Ignore Raycast", "UI");
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (player == null)
            {
                PlaceholderPlayerController controller = FindAnyObjectByType<PlaceholderPlayerController>();
                player = controller != null ? controller.transform : null;
            }

            if (player == null)
            {
                Debug.LogWarning($"[{nameof(ControlPanel)}] '{name}' found no player. Assign one.", this);
            }
            else
            {
                playerCollider = player.GetComponent<Collider>();
            }

            if (activation == PanelActivation.Interact && showPrompt)
            {
                prompt = CreatePrompt();
            }
        }

        private void Start()
        {
            CaptureStartPower();
            if (showWires)
            {
                wires.Build(transform, targets, wireMaterial, wireWidth, wireDepth);
            }

            ShowState();
        }

        private void OnEnable()
        {
            panels.Add(this);
            interactAction.performed += OnInteractPerformed;
            interactAction.Enable();
        }

        private void OnDisable()
        {
            panels.Remove(this);
            interactAction.performed -= OnInteractPerformed;
            interactAction.Disable();
        }

        private void OnValidate()
        {
            if (activation == PanelActivation.Interact && response == PanelResponse.Hold)
            {
                Debug.LogWarning($"[{nameof(ControlPanel)}] '{name}': Hold needs a pad. An Interact panel treats it as Toggle.", this);
            }
        }

        private void Update()
        {
            if (activation == PanelActivation.Interact)
            {
                playerInReach = CanReach(out bool inRange);
                UpdatePrompt(inRange);
                return;
            }

            isOccupied = PadOccupied();
            if (isOccupied)
            {
                lastOccupiedTime = Time.time;
            }

            switch (response)
            {
                case PanelResponse.Hold:
                    SetOn(Time.time - lastOccupiedTime <= releaseDelay);
                    break;
                case PanelResponse.Latch:
                    if (isOccupied)
                    {
                        SetOn(true);
                    }

                    break;
                default:
                    if (isOccupied && !wasOccupied)
                    {
                        SetOn(!isOn);
                    }

                    break;
            }

            wasOccupied = isOccupied;
        }

        //==================== PRIVATE =====================
        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            if (activation != PanelActivation.Interact || !CanReach(out _) || !IsNearestReachable())
            {
                return;
            }

            Activate();
        }

        // Two panels in reach: only the closer one reacts.
        private bool IsNearestReachable()
        {
            float own = Vector3.Distance(PlayerCenter(), transform.position);
            for (int i = 0; i < panels.Count; i++)
            {
                ControlPanel other = panels[i];
                if (other == this || other.activation != PanelActivation.Interact || other.player != player)
                {
                    continue;
                }

                if (other.CanReach(out _) && Vector3.Distance(PlayerCenter(), other.transform.position) < own)
                {
                    return false;
                }
            }

            return true;
        }

        private Vector3 PlayerCenter()
        {
            return playerCollider != null ? playerCollider.bounds.center : player.position;
        }

        // Read once, after every mechanism has set its starting power in Awake: in Start, or at the first
        // switch if a script switches the panel earlier.
        private void CaptureStartPower()
        {
            if (startPower.Count == targets.Count)
            {
                return;
            }

            startPower.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                startPower.Add(targets[i] != null && targets[i].IsPowered);
            }
        }

        private bool PadOccupied()
        {
            Vector3 center = transform.TransformPoint(padCenter);
            int count = Physics.OverlapBoxNonAlloc(center, padSize * 0.5f, buffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform owner = KinematicSafety.MovableOwner(buffer[i]);
                if (owner == null || owner.IsChildOf(transform))
                {
                    continue;
                }

                // A body in the middle of a smooth swap is shrinking away; the grace period covers the gap.
                Swappable swappable = owner.GetComponent<Swappable>();
                if (swappable != null && swappable.IsFrozen)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void UpdatePrompt(bool inRange)
        {
            if (prompt == null)
            {
                return;
            }

            bool latchedOn = response == PanelResponse.Latch && isOn;
            bool visible = inRange && !latchedOn;
            prompt.gameObject.SetActive(visible);
            if (visible)
            {
                prompt.text = playerInReach ? UsePrompt : BlockedPrompt;
            }
        }

        private TextMesh CreatePrompt()
        {
            var holder = new GameObject("Prompt");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = promptOffset;
            holder.layer = LayerMask.NameToLayer("Ignore Raycast");

            TextMesh text = holder.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            text.font = font;
            holder.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = PromptFontSize;
            text.characterSize = promptSize;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            holder.SetActive(false);
            return text;
        }

        private void ShowState()
        {
            Color color = isOn ? onColor : offColor;
            wires.SetColor(color);

            if (indicator == null || indicator.sharedMaterial == null)
            {
                return;
            }

            int colorId = indicator.sharedMaterial.HasProperty(BaseColorId) ? BaseColorId : LegacyColorId;
            indicator.GetPropertyBlock(block);
            block.SetColor(colorId, color);
            indicator.SetPropertyBlock(block);
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!GeneratedRoom.ShowsGizmos(this))
            {
                return;
            }

            Gizmos.color = Color.yellow;
            if (activation == PanelActivation.Interact)
            {
                Gizmos.DrawWireSphere(transform.position, interactRange);
            }
            else
            {
                Gizmos.DrawWireCube(transform.TransformPoint(padCenter), padSize);
            }

            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null)
                {
                    Gizmos.DrawLine(transform.position, targets[i].transform.position);
                }
            }
        }
    }
}

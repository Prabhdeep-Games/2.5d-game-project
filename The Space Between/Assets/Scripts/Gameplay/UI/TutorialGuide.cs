using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace TheSpaceBetween
{
    /// <summary>
    /// The tutorial guidance for a level: a short opening line with the objective, signs floating in the level that show the
    /// controls where they are first needed, one contextual hint at a time as each mechanic comes up, and a closing line at the
    /// goal.
    ///
    /// Every key or button shown is read from the real input actions on the Player, the panels, the observation points and the
    /// Room Resets, so the text always matches the bindings, for the keyboard and mouse or for a gamepad, whichever was used
    /// last. Signs and hints use tokens such as {Jump} or {Swap}, which become that key or button.
    ///
    /// Hints come from the level's existing Room Resets: a hint shows while the player is in its room (and near its object, if
    /// it has one), and stops for good once its Done point is passed. Restarting a room (R, a fall or a hit) clears its hints'
    /// progress, so they can show again and nothing stale stays on screen. The UI is built when Play starts, takes no clicks
    /// and never pauses or locks the player.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";
        private const string NotSet = "not set";

        private static readonly Regex Token = new Regex(@"\{(\w+)\}");

        /// <summary>The input devices the guidance can describe.</summary>
        public enum InputDevice
        {
            KeyboardMouse,
            Gamepad
        }

        /// <summary>When a hint has served its purpose.</summary>
        public enum DoneRule
        {
            Never,
            PastX,
            AboveY
        }

        /// <summary>One line of contextual guidance.</summary>
        [Serializable]
        public class Hint
        {
            [Tooltip("A name for the hint, used in tests and the Console.")]
            public string name;

            [Tooltip("What the player reads. Tokens such as {Jump}, {Run}, {Slide}, {Swap}, {Aim}, {Use}, {Observe}, {Restart}, {Move}, {Left} and {Right} become the real key or button.")]
            [TextArea(1, 3)] public string text;

            [Tooltip("The room the hint belongs to. It shows only while the player is inside this Room Reset's area.")]
            public RoomReset room;

            [Tooltip("Optional: the hint shows only within Near Radius of this object.")]
            public Transform showNear;

            [Tooltip("Distance from Show Near, in world units, within which the hint shows.")]
            [Min(0f)] public float nearRadius = 6f;

            [Tooltip("Optional: the hint shows only while this camera has raised its alarm.")]
            public SecurityCamera onlyWhileAlarmed;

            [Tooltip("When the hint is done for good: once the player passes Done At along X, or rises above it.")]
            public DoneRule doneRule = DoneRule.PastX;

            [Tooltip("The point that ends the hint.")]
            public Transform doneAt;

            [Tooltip("Uses the security amber accent instead of the usual text colour.")]
            public bool security;

            [NonSerialized] public bool done;
        }


        /// <summary>A sign in the level whose keys are filled in from the bindings.</summary>
        [Serializable]
        public class Sign
        {
            [Tooltip("The 3D text in the level that shows this sign.")]
            public TextMesh text;

            [Tooltip("What it says, with tokens such as {Move}, {Run} or {Jump} for the keys.")]
            [TextArea(1, 4)] public string content;
        }

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The player. Found in the scene when empty.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The player's swap. Found on the player when empty.")]
        [SerializeField] private SwapAbility swap;

        [Tooltip("The goal. Reaching it shows the closing line. Found in the scene when empty.")]
        [SerializeField] private LevelGoal goal;

        [Tooltip("The end of the first room, normally its exit door. The opening line only shows before it.")]
        [SerializeField] private Transform controlsUntil;

        //==================== TEXT =====================
        [Header("Text")]
        [Tooltip("The opening line: the level's objective.")]
        [TextArea(1, 3)]
        [SerializeField] private string introText = "Reach the end of the facility. Each room has one way through.";

        [Tooltip("Seconds the opening line stays before hints may replace it.")]
        [SerializeField, Min(0f)] private float introSeconds = 7f;

        [Tooltip("Shown once the goal is reached. Leave empty when an Ending Sequence shows the ending instead.")]
        [SerializeField] private string endText = "End of the Final Playtest. Thank you.";

        [Tooltip("Font of the hint and the closing line. Empty means Unity's built-in font.")]
        [SerializeField] private Font uiFont;


        [Tooltip("Signs in the level that show the controls. Their keys follow the bindings and the device in use.")]
        [SerializeField] private List<Sign> signs = new List<Sign>();

        [Tooltip("The contextual hints, in priority order: the first one that may show is the one shown.")]
        [SerializeField] private List<Hint> hints = new List<Hint>();

        //==================== STYLE =====================
        [Header("Style")]
        [SerializeField, Min(8)] private int hintFontSize = 30;
        [SerializeField, Min(8)] private int endFontSize = 40;
        [SerializeField, Min(0.01f)] private float fadeSeconds = 0.25f;

        [Tooltip("Panel background: the Art Direction Bible's deep shadow, partly see-through.")]
        [SerializeField] private Color panelColor = new Color(0.145f, 0.176f, 0.196f, 0.78f);

        [Tooltip("Body text: mineral grey.")]
        [SerializeField] private Color textColor = new Color(0.702f, 0.710f, 0.678f);

        [Tooltip("Keys and buttons: a lighter grey, in bold.")]
        [SerializeField] private Color keyColor = new Color(0.902f, 0.894f, 0.863f);

        [Tooltip("The swap key: swap cyan, which is reserved for swapping.")]
        [SerializeField] private Color swapColor = new Color(0.659f, 0.863f, 0.878f);

        [Tooltip("Security hints: security amber.")]
        [SerializeField] private Color securityColor = new Color(0.827f, 0.631f, 0.302f);

        //==================== STATE =====================
        private InputDevice device = InputDevice.KeyboardMouse;
        private InputDevice builtFor = (InputDevice)(-1);
        private float startTime;
        private Font font;
        private Canvas canvas;
        private CanvasGroup hintGroup;
        private Text hintLabel;
        private CanvasGroup endGroup;
        private Text endLabel;
        private string shownHint;
        private string wantedHint;
        private bool hintIsSecurity;
        private readonly Dictionary<RoomReset, System.Action> resetListeners = new Dictionary<RoomReset, System.Action>();

        //==================== READ ACCESS =====================
        /// <summary>True while a Facility UI screen or sequence covers the game; the hints stay hidden until it ends.</summary>
        public static bool HiddenByInterface { get; set; }

        /// <summary>The device the player used last, for prompts elsewhere in the level.</summary>
        public static InputDevice LastDevice { get; private set; }

        /// <summary>The device the guidance currently describes.</summary>
        public InputDevice ActiveDevice => device;

        /// <summary>The name of the hint on screen, "Intro" for the opening line, or null.</summary>
        public string CurrentHint { get; private set; }

        /// <summary>The text on screen in the hint area, with keys filled in.</summary>
        public string CurrentHintText => hintLabel != null && hintGroup.alpha > 0f ? StripTags(hintLabel.text) : null;


        public IReadOnlyList<Hint> Hints => hints;

        public bool EndVisible => endGroup != null && endGroup.alpha > 0.5f;

        /// <summary>What a sign says now, without formatting.</summary>
        public string SignText(int index) => index >= 0 && index < signs.Count && signs[index].text != null ? StripTags(signs[index].text.text) : null;

        public int SignCount => signs.Count;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }

            if (swap == null && player != null)
            {
                swap = player.GetComponent<SwapAbility>();
            }

            if (goal == null)
            {
                goal = FindAnyObjectByType<LevelGoal>();
            }

            // A generated room replaces its own Room Restart when it is rebuilt, which empties a reference to it. The hint's
            // markers live in the room, so its room is found again from where they are.
            foreach (Hint hint in hints)
            {
                if (hint.room == null)
                {
                    Transform anchor = hint.showNear != null ? hint.showNear : hint.doneAt;
                    hint.room = anchor != null ? RoomReset.FindContaining(anchor.position) : null;
                }
            }

            font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>(BuiltinFontName);
            BuildInterface();
        }

        private void OnEnable()
        {
            foreach (Hint hint in hints)
            {
                RoomReset room = hint.room;
                if (room != null && !resetListeners.ContainsKey(room))
                {
                    System.Action listener = () => OnRoomReset(room);
                    resetListeners[room] = listener;
                    room.WasReset += listener;
                }
            }
        }

        private void OnDisable()
        {
            foreach (KeyValuePair<RoomReset, System.Action> pair in resetListeners)
            {
                if (pair.Key != null)
                {
                    pair.Key.WasReset -= pair.Value;
                }
            }

            resetListeners.Clear();
        }

        private void Start()
        {
            startTime = Time.unscaledTime;
        }

        // A restarted room teaches again from the start; its hints are re-checked from the player's new place next frame.
        private void OnRoomReset(RoomReset room)
        {
            foreach (Hint hint in hints)
            {
                if (hint.room == room)
                {
                    hint.done = false;
                }
            }
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            UpdateDevice();
            LastDevice = device;
            if (canvas != null && canvas.enabled == HiddenByInterface)
            {
                canvas.enabled = !HiddenByInterface;
            }

            if (builtFor != device)
            {
                RefreshSigns();
            }

            bool finished = goal != null && goal.Reached;
            Vector3 at = player.transform.position;
            foreach (Hint hint in hints)
            {
                if (!hint.done && IsDone(hint, at))
                {
                    hint.done = true;
                }
            }

            // The opening line comes first, in the first room, until it has been up for Intro Seconds. A hint about what is
            // right in front of the player takes over at once, so a player who sets off straight away still gets it.
            Hint current = finished ? null : FirstShowable(at);
            bool inFirstRoom = controlsUntil == null || at.x < controlsUntil.position.x;
            bool intro = !finished && current == null && inFirstRoom && Time.unscaledTime - startTime < introSeconds;
            if (intro)
            {
                SetHint("Intro", introText, false);
            }
            else if (current != null)
            {
                SetHint(current.name, current.text, current.security);
            }
            else
            {
                SetHint(null, null, false);
            }

            Fade(endGroup, finished && !string.IsNullOrEmpty(endText));
            AnimateHint();
        }

        //==================== HINTS =====================
        private Hint FirstShowable(Vector3 at)
        {
            foreach (Hint hint in hints)
            {
                if (hint.done || (hint.room != null && !hint.room.PlayerInside))
                {
                    continue;
                }

                if (hint.showNear != null && Vector3.Distance(at, hint.showNear.position) > hint.nearRadius)
                {
                    continue;
                }

                if (hint.onlyWhileAlarmed != null && !hint.onlyWhileAlarmed.IsAlarmed)
                {
                    continue;
                }

                return hint;
            }

            return null;
        }

        private static bool IsDone(Hint hint, Vector3 at)
        {
            if (hint.doneAt == null)
            {
                return false;
            }

            switch (hint.doneRule)
            {
                case DoneRule.PastX:
                    return at.x >= hint.doneAt.position.x;
                case DoneRule.AboveY:
                    return at.y >= hint.doneAt.position.y;
                default:
                    return false;
            }
        }

        private void SetHint(string hintName, string text, bool security)
        {
            CurrentHint = hintName;
            wantedHint = text != null ? Fill(text, device, security ? securityColor : keyColor) : null;
            hintIsSecurity = security;
        }

        // Fades the old line out before the new one fades in, so two hints never share the screen.
        private void AnimateHint()
        {
            float step = Time.unscaledDeltaTime / fadeSeconds;
            if (shownHint != wantedHint)
            {
                hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, 0f, step);
                if (hintGroup.alpha <= 0f)
                {
                    shownHint = wantedHint;
                    hintLabel.text = shownHint ?? string.Empty;
                    hintLabel.color = hintIsSecurity ? securityColor : textColor;
                }

                return;
            }

            hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, shownHint != null ? 1f : 0f, step);
        }

        private void Fade(CanvasGroup group, bool visible)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime / fadeSeconds);
        }

        //==================== KEYS FROM THE BINDINGS =====================
        // The last device that was really used: a pressed key, a mouse click or movement, or a gamepad button or stick.
        private void UpdateDevice()
        {
            Gamepad pad = Gamepad.current;
            if (pad != null && pad.wasUpdatedThisFrame && GamepadInUse(pad))
            {
                device = InputDevice.Gamepad;
            }

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool keyboardUsed = keyboard != null && keyboard.anyKey.wasPressedThisFrame;
            bool mouseUsed = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 4f);
            if (keyboardUsed || mouseUsed)
            {
                device = InputDevice.KeyboardMouse;
            }
        }

        private static bool GamepadInUse(Gamepad pad)
        {
            if (pad.leftStick.ReadValue().sqrMagnitude > 0.25f || pad.rightStick.ReadValue().sqrMagnitude > 0.25f)
            {
                return true;
            }

            foreach (InputControl control in pad.allControls)
            {
                if (control is ButtonControl button && !(control.parent is StickControl) && button.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Replaces the tokens in a text with the real keys for a device, in bold and colour.</summary>
        public string Fill(string text, InputDevice forDevice, Color keyTint)
        {
            return Token.Replace(text, match =>
            {
                string token = match.Groups[1].Value;
                Color tint = token == "Swap" ? swapColor : keyTint;
                return $"<b><color=#{ColorUtility.ToHtmlStringRGB(tint)}>{KeyLabel(token, forDevice)}</color></b>";
            });
        }

        /// <summary>The real key or button for a token on a device, read from its input action. "not set" when it has none.</summary>
        public string KeyLabel(string token, InputDevice forDevice)
        {
            switch (token)
            {
                case "Move":
                    return CompositeLabel(player != null ? player.MoveAction : null, forDevice, null);
                case "Left":
                    return CompositeLabel(player != null ? player.MoveAction : null, forDevice, "negative");
                case "Right":
                    return CompositeLabel(player != null ? player.MoveAction : null, forDevice, "positive");
                case "Run":
                    return Label(player != null ? player.RunAction : null, forDevice);
                case "Jump":
                    return Label(player != null ? player.JumpAction : null, forDevice);
                case "Slide":
                    return Label(player != null ? player.SlideAction : null, forDevice);
                case "Swap":
                    return Label(swap != null ? swap.SwapAction : null, forDevice);
                case "Aim":
                    return forDevice == InputDevice.Gamepad
                        ? Label(swap != null ? swap.AimStickAction : null, forDevice)
                        : Label(swap != null ? swap.PointerAction : null, forDevice);
                case "Use":
                    ControlPanel panel = FindPanel();
                    return Label(panel != null ? panel.InteractAction : null, forDevice);
                case "Observe":
                    ObservationPoint point = FindAnyObjectByType<ObservationPoint>();
                    return Label(point != null ? point.ObserveAction : null, forDevice);
                case "Restart":
                    RoomReset reset = FindAnyObjectByType<RoomReset>();
                    return Label(reset != null ? reset.ResetAction : null, forDevice);
                default:
                    return "{" + token + "}";
            }
        }

        // A panel the player presses, not a pressure pad, which nobody presses.
        private static ControlPanel FindPanel()
        {
            foreach (ControlPanel panel in FindObjectsByType<ControlPanel>(FindObjectsSortMode.InstanceID))
            {
                if (panel.InteractAction != null && panel.InteractAction.bindings.Count > 0)
                {
                    return panel;
                }
            }

            return null;
        }

        private static bool ForDevice(string path, InputDevice forDevice)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return forDevice == InputDevice.Gamepad
                ? path.StartsWith("<Gamepad>", StringComparison.Ordinal)
                : path.StartsWith("<Keyboard>", StringComparison.Ordinal) || path.StartsWith("<Mouse>", StringComparison.Ordinal);
        }

        internal static string Label(InputAction action, InputDevice forDevice)
        {
            if (action == null)
            {
                return NotSet;
            }

            foreach (InputBinding binding in action.bindings)
            {
                if (!binding.isComposite && !binding.isPartOfComposite && ForDevice(binding.effectivePath, forDevice))
                {
                    return Readable(binding.effectivePath);
                }
            }

            return NotSet;
        }

        // A two-way composite such as A and D: both parts, or just one of them.
        internal static string CompositeLabel(InputAction action, InputDevice forDevice, string part)
        {
            if (action == null)
            {
                return NotSet;
            }

            string negative = null;
            string positive = null;
            foreach (InputBinding binding in action.bindings)
            {
                if (binding.isPartOfComposite && ForDevice(binding.effectivePath, forDevice))
                {
                    if (string.Equals(binding.name, "negative", StringComparison.OrdinalIgnoreCase) && negative == null)
                    {
                        negative = Readable(binding.effectivePath);
                    }
                    else if (string.Equals(binding.name, "positive", StringComparison.OrdinalIgnoreCase) && positive == null)
                    {
                        positive = Readable(binding.effectivePath);
                    }
                }
            }

            if (negative == null && positive == null)
            {
                // A stick axis drives both directions.
                string axis = Label(action, forDevice);
                return part == "negative" ? axis + " left" : part == "positive" ? axis + " right" : axis;
            }

            if (part == "negative")
            {
                return negative ?? NotSet;
            }

            if (part == "positive")
            {
                return positive ?? NotSet;
            }

            return $"{negative ?? NotSet} / {positive ?? NotSet}";
        }

        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "<Mouse>/leftButton", "Left click" },
            { "<Mouse>/rightButton", "Right click" },
            { "<Mouse>/position", "Mouse" },
            { "<Gamepad>/buttonSouth", "A / Cross" },
            { "<Gamepad>/buttonEast", "B / Circle" },
            { "<Gamepad>/buttonWest", "X / Square" },
            { "<Gamepad>/buttonNorth", "Y / Triangle" },
            { "<Gamepad>/leftStick", "Left stick" },
            { "<Gamepad>/leftStick/x", "Left stick" },
            { "<Gamepad>/rightStick", "Right stick" },
            { "<Gamepad>/leftStickPress", "L3 (left stick press)" },
            { "<Gamepad>/rightStickPress", "R3 (right stick press)" },
            { "<Gamepad>/select", "View / Select" },
            { "<Gamepad>/start", "Menu / Start" },
            { "<Keyboard>/leftShift", "Left Shift" },
            { "<Keyboard>/space", "Space" }
        };

        private static string Readable(string path)
        {
            if (Names.TryGetValue(path, out string name))
            {
                return name;
            }

            // A key bound by the character it types, such as <Keyboard>/#(W).
            Match typed = Regex.Match(path, @"#\((.+)\)$");
            if (typed.Success)
            {
                return typed.Groups[1].Value.ToUpperInvariant();
            }

            string readable = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return readable.Length == 1 ? readable.ToUpperInvariant() : readable;
        }

        private static string StripTags(string text)
        {
            return Regex.Replace(text ?? string.Empty, "<.*?>", string.Empty);
        }

        //==================== INTERFACE =====================
        // A screen overlay scaled from 1920 x 1080, so it keeps its proportions at any resolution and aspect ratio: the hint
        // line at the top centre and the closing line in the upper middle, both clear of the player, who stands low in the frame.
        private void BuildInterface()
        {
            var canvasObject = new GameObject("Tutorial UI", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform hintPanel = Panel("Hint", canvasObject.transform, new Vector2(0.5f, 1f), new Vector2(0f, -36f), 1100f, out hintGroup);
            hintLabel = MakeText(hintPanel, hintFontSize, textColor, TextAnchor.MiddleCenter, 1064f);
            hintGroup.alpha = 0f;


            RectTransform endPanel = Panel("End", canvasObject.transform, new Vector2(0.5f, 0.72f), Vector2.zero, 900f, out endGroup);
            endLabel = MakeText(endPanel, endFontSize, keyColor, TextAnchor.MiddleCenter, 860f);
            endLabel.text = endText;
            endGroup.alpha = 0f;
            RefreshSigns();
        }


        // The signs show the keys of the device in use, so a gamepad player reads buttons, not keys.
        private void RefreshSigns()
        {
            builtFor = device;
            foreach (Sign sign in signs)
            {
                if (sign.text != null)
                {
                    sign.text.richText = true;
                    sign.text.text = Fill(sign.content, device, keyColor);
                }
            }
        }

        private RectTransform Panel(string panelName, Transform parent, Vector2 anchor, Vector2 offset, float width, out CanvasGroup group)
        {
            var panelObject = new GameObject(panelName, typeof(RectTransform));
            panelObject.transform.SetParent(parent, false);
            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y >= 1f ? 1f : 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(width, 0f);

            var background = panelObject.AddComponent<Image>();
            background.color = panelColor;
            background.raycastTarget = false;
            var layout = panelObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 14, 16);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = panelObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            group = panelObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            return rect;
        }

        private Text MakeText(Transform parent, int size, Color color, TextAnchor alignment, float width)
        {
            var labelObject = new GameObject("Text", typeof(RectTransform));
            labelObject.transform.SetParent(parent, false);
            var text = labelObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var element = labelObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            return text;
        }
    }
}

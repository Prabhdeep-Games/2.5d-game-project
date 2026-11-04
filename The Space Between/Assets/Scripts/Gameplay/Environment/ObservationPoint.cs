using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// A place where the player may stop and look at a part of the facility they cannot reach. Standing close shows a
    /// small prompt; pressing the action hands the view to the ObservationCameraController, and pressing it again
    /// returns it. The player can always leave: it also ends if the time limit runs out or the player is moved away,
    /// for example by a room reset.
    ///
    /// This object only provides data and detects the player. The camera controller runs the transition, so authoring
    /// a new point means placing this object and two empty transforms: where the camera stands, and what it looks at.
    /// Use these sparingly, and only where the view reveals something about the facility.
    ///
    /// It reads its own input action, like the other interactive objects in this project. Keep points away from
    /// control panels, which use the same key.
    /// </summary>
    public class ObservationPoint : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";
        private const int PromptFontSize = 64;
        private const float LeaveMargin = 1f;   // How far past the radius the player must be moved before observing ends.

        //==================== INPUT =====================
        [Header("Input")]
        [Tooltip("Enters and leaves observation. Reads E and the gamepad north button (the west button is the swap).")]
        [SerializeField] private InputAction observeAction = new InputAction("Observe", InputActionType.Button);

        //==================== OBSERVATION SETUP =====================
        [Header("Observation Setup")]
        [Tooltip("The camera controller that runs the transition. Empty means the one in the scene.")]
        [SerializeField] private ObservationCameraController cameraController;

        [Tooltip("Transform used as the camera position while observing this architectural feature.")]
        [SerializeField] private Transform cameraAnchor;

        [Tooltip("Transform the observation camera looks toward.")]
        [SerializeField] private Transform lookTarget;

        [Tooltip("Vertical field of view while observing, in degrees. Authored per point, to frame what it reveals.")]
        [SerializeField, Range(10f, 90f)] private float fieldOfView = 50f;

        [Tooltip("Distance from the player at which this observation point becomes available.")]
        [SerializeField, Min(0.1f)] private float interactionRadius = 2f;

        //==================== PROMPTS =====================
        [Header("Prompts")]
        [Tooltip("Text shown when the player can enter observation mode.")]
        [SerializeField] private string interactionPrompt = "E - Observe";

        [Tooltip("Text shown while observing, when the player can leave by pressing the action again.")]
        [SerializeField] private string exitPrompt = "E - Return";

        [Tooltip("Where the entry prompt appears, relative to this object.")]
        [SerializeField] private Vector3 promptOffset = new Vector3(0f, 2.6f, 0f);

        [Tooltip("Size of the entry prompt text in world units per font pixel.")]
        [SerializeField, Min(0.01f)] private float promptSize = 0.05f;

        [Tooltip("Colour of the entry prompt. Neutral, so it is not mistaken for the swap or a security warning.")]
        [SerializeField] private Color promptColor = new Color(0.8f, 0.82f, 0.8f, 0.9f);

        //==================== CAMERA TRANSITION =====================
        [Header("Camera Transition")]
        [Tooltip("Time in seconds used to move from the gameplay camera to the observation camera.")]
        [SerializeField, Min(0f)] private float enterTransitionDuration = 0.75f;

        [Tooltip("Time in seconds used to return to the gameplay camera. Control returns to the player when it ends.")]
        [SerializeField, Min(0f)] private float exitTransitionDuration = 0.5f;

        //==================== OBSERVATION RULES =====================
        [Header("Observation Rules")]
        [Tooltip("If enabled, player movement and the swap are disabled while observing.")]
        [SerializeField] private bool lockPlayerControl = true;

        [Tooltip("If enabled, the player can leave observation mode by pressing the action again. If off, set a Maximum Observation Duration, or the player cannot leave.")]
        [SerializeField] private bool allowManualExit = true;

        [Tooltip("Optional maximum time in seconds before observation mode exits automatically. Set to zero for no limit.")]
        [SerializeField, Min(0f)] private float maximumObservationDuration;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked when observing starts. Use it for a change of sound or an activity in the observed room.")]
        [SerializeField] private UnityEvent enteredEvent;

        [Tooltip("Invoked when observing ends.")]
        [SerializeField] private UnityEvent exitedEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Draw the radius, the camera position and its view line in the Scene view.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("Whether the player could start observing here right now.")]
        [ReadOnly, SerializeField] private bool isAvailable;

        [Tooltip("Whether this point is being observed right now.")]
        [ReadOnly, SerializeField] private bool isObserving;

        private TextMesh prompt;
        private PlaceholderPlayerController player;
        private Swappable playerSwappable;
        private float enteredAt;

        public Transform CameraAnchor => cameraAnchor;

        public Transform LookTarget => lookTarget;

        public float FieldOfView => fieldOfView;

        public float EnterTransitionDuration => enterTransitionDuration;

        public float ExitTransitionDuration => exitTransitionDuration;

        public bool LockPlayerControl => lockPlayerControl;

        public bool IsAvailable => isAvailable;

        public bool IsObserving => isObserving;

        /// <summary>The text to show while observing. Empty when the player cannot leave by hand.</summary>
        public string ExitPrompt => allowManualExit ? exitPrompt : string.Empty;

        /// <summary>The text the entry prompt shows right now, or empty when it is hidden.</summary>
        public string PromptText => prompt != null && prompt.gameObject.activeSelf ? prompt.text : string.Empty;

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            observeAction = new InputAction("Observe", InputActionType.Button, "<Keyboard>/e");
            observeAction.AddBinding("<Gamepad>/buttonNorth");
        }

        private void Awake()
        {
            if (cameraController == null)
            {
                cameraController = FindAnyObjectByType<ObservationCameraController>();
            }

            if (cameraController == null || cameraAnchor == null || lookTarget == null)
            {
                Debug.LogWarning($"[{nameof(ObservationPoint)}] '{name}' needs a camera controller, a Camera Anchor and a Look Target. It stays unavailable until it has them.", this);
            }

            if (cameraController != null)
            {
                player = cameraController.Player;
                playerSwappable = player != null ? player.GetComponent<Swappable>() : null;
            }

            if (!allowManualExit && maximumObservationDuration <= 0f)
            {
                Debug.LogWarning($"[{nameof(ObservationPoint)}] '{name}' has no manual exit and no time limit, so the player could never leave. It will allow a manual exit.", this);
                allowManualExit = true;
            }

            prompt = CreatePrompt();
        }

        private void OnEnable()
        {
            observeAction.performed += OnObservePerformed;
            observeAction.Enable();
        }

        private void OnDisable()
        {
            observeAction.performed -= OnObservePerformed;
            observeAction.Disable();
            Leave();
            if (prompt != null)
            {
                prompt.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            isAvailable = CanEnter();
            prompt.gameObject.SetActive(isAvailable);

            if (!isObserving)
            {
                return;
            }

            bool timeIsUp = maximumObservationDuration > 0f && Time.unscaledTime - enteredAt >= maximumObservationDuration;
            if (timeIsUp || !PlayerStillHere())
            {
                Leave();
            }
        }

        //==================== OBSERVING =====================
        private void OnObservePerformed(InputAction.CallbackContext context)
        {
            if (isObserving)
            {
                if (allowManualExit)
                {
                    Leave();
                }
            }
            else if (CanEnter() && cameraController.Enter(this))
            {
                isObserving = true;
                enteredAt = Time.unscaledTime;
                enteredEvent?.Invoke();
            }
        }

        private void Leave()
        {
            if (!isObserving)
            {
                return;
            }

            isObserving = false;
            if (cameraController != null)
            {
                cameraController.Exit();
            }

            exitedEvent?.Invoke();
        }

        // Only from standing still on the ground: no observing in the middle of a jump, a wall run or a swap.
        private bool CanEnter()
        {
            if (player == null || cameraAnchor == null || lookTarget == null || cameraController.IsBusy)
            {
                return false;
            }

            bool settled = player.IsGrounded && !player.IsWallRunning && (playerSwappable == null || !playerSwappable.IsFrozen);
            return settled && Vector3.Distance(player.transform.position, transform.position) <= interactionRadius;
        }

        private bool PlayerStillHere()
        {
            return player != null && Vector3.Distance(player.transform.position, transform.position) <= interactionRadius + LeaveMargin;
        }

        private TextMesh CreatePrompt()
        {
            var holder = new GameObject("Observe Prompt");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = promptOffset;
            holder.layer = LayerMask.NameToLayer("Ignore Raycast");

            TextMesh text = holder.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            text.font = font;
            holder.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = PromptFontSize;
            text.characterSize = promptSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = promptColor;
            text.text = interactionPrompt;

            holder.SetActive(false);
            return text;
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            Gizmos.color = new Color(0.8f, 0.82f, 0.8f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactionRadius);

            if (cameraAnchor != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(cameraAnchor.position, 0.25f);
                if (lookTarget != null)
                {
                    Gizmos.DrawLine(cameraAnchor.position, lookTarget.position);
                }
            }
        }
    }
}

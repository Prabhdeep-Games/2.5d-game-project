using Unity.Cinemachine;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Moves the view to an ObservationPoint and back, and keeps the player safe while it does.
    ///
    /// The points only supply data: where the camera stands, what it looks at, how long the blends take. This is the one
    /// place that runs the transition, so there is one shared observation camera however many points a level has.
    /// It is a CinemachineCamera with no follow target, switched on while observing. Its high priority makes the
    /// Cinemachine Brain blend to it, and switching it off blends back to the gameplay camera, which keeps following the
    /// player and is left untouched.
    ///
    /// While observing, player movement is locked and the swap is switched off. Control only returns when the blend
    /// back has finished, so nothing can happen while the player cannot see their own surroundings.
    ///
    /// Put this on the CinemachineCamera used for observing. Leave that component switched off in the scene.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class ObservationCameraController : MonoBehaviour
    {
        private enum ObservationState
        {
            Idle,
            Entering,
            Observing,
            Returning
        }

        private const string BuiltinFontName = "LegacyRuntime.ttf";
        private const int PromptFontSize = 64;

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The player to lock while observing. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The brain that does the blending. Empty means the one on the main camera.")]
        [SerializeField] private CinemachineBrain brain;

        //==================== EXIT PROMPT =====================
        [Header("Exit Prompt")]
        [Tooltip("How far below the middle of the screen the exit prompt sits: 0 is the middle, 1 is the bottom edge. It stays there whatever field of view a point uses.")]
        [SerializeField, Range(0f, 1f)] private float promptScreenHeight = 0.88f;

        [Tooltip("How far in front of the observation camera the exit prompt is drawn, in world units. Close, so nothing in the view can cover it.")]
        [SerializeField, Min(0.5f)] private float promptDistance = 2f;

        [Tooltip("Size of the prompt text in world units per font pixel.")]
        [SerializeField, Min(0.0001f)] private float promptSize = 0.0045f;

        [Tooltip("Colour of the prompt text. Neutral, so it is not mistaken for the swap or a security warning.")]
        [SerializeField] private Color promptColor = new Color(0.88f, 0.9f, 0.88f, 1f);

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each change of state to the Console.")]
        [SerializeField] private bool logChanges;

        [Tooltip("What the observation camera is doing right now.")]
        [ReadOnly, SerializeField] private ObservationState state = ObservationState.Idle;

        [Tooltip("The point being observed. Empty when not observing.")]
        [ReadOnly, SerializeField] private ObservationPoint currentPoint;

        private CinemachineCamera observationCamera;
        private CinemachineBlendDefinition gameplayBlend;
        private Behaviour[] lockedAbilities = new Behaviour[0];
        private TextMesh exitPrompt;
        private float stateEndsAt;

        /// <summary>True from entering until control is back with the player.</summary>
        public bool IsBusy => state != ObservationState.Idle;

        /// <summary>The point being observed, or null.</summary>
        public ObservationPoint CurrentPoint => currentPoint;

        /// <summary>The prompt shown while observing, or empty when it is hidden.</summary>
        public string ExitPromptText => exitPrompt != null && exitPrompt.gameObject.activeSelf ? exitPrompt.text : string.Empty;

        public PlaceholderPlayerController Player => player;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            observationCamera = GetComponent<CinemachineCamera>();
            observationCamera.enabled = false;

            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }

            if (brain == null && Camera.main != null)
            {
                brain = Camera.main.GetComponent<CinemachineBrain>();
            }

            if (player == null || brain == null)
            {
                Debug.LogWarning($"[{nameof(ObservationCameraController)}] '{name}' needs a player and a Cinemachine Brain. Assign them.", this);
            }
            else
            {
                gameplayBlend = brain.DefaultBlend;
                lockedAbilities = new Behaviour[]
                {
                    player.GetComponent<SwapAbility>(),
                    player.GetComponent<SwapFeedback>(),
                    player.GetComponent<SwapTargetIndicator>()
                };
            }

            exitPrompt = CreatePrompt();
        }

        private void OnDisable()
        {
            // Never leave the player locked, whatever ends this.
            if (IsBusy)
            {
                FinishReturning();
            }
        }

        // Uses real time, so a change of game speed can never leave the player stuck here.
        private void Update()
        {
            if (Time.unscaledTime < stateEndsAt)
            {
                return;
            }

            if (state == ObservationState.Entering)
            {
                SetState(ObservationState.Observing);
                exitPrompt.text = currentPoint != null ? currentPoint.ExitPrompt : string.Empty;
                exitPrompt.gameObject.SetActive(!string.IsNullOrEmpty(exitPrompt.text));
            }
            else if (state == ObservationState.Returning)
            {
                FinishReturning();
            }
        }

        //==================== PUBLIC API =====================
        /// <summary>Starts observing the given point. Refused while a transition is running. Returns true when it started.</summary>
        public bool Enter(ObservationPoint point)
        {
            if (IsBusy || point == null || player == null || brain == null || point.CameraAnchor == null)
            {
                return false;
            }

            currentPoint = point;
            SetPlayerLocked(point.LockPlayerControl);

            transform.position = point.CameraAnchor.position;
            Vector3 toTarget = point.LookTarget != null ? point.LookTarget.position - transform.position : point.CameraAnchor.forward;
            transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);

            LensSettings lens = observationCamera.Lens;
            lens.FieldOfView = point.FieldOfView;
            observationCamera.Lens = lens;

            float halfHeight = Mathf.Tan(point.FieldOfView * 0.5f * Mathf.Deg2Rad) * promptDistance;
            exitPrompt.transform.localPosition = new Vector3(0f, -halfHeight * promptScreenHeight, promptDistance);

            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, point.EnterTransitionDuration);
            observationCamera.enabled = true;

            stateEndsAt = Time.unscaledTime + point.EnterTransitionDuration;
            SetState(ObservationState.Entering);
            return true;
        }

        /// <summary>Starts the blend back to the gameplay camera. Control returns to the player when it has finished.</summary>
        public void Exit()
        {
            if (state != ObservationState.Entering && state != ObservationState.Observing)
            {
                return;
            }

            exitPrompt.gameObject.SetActive(false);
            float seconds = currentPoint != null ? currentPoint.ExitTransitionDuration : gameplayBlend.Time;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, seconds);
            observationCamera.enabled = false;

            stateEndsAt = Time.unscaledTime + seconds;
            SetState(ObservationState.Returning);
        }

        //==================== PRIVATE =====================
        private void FinishReturning()
        {
            SetPlayerLocked(false);
            observationCamera.enabled = false;
            if (exitPrompt != null)
            {
                exitPrompt.gameObject.SetActive(false);
            }

            if (brain != null)
            {
                brain.DefaultBlend = gameplayBlend;
            }

            currentPoint = null;
            SetState(ObservationState.Idle);
        }

        private void SetPlayerLocked(bool locked)
        {
            if (player == null)
            {
                return;
            }

            player.ControlLocked = locked;
            foreach (Behaviour ability in lockedAbilities)
            {
                if (ability != null)
                {
                    ability.enabled = !locked;
                }
            }
        }

        private void SetState(ObservationState next)
        {
            state = next;
            if (logChanges)
            {
                Debug.Log($"[{nameof(ObservationCameraController)}] {next}" + (currentPoint != null ? $" '{currentPoint.name}'" : string.Empty), this);
            }
        }

        private TextMesh CreatePrompt()
        {
            var holder = new GameObject("Exit Prompt");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = Vector3.forward * promptDistance;
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

            holder.SetActive(false);
            return text;
        }
    }
}

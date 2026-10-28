using Unity.Cinemachine;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Widens the view while the player wall runs or jumps between walls, so more of what lies ahead is visible.
    /// It pulls the camera back a little and turns on the Position Composer's look ahead, then eases both back
    /// to the normal framing when the player is on the ground again.
    ///
    /// Put this on the CinemachineCamera object that has the Position Composer.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera), typeof(CinemachinePositionComposer))]
    public class CameraWallRunFraming : MonoBehaviour
    {
        // How strongly the look ahead target is smoothed by Cinemachine. Low values react quickly but can shake.
        private const float LookaheadSmoothing = 4f;

        //==================== TARGET =====================
        [Header("Target")]
        [Tooltip("The player to watch. Empty means the PlaceholderPlayerController on the camera's Tracking Target.")]
        [SerializeField] private PlaceholderPlayerController player;

        //==================== FRAMING =====================
        [Header("Framing")]
        [Tooltip("How much further back the camera sits while wall running, in world units. More shows more of the route ahead but makes the player smaller.")]
        [SerializeField, Min(0f)] private float zoomOut = 3f;

        [Tooltip("How far ahead of the player's movement the camera aims, in seconds of travel. 0 turns look ahead off.")]
        [SerializeField, Range(0f, 1f)] private float lookAhead = 0.3f;

        [Tooltip("How quickly the camera changes to and from the wall run framing. 1 takes about a second; higher is quicker.")]
        [SerializeField, Min(0.1f)] private float blendSpeed = 2.5f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("0 is the normal framing, 1 is the full wall run framing.")]
        [ReadOnly, SerializeField] private float framingWeight;

        private CinemachineCamera cinemachineCamera;
        private CinemachinePositionComposer composer;
        private float baseDistance;
        private LookaheadSettings baseLookahead;

        public float FramingWeight => framingWeight;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            cinemachineCamera = GetComponent<CinemachineCamera>();
            composer = GetComponent<CinemachinePositionComposer>();
        }

        private void OnEnable()
        {
            if (player == null && cinemachineCamera.Follow != null)
            {
                player = cinemachineCamera.Follow.GetComponent<PlaceholderPlayerController>();
            }

            if (player == null)
            {
                Debug.LogWarning($"[{nameof(CameraWallRunFraming)}] No PlaceholderPlayerController found. Assign one or set the camera's Tracking Target.", this);
            }

            baseDistance = composer.CameraDistance;
            baseLookahead = composer.Lookahead;
        }

        private void OnDisable()
        {
            // Leave the composer exactly as it was set up in the scene.
            composer.CameraDistance = baseDistance;
            composer.Lookahead = baseLookahead;
        }

        // Runs before the Cinemachine Brain's LateUpdate, so the camera uses the new values in the same frame.
        private void Update()
        {
            if (player == null)
            {
                return;
            }

            float target = player.IsWallRunning || player.IsInWallJump ? 1f : 0f;
            framingWeight = Mathf.MoveTowards(framingWeight, target, blendSpeed * Time.deltaTime);
            float eased = Mathf.SmoothStep(0f, 1f, framingWeight);

            composer.CameraDistance = baseDistance + zoomOut * eased;

            LookaheadSettings settings = composer.Lookahead;
            settings.Enabled = lookAhead > 0f;
            settings.Time = lookAhead * eased;
            settings.Smoothing = LookaheadSmoothing;
            settings.IgnoreY = false;
            composer.Lookahead = settings;
        }
    }
}

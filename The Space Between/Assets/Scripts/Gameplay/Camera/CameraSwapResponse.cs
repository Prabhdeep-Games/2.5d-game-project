using Unity.Cinemachine;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Tells a Cinemachine camera what to do when the player is moved by a swap.
    ///
    /// Cut: the camera jumps by the same distance as the player, so the player stays where they
    ///      were on screen. The swap itself is the transition, and nothing slides across the view.
    /// Glide: the camera follows with its normal damping and sweeps across to the new position.
    ///
    /// Put this on the CinemachineCamera object.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraSwapResponse : MonoBehaviour
    {
        //==================== TARGET =====================
        [Header("Target")]
        [Tooltip("The ability whose swaps move the player. Empty means the SwapAbility on the camera's Tracking Target.")]
        [SerializeField] private SwapAbility ability;

        //==================== RESPONSE =====================
        [Header("Response")]
        [Tooltip("On: the camera cuts with the player. Off: the camera glides to the new position using its damping.")]
        [SerializeField] private bool cutOnSwap = true;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write the distance the camera was moved to the Console after each swap.")]
        [SerializeField] private bool logWarps;

        private CinemachineCamera cinemachineCamera;
        private Transform player;
        private Vector3 lastPlayerPosition;
        private SmoothSwapAbility smoothAbility;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            cinemachineCamera = GetComponent<CinemachineCamera>();
        }

        private void OnEnable()
        {
            if (ability == null && cinemachineCamera.Follow != null)
            {
                ability = cinemachineCamera.Follow.GetComponent<SwapAbility>();
            }

            if (ability == null)
            {
                Debug.LogWarning($"[{nameof(CameraSwapResponse)}] No SwapAbility found. Assign one or set the camera's Tracking Target.", this);
                return;
            }

            player = ability.transform;
            lastPlayerPosition = player.position;

            // A smooth swap moves the player part way through, so wait until it has finished.
            smoothAbility = ability as SmoothSwapAbility;
            if (smoothAbility != null)
            {
                smoothAbility.SwapCompleted += OnSwapFinished;
            }
            else
            {
                ability.Swapped += OnSwapFinished;
            }
        }

        private void OnDisable()
        {
            if (ability == null)
            {
                return;
            }

            if (smoothAbility != null)
            {
                smoothAbility.SwapCompleted -= OnSwapFinished;
            }
            else
            {
                ability.Swapped -= OnSwapFinished;
            }
        }

        private void LateUpdate()
        {
            // The position is not recorded during a swap, so the jump is measured from before it.
            if (player != null && !ability.IsSwapping)
            {
                lastPlayerPosition = player.position;
            }
        }

        //==================== RESPONSE =====================
        private void OnSwapFinished(Swappable other)
        {
            Vector3 delta = player.position - lastPlayerPosition;
            lastPlayerPosition = player.position;

            if (cutOnSwap)
            {
                cinemachineCamera.OnTargetObjectWarped(player, delta);
            }

            if (logWarps)
            {
                Debug.Log($"[{nameof(CameraSwapResponse)}] Swap moved the player by {delta.magnitude:F2} units. Camera {(cutOnSwap ? "cut" : "glides")}.", this);
            }
        }
    }
}

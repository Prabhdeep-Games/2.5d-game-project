using Unity.Cinemachine;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Tells a Cinemachine camera what to do around a swap.
    ///
    /// Cut: the camera jumps by the same distance as the player, so the player stays where they
    ///      were on screen. The swap itself is the transition, and nothing slides across the view.
    /// Glide: the camera follows with its normal damping and sweeps across to the new position.
    ///
    /// Lean: while a valid swap target is in sight, the camera drifts a little toward it, by moving the Position
    ///       Composer's Target Offset and, if the camera has a Cinemachine Recomposer, turning it a degree or so. Half
    ///       strength while aiming, full strength from the swap press until the swap has played out. Only on the ground
    ///       and off the walls, so platforming framing is left alone. The composer's own damping smooths it.
    ///
    /// Put this on the CinemachineCamera object.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraSwapResponse : MonoBehaviour
    {
        // Furthest the camera's aim point moves toward a target, in world units, and furthest it turns, in degrees.
        private const float MaxLeanDistance = 1f;
        private const float MaxLeanDegrees = 2f;

        // Lean strength while a valid target is only aimed at. Pressing swap raises it to 1.
        private const float AimedLeanStrength = 0.5f;

        // Seconds the lean takes to grow from nothing to full, and to fade back out. Game time, so it holds during a swap.
        private const float LeanInSeconds = 0.35f;
        private const float LeanOutSeconds = 0.5f;

        //==================== TARGET =====================
        [Header("Target")]
        [Tooltip("The ability whose swaps move the player. Empty means the SwapAbility on the camera's Tracking Target.")]
        [SerializeField] private SwapAbility ability;

        //==================== RESPONSE =====================
        [Header("Response")]
        [Tooltip("On: the camera cuts with the player. Off: the camera glides to the new position using its damping.")]
        [SerializeField] private bool cutOnSwap = true;

        [Tooltip("Drift the camera a little toward a valid swap target. Turning needs a Cinemachine Recomposer on this camera; without one it only moves.")]
        [SerializeField] private bool leanTowardTarget;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write the distance the camera was moved to the Console after each swap.")]
        [SerializeField] private bool logWarps;

        [Tooltip("How strongly the camera leans toward the target right now, from 0 to 1.")]
        [ReadOnly, SerializeField] private float leanWeight;

        private CinemachineCamera cinemachineCamera;
        private CinemachinePositionComposer composer;
        private CinemachineRecomposer recomposer;
        private PlaceholderPlayerController movement;
        private Transform player;
        private Vector3 lastPlayerPosition;
        private Vector3 baseTargetOffset;
        private float basePan;
        private float baseTilt;
        private Vector3 leanDirection;

        /// <summary>How strongly the camera leans toward the target, from 0 to 1.</summary>
        public float LeanWeight => leanWeight;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            cinemachineCamera = GetComponent<CinemachineCamera>();
            composer = GetComponent<CinemachinePositionComposer>();
            recomposer = GetComponent<CinemachineRecomposer>();
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
            movement = ability.GetComponent<PlaceholderPlayerController>();
            lastPlayerPosition = player.position;

            if (composer != null)
            {
                baseTargetOffset = composer.TargetOffset;
            }

            if (recomposer != null)
            {
                basePan = recomposer.Pan;
                baseTilt = recomposer.Tilt;
            }

            // A Shrink swap moves the player part way through, so wait until both bodies have arrived.
            ability.SwapCompleted += OnSwapFinished;
        }

        private void OnDisable()
        {
            if (ability == null)
            {
                return;
            }

            // Leave the camera exactly as it was set up in the scene.
            if (leanTowardTarget)
            {
                leanWeight = 0f;
                ApplyLean();
            }

            ability.SwapCompleted -= OnSwapFinished;
        }

        // Runs before the Cinemachine Brain's LateUpdate, so the camera uses the new lean in the same frame.
        private void Update()
        {
            if (player == null || !leanTowardTarget)
            {
                return;
            }

            UpdateLean();
            ApplyLean();
        }

        private void LateUpdate()
        {
            // The position is not recorded during a swap, so the jump is measured from before it.
            if (player != null && !ability.IsSwapping)
            {
                lastPlayerPosition = player.position;
            }
        }

        //==================== LEAN =====================
        private void UpdateLean()
        {
            // The same target the character reaches for: the locked one after the press, otherwise an accepted aim.
            Swappable target = ability.IsGesturing ? ability.FocusTarget
                : ability.TargetStatus == SwapRejection.None ? ability.Target : null;

            if (target != null)
            {
                Vector3 toTarget = target.Center - ability.Origin;
                toTarget.z = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    leanDirection = toTarget.normalized;
                }
            }

            bool pressed = ability.IsGesturing || ability.IsSwapping;
            bool steady = movement == null || (movement.IsGrounded && !movement.IsWallRunning && !movement.IsInWallJump);
            float goal = steady && (target != null || ability.IsSwapping) ? (pressed ? 1f : AimedLeanStrength) : 0f;
            float seconds = goal > leanWeight ? LeanInSeconds : LeanOutSeconds;
            leanWeight = Mathf.MoveTowards(leanWeight, goal, Time.deltaTime / seconds);
        }

        private void ApplyLean()
        {
            float eased = Mathf.SmoothStep(0f, 1f, leanWeight);

            if (composer != null)
            {
                // Target Offset is read in the tracking target's own frame.
                Vector3 shift = leanDirection * (MaxLeanDistance * eased);
                composer.TargetOffset = baseTargetOffset + (player != null ? player.InverseTransformDirection(shift) : shift);
            }

            if (recomposer != null)
            {
                // Pan turns right for a positive value; Tilt turns down for a positive value.
                recomposer.Pan = basePan + leanDirection.x * MaxLeanDegrees * eased;
                recomposer.Tilt = baseTilt - leanDirection.y * MaxLeanDegrees * eased;
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

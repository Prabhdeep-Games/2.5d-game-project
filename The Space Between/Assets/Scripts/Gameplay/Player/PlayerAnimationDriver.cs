using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Passes the player's movement state to the Animator. It only reports what the movement already did; the
    /// Animator never decides whether the player moves, jumps or wall runs, so a missing or unfinished clip can
    /// never block traversal.
    ///
    /// Sustained conditions are written every frame:
    ///   Speed          float  how fast the player moved last frame (idle or running)
    ///   IsGrounded     bool   standing on something
    ///   IsWallRunning  bool   on a surface in a wall run
    ///   IsTraversing   bool   the body is still turning to a new surface direction (a curve, or attaching to a wall)
    ///   IsSliding      bool   sliding along the ground
    /// One-shot events are triggers, set when the movement raises the matching event:
    ///   Jump, WallJump, Land (only for a landing at least Min Landing Speed hard), Swap
    ///
    /// Put this on the player, next to the PlaceholderPlayerController. The Animator sits on the Body child.
    /// </summary>
    // Runs after the movement's Update, so the parameters describe this frame's movement. The Animator reads them
    // after every Update has run, so nothing is a frame late.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(PlaceholderPlayerController))]
    public class PlayerAnimationDriver : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int IsGroundedId = Animator.StringToHash("IsGrounded");
        private static readonly int IsWallRunningId = Animator.StringToHash("IsWallRunning");
        private static readonly int IsTraversingId = Animator.StringToHash("IsTraversing");
        private static readonly int IsSlidingId = Animator.StringToHash("IsSliding");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int WallJumpId = Animator.StringToHash("WallJump");
        private static readonly int LandId = Animator.StringToHash("Land");
        private static readonly int SwapId = Animator.StringToHash("Swap");

        private static readonly string[] RequiredParameters =
        {
            "Speed", "IsGrounded", "IsWallRunning", "IsTraversing", "IsSliding", "Jump", "WallJump", "Land", "Swap"
        };

        // The states of the player controllers, so the debug field can name the current one.
        private static readonly string[] KnownStates =
        {
            "Idle", "Run", "Locomotion", "Jump", "Fall", "Land", "Slide", "WallRun", "Traverse", "WallJump", "Swap"
        };

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The Animator on the player's visible body. Empty means the first Animator in the children.")]
        [SerializeField] private Animator animator;

        //==================== LANDING =====================
        [Header("Landing")]
        [Tooltip("Slowest downward speed, in units per second, that sets the Land trigger. Smaller drops, like stepping off a low ledge, go straight back to idle or running.")]
        [SerializeField, Min(0f)] private float minLandingSpeed = 4f;

        //==================== SMOOTHING =====================
        [Header("Smoothing")]
        [Tooltip("Seconds the Speed parameter takes to catch up with the real speed. Above 0 stops a blend between walking and running from jumping when the speed changes suddenly. 0 passes it straight through.")]
        [SerializeField, Min(0f)] private float speedDampSeconds;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write every trigger to the Console.")]
        [SerializeField] private bool logTriggers;

        [Tooltip("The Animator state the body is in right now.")]
        [ReadOnly, SerializeField] private string currentState = "none";

        private PlaceholderPlayerController movement;
        private SwapAbility swapAbility;
        private bool animatorReady;

        /// <summary>The name of the Animator state the body is in right now, or "none".</summary>
        public string CurrentState => currentState;

        public Animator Animator => animator;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            movement = GetComponent<PlaceholderPlayerController>();
            swapAbility = GetComponent<SwapAbility>();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            animatorReady = CheckAnimator();
        }

        private void OnEnable()
        {
            movement.Jumped += OnJumped;
            movement.WallJumped += OnWallJumped;
            movement.Landed += OnLanded;
            if (swapAbility != null)
            {
                swapAbility.Swapped += OnSwapped;
            }
        }

        private void OnDisable()
        {
            movement.Jumped -= OnJumped;
            movement.WallJumped -= OnWallJumped;
            movement.Landed -= OnLanded;
            if (swapAbility != null)
            {
                swapAbility.Swapped -= OnSwapped;
            }
        }

        private void Update()
        {
            if (!animatorReady)
            {
                return;
            }

            animator.SetFloat(SpeedId, movement.Speed, speedDampSeconds, Time.deltaTime);
            animator.SetBool(IsGroundedId, movement.IsGrounded);
            animator.SetBool(IsWallRunningId, movement.IsWallRunning);
            animator.SetBool(IsTraversingId, movement.IsTraversing);
            animator.SetBool(IsSlidingId, movement.IsSliding);

            // Landing straight into a slide skips the landing animation, so its trigger must not wait around for later.
            if (movement.IsSliding)
            {
                animator.ResetTrigger(LandId);
            }

            currentState = NameOf(animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
        }

        //==================== EVENTS =====================
        private void OnJumped(bool fromGround)
        {
            Fire(JumpId, "Jump");
        }

        private void OnWallJumped()
        {
            Fire(WallJumpId, "WallJump");
        }

        private void OnLanded(float impactSpeed)
        {
            if (impactSpeed >= minLandingSpeed)
            {
                Fire(LandId, "Land");
            }
        }

        private void OnSwapped(Swappable target)
        {
            Fire(SwapId, "Swap");
        }

        private void Fire(int id, string parameter)
        {
            if (!animatorReady)
            {
                return;
            }

            animator.SetTrigger(id);
            if (logTriggers)
            {
                Debug.Log($"[{nameof(PlayerAnimationDriver)}] {parameter} at {Time.time:F2} s", this);
            }
        }

        //==================== CHECKS =====================
        // A missing Animator or parameter is reported once, instead of failing silently every frame.
        private bool CheckAnimator()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[{nameof(PlayerAnimationDriver)}] '{name}' has no Animator with a controller, so no animation plays. Movement is unaffected.", this);
                return false;
            }

            foreach (string parameter in RequiredParameters)
            {
                if (!HasParameter(parameter))
                {
                    Debug.LogWarning($"[{nameof(PlayerAnimationDriver)}] The Animator controller '{animator.runtimeAnimatorController.name}' has no '{parameter}' parameter. Add it, spelled exactly like this.", this);
                }
            }

            return true;
        }

        private bool HasParameter(string parameter)
        {
            foreach (AnimatorControllerParameter existing in animator.parameters)
            {
                if (existing.name == parameter)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NameOf(int stateHash)
        {
            foreach (string state in KnownStates)
            {
                if (Animator.StringToHash(state) == stateHash)
                {
                    return state;
                }
            }

            return "other";
        }
    }
}

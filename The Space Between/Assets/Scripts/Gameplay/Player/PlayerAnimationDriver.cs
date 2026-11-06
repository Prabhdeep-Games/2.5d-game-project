using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Passes the player's movement state to the Animator. It only reports what the movement already did; the
    /// Animator never decides whether the player moves, jumps or wall runs, so a missing or unfinished clip can
    /// never block traversal.
    ///
    /// Sustained conditions are written every frame:
    ///   Speed          float  how fast the player really moves along the ground or a surface, or sideways in the air (smoothed)
    ///   VerticalSpeed  float  how fast the player really moves up (positive) or down (negative), for the jump apex and the fall
    ///   IsGrounded     bool   standing on something
    ///   IsWallRunning  bool   on a surface in a wall run
    ///   IsTraversing   bool   the body is still turning to a new surface direction (a curve, or attaching to a wall)
    ///   IsSliding      bool   sliding along the ground
    /// One-shot events are triggers, set when the movement raises the matching event:
    ///   Jump, WallJump, Swap
    ///   Land      a landing at least Min Landing Speed hard
    ///   HardLand  such a landing after a fall of at least Hard Landing Height, played as the roll
    ///
    /// Put this on the player, next to the PlaceholderPlayerController. The Animator sits on the Body child.
    /// </summary>
    // Runs after the movement's Update, so the parameters describe this frame's movement. The Animator reads them
    // after every Update has run, so nothing is a frame late.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(PlaceholderPlayerController))]
    public class PlayerAnimationDriver : MonoBehaviour
    {
        private const string RollState = "Roll";

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
        private static readonly int IsGroundedId = Animator.StringToHash("IsGrounded");
        private static readonly int IsWallRunningId = Animator.StringToHash("IsWallRunning");
        private static readonly int IsTraversingId = Animator.StringToHash("IsTraversing");
        private static readonly int IsSlidingId = Animator.StringToHash("IsSliding");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int WallJumpId = Animator.StringToHash("WallJump");
        private static readonly int LandId = Animator.StringToHash("Land");
        private static readonly int HardLandId = Animator.StringToHash("HardLand");
        private static readonly int SwapId = Animator.StringToHash("Swap");

        private static readonly string[] RequiredParameters =
        {
            "Speed", "VerticalSpeed", "IsGrounded", "IsWallRunning", "IsTraversing", "IsSliding",
            "Jump", "WallJump", "Land", "HardLand", "Swap"
        };

        // The states of the player controllers, so the debug field can name the current one.
        private static readonly string[] KnownStates =
        {
            "Idle", "Run", "Locomotion", "Jump", "Fall", "Land", RollState, "Slide", "WallRun", "Traverse", "WallJump", "Swap"
        };

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The Animator on the player's visible body. Empty means the first Animator in the children.")]
        [SerializeField] private Animator animator;

        //==================== LANDING =====================
        [Header("Landing")]
        [Tooltip("Slowest downward speed, in units per second, that plays a landing. Smaller drops, like an ordinary jump or stepping off a low ledge, go straight back to idle or running.")]
        [SerializeField, Min(0f)] private float minLandingSpeed = 4f;

        [Tooltip("Fall height in metres (1 world unit is 1 m), from the highest point in the air down to the ground, at which a landing becomes the landing roll. The player is 2 m tall and an ordinary jump peaks 1.8 m up. Without a Roll state in the Animator controller, every landing is a normal one.")]
        [SerializeField, Min(0f)] private float hardLandingHeight = 6f;

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

        [Tooltip("How the last landing was played, with its fall height and impact speed.")]
        [ReadOnly, SerializeField] private string lastLanding = "none yet";

        private PlaceholderPlayerController movement;
        private SwapAbility swapAbility;
        private bool animatorReady;
        private bool hasRollState;

        /// <summary>The name of the Animator state the body is in right now, or "none".</summary>
        public string CurrentState => currentState;

        /// <summary>How the last landing was played: "roll", "land" or "none", with its fall height and impact speed.</summary>
        public string LastLanding => lastLanding;

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

        // The Animator has bound its controller by now, so its states can be looked up.
        private void Start()
        {
            hasRollState = animatorReady && animator.HasState(0, Animator.StringToHash(animator.GetLayerName(0) + "." + RollState));
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

            animator.SetFloat(SpeedId, LocomotionSpeed(), speedDampSeconds, Time.deltaTime);
            animator.SetFloat(VerticalSpeedId, movement.Velocity.y);
            animator.SetBool(IsGroundedId, movement.IsGrounded);
            animator.SetBool(IsWallRunningId, movement.IsWallRunning);
            animator.SetBool(IsTraversingId, movement.IsTraversing);
            animator.SetBool(IsSlidingId, movement.IsSliding);

            // A landing only plays on the ground it happened on. Jumping straight off again, or landing into a slide,
            // drops it, so it cannot fire later in mid-air.
            if (!movement.IsGrounded || movement.IsSliding)
            {
                animator.ResetTrigger(LandId);
                animator.ResetTrigger(HardLandId);
            }

            currentState = NameOf(animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
        }

        //==================== PARAMETERS =====================
        // Along the ground or a surface the full speed counts, so running up a slope or a wall still reads as running.
        // In the air only the sideways part counts, so a standing jump is not mistaken for a running one.
        private float LocomotionSpeed()
        {
            Vector3 velocity = movement.Velocity;
            return movement.IsGrounded || movement.IsWallRunning ? velocity.magnitude : Mathf.Abs(velocity.x);
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

        // The impact speed confirms a real fall, so being placed somewhere lower, as a test or a reset does, never rolls.
        private void OnLanded(float impactSpeed)
        {
            float fallHeight = movement.LastFallHeight;
            string measured = $"after {fallHeight:F1} units at {impactSpeed:F1} u/s";

            if (impactSpeed < minLandingSpeed)
            {
                lastLanding = "none " + measured;
                return;
            }

            bool hard = hasRollState && fallHeight >= hardLandingHeight;
            lastLanding = (hard ? "roll " : "land ") + measured;
            Fire(hard ? HardLandId : LandId, hard ? "HardLand" : "Land");
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

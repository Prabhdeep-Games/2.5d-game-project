using UnityEngine;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// Temporary side-scroller movement so the swap mechanic can be tried in a scene.
    /// Moves along the world X axis, jumps, and falls. It will be replaced by the real
    /// movement work (Trello card 2), so it is deliberately small.
    ///
    /// It reads its own input actions, so no Player Input component is needed on the player.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlaceholderPlayerController : MonoBehaviour, IMotionResettable, IAirJumpGrantable
    {
        private const float GroundedStickSpeed = -2f;

        //==================== INPUT =====================
        [Header("Input")]
        [Tooltip("Horizontal movement. Reads A and D, the arrow keys, and the gamepad left stick.")]
        [SerializeField] private InputAction moveAction = new InputAction("Move", InputActionType.Value);

        [Tooltip("Jump. Reads Space and the gamepad south button.")]
        [SerializeField] private InputAction jumpAction = new InputAction("Jump", InputActionType.Button);

        //==================== MOVEMENT =====================
        [Header("Movement")]
        [Tooltip("Horizontal speed in units per second.")]
        [SerializeField, Min(0f)] private float moveSpeed = 6f;

        [Tooltip("How high a jump lifts the player, in world units.")]
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.8f;

        [Tooltip("Downward acceleration in units per second squared. Use a positive number.")]
        [SerializeField, Min(0.1f)] private float gravity = 25f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Whether the player is standing on something.")]
        [ReadOnly, SerializeField] private bool isGrounded;

        [Tooltip("Current vertical speed.")]
        [ReadOnly, SerializeField] private float verticalSpeed;

        [Tooltip("Extra jumps that can still be used in mid-air. A swap that leaves the player airborne grants one.")]
        [ReadOnly, SerializeField] private int airJumpsLeft;

        private CharacterController controller;
        private Swappable swappable;
        private bool treatAsAirborneThisFrame;

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            moveAction = new InputAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            moveAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick/x");

            jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            swappable = GetComponent<Swappable>();
        }

        private void OnEnable()
        {
            moveAction.Enable();
            jumpAction.Enable();
        }

        private void OnDisable()
        {
            moveAction.Disable();
            jumpAction.Disable();
        }

        private void Update()
        {
            // A swap in progress holds the player still, so input is ignored until it ends.
            if (swappable != null && swappable.IsFrozen)
            {
                return;
            }

            // Right after a teleport the controller's grounded flag is out of date, so one frame is skipped.
            isGrounded = controller.isGrounded && !treatAsAirborneThisFrame;
            treatAsAirborneThisFrame = false;

            if (isGrounded)
            {
                airJumpsLeft = 0;
                if (verticalSpeed < 0f)
                {
                    verticalSpeed = GroundedStickSpeed;
                }
            }

            if (jumpAction.WasPressedThisFrame())
            {
                if (isGrounded)
                {
                    verticalSpeed = JumpSpeed();
                }
                else if (airJumpsLeft > 0)
                {
                    airJumpsLeft--;
                    verticalSpeed = JumpSpeed();
                }
            }

            verticalSpeed -= gravity * Time.deltaTime;

            float horizontal = moveAction.ReadValue<float>() * moveSpeed;
            Vector3 velocity = new Vector3(horizontal, verticalSpeed, 0f);
            controller.Move(velocity * Time.deltaTime);
        }

        //==================== PUBLIC API =====================
        /// <summary>Gives one extra jump that works in mid-air. Landing removes it if unused.</summary>
        public void GrantAirJump()
        {
            airJumpsLeft = 1;
            treatAsAirborneThisFrame = true;
        }

        /// <summary>Removes the vertical speed, called after a swap so no momentum carries over.</summary>
        public void ResetMotion()
        {
            verticalSpeed = 0f;
        }

        private float JumpSpeed()
        {
            return Mathf.Sqrt(2f * gravity * jumpHeight);
        }
    }
}

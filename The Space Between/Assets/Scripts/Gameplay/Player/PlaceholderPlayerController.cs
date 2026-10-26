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
    public class PlaceholderPlayerController : MonoBehaviour, IMotionResettable
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

        private CharacterController controller;

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
            isGrounded = controller.isGrounded;

            if (isGrounded && verticalSpeed < 0f)
            {
                verticalSpeed = GroundedStickSpeed;
            }

            if (isGrounded && jumpAction.WasPressedThisFrame())
            {
                verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
            }

            verticalSpeed -= gravity * Time.deltaTime;

            float horizontal = moveAction.ReadValue<float>() * moveSpeed;
            Vector3 velocity = new Vector3(horizontal, verticalSpeed, 0f);
            controller.Move(velocity * Time.deltaTime);
        }

        //==================== PUBLIC API =====================
        /// <summary>Removes the vertical speed, called after a swap so no momentum carries over.</summary>
        public void ResetMotion()
        {
            verticalSpeed = 0f;
        }
    }
}

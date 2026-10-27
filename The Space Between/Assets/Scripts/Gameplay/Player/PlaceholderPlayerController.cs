using UnityEngine;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// Temporary side-scroller movement so the swap mechanic can be tried in a scene.
    /// Moves along the world X axis, runs, jumps, falls, and runs up marked surfaces.
    /// It will be replaced by the real movement work (Trello card 2), so it is deliberately small.
    ///
    /// Wall running: while running, pushing into a surface that has a WallRunSurface component and is
    /// steeper than the Character Controller's Slope Limit starts a wall run. The player then travels up the surface at
    /// Wall Run Speed, following the surface if it curves. The run ends when the player stops
    /// running or pushing, the surface ends, the time limit passes, or jump is pressed. A plain
    /// steep surface without the component is just a wall.
    ///
    /// It reads its own input actions, so no Player Input component is needed on the player.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlaceholderPlayerController : MonoBehaviour, IMotionResettable, IAirJumpGrantable, IFacingSource
    {
        private const float FacingInputThreshold = 0.1f;
        private const float GroundedStickSpeed = -2f;
        private const float ContactGraceSeconds = 0.1f;
        private const float WallRunCooldownSeconds = 0.35f;

        //==================== INPUT =====================
        [Header("Input")]
        [Tooltip("Horizontal movement. Reads A and D, the arrow keys, and the gamepad left stick.")]
        [SerializeField] private InputAction moveAction = new InputAction("Move", InputActionType.Value);

        [Tooltip("Jump. Reads Space and the gamepad south button.")]
        [SerializeField] private InputAction jumpAction = new InputAction("Jump", InputActionType.Button);

        [Tooltip("Hold to run. Reads Left Shift and a press of the gamepad left stick (L3).")]
        [SerializeField] private InputAction runAction = new InputAction("Run", InputActionType.Button);

        //==================== MOVEMENT =====================
        [Header("Movement")]
        [Tooltip("Walking speed in units per second.")]
        [SerializeField, Min(0f)] private float moveSpeed = 6f;

        [Tooltip("Running speed in units per second. Running is required to start a wall run.")]
        [SerializeField, Min(0f)] private float runSpeed = 10f;

        [Tooltip("How high a jump lifts the player, in world units.")]
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.8f;

        [Tooltip("Downward acceleration in units per second squared. Use a positive number.")]
        [SerializeField, Min(0.1f)] private float gravity = 25f;

        //==================== WALL RUN =====================
        [Header("Wall Run")]
        [Tooltip("Master switch for the wall run prototype.")]
        [SerializeField] private bool wallRunEnabled = true;

        [Tooltip("Speed along the surface while wall running, in units per second.")]
        [SerializeField, Min(0.1f)] private float wallRunSpeed = 8f;

        [Tooltip("Steepest surface angle that can be run on, measured from flat ground. 90 is a vertical wall. Surfaces gentler than the Character Controller's Slope Limit are simply walked on.")]
        [SerializeField, Range(60f, 120f)] private float wallRunMaxAngle = 100f;

        [Tooltip("Longest a single wall run can last, in seconds.")]
        [SerializeField, Min(0.1f)] private float wallRunMaxDuration = 1.6f;

        [Tooltip("Speed of the small push into the surface that keeps the player attached, in units per second.")]
        [SerializeField, Min(0f)] private float wallRunStickSpeed = 2f;

        [Tooltip("How far past the player's body the surface is searched for each frame. Larger values follow tighter curves but may grab distant surfaces.")]
        [SerializeField, Min(0.05f)] private float surfaceProbeDistance = 0.4f;

        [Tooltip("Fraction of the upward speed kept when a wall run ends. It carries the player over a ledge.")]
        [SerializeField, Range(0f, 1f)] private float exitMomentum = 0.6f;

        [Tooltip("How far the body leans to match the surface. 0 stays upright, 1 stands on the surface.")]
        [SerializeField, Range(0f, 1f)] private float tiltAmount = 0.6f;

        [Tooltip("How quickly the lean follows the surface. Higher is snappier.")]
        [SerializeField, Min(0.1f)] private float tiltSharpness = 12f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Draw the surface normal and the run direction while wall running, when selected.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("Whether the player is standing on something.")]
        [ReadOnly, SerializeField] private bool isGrounded;

        [Tooltip("Whether the run button is held while moving.")]
        [ReadOnly, SerializeField] private bool isRunning;

        [Tooltip("Whether the player is currently running up a surface.")]
        [ReadOnly, SerializeField] private bool isWallRunning;

        [Tooltip("Current vertical speed.")]
        [ReadOnly, SerializeField] private float verticalSpeed;

        [Tooltip("Extra jumps that can still be used in mid-air. A swap that leaves the player airborne grants one.")]
        [ReadOnly, SerializeField] private int airJumpsLeft;

        private CharacterController controller;
        private Swappable swappable;
        private bool treatAsAirborneThisFrame;
        private float facingSign = 1f;

        private Vector3 contactNormal;
        private float contactTime = float.NegativeInfinity;
        private Vector3 wallNormal;
        private float wallRunTime;
        private float cooldownUntil;

        /// <summary>The way the player last moved, as a world direction along X.</summary>
        public Vector3 Facing => new Vector3(facingSign, 0f, 0f);

        public bool IsRunning => isRunning;

        public bool IsWallRunning => isWallRunning;

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

            runAction = new InputAction("Run", InputActionType.Button, "<Keyboard>/leftShift");
            runAction.AddBinding("<Gamepad>/leftStickPress");
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
            runAction.Enable();
        }

        private void OnDisable()
        {
            moveAction.Disable();
            jumpAction.Disable();
            runAction.Disable();
            isWallRunning = false;
        }

        private void Update()
        {
            // A swap in progress holds the player still, so input is ignored until it ends.
            if (swappable != null && swappable.IsFrozen)
            {
                return;
            }

            float input = moveAction.ReadValue<float>();
            if (Mathf.Abs(input) > FacingInputThreshold)
            {
                facingSign = Mathf.Sign(input);
            }

            isRunning = runAction.IsPressed() && Mathf.Abs(input) > FacingInputThreshold;

            if (!isWallRunning && CanStartWallRun(input))
            {
                BeginWallRun();
            }

            if (isWallRunning)
            {
                UpdateWallRun(input);
                return;
            }

            UpdateWalking(input);
        }

        private void LateUpdate()
        {
            ApplyTilt();
        }

        //==================== WALKING AND JUMPING =====================
        private void UpdateWalking(float input)
        {
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

            float horizontal = input * (isRunning ? runSpeed : moveSpeed);
            Vector3 velocity = new Vector3(horizontal, verticalSpeed, 0f);
            controller.Move(velocity * Time.deltaTime);
        }

        //==================== WALL RUN =====================
        // The controller reports every surface it touches while moving. Remember steep, marked ones.
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!wallRunEnabled || !IsRunnable(hit.collider, hit.normal))
            {
                return;
            }

            contactNormal = Flatten(hit.normal);
            contactTime = Time.time;
        }

        private bool IsRunnable(Collider other, Vector3 normal)
        {
            WallRunSurface surface = other.GetComponentInParent<WallRunSurface>();
            if (surface == null || !surface.AllowWallRun)
            {
                return false;
            }

            float angle = Vector3.Angle(normal, Vector3.up);
            return angle > controller.slopeLimit && angle <= wallRunMaxAngle;
        }

        // The game plane is XY, so surface normals are kept in it.
        private static Vector3 Flatten(Vector3 normal)
        {
            normal.z = 0f;
            return normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        }

        private bool CanStartWallRun(float input)
        {
            if (!wallRunEnabled || !isRunning || Time.time < cooldownUntil)
            {
                return false;
            }

            bool touchedRecently = Time.time - contactTime <= ContactGraceSeconds;
            return touchedRecently && PushingInto(contactNormal, input);
        }

        // True when the player's input points into the surface rather than away from it.
        private static bool PushingInto(Vector3 normal, float input)
        {
            return input * normal.x < 0f;
        }

        private void BeginWallRun()
        {
            isWallRunning = true;
            isGrounded = false;
            wallNormal = contactNormal;
            wallRunTime = 0f;
            verticalSpeed = 0f;
        }

        private void UpdateWallRun(float input)
        {
            wallRunTime += Time.deltaTime;

            if (jumpAction.WasPressedThisFrame())
            {
                EndWallRun();
                verticalSpeed = JumpSpeed();
                return;
            }

            // Look at the surface in front of the body again every frame, so a curve is followed.
            Vector3 origin = transform.position + controller.center;
            float probeLength = controller.height * 0.5f + surfaceProbeDistance;
            bool onSurface = Physics.Raycast(origin, -wallNormal, out RaycastHit probe, probeLength, ~0, QueryTriggerInteraction.Ignore)
                             && IsRunnable(probe.collider, probe.normal);

            if (onSurface)
            {
                wallNormal = Flatten(probe.normal);
            }

            if (!onSurface || !isRunning || !PushingInto(wallNormal, input) || wallRunTime > wallRunMaxDuration)
            {
                EndWallRun();
                return;
            }

            Vector3 along = AlongSurface();
            Vector3 velocity = along * wallRunSpeed - wallNormal * wallRunStickSpeed;
            controller.Move(velocity * Time.deltaTime);
        }

        // The direction straight up the surface.
        private Vector3 AlongSurface()
        {
            return Vector3.ProjectOnPlane(Vector3.up, wallNormal).normalized;
        }

        private void EndWallRun()
        {
            // Keep some of the climb so the player is carried over the top of a ledge.
            verticalSpeed = Mathf.Max(AlongSurface().y * wallRunSpeed * exitMomentum, 0f);
            isWallRunning = false;
            cooldownUntil = Time.time + WallRunCooldownSeconds;
        }

        // Leans the body toward the surface while wall running and back to upright afterwards.
        private void ApplyTilt()
        {
            Quaternion target = Quaternion.identity;
            if (isWallRunning)
            {
                Quaternion onSurface = Quaternion.FromToRotation(Vector3.up, wallNormal);
                target = Quaternion.Slerp(Quaternion.identity, onSurface, tiltAmount);
            }

            float blend = 1f - Mathf.Exp(-tiltSharpness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, blend);
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
            isWallRunning = false;
        }

        private float JumpSpeed()
        {
            return Mathf.Sqrt(2f * gravity * jumpHeight);
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !isWallRunning)
            {
                return;
            }

            Vector3 origin = transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + wallNormal * 1.5f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + AlongSurface() * 1.5f);
        }
    }
}

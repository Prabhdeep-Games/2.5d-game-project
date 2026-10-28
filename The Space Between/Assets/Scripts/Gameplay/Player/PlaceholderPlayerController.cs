using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace TheSpaceBetween
{
    /// <summary>
    /// Temporary side-scroller movement so the swap mechanic can be tried in a scene.
    /// Moves along the world X axis, runs, jumps, falls, and runs along marked surfaces.
    /// It will be replaced by the real movement work (Trello card 2), so it is deliberately small.
    ///
    /// Wall running: while running, pushing into a surface that has a WallRunSurface component and is
    /// steeper than the Character Controller's Slope Limit starts a wall run. The run keeps the speed the
    /// player arrived with and turns it along the surface. Holding run plus either movement key keeps running
    /// along it at Run Speed; letting go of run or of the movement keys slides slowly down. Jump is the way off: it launches away from the surface, and
    /// movement keys are ignored until the jump ends. If the player's capsule touches another marked surface
    /// during that jump, the wall run continues on it; otherwise the player falls. Landing on a marked steep
    /// surface without running slides the player down it. Running into a ceiling ends the run and the player drops.
    /// A swap that moves the player ends a wall jump in progress and gives the movement keys back.
    /// A plain steep surface without the component is just a wall.
    ///
    /// It reads its own input actions, so no Player Input component is needed on the player.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlaceholderPlayerController : MonoBehaviour, IMotionResettable, IAirJumpGrantable, IFacingSource
    {
        private const float FacingInputThreshold = 0.1f;
        private const float GroundedStickSpeed = -2f;

        // Fixed wall run tuning. These rarely need changing, so they are kept out of the Inspector.
        private const float ContactGraceSeconds = 0.1f;         // How long a touched surface still counts as touched.
        private const float SameWallCooldownSeconds = 0.35f;    // After leaving a surface, it cannot be grabbed again for this long.
        private const float SameWallDot = 0.5f;                 // Surfaces whose normals agree more than this count as the same wall.
        private const float MinEntrySpeed = 4f;                 // Approach speed needed to start a wall run from the ground.
        private const float SurfaceAcceleration = 40f;          // How fast the speed along a surface changes toward its target.
        private const float SurfaceProbeDistance = 0.4f;        // How far past the body the surface is searched for each frame.
        private const float StickSpeed = 2f;                    // Fastest pull toward a surface that curves away.
        private const float GroundProbeDistance = 0.3f;         // How far below the feet walkable ground is looked for.
        private const float MinSlideNormalY = 0.05f;            // A surface must face at least this much upward to be slid down.
        private const float CarriedMomentumDecay = 6f;          // How fast speed left over from a wall run fades, per second.
        private const float WallJumpScanDistance = 20f;         // How far ahead the debug scan looks for an opposite wall.
        private const float CeilingProbeDistance = 0.2f;        // How far above the head a ceiling is looked for.
        private const float MinCeilingNormalY = 0.5f;           // A ceiling must face at least this much downward.

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

        [Tooltip("Running speed in units per second. Also the speed of running along a wall, which needs run held too.")]
        [SerializeField, Min(0f)] private float runSpeed = 10f;

        [Tooltip("How high a jump lifts the player, in world units. Also sets the upward push of a wall jump.")]
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.8f;

        [Tooltip("Downward acceleration in units per second squared. Use a positive number. Pulls the player down during a wall jump too.")]
        [SerializeField, Min(0.1f)] private float gravity = 25f;

        //==================== WALL RUN =====================
        [Header("Wall Run")]
        [Tooltip("Master switch for wall running.")]
        [SerializeField] private bool wallRunEnabled = true;

        [Tooltip("Steepest surface the player can run along, as the surface's tilt from flat ground: 90 is a vertical wall, above 90 leans over the player. Surfaces gentler than the Character Controller's Slope Limit are walked on normally.")]
        [FormerlySerializedAs("wallRunMaxAngle")]
        [SerializeField, Range(45f, 150f)] private float maxWallRunSurfaceAngle = 100f;

        [Tooltip("How fast the player slides down a surface when run or the movement keys are let go, in units per second. 0 clings in place.")]
        [SerializeField, Min(0f)] private float wallSlideSpeed = 2f;

        //==================== WALL JUMP =====================
        [Header("Wall Jump")]
        [Tooltip("Sideways speed of a jump off a surface, in units per second. Higher crosses wider gaps between opposite walls.")]
        [SerializeField, Min(0f)] private float wallJumpAwaySpeed = 9f;

        //==================== BODY =====================
        [Header("Body")]
        [Tooltip("The visible body. It turns to follow the surface and is moved so its feet rest on it. Leave empty to turn the whole player instead, which clips into the surface because the Character Controller capsule never rotates.")]
        [SerializeField] private Transform body;

        [Tooltip("How quickly the body turns to a new surface and back upright. Higher is snappier.")]
        [FormerlySerializedAs("tiltSharpness")]
        [SerializeField, Min(0.1f)] private float bodyTurnSpeed = 14f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Draw the surface normal, the run direction and the last wall jump scan, when selected.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("Write each wall jump to the Console: what lies ahead, and whether a wall was actually reached.")]
        [SerializeField] private bool logWallJumps;

        [Tooltip("Whether the player is standing on something.")]
        [ReadOnly, SerializeField] private bool isGrounded;

        [Tooltip("Whether the run button is held while moving.")]
        [ReadOnly, SerializeField] private bool isRunning;

        [Tooltip("Whether the player is currently on a surface in a wall run.")]
        [ReadOnly, SerializeField] private bool isWallRunning;

        [Tooltip("Speed along the surface. Positive is up the surface, negative is sliding down.")]
        [ReadOnly, SerializeField] private float surfaceSpeed;

        [Tooltip("Current vertical speed.")]
        [ReadOnly, SerializeField] private float verticalSpeed;

        [Tooltip("Extra jumps that can still be used in mid-air. A swap that leaves the player airborne grants one.")]
        [ReadOnly, SerializeField] private int airJumpsLeft;

        [Tooltip("What the last wall jump was heading for, and whether it was reached.")]
        [ReadOnly, SerializeField] private string lastWallJump = "none yet";

        private CharacterController controller;
        private Swappable swappable;
        private bool treatAsAirborneThisFrame;
        private bool wasFrozen;
        private float facingSign = 1f;

        private Vector3 approachVelocity;
        private float carriedX;

        private Vector3 contactNormal;
        private Collider contactCollider;
        private float contactTime = float.NegativeInfinity;
        private Vector3 wallNormal = Vector3.up;
        private Vector3 lastWallNormal;
        private float surfaceDistance;
        private float cooldownUntil;

        private bool inWallJump;
        private float wallJumpVelocityX;
        private float wallJumpTime;
        private Vector3 scanFrom;
        private Vector3 scanTo;
        private bool scanFoundWall;

        private Vector3 tiltNormal = Vector3.up;
        private float attachWeight;

        /// <summary>The way the player last moved, as a world direction along X.</summary>
        public Vector3 Facing => new Vector3(facingSign, 0f, 0f);

        public bool IsRunning => isRunning;

        public bool IsWallRunning => isWallRunning;

        /// <summary>True from a wall jump until the player reaches another surface or the ground.</summary>
        public bool IsInWallJump => inWallJump;

        /// <summary>Speed along the surface during a wall run. Positive is up, negative is sliding down.</summary>
        public float SurfaceSpeed => surfaceSpeed;

        /// <summary>The steepest surface tilt that can be run along. Changing it at runtime takes effect immediately.</summary>
        public float MaxWallRunSurfaceAngle
        {
            get => maxWallRunSurfaceAngle;
            set => maxWallRunSurfaceAngle = value;
        }

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

            WarnIfUnbound(moveAction);
            WarnIfUnbound(jumpAction);
            WarnIfUnbound(runAction);
        }

        // An action added to an existing component starts with no bindings, so its key silently does nothing.
        private void WarnIfUnbound(InputAction action)
        {
            if (action.bindings.Count == 0)
            {
                Debug.LogWarning($"{name}: the {action.name} action has no bindings, so it can never be pressed. Add them in the Inspector under Input.", this);
            }
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
            inWallJump = false;
        }

        private void Update()
        {
            // A swap in progress holds the player still, so input is ignored until it ends.
            if (swappable != null && swappable.IsFrozen)
            {
                wasFrozen = true;
                return;
            }

            if (wasFrozen)
            {
                wasFrozen = false;
                OnMovedBySwap();
            }

            float input = moveAction.ReadValue<float>();
            if (Mathf.Abs(input) > FacingInputThreshold)
            {
                facingSign = Mathf.Sign(input);
            }

            isRunning = runAction.IsPressed() && Mathf.Abs(input) > FacingInputThreshold;

            if (!isWallRunning && CanStartWallRun(input))
            {
                BeginWallRun(EntrySpeed(contactNormal));
            }
            else if (!isWallRunning && CanStartSlide())
            {
                // Landing on a steep surface without running keeps only the speed that already points along it.
                BeginWallRun(Vector3.Dot(approachVelocity, UpAlong(contactNormal)));
            }

            if (isWallRunning)
            {
                UpdateWallRun(input);
            }

            // A wall run that ended this frame did not move, so normal movement takes the frame instead of stalling.
            if (!isWallRunning)
            {
                UpdateWalking(input);
            }
        }

        private void LateUpdate()
        {
            ApplyBodyPose();
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

            // The controller also reports steep slopes as ground, so a wall jump only ends on ground that can be walked on.
            if (inWallJump && isGrounded && verticalSpeed <= 0f && StandingOnWalkableGround())
            {
                inWallJump = false;
                ReportWallJump("landed on the ground without reaching a wall");
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

            float horizontal;
            if (inWallJump)
            {
                // The launch alone carries the player across, whichever key is held, so a wall jump always heads
                // for the opposite wall and never turns back to the one just left.
                horizontal = wallJumpVelocityX;
            }
            else
            {
                // Speed left over from a wall run fades out so the change to normal control is smooth.
                carriedX *= Mathf.Exp(-CarriedMomentumDecay * Time.deltaTime);
                horizontal = input * (isRunning ? runSpeed : moveSpeed) + carriedX;
            }

            // The movement actually made last frame. The frame that touches a surface is blocked by it,
            // so a wall run started next frame uses this, not the blocked result.
            approachVelocity = controller.velocity;

            Vector3 velocity = new Vector3(horizontal, verticalSpeed, 0f);
            controller.Move(velocity * Time.deltaTime);
        }

        //==================== WALL RUN: STARTING =====================
        // The controller reports every surface it touches while moving. Remember steep, marked ones.
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!wallRunEnabled || !IsRunnable(hit.collider, hit.normal))
            {
                return;
            }

            contactNormal = Flatten(hit.normal);
            contactCollider = hit.collider;
            contactTime = Time.time;
        }

        // The surface angle is the angle between the surface normal and world up, which equals the surface's tilt
        // from flat ground: 0 is a floor, 90 a vertical wall, above 90 an overhang. Below the Slope Limit it is ground.
        private bool IsRunnable(Collider other, Vector3 normal)
        {
            WallRunSurface surface = other.GetComponentInParent<WallRunSurface>();
            if (surface == null || !surface.AllowWallRun)
            {
                return false;
            }

            float angle = Vector3.Angle(normal, Vector3.up);
            return angle > controller.slopeLimit && angle <= maxWallRunSurfaceAngle;
        }

        // The game plane is XY, so surface normals are kept in it.
        private static Vector3 Flatten(Vector3 normal)
        {
            normal.z = 0f;
            return normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        }

        private bool TouchedRecently()
        {
            return Time.time - contactTime <= ContactGraceSeconds;
        }

        // The surface just left cannot be grabbed again straight away; a different surface can.
        private bool BlockedAsSameWall()
        {
            return Time.time < cooldownUntil && Vector3.Dot(contactNormal, lastWallNormal) > SameWallDot;
        }

        private bool CanStartWallRun(float input)
        {
            if (!wallRunEnabled || !TouchedRecently() || BlockedAsSameWall())
            {
                return false;
            }

            // During a wall jump, actually touching another surface while moving into it is enough.
            if (inWallJump)
            {
                return Vector3.Dot(approachVelocity, contactNormal) < 0f;
            }

            return isRunning && PushingInto(contactNormal, input) && EntrySpeed(contactNormal) >= MinEntrySpeed;
        }

        // The Character Controller does not slide down surfaces steeper than its Slope Limit; it just stops on them.
        // Resting on a marked steep surface with no walkable ground below therefore starts a slide down it.
        private bool CanStartSlide()
        {
            if (!wallRunEnabled || !TouchedRecently() || BlockedAsSameWall())
            {
                return false;
            }

            return contactNormal.y > MinSlideNormalY && !StandingOnWalkableGround();
        }

        // True when the player's input points into the surface rather than away from it.
        private static bool PushingInto(Vector3 normal, float input)
        {
            return input * normal.x < 0f;
        }

        // The approach speed turned along the surface, not reduced to its upward share.
        private float EntrySpeed(Vector3 normal)
        {
            return Mathf.Max(Mathf.Abs(approachVelocity.x), Vector3.Dot(approachVelocity, UpAlong(normal)));
        }

        private void BeginWallRun(float speedAlongSurface)
        {
            if (inWallJump)
            {
                ReportWallJump($"reached {contactCollider.name} after {Time.time - wallJumpTime:F2} s at {speedAlongSurface:F1} u/s");
            }

            isWallRunning = true;
            isGrounded = false;
            inWallJump = false;
            wallNormal = contactNormal;
            surfaceDistance = controller.radius;
            surfaceSpeed = speedAlongSurface;
            verticalSpeed = 0f;
            carriedX = 0f;
        }

        //==================== WALL RUN: ON THE SURFACE =====================
        private void UpdateWallRun(float input)
        {
            // Look at the surface in front of the body again every frame, so a curve is followed.
            Vector3 center = transform.position + controller.center;
            float probeLength = controller.height * 0.5f + SurfaceProbeDistance;
            bool onSurface = Physics.Raycast(center, -wallNormal, out RaycastHit probe, probeLength, ~0, QueryTriggerInteraction.Ignore)
                             && IsRunnable(probe.collider, probe.normal);

            if (onSurface)
            {
                wallNormal = Flatten(probe.normal);
                surfaceDistance = Vector3.Dot(center - probe.point, wallNormal);
            }

            // Sliding all the way down puts the player back on walkable ground.
            bool slidOff = surfaceSpeed <= 0f && StandingOnWalkableGround();
            if (!onSurface || slidOff)
            {
                EndWallRun(input);
                return;
            }

            // Jumping is the way to leave a surface on purpose.
            if (jumpAction.WasPressedThisFrame())
            {
                WallJump(input);
                return;
            }

            // Run plus either movement key keeps running along the surface, so a key still held from the last wall
            // keeps the run going. Letting go of run, or of both movement keys, slides slowly down.
            float targetSpeed = isRunning ? runSpeed : -wallSlideSpeed;
            surfaceSpeed = Mathf.MoveTowards(surfaceSpeed, targetSpeed, SurfaceAcceleration * Time.deltaTime);

            // Pull toward the surface only to close a gap, for example where it curves away. A constant push
            // while already touching blocks the controller, which refuses to move sideways into a steep slope.
            float touching = HalfSegment() * Mathf.Abs(wallNormal.y) + controller.radius + controller.skinWidth;
            float gap = Mathf.Max(surfaceDistance - touching, 0f);
            float stick = Mathf.Min(gap / Time.deltaTime, StickSpeed);

            Vector3 velocity = AlongSurface() * surfaceSpeed - wallNormal * stick;
            CollisionFlags flags = controller.Move(velocity * Time.deltaTime);

            // Running into a ceiling ends the run like a jump that hits its head, so the player drops
            // instead of being held against it.
            if (surfaceSpeed > 0f && (flags & CollisionFlags.Above) != 0 && CeilingOverhead())
            {
                surfaceSpeed = 0f;
                EndWallRun(input);
            }
        }

        // True when something facing down, and not itself a surface that can be run on, is right above the head.
        private bool CeilingOverhead()
        {
            Vector3 center = transform.position + controller.center;
            float reach = controller.height * 0.5f + controller.skinWidth + CeilingProbeDistance;
            return Physics.Raycast(center, Vector3.up, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore)
                   && hit.normal.y < -MinCeilingNormalY
                   && !IsRunnable(hit.collider, hit.normal);
        }

        // The controller's own grounded flag also fires on steep slopes, so the ground below is checked directly.
        private bool StandingOnWalkableGround()
        {
            Vector3 center = transform.position + controller.center;
            float reach = controller.height * 0.5f + controller.skinWidth + GroundProbeDistance;
            if (!Physics.Raycast(center, Vector3.down, out RaycastHit ground, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return Vector3.Angle(ground.normal, Vector3.up) <= controller.slopeLimit;
        }

        // Half the length of the straight middle part of the capsule.
        private float HalfSegment()
        {
            return Mathf.Max(controller.height * 0.5f - controller.radius, 0f);
        }

        // The direction straight up the current surface.
        private Vector3 AlongSurface()
        {
            return UpAlong(wallNormal);
        }

        // The direction straight up a surface with the given normal.
        private static Vector3 UpAlong(Vector3 normal)
        {
            return Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
        }

        // Hands the speed along the surface back to normal movement as an ordinary velocity, so nothing is lost.
        private void EndWallRun(float input)
        {
            Vector3 velocity = AlongSurface() * surfaceSpeed;
            verticalSpeed = velocity.y;
            carriedX = velocity.x - input * (isRunning ? runSpeed : moveSpeed);

            isWallRunning = false;
            surfaceSpeed = 0f;
            lastWallNormal = wallNormal;
            cooldownUntil = Time.time + SameWallCooldownSeconds;
        }

        //==================== WALL JUMP =====================
        // Launches away from the surface, keeping any upward speed. Another wall is only grabbed if the capsule touches it.
        private void WallJump(float input)
        {
            float upwardSpeed = Mathf.Max(AlongSurface().y * surfaceSpeed, 0f);
            float awaySign = Mathf.Sign(wallNormal.x);
            EndWallRun(input);

            verticalSpeed = Mathf.Max(JumpSpeed(), upwardSpeed);
            carriedX = 0f;
            inWallJump = true;
            wallJumpVelocityX = awaySign * wallJumpAwaySpeed;
            wallJumpTime = Time.time;

            ScanForOppositeWall(awaySign);
        }

        // Debug only: looks straight ahead along the launch to show whether a wall lies there. It never attaches the player.
        private void ScanForOppositeWall(float awaySign)
        {
            scanFrom = transform.position + controller.center;
            Vector3 direction = new Vector3(awaySign, 0f, 0f);
            scanFoundWall = Physics.SphereCast(scanFrom, controller.radius * 0.9f, direction, out RaycastHit hit, WallJumpScanDistance, ~0, QueryTriggerInteraction.Ignore)
                            && IsRunnable(hit.collider, hit.normal);

            if (scanFoundWall)
            {
                scanTo = scanFrom + direction * hit.distance;
                ReportWallJump($"jumped toward {hit.collider.name}, {hit.distance:F1} units ahead");
            }
            else
            {
                scanTo = scanFrom + direction * WallJumpScanDistance;
                ReportWallJump($"jumped; no wall run surface within {WallJumpScanDistance:F0} units, the player will fall");
            }
        }

        private void ReportWallJump(string message)
        {
            lastWallJump = message;
            if (logWallJumps)
            {
                Debug.Log($"[Wall jump] {name} {message}", this);
            }
        }

        //==================== BODY =====================
        // Turns the body to match the surface and rests its feet on it, then eases it back upright afterwards.
        private void ApplyBodyPose()
        {
            float blend = 1f - Mathf.Exp(-bodyTurnSpeed * Time.deltaTime);
            Vector3 targetNormal = isWallRunning ? wallNormal : Vector3.up;
            tiltNormal = Vector3.Slerp(tiltNormal, targetNormal, blend).normalized;
            attachWeight = Mathf.Lerp(attachWeight, isWallRunning ? 1f : 0f, blend);

            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, tiltNormal);

            if (body == null)
            {
                transform.rotation = tilt;
                return;
            }

            // The controller capsule stays upright, so a turned body would sink into the surface.
            // Lifting it by the missing distance keeps the lowest point of the capsule on the surface.
            Vector3 bodyUp = tilt * Vector3.up;
            float reach = HalfSegment() * Mathf.Abs(Vector3.Dot(bodyUp, wallNormal)) + controller.radius;
            float lift = Mathf.Max(reach - surfaceDistance, 0f) * attachWeight;

            body.SetPositionAndRotation(transform.position + controller.center + wallNormal * lift, tilt);
        }

        // A smooth swap keeps momentum, but the player is now somewhere else. A wall jump was heading for a wall
        // near the old position, so it ends here; its sideways speed is kept and fades out under normal control.
        private void OnMovedBySwap()
        {
            if (!inWallJump)
            {
                return;
            }

            inWallJump = false;
            carriedX = wallJumpVelocityX;
            ReportWallJump("ended by a swap");
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
            carriedX = 0f;
            surfaceSpeed = 0f;
            isWallRunning = false;
            inWallJump = false;
        }

        private float JumpSpeed()
        {
            return Mathf.Sqrt(2f * gravity * jumpHeight);
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            // The last wall jump scan: green reached a wall run surface, red found nothing to land on.
            if (scanFrom != scanTo)
            {
                Gizmos.color = scanFoundWall ? Color.green : Color.red;
                Gizmos.DrawLine(scanFrom, scanTo);
                Gizmos.DrawWireSphere(scanTo, controller != null ? controller.radius * 0.9f : 0.45f);
            }

            if (!isWallRunning)
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

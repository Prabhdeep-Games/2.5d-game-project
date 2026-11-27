using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// An authored slow-motion moment at one ledge. When the player leaves the ground inside this object's Box Collider
    /// while moving fast (a running jump, or running off the edge), time slows while they are in the air, holds a moment
    /// longer if the landing becomes a roll, and then eases back. Ordinary jumps anywhere else are never affected.
    ///
    /// It plays on the first attempt and then on every other one (Play Every 2), so retries stay quick. Only one moment
    /// can run at a time. Control stays with the player throughout: movement, the camera and the animation all run on
    /// game time, so they slow together, and physics steps are scaled with it so moving objects stay smooth.
    ///
    /// Safety: it never changes time while the game is paused or a swap has stopped time; it ends at once on a
    /// "position lost" return, a level change, a cutscene or the ending; and it can never last longer than Max Total
    /// Seconds of real time. Normal time and the physics step are always restored.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class SlowMotionMoment : MonoBehaviour
    {
        private const string RollState = "Roll";

        private static SlowMotionMoment active;
        private static float normalFixedDeltaTime = -1f;

        //==================== TRIGGER =====================
        [Header("Trigger")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("Slowest horizontal speed that counts as the intended jump, in units per second. Walking off the ledge does not.")]
        [SerializeField, Min(0f)] private float minimumSpeed = 5f;

        [Tooltip("Plays on the first attempt, then once every this many attempts. 1 plays every time.")]
        [SerializeField, Min(1)] private int playEvery = 2;

        //==================== EFFECT =====================
        [Header("Effect")]
        [Tooltip("Game speed during the moment. 1 is normal.")]
        [SerializeField, Range(0.1f, 1f)] private float slowScale = 0.35f;

        [Tooltip("Real seconds to slow down after leaving the ground.")]
        [SerializeField, Min(0.01f)] private float easeInSeconds = 0.12f;

        [Tooltip("Longest the slow motion lasts in the air, in real seconds.")]
        [SerializeField, Min(0.1f)] private float maxAirSeconds = 1.5f;

        [Tooltip("Real seconds the slow motion holds when the landing becomes a roll.")]
        [SerializeField, Min(0f)] private float rollHoldSeconds = 1f;

        [Tooltip("Real seconds to return to normal speed.")]
        [SerializeField, Min(0.01f)] private float easeOutSeconds = 0.3f;

        [Tooltip("Hard limit on the whole moment, in real seconds.")]
        [SerializeField, Min(0.5f)] private float maxTotalSeconds = 3.5f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each attempt and each moment to the Console.")]
        [SerializeField] private bool logMoments;

        [ReadOnly, SerializeField] private string phase = "Idle";
        [ReadOnly, SerializeField] private int attempts;
        [ReadOnly, SerializeField] private int plays;

        private enum Phase { Idle, Air, Landing, Out }

        private BoxCollider area;
        private SwapAbility swap;
        private PlayerAnimationDriver animationDriver;
        private Phase state = Phase.Idle;
        private float scale = 1f;
        private float phaseSeconds;
        private float totalSeconds;
        private float holdUntil;
        private bool wasGrounded = true;
        private int lastTriggerFrame = -1;

        /// <summary>True while any slow-motion moment is running.</summary>
        public static bool AnyActive => active != null;

        public bool IsActive => active == this;
        public int Attempts => attempts;
        public int Plays => plays;
        public string CurrentPhase => phase;

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            area = GetComponent<BoxCollider>();
            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }

            if (player != null)
            {
                swap = player.GetComponent<SwapAbility>();
                animationDriver = player.GetComponentInChildren<PlayerAnimationDriver>();
            }

            // Remember the project's physics step once, while no moment has changed it.
            if (normalFixedDeltaTime < 0f || active == null)
            {
                normalFixedDeltaTime = Time.fixedDeltaTime;
            }
        }

        private void OnEnable()
        {
            if (player != null)
            {
                player.Jumped += OnJumped;
                player.Landed += OnLanded;
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.Jumped -= OnJumped;
                player.Landed -= OnLanded;
            }

            if (active == this)
            {
                Stop(true);
            }
        }

        private void Update()
        {
            if (player != null)
            {
                // Running off the edge counts as well as a jump.
                bool grounded = player.IsGrounded;
                if (wasGrounded && !grounded && Inside())
                {
                    TryStart();
                }

                wasGrounded = grounded;
            }

            if (active == this)
            {
                Tick();
            }
        }

        //==================== TRIGGER =====================
        private void OnJumped(bool fromWall)
        {
            if (!fromWall && Inside())
            {
                TryStart();
            }
        }

        private bool Inside()
        {
            return area.bounds.Contains(player.transform.position + Vector3.up * 0.5f);
        }

        private void TryStart()
        {
            // A jump fires both the Jumped event and the leave-ground check in the same frame.
            if (lastTriggerFrame == Time.frameCount || active != null || Held())
            {
                return;
            }

            Vector3 velocity = player.Velocity;
            if (new Vector2(velocity.x, velocity.z).magnitude < minimumSpeed && player.SurfaceSpeed < minimumSpeed)
            {
                return;
            }

            lastTriggerFrame = Time.frameCount;
            attempts++;
            if ((attempts - 1) % playEvery != 0)
            {
                Log($"attempt {attempts}: normal speed this time");
                return;
            }

            plays++;
            active = this;
            SetPhase(Phase.Air);
            totalSeconds = 0f;
            Log($"attempt {attempts}: slow motion");
        }

        private void OnLanded(float fallHeight)
        {
            if (active == this && state == Phase.Air)
            {
                SetPhase(Phase.Landing);
                holdUntil = 0.25f;
            }
        }

        //==================== EFFECT =====================
        private void Tick()
        {
            FacilityUI ui = FacilityUI.Current;
            if (ui != null && (ui.IsReturning || ui.IsLeaving || ui.IsEnding || ui.IsCutscene))
            {
                Stop(true);
                return;
            }

            // Paused, or a Shrink swap has stopped time: leave time alone and wait, so nothing fights over it.
            if (Held())
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            phaseSeconds += dt;
            totalSeconds += dt;
            float target = slowScale;
            float rate = 1f / easeInSeconds;
            switch (state)
            {
                case Phase.Air:
                    if (phaseSeconds >= maxAirSeconds)
                    {
                        SetPhase(Phase.Out);
                    }

                    break;
                case Phase.Landing:
                    // A roll keeps the moment going; any other landing lets it go.
                    if (animationDriver != null && animationDriver.CurrentState == RollState && holdUntil < rollHoldSeconds)
                    {
                        holdUntil = rollHoldSeconds;
                    }

                    if (phaseSeconds >= holdUntil)
                    {
                        SetPhase(Phase.Out);
                    }

                    break;
            }

            if (state == Phase.Out)
            {
                target = 1f;
                rate = 1f / easeOutSeconds;
            }

            if (totalSeconds >= maxTotalSeconds && state != Phase.Out)
            {
                SetPhase(Phase.Out);
            }

            scale = Mathf.MoveTowards(scale, target, rate * dt);
            Apply(scale);
            if (state == Phase.Out && Mathf.Approximately(scale, 1f))
            {
                Stop(false);
            }
        }

        private bool Held()
        {
            FacilityUI ui = FacilityUI.Current;
            return (ui != null && ui.IsPaused) || (swap != null && swap.IsSwapping);
        }

        private void Apply(float value)
        {
            Time.timeScale = value;
            Time.fixedDeltaTime = normalFixedDeltaTime * value;
        }

        // Ends the moment and puts normal time back, unless the pause or a swap owns time right now.
        private void Stop(bool cancelled)
        {
            if (!Held())
            {
                Time.timeScale = 1f;
            }

            Time.fixedDeltaTime = normalFixedDeltaTime;
            scale = 1f;
            active = null;
            SetPhase(Phase.Idle);
            Log(cancelled ? "cancelled" : "finished");
        }

        private void SetPhase(Phase next)
        {
            state = next;
            phase = next.ToString();
            phaseSeconds = 0f;
        }

        private void Log(string message)
        {
            if (logMoments)
            {
                Debug.Log($"[{nameof(SlowMotionMoment)}] '{name}' {message}.", this);
            }
        }

        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(0.85f, 0.6f, 0.3f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}

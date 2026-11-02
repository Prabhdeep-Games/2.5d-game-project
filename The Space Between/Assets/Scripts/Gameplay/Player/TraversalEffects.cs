using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Temporary particle feedback for traversal, driven by the movement events:
    ///   Jump dust      a small burst at the feet on a jump from the ground
    ///   Landing dust   a wider burst of air and dust pushed outward on a hard landing
    ///   Wall run       sparks at the feet and a short trail, only while a wall run is active
    ///
    /// The particle systems already exist as children of the player and are reused; nothing is created at runtime.
    /// The dust systems simulate in world space, so a burst stays where it was emitted while the player moves on.
    /// Put this on the player next to the PlaceholderPlayerController. Each reference is optional.
    /// </summary>
    // Runs after the movement's Update, so a wall run that starts or ends this frame switches the effects this frame.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(PlaceholderPlayerController))]
    public class TraversalEffects : MonoBehaviour
    {
        //==================== JUMP =====================
        [Header("Jump")]
        [Tooltip("Burst at the feet when the player jumps from the ground. Not used for mid-air jumps.")]
        [SerializeField] private ParticleSystem jumpDust;

        [Tooltip("How many particles one jump emits.")]
        [SerializeField, Min(0)] private int jumpParticles = 10;

        //==================== LANDING =====================
        [Header("Landing")]
        [Tooltip("Burst at the feet when the player lands hard.")]
        [SerializeField] private ParticleSystem landingDust;

        [Tooltip("How many particles one landing emits.")]
        [SerializeField, Min(0)] private int landingParticles = 18;

        [Tooltip("Slowest downward speed, in units per second, that shows the landing burst. Keeps small steps and slides off a wall quiet.")]
        [SerializeField, Min(0f)] private float minLandingSpeed = 6f;

        //==================== WALL RUN =====================
        [Header("Wall Run")]
        [Tooltip("Sparks at the feet. They play only while a wall run is active.")]
        [SerializeField] private ParticleSystem wallRunSparks;

        [Tooltip("Short trail behind the player. It emits only while a wall run is active and fades out when it ends.")]
        [SerializeField] private TrailRenderer wallRunTrail;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each effect to the Console.")]
        [SerializeField] private bool logEffects;

        [Tooltip("Whether the wall run sparks and trail are on right now.")]
        [ReadOnly, SerializeField] private bool wallRunEffectsOn;

        [Tooltip("How many jump bursts have played.")]
        [ReadOnly, SerializeField] private int jumpBursts;

        [Tooltip("How many landing bursts have played.")]
        [ReadOnly, SerializeField] private int landingBursts;

        private PlaceholderPlayerController movement;
        private Swappable swappable;

        public bool WallRunEffectsOn => wallRunEffectsOn;

        public int JumpBursts => jumpBursts;

        public int LandingBursts => landingBursts;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            movement = GetComponent<PlaceholderPlayerController>();
            swappable = GetComponent<Swappable>();
            SetWallRunEffects(false);
        }

        private void OnEnable()
        {
            movement.Jumped += OnJumped;
            movement.Landed += OnLanded;
        }

        private void OnDisable()
        {
            movement.Jumped -= OnJumped;
            movement.Landed -= OnLanded;
            SetWallRunEffects(false);
        }

        private void Update()
        {
            // Off while a swap holds the player, so the trail never draws a line across the swap.
            bool wanted = movement.IsWallRunning && (swappable == null || !swappable.IsFrozen);
            if (wanted != wallRunEffectsOn)
            {
                SetWallRunEffects(wanted);
            }
        }

        //==================== EFFECTS =====================
        private void OnJumped(bool fromGround)
        {
            if (!fromGround || jumpDust == null)
            {
                return;
            }

            jumpDust.Emit(jumpParticles);
            jumpBursts++;
            Log("jump dust");
        }

        private void OnLanded(float impactSpeed)
        {
            if (impactSpeed < minLandingSpeed || landingDust == null)
            {
                return;
            }

            landingDust.Emit(landingParticles);
            landingBursts++;
            Log($"landing dust at {impactSpeed:F1} u/s");
        }

        private void SetWallRunEffects(bool on)
        {
            wallRunEffectsOn = on;

            if (wallRunSparks != null)
            {
                if (on)
                {
                    wallRunSparks.Play();
                }
                else
                {
                    // Sparks already in the air finish their short life instead of vanishing.
                    wallRunSparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (wallRunTrail != null)
            {
                // A new wall run starts a fresh trail, so it never joins up with the end of the last one.
                if (on)
                {
                    wallRunTrail.Clear();
                }

                wallRunTrail.emitting = on;
            }

            Log(on ? "wall run effects on" : "wall run effects off");
        }

        private void Log(string message)
        {
            if (logEffects)
            {
                Debug.Log($"[{nameof(TraversalEffects)}] {message}", this);
            }
        }
    }
}

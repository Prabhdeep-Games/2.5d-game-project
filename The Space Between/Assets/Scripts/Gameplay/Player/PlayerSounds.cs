using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The protagonist's body sounds, driven by the movement events and the animation:
    ///   Jump        a short grunt on every jump, mid-air jump and wall jump, picked at random from a set and never
    ///               the same one twice in a row
    ///   Footsteps   one step each time a foot comes down in the animation (walking set, or the running set while
    ///               the run key is held), on the ground and on a wall run
    ///   Landing     a heavier step on landing, louder the harder the impact
    ///   Slide       a concrete scuff when a slide starts, one picked at random from a set, faded out early if the
    ///               slide ends before it does (empty plays nothing)
    ///
    /// Steps follow the feet: a step plays at the moment the swinging foot comes down past the planted one, measured in
    /// the character's own up direction, so the rhythm always matches the walk, run or wall-run cycle on screen.
    /// Put this on a child of the player with an AudioSource whose AudioChannel is Sound Effects. The voice and the slide
    /// each use a source on their own child, so everything follows the Sound Effects volume in Settings.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(AudioSource))]
    public sealed class PlayerSounds : MonoBehaviour
    {
        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The player. Empty means the one this object belongs to.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The character's humanoid Animator, for the feet. Empty means the one under the player.")]
        [SerializeField] private Animator animator;

        [Tooltip("Plays the steps and landings. Empty means the AudioSource on this object.")]
        [SerializeField] private AudioSource oneShots;

        [Tooltip("Plays the jump grunts, on its own so a step never bends a grunt that is still sounding. Empty uses the steps' source.")]
        [SerializeField] private AudioSource voiceSource;

        [Tooltip("Plays the slide scuff. Optional.")]
        [SerializeField] private AudioSource slideSource;

        //==================== JUMP =====================
        [Header("Jump")]
        [Tooltip("Grunts, one picked at random for each jump.")]
        [SerializeField] private AudioClip[] jumpClips = new AudioClip[0];

        [Tooltip("Volume of a jump grunt.")]
        [SerializeField, Range(0f, 1f)] private float jumpVolume = 0.7f;

        //==================== FOOTSTEPS =====================
        [Header("Footsteps")]
        [Tooltip("Steps while moving normally.")]
        [SerializeField] private AudioClip[] walkSteps = new AudioClip[0];

        [Tooltip("Steps while the run key is held.")]
        [SerializeField] private AudioClip[] runSteps = new AudioClip[0];

        [Tooltip("Volume of a walking step.")]
        [SerializeField, Range(0f, 1f)] private float walkVolume = 0.35f;

        [Tooltip("Volume of a running step.")]
        [SerializeField, Range(0f, 1f)] private float runVolume = 0.5f;

        [Tooltip("Slowest speed, in units per second, that makes steps. Keeps standing still and turning quiet.")]
        [SerializeField, Min(0f)] private float minStepSpeed = 0.5f;

        [Tooltip("How far, in metres, the swinging foot must come below the planted one to count as a step. Stops jitter while standing.")]
        [SerializeField, Min(0f)] private float footMargin = 0.02f;

        [Tooltip("Shortest time between two steps, in seconds.")]
        [SerializeField, Min(0f)] private float minStepInterval = 0.15f;

        //==================== LANDING =====================
        [Header("Landing")]
        [Tooltip("Slowest downward speed at impact, in units per second, that plays a landing.")]
        [SerializeField, Min(0f)] private float minLandingSpeed = 2f;

        [Tooltip("Downward speed at which a landing reaches full volume.")]
        [SerializeField, Min(0.1f)] private float fullLandingSpeed = 16f;

        [Tooltip("Volume of the hardest landing.")]
        [SerializeField, Range(0f, 1f)] private float landingVolume = 0.85f;

        //==================== SLIDE =====================
        [Header("Slide")]
        [Tooltip("Scuffs, one picked at random when a slide starts. Empty plays nothing.")]
        [SerializeField] private AudioClip[] slideClips = new AudioClip[0];

        [Tooltip("Volume of the slide scuff.")]
        [SerializeField, Range(0f, 1f)] private float slideVolume = 0.6f;

        [Tooltip("Seconds over which the scuff fades out when the slide ends before it does.")]
        [SerializeField, Min(0.01f)] private float slideFadeSeconds = 0.15f;

        //==================== VARIATION =====================
        [Header("Variation")]
        [Tooltip("Random pitch change on each sound, so repeats do not sound identical. 0.05 is plus or minus 5%.")]
        [SerializeField, Range(0f, 0.2f)] private float pitchVariation = 0.05f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [ReadOnly, SerializeField] private int jumpsPlayed;
        [ReadOnly, SerializeField] private int stepsPlayed;
        [ReadOnly, SerializeField] private int landingsPlayed;
        [ReadOnly, SerializeField] private int slidesPlayed;
        [ReadOnly, SerializeField] private string lastSound;

        private readonly Dictionary<AudioClip[], int> lastPicked = new Dictionary<AudioClip[], int>();
        private Transform leftFoot;
        private Transform rightFoot;
        private int lowerFoot;          // -1 left, 1 right, 0 not known yet.
        private float lastStepTime = float.NegativeInfinity;
        private float slideBaseVolume;
        private float slideFadeStart = -1f;
        private bool subscribed;

        public int JumpsPlayed => jumpsPlayed;

        public int StepsPlayed => stepsPlayed;

        public int LandingsPlayed => landingsPlayed;

        public int SlidesPlayed => slidesPlayed;

        /// <summary>The clip played last, for tests and debugging.</summary>
        public AudioClip LastClip { get; private set; }

        public bool IsSlideSounding => slideSource != null && slideSource.isPlaying;

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponentInParent<PlaceholderPlayerController>();
            }

            if (oneShots == null)
            {
                oneShots = GetComponent<AudioSource>();
            }

            if (voiceSource == null)
            {
                voiceSource = oneShots;
            }

            if (animator == null && player != null)
            {
                animator = player.GetComponentInChildren<Animator>();
            }

            if (animator != null && animator.isHuman)
            {
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
        }

        private void OnEnable()
        {
            if (player == null || subscribed)
            {
                return;
            }

            player.Jumped += OnJumped;
            player.WallJumped += OnWallJumped;
            player.Landed += OnLanded;
            player.SlideStarted += OnSlideStarted;
            player.SlideEnded += OnSlideEnded;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (player != null && subscribed)
            {
                player.Jumped -= OnJumped;
                player.WallJumped -= OnWallJumped;
                player.Landed -= OnLanded;
                player.SlideStarted -= OnSlideStarted;
                player.SlideEnded -= OnSlideEnded;
            }

            subscribed = false;
            if (slideSource != null)
            {
                slideSource.Stop();
            }
        }

        private void Update()
        {
            UpdateFootsteps();
            UpdateSlideFade();
        }

        //==================== EVENTS =====================
        private void OnJumped(bool fromGround)
        {
            PlayJump();
        }

        private void OnWallJumped()
        {
            PlayJump();
        }

        private void PlayJump()
        {
            if (Play(jumpClips, jumpVolume, voiceSource))
            {
                jumpsPlayed++;
                lastSound = "Jump";
            }
        }

        private void OnLanded(float downwardSpeed)
        {
            if (downwardSpeed < minLandingSpeed)
            {
                return;
            }

            float t = Mathf.InverseLerp(minLandingSpeed, fullLandingSpeed, downwardSpeed);
            AudioClip[] set = runSteps.Length > 0 ? runSteps : walkSteps;
            if (Play(set, landingVolume * Mathf.Lerp(0.45f, 1f, t), oneShots))
            {
                landingsPlayed++;
                lastSound = "Landing";

                // The feet touch down together, so the landing stands in for the next step.
                lastStepTime = Time.time;
            }
        }

        private void OnSlideStarted()
        {
            AudioClip clip = Pick(slideClips);
            if (slideSource == null || clip == null)
            {
                return;
            }

            slideSource.Stop();
            slideSource.clip = clip;
            slideSource.loop = false;
            slideSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            slideSource.time = 0f;
            slideFadeStart = -1f;
            AudioChannel channel = slideSource.GetComponent<AudioChannel>();
            if (channel != null)
            {
                channel.Refresh();
            }

            slideBaseVolume = slideSource.volume;
            slideSource.volume = slideBaseVolume * slideVolume;
            slideSource.Play();
            slidesPlayed++;
            LastClip = clip;
            lastSound = "Slide";
        }

        private void OnSlideEnded()
        {
            if (slideSource != null && slideSource.isPlaying)
            {
                slideFadeStart = Time.unscaledTime;
            }
        }

        private void UpdateSlideFade()
        {
            if (slideFadeStart < 0f || slideSource == null)
            {
                return;
            }

            float t = (Time.unscaledTime - slideFadeStart) / slideFadeSeconds;
            if (t >= 1f)
            {
                slideSource.Stop();
                slideFadeStart = -1f;
                return;
            }

            slideSource.volume = slideBaseVolume * slideVolume * (1f - t);
        }

        //==================== FOOTSTEPS =====================
        private void UpdateFootsteps()
        {
            if (player == null || leftFoot == null || rightFoot == null)
            {
                return;
            }

            Vector3 up = animator.transform.up;
            Vector3 root = animator.transform.position;
            float left = Vector3.Dot(leftFoot.position - root, up);
            float right = Vector3.Dot(rightFoot.position - root, up);

            // The planted foot is the lower one. A step is the moment the other foot comes down past it.
            int lower = lowerFoot;
            if (left < right - footMargin)
            {
                lower = -1;
            }
            else if (right < left - footMargin)
            {
                lower = 1;
            }

            bool changed = lowerFoot != 0 && lower != lowerFoot;
            lowerFoot = lower;
            if (!changed)
            {
                return;
            }

            bool onSurface = player.IsGrounded || player.IsWallRunning;
            bool moving = player.Speed >= minStepSpeed;
            if (!onSurface || !moving || player.IsSliding || player.IsMovedByAnimation || Time.time - lastStepTime < minStepInterval)
            {
                return;
            }

            bool running = player.IsRunning && runSteps.Length > 0;
            if (Play(running ? runSteps : walkSteps, running ? runVolume : walkVolume, oneShots))
            {
                stepsPlayed++;
                lastStepTime = Time.time;
                lastSound = running ? "Run step" : "Walk step";
            }
        }

        //==================== PLAYING =====================
        // One clip of the set at random, never the one picked last from the same set when there is a choice.
        private AudioClip Pick(AudioClip[] set)
        {
            if (set == null || set.Length == 0)
            {
                return null;
            }

            lastPicked.TryGetValue(set, out int previous);
            int index = Random.Range(0, set.Length);
            if (set.Length > 1 && index == previous - 1)
            {
                index = (index + 1 + Random.Range(0, set.Length - 1)) % set.Length;
            }

            lastPicked[set] = index + 1;
            return set[index];
        }

        private bool Play(AudioClip[] set, float volume, AudioSource source)
        {
            if (source == null || set == null || set.Length == 0)
            {
                return false;
            }

            AudioClip clip = Pick(set);
            if (clip == null)
            {
                return false;
            }

            source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip, volume);
            LastClip = clip;
            return true;
        }
    }
}

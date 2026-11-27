using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The first two beats of the game, with no player control: The Transport and The Awakening.
    ///
    /// The Transport: the view starts black, a hum rises, and containment chambers glide past in the dark on their carriage,
    /// side-on, the camera travelling with them. For a moment the protagonist's chamber is lit in the foreground. Then black,
    /// an impact, silence, a crash and breaking glass; a few shards fall in the dark.
    ///
    /// The Awakening: an emergency strip fades up on the damaged chamber, seen from low down. The protagonist crawls out
    /// through the broken front towards the service corridor and gets up, then the gameplay camera takes over and walking is
    /// handed to the player (Opening Controls), which brings up the move prompt.
    ///
    /// The crawl and the getting up play only when the Protagonist's Animator has the states named below. Until those clips
    /// exist, the crawl happens out of sight: the low camera holds on the broken chamber, and the protagonist is first seen
    /// standing at the corridor's threshold. No other pose stands in for them.
    ///
    /// Sound slots with no clip stay silent, so the final hum, impact and glass can be dropped in later. Everything runs on
    /// real time. A player who continues the game from a later room skips the sequence.
    /// </summary>
    public sealed class OpeningSequence : MonoBehaviour
    {
        //==================== CAST =====================
        [Header("Cast")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The abilities handed over in the opening. Walking is allowed when the awakening ends.")]
        [SerializeField] private OpeningControls controls;

        [Tooltip("The sequence plays only when the player starts within this distance of the Crawl Start, so Continue from a later room skips it.")]
        [SerializeField, Min(0.5f)] private float startRadius = 6f;

        //==================== THE TRANSPORT =====================
        [Header("The Transport")]
        [Tooltip("Everything that rides the transport: the chambers and the camera's focus. Moved along X at a constant speed.")]
        [SerializeField] private Transform carriage;

        [Tooltip("Units per second the carriage moves.")]
        [SerializeField, Min(0f)] private float transportSpeed = 1.6f;

        [Tooltip("The side-on camera that travels with the transport.")]
        [SerializeField] private CinemachineCamera transportCamera;

        [Tooltip("The transport hall. Switched off once the transport has gone, as nothing returns to it.")]
        [SerializeField] private GameObject transportHall;

        [Tooltip("The faint lights that slowly make the chambers' silhouettes readable. They rise from 0 to their own intensity.")]
        [SerializeField] private List<Light> revealLights = new List<Light>();

        [Tooltip("The light that shows the protagonist's chamber for a moment.")]
        [SerializeField] private Light protagonistLight;

        [Tooltip("Real seconds of full black before anything is seen.")]
        [SerializeField, Min(0f)] private float blackSeconds = 2f;

        [Tooltip("Real seconds the view takes to come up from black.")]
        [SerializeField, Min(0.1f)] private float revealSeconds = 7f;

        [Tooltip("Real seconds from the start until the protagonist's chamber is lit.")]
        [SerializeField, Min(0f)] private float protagonistMomentAt = 11f;

        [Tooltip("Real seconds the protagonist's chamber light takes to rise, holds, and takes to fall.")]
        [SerializeField] private Vector3 protagonistMomentSeconds = new Vector3(1.2f, 1.6f, 1.4f);

        [Tooltip("Real seconds from the start until the view starts going to black.")]
        [SerializeField, Min(0f)] private float blackoutAt = 17f;

        [Tooltip("Real seconds the view takes to go to black.")]
        [SerializeField, Min(0.1f)] private float blackoutSeconds = 2.5f;

        //==================== THE ACCIDENT =====================
        [Header("The Accident")]
        [Tooltip("Real seconds in the black before the impact.")]
        [SerializeField, Min(0f)] private float impactDelay = 0.4f;

        [Tooltip("Real seconds of silence between the impact and the crash.")]
        [SerializeField, Min(0f)] private float silenceSeconds = 1.4f;

        [Tooltip("Small pieces of glass that fall and scatter at the crash. They come to rest and stop colliding, so they never block the player.")]
        [SerializeField] private List<Rigidbody> shards = new List<Rigidbody>();

        [Tooltip("Real seconds the shards are free to fall before they settle for good.")]
        [SerializeField, Min(0.2f)] private float shardSettleSeconds = 2.5f;

        //==================== THE AWAKENING =====================
        [Header("The Awakening")]
        [Tooltip("The low, still camera on the damaged chamber.")]
        [SerializeField] private CinemachineCamera chamberCamera;

        [Tooltip("The close camera that follows the protagonist out of the chamber.")]
        [SerializeField] private CinemachineCamera crawlCamera;

        [Tooltip("The emergency strip: lights that rise from 0 to their own intensity.")]
        [SerializeField] private List<Light> emergencyLights = new List<Light>();

        [Tooltip("Real seconds in the dark after the crash before the emergency strip comes on.")]
        [SerializeField, Min(0f)] private float darkAfterCrashSeconds = 2f;

        [Tooltip("Real seconds the emergency strip takes to come up.")]
        [SerializeField, Min(0.1f)] private float emergencySeconds = 5f;

        [Tooltip("Where the protagonist lies inside the chamber (feet).")]
        [SerializeField] private Transform crawlStart;

        [Tooltip("Where the protagonist gets up, on the corridor floor (feet).")]
        [SerializeField] private Transform standPoint;

        [Tooltip("The Animator state of the crawl. Used only if the Protagonist's Animator has it.")]
        [SerializeField] private string crawlState = "Crawl";

        [Tooltip("The Animator state of getting up. Used only if the Protagonist's Animator has it.")]
        [SerializeField] private string getUpState = "GetUp";

        [Tooltip("Units per second the protagonist crawls.")]
        [SerializeField, Min(0.1f)] private float crawlSpeed = 0.7f;

        [Tooltip("Real seconds the low camera holds on the chamber once the light is up, before the crawl.")]
        [SerializeField, Min(0f)] private float chamberHoldSeconds = 2.5f;

        [Tooltip("Real seconds the protagonist pauses on the corridor floor before getting up.")]
        [SerializeField, Min(0f)] private float pauseSeconds = 1.2f;

        [Tooltip("Real seconds on the protagonist standing before control is handed over.")]
        [SerializeField, Min(0f)] private float standSeconds = 1.6f;

        //==================== SOUND SLOTS =====================
        [Header("Sound Slots (empty until the final sounds are supplied)")]
        [SerializeField] private AudioSource hum;
        [SerializeField, Range(0f, 1f)] private float humVolume = 0.5f;
        [SerializeField, Min(0.1f)] private float humFadeSeconds = 6f;
        [SerializeField] private AudioSource impact;
        [SerializeField] private AudioSource crash;
        [SerializeField] private AudioSource glass;

        //==================== DEBUG =====================
        [Header("Debug")]
        [ReadOnly, SerializeField] private string stage = "Waiting";

        private readonly List<float> revealTargets = new List<float>();
        private readonly List<float> emergencyTargets = new List<float>();
        private float protagonistTarget;
        private bool moveCarriage;
        private bool skipped;
        private readonly List<(Renderer renderer, bool enabled)> hiddenRenderers = new List<(Renderer, bool)>();

        public string Stage => stage;

        /// <summary>True once control has been handed over, or the sequence was skipped.</summary>
        public bool Finished => stage == "Done";

        public bool Skipped => skipped;

        /// <summary>True when the Protagonist's Animator has both the crawl and the getting-up states.</summary>
        public bool HasCrawlAnimation { get; private set; }

        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }

            if (controls == null)
            {
                controls = FindAnyObjectByType<OpeningControls>();
            }

            Capture(revealLights, revealTargets);
            Capture(emergencyLights, emergencyTargets);
            protagonistTarget = protagonistLight != null ? protagonistLight.intensity : 0f;
            if (protagonistLight != null)
            {
                protagonistLight.intensity = 0f;
            }

            foreach (Rigidbody shard in shards)
            {
                if (shard != null)
                {
                    shard.gameObject.SetActive(false);
                }
            }
        }

        private void Start()
        {
            // The level's own start is the chamber; a player put anywhere else (Continue, a section key) has had the opening.
            if (!NearStart())
            {
                Skip();
                return;
            }

            StartCoroutine(Play());
        }

        private void Update()
        {
            if (moveCarriage && carriage != null)
            {
                carriage.position += Vector3.right * (transportSpeed * Time.unscaledDeltaTime);
            }
        }

        //==================== SEQUENCE =====================
        private IEnumerator Play()
        {
            FacilityUI ui = FacilityUI.Current;
            stage = "Black";
            ui?.SetCutscene(true);
            ui?.FadeToDark(0f);
            HidePlayer(true);
            Animator animator = player.GetComponentInChildren<Animator>();
            PlayerAnimationDriver driver = player.GetComponentInChildren<PlayerAnimationDriver>();
            HasCrawlAnimation = animator != null && HasState(animator, crawlState) && HasState(animator, getUpState);
            Place(crawlStart.position);
            Show(transportCamera);
            Set(emergencyLights, emergencyTargets, 0f);
            Set(revealLights, revealTargets, 0f);

            // The loading screen from the menu comes down first; the sequence starts in the black behind it.
            while (ui != null && ui.LoadingVisible)
            {
                yield return null;
            }

            // Continue from a later room: the arrival has put the player there, so the opening is over for them.
            if (!NearStart())
            {
                HidePlayer(false);
                Skip();
                ui?.SetCutscene(false);
                ui?.FadeFromDark(0.5f);
                yield break;
            }

            float began = Time.unscaledTime;
            moveCarriage = true;
            SetHum(0f);
            PlaySlot(hum);

            // The Transport.
            yield return WaitUntilTime(began, blackSeconds, () => FadeHum(began, humVolume));
            stage = "Transport";
            ui?.FadeFromDark(revealSeconds);
            float revealFrom = Time.unscaledTime;
            float protagonistFrom = began + protagonistMomentAt;
            while (Time.unscaledTime - began < blackoutAt)
            {
                FadeHum(began, humVolume);
                Set(revealLights, revealTargets, Mathf.Clamp01((Time.unscaledTime - revealFrom) / revealSeconds));
                if (protagonistLight != null)
                {
                    protagonistLight.intensity = protagonistTarget * Moment(Time.unscaledTime - protagonistFrom);
                }

                if (Time.unscaledTime >= protagonistFrom && stage == "Transport")
                {
                    stage = "Protagonist";
                }

                yield return null;
            }

            stage = "Blackout";
            ui?.FadeToDark(blackoutSeconds);
            float blackoutFrom = Time.unscaledTime;
            while (Time.unscaledTime - blackoutFrom < blackoutSeconds)
            {
                float t = (Time.unscaledTime - blackoutFrom) / blackoutSeconds;
                SetHum(humVolume * (1f - t));

                yield return null;
            }

            // The accident, in the black.
            moveCarriage = false;
            if (hum != null)
            {
                hum.Stop();
            }

            yield return Wait(impactDelay);
            stage = "Impact";
            PlaySlot(impact);
            yield return Wait(silenceSeconds);
            stage = "Crash";
            PlaySlot(crash);
            PlaySlot(glass);
            DropShards();
            if (transportHall != null)
            {
                transportHall.SetActive(false);
            }

            // The Awakening.
            Show(chamberCamera);
            Hide(transportCamera);
            yield return Wait(darkAfterCrashSeconds);
            stage = "Emergency Light";
            ui?.FadeFromDark(emergencySeconds * 0.8f);
            float lightFrom = Time.unscaledTime;
            while (Time.unscaledTime - lightFrom < emergencySeconds)
            {
                Set(emergencyLights, emergencyTargets, Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - lightFrom) / emergencySeconds));
                yield return null;
            }

            Set(emergencyLights, emergencyTargets, 1f);
            yield return Wait(chamberHoldSeconds);

            if (driver != null)
            {
                driver.enabled = false;
            }

            if (HasCrawlAnimation)
            {
                // The crawl, seen close: the protagonist drags out of the broken front onto the corridor floor.
                stage = "Crawl";
                HidePlayer(false);
                player.enabled = false;
                animator.CrossFadeInFixedTime(crawlState, 0.2f);
                Show(crawlCamera);
                CharacterController body = player.GetComponent<CharacterController>();
                while (player.transform.position.x < standPoint.position.x)
                {
                    float step = crawlSpeed * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                    if (body != null && body.enabled)
                    {
                        body.Move(new Vector3(step, -2f * Time.unscaledDeltaTime, 0f));
                    }
                    else
                    {
                        player.transform.position += Vector3.right * step;
                    }

                    yield return null;
                }

                stage = "Pause";
                yield return Wait(pauseSeconds);
                stage = "Getting Up";
                animator.CrossFadeInFixedTime(getUpState, 0.2f);
                yield return null;
                yield return Wait(Mathf.Max(animator.GetCurrentAnimatorStateInfo(0).length, 0.5f));
                animator.SetFloat("Speed", 0f);
                animator.SetBool("IsGrounded", true);
                animator.CrossFadeInFixedTime("Locomotion", 0.25f);
                player.enabled = true;
            }
            else
            {
                // No crawl clip yet: it happens out of sight, and the protagonist is first seen standing at the threshold.
                stage = "Crawl (out of sight)";
                yield return Wait(pauseSeconds);
                Place(standPoint.position);
                HidePlayer(false);
                Show(crawlCamera);
            }

            stage = "Standing";
            Hide(chamberCamera);
            yield return Wait(standSeconds);
            Finish(ui, driver);
        }

        private void Finish(FacilityUI ui, PlayerAnimationDriver driver)
        {
            stage = "Handover";
            Hide(crawlCamera);
            Hide(chamberCamera);
            Hide(transportCamera);
            if (driver != null)
            {
                driver.enabled = true;
            }

            controls?.Allow(OpeningControls.Ability.Move);
            ui?.SetCutscene(false);
            stage = "Done";
        }

        private void Skip()
        {
            skipped = true;
            moveCarriage = false;
            Set(emergencyLights, emergencyTargets, 1f);
            Set(revealLights, revealTargets, 1f);
            Hide(transportCamera);
            Hide(chamberCamera);
            Hide(crawlCamera);
            if (transportHall != null)
            {
                transportHall.SetActive(false);
            }

            foreach (Rigidbody shard in shards)
            {
                if (shard != null)
                {
                    shard.gameObject.SetActive(true);
                    Settle(shard);
                }
            }

            stage = "Done";
        }

        //==================== HELPERS =====================
        // Rise, hold and fall of the protagonist's moment, 0 to 1, from the time it starts.
        private float Moment(float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }

            if (t < protagonistMomentSeconds.x)
            {
                return Mathf.SmoothStep(0f, 1f, t / protagonistMomentSeconds.x);
            }

            t -= protagonistMomentSeconds.x;
            if (t < protagonistMomentSeconds.y)
            {
                return 1f;
            }

            t -= protagonistMomentSeconds.y;
            return t < protagonistMomentSeconds.z ? Mathf.SmoothStep(1f, 0f, t / protagonistMomentSeconds.z) : 0f;
        }

        private void FadeHum(float began, float volume)
        {
            SetHum(volume * Mathf.Clamp01((Time.unscaledTime - began - 0.5f) / humFadeSeconds));
        }

        // Through the sound's Audio Channel when it has one, so the Ambience setting still applies.
        private void SetHum(float volume)
        {
            if (hum == null)
            {
                return;
            }

            AudioChannel channel = hum.GetComponent<AudioChannel>();
            if (channel != null)
            {
                channel.BaseVolume = volume;
            }
            else
            {
                hum.volume = volume;
            }
        }

        private void DropShards()
        {
            var random = new System.Random(7);
            foreach (Rigidbody shard in shards)
            {
                if (shard == null)
                {
                    continue;
                }

                shard.gameObject.SetActive(true);
                shard.isKinematic = false;
                shard.linearVelocity = new Vector3((float)random.NextDouble() * 2.5f + 0.5f, (float)random.NextDouble() * 1.5f, (float)(random.NextDouble() - 0.5) * 1.2f);
                shard.angularVelocity = new Vector3((float)random.NextDouble() * 8f, (float)random.NextDouble() * 8f, (float)random.NextDouble() * 8f);
            }

            StartCoroutine(SettleShards());
        }

        private IEnumerator SettleShards()
        {
            yield return Wait(shardSettleSeconds);
            foreach (Rigidbody shard in shards)
            {
                Settle(shard);
            }
        }

        // A settled shard is scenery: it no longer moves or collides, so the player walks over it.
        private static void Settle(Rigidbody shard)
        {
            if (shard == null)
            {
                return;
            }

            shard.isKinematic = true;
            foreach (Collider c in shard.GetComponentsInChildren<Collider>())
            {
                c.enabled = false;
            }
        }

        private void HidePlayer(bool hide)
        {
            if (hide)
            {
                hiddenRenderers.Clear();
                foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true))
                {
                    hiddenRenderers.Add((r, r.enabled));
                    r.enabled = false;
                }

                return;
            }

            foreach ((Renderer r, bool enabled) in hiddenRenderers)
            {
                if (r != null)
                {
                    r.enabled = enabled;
                }
            }

            hiddenRenderers.Clear();
        }

        // Puts the player's feet on a point, the way a Room Reset does.
        private void Place(Vector3 feet)
        {
            Swappable body = player.GetComponent<Swappable>();
            if (body != null)
            {
                body.MoveTo(feet + (body.Position - body.Anchor));
            }
            else
            {
                player.transform.position = feet + Vector3.up;
            }

            Physics.SyncTransforms();
        }

        private static bool HasState(Animator animator, string state)
        {
            return !string.IsNullOrEmpty(state)
                && (animator.HasState(0, Animator.StringToHash(state)) || animator.HasState(0, Animator.StringToHash("Base Layer." + state)));
        }

        private static void Show(CinemachineCamera camera)
        {
            if (camera != null)
            {
                camera.enabled = true;
                camera.Prioritize();
            }
        }

        private static void Hide(CinemachineCamera camera)
        {
            if (camera != null)
            {
                camera.enabled = false;
            }
        }

        private static void PlaySlot(AudioSource source)
        {
            if (source != null && source.clip != null)
            {
                source.Play();
            }
        }

        private static void Capture(List<Light> lights, List<float> targets)
        {
            targets.Clear();
            foreach (Light light in lights)
            {
                targets.Add(light != null ? light.intensity : 0f);
            }
        }

        private static void Set(List<Light> lights, List<float> targets, float amount)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].intensity = targets[i] * amount;
                }
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, v.y, 0f);

        private bool NearStart()
        {
            return player != null && crawlStart != null
                && Vector3.Distance(Flat(player.transform.position), Flat(crawlStart.position)) <= startRadius;
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntilTime(float began, float seconds, System.Action each)
        {
            while (Time.unscaledTime - began < seconds)
            {
                each?.Invoke();
                yield return null;
            }
        }
    }
}

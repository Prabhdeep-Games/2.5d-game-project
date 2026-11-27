using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheSpaceBetween
{
    /// <summary>
    /// The Run: the end of the opening. When the player's middle enters this object's Box Collider, control is taken and the
    /// protagonist runs on by themselves, deeper into the passage, as its lights fade. A run camera follows for a few
    /// seconds, then gives way to a still camera placed where it is: the view slows and stops, and the protagonist runs
    /// on out of the frame. The camera holds on the empty corridor, a mechanical sound echoes, the view cuts to black,
    /// and the next scene loads with no loading text.
    ///
    /// It plays once and cannot be interrupted: the Facility UI treats it as a cutscene (no pause, no restart). It moves the
    /// player the way the drone cutscene does, with the player controller switched off, so the controller is unchanged.
    /// Real time throughout.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ScriptedRun : MonoBehaviour
    {
        //==================== CAST =====================
        [Header("Cast")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        //==================== RUN =====================
        [Header("Run")]
        [Tooltip("How fast the protagonist runs once control is taken, in units per second. The player's own run is 7.6.")]
        [SerializeField, Min(1f)] private float runSpeed = 7.6f;

        [Tooltip("Where the protagonist stops running, well out of the still camera's view: the distant opening.")]
        [SerializeField] private Transform runTo;

        //==================== CAMERA =====================
        [Header("Camera")]
        [Tooltip("The camera that follows the run.")]
        [SerializeField] private CinemachineCamera runCamera;

        [Tooltip("The camera that is left behind. Put where the run camera is when the follow ends; it slows to a stop there.")]
        [SerializeField] private CinemachineCamera stillCamera;

        [Tooltip("Real seconds the run camera follows before the view slows and stops.")]
        [SerializeField, Min(0f)] private float followSeconds = 3.5f;

        [Tooltip("Real seconds the view holds on the empty corridor before the cut.")]
        [SerializeField, Min(0f)] private float emptySeconds = 2.6f;

        [Tooltip("Real seconds into the empty hold when the mechanical sound plays.")]
        [SerializeField, Min(0f)] private float soundAt = 0.8f;

        [Tooltip("Real seconds of black before the next scene starts loading.")]
        [SerializeField, Min(0f)] private float blackSeconds = 1.2f;

        //==================== LIGHT =====================
        [Header("Light")]
        [Tooltip("The passage's lights. They fade towards Remaining Light over the run, so the passage grows darker.")]
        [SerializeField] private List<Light> passageLights = new List<Light>();

        [Tooltip("How much of each light is left by the end of the run.")]
        [SerializeField, Range(0f, 1f)] private float remainingLight = 0.15f;

        //==================== NEXT SCENE =====================
        [Header("Next Scene")]
        [Tooltip("The scene that begins at the First Gate. It must be in Build Settings.")]
        [SerializeField] private string nextScene = "L01_TestingChamber";

        //==================== SOUND SLOTS =====================
        [Header("Sound Slots (empty until the final sounds are supplied)")]
        [SerializeField] private AudioSource mechanicalSound;

        //==================== DEBUG =====================
        [Header("Debug")]
        [ReadOnly, SerializeField] private string stage = "Waiting";

        private BoxCollider area;
        private bool started;

        public string Stage => stage;

        public bool Started => started;

        public string NextScene => nextScene;

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
        }

        private void Update()
        {
            if (started || player == null || !area.bounds.Contains(player.transform.position + Vector3.up))
            {
                return;
            }

            FacilityUI ui = FacilityUI.Current;
            if (ui != null && (ui.IsReturning || ui.IsPaused || ui.IsLeaving || ui.IsEnding || ui.IsCutscene))
            {
                return;
            }

            started = true;
            StartCoroutine(Play(ui));
        }

        private IEnumerator Play(FacilityUI ui)
        {
            stage = "Running";
            ui?.SetCutscene(true);
            Animator animator = player.GetComponentInChildren<Animator>();
            PlayerAnimationDriver driver = player.GetComponentInChildren<PlayerAnimationDriver>();
            CharacterController body = player.GetComponent<CharacterController>();
            if (driver != null)
            {
                driver.enabled = false;
            }

            // The script runs the player from here: the controller would otherwise slow them to a stop.
            player.enabled = false;
            if (animator != null)
            {
                animator.SetBool("IsGrounded", true);
                animator.SetFloat("Speed", runSpeed);
                animator.CrossFadeInFixedTime("Locomotion", 0.15f);
            }

            if (runCamera != null)
            {
                runCamera.enabled = true;
                runCamera.Prioritize();
            }

            var startIntensity = new float[passageLights.Count];
            for (int i = 0; i < passageLights.Count; i++)
            {
                startIntensity[i] = passageLights[i] != null ? passageLights[i].intensity : 0f;
            }

            float began = Time.unscaledTime;
            float endX = runTo != null ? runTo.position.x : player.transform.position.x + 60f;
            float totalSeconds = Mathf.Max((endX - player.transform.position.x) / runSpeed, 0.1f);
            bool stopped = false;
            while (player.transform.position.x < endX)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                if (body != null && body.enabled)
                {
                    body.Move(new Vector3(runSpeed * dt, -2f * dt, 0f));
                }

                if (animator != null)
                {
                    animator.SetFloat("Speed", runSpeed);
                }

                float darkening = Mathf.Clamp01((Time.unscaledTime - began) / totalSeconds);
                for (int i = 0; i < passageLights.Count; i++)
                {
                    if (passageLights[i] != null)
                    {
                        passageLights[i].intensity = Mathf.Lerp(startIntensity[i], startIntensity[i] * remainingLight, darkening);
                    }
                }

                // The camera lets go: the still camera takes the run camera's place and the blend slows it to a stop.
                if (!stopped && Time.unscaledTime - began >= followSeconds)
                {
                    stopped = true;
                    stage = "Camera Stopping";
                    if (stillCamera != null && runCamera != null)
                    {
                        stillCamera.transform.SetPositionAndRotation(runCamera.State.GetFinalPosition(), runCamera.State.GetFinalOrientation());
                        stillCamera.Lens = runCamera.State.Lens;
                        stillCamera.enabled = true;
                        stillCamera.Prioritize();
                    }
                }

                yield return null;
            }

            // Gone from view: the protagonist stops in the dark beyond the frame.
            stage = "Empty Corridor";
            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }

            float emptyFrom = Time.unscaledTime;
            bool played = false;
            while (Time.unscaledTime - emptyFrom < emptySeconds)
            {
                if (!played && Time.unscaledTime - emptyFrom >= soundAt)
                {
                    played = true;
                    if (mechanicalSound != null && mechanicalSound.clip != null)
                    {
                        mechanicalSound.Play();
                    }
                }

                yield return null;
            }

            // Cut to black.
            stage = "Black";
            ui?.FadeToDark(0f);
            float blackFrom = Time.unscaledTime;
            while (Time.unscaledTime - blackFrom < blackSeconds)
            {
                yield return null;
            }

            stage = "Loading";
            if (ui != null)
            {
                ui.SetCutscene(false);
                ui.LoadScene(nextScene, string.Empty, 0);
            }
            else
            {
                SceneManager.LoadScene(nextScene);
            }
        }

        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(0.83f, 0.63f, 0.3f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}

using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// The First Gate, the last beat of the opening, at the start of the next scene. The scene opens on a still camera at
    /// the gate. The protagonist runs in through the open gate, carried on from The Run, and as soon as they cross the
    /// threshold the heavy gate starts closing behind them. They slow to a stop; the gate shuts (CLANG); the camera holds on
    /// it; an intercom crackles. Then the camera returns to the normal gameplay view and control comes back.
    ///
    /// The intercom has no words yet: its two sound slots stay silent and the Intercom event is the hook for the final
    /// message, which is added later. Nothing is shown on screen for it.
    ///
    /// The sequence plays when the scene starts with the player in front of the gate (the room's Room Reset puts them
    /// there on arrival). Once the gate is shut, that Room Reset's entry point moves inside, so a restart never puts the
    /// player outside a closed gate. Real time throughout; the player controller is switched off while it runs, so the
    /// controller itself is unchanged.
    /// </summary>
    public sealed class FirstGateSequence : MonoBehaviour
    {
        private const float MaxStep = 0.05f;

        //==================== CAST =====================
        [Header("Cast")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The gate. It starts open and unlocked, and is closed and locked behind the protagonist.")]
        [SerializeField] private AccessDoor gate;

        //==================== ROUTE =====================
        [Header("Route")]
        [Tooltip("The threshold. Once the protagonist's position passes it along X, the gate starts closing.")]
        [SerializeField] private Transform threshold;

        [Tooltip("Where the protagonist comes to a stop inside the room (feet).")]
        [SerializeField] private Transform stopPoint;

        [Tooltip("The Room Reset entry point to move inside once the gate is shut. Optional.")]
        [SerializeField] private Transform resetEntry;

        [Tooltip("Where that entry point goes once the gate is shut.")]
        [SerializeField] private Transform entryInside;

        [Tooltip("The protagonist's speed coming in, matching The Run. Units per second.")]
        [SerializeField, Min(0.5f)] private float entrySpeed = 7.6f;

        [Tooltip("Real seconds after the scene starts before the protagonist moves, so the arrival has placed them.")]
        [SerializeField, Min(0f)] private float arriveSeconds = 0.35f;

        //==================== CAMERA =====================
        [Header("Camera")]
        [Tooltip("The still camera on the gate. Shown from the start; released to the gameplay camera at the end.")]
        [SerializeField] private CinemachineCamera gateCamera;

        [Tooltip("Real seconds the camera holds on the shut gate.")]
        [SerializeField, Min(0f)] private float holdSeconds = 2.4f;

        [Tooltip("Real seconds given to the intercom when its slots are empty, so the timing is already right.")]
        [SerializeField, Min(0f)] private float intercomSeconds = 3.5f;

        [Tooltip("Longest wait for the gate to shut, in real seconds, in case something stands in its way.")]
        [SerializeField, Min(1f)] private float closeTimeout = 6f;

        //==================== SOUND SLOTS =====================
        [Header("Sound Slots (empty until the final sounds are supplied)")]
        [SerializeField] private AudioSource clang;
        [SerializeField] private AudioSource intercomCrackle;

        [Tooltip("[INTERCOM MESSAGE TO BE ADDED LATER]")]
        [SerializeField] private AudioSource intercomVoice;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked when the intercom begins. The hook for the final intercom message.")]
        [SerializeField] private UnityEvent intercom;

        //==================== DEBUG =====================
        [Header("Debug")]
        [ReadOnly, SerializeField] private string stage = "Waiting";

        public string Stage => stage;

        public bool Finished => stage == "Done" || stage == "Skipped";

        /// <summary>The real time the gate finished shutting, or -1.</summary>
        public float ShutAt { get; private set; } = -1f;

        /// <summary>The player's X when the gate started closing.</summary>
        public float ClosedFromX { get; private set; } = float.NaN;

        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }
        }

        private void Start()
        {
            if (player == null || gate == null || threshold == null || player.transform.position.x >= threshold.position.x)
            {
                stage = "Skipped";
                return;
            }

            FacilityUI ui = FacilityUI.Current;
            ui?.SetCutscene(true);
            if (gateCamera != null)
            {
                gateCamera.enabled = true;
                gateCamera.Prioritize();
                gateCamera.PreviousStateIsValid = false;
            }

            StartCoroutine(Play(ui));
        }

        private IEnumerator Play(FacilityUI ui)
        {
            Animator animator = player.GetComponentInChildren<Animator>();
            PlayerAnimationDriver driver = player.GetComponentInChildren<PlayerAnimationDriver>();
            CharacterController body = player.GetComponent<CharacterController>();

            // The loading screen comes down on the gate with the protagonist already on the way in.
            stage = "Arriving";
            if (driver != null)
            {
                driver.enabled = false;
            }

            player.enabled = false;
            if (animator != null)
            {
                animator.SetBool("IsGrounded", true);
                animator.SetFloat("Speed", entrySpeed);
                animator.Play("Locomotion", 0, 0f);
            }

            // The Facility UI puts the player at the room's entry a frame after the scene starts, then holds the loading
            // screen for 0.3 s; the run in starts as it begins to fade.
            yield return Wait(arriveSeconds);

            // Continue into a later room: the arrival put the player past the gate, so the gate is simply shut already.
            if (player.transform.position.x >= threshold.position.x)
            {
                gate.Lock();
                if (resetEntry != null && entryInside != null)
                {
                    resetEntry.position = entryInside.position;
                }

                if (gateCamera != null)
                {
                    gateCamera.enabled = false;
                }

                player.enabled = true;
                if (driver != null)
                {
                    driver.enabled = true;
                }

                ui?.SetCutscene(false);
                stage = "Skipped";
                yield break;
            }

            float stopX = stopPoint != null ? stopPoint.position.x : threshold.position.x + 5f;
            float startX = threshold.position.x;
            float speed = entrySpeed;
            stage = "Entering";
            while (player.transform.position.x < stopX - 0.02f)
            {
                // A long frame (a hitch) never carries the protagonist past the threshold unseen.
                float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
                float x = player.transform.position.x;

                // Past the threshold the run eases off, coming to rest on the stop point.
                if (x >= startX)
                {
                    float remaining = Mathf.Max(stopX - x, 0f);
                    speed = Mathf.Min(entrySpeed, Mathf.Max(0.6f, Mathf.Sqrt(2f * 6f * remaining)));
                }

                float step = Mathf.Min(speed * dt, stopX - x);
                if (body != null && body.enabled)
                {
                    body.Move(new Vector3(step, -2f * dt, 0f));
                }

                // The gate starts closing the moment the protagonist is through.
                if (player.transform.position.x >= startX && (gate.State == AccessDoor.DoorState.Open || gate.State == AccessDoor.DoorState.Opening))
                {
                    stage = "Gate Closing";
                    ClosedFromX = player.transform.position.x;
                    gate.Lock();
                }

                if (animator != null)
                {
                    animator.SetFloat("Speed", speed);
                }

                yield return null;
            }

            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }

            // The protagonist stops; the gate finishes shutting behind them.
            stage = "Stopped";
            float waitFrom = Time.unscaledTime;
            while (gate.State != AccessDoor.DoorState.Closed && Time.unscaledTime - waitFrom < closeTimeout)
            {
                yield return null;
            }

            ShutAt = Time.unscaledTime;
            stage = "Shut";
            if (clang != null && clang.clip != null)
            {
                clang.Play();
            }

            if (resetEntry != null && entryInside != null)
            {
                resetEntry.position = entryInside.position;
            }

            yield return Wait(holdSeconds);

            // The intercom: a crackle, then the message that is added later.
            stage = "Intercom";
            float intercomLength = intercomSeconds;
            if (intercomCrackle != null && intercomCrackle.clip != null)
            {
                intercomCrackle.Play();
                intercomLength = Mathf.Max(intercomLength, intercomCrackle.clip.length);
            }

            if (intercomVoice != null && intercomVoice.clip != null)
            {
                intercomVoice.PlayDelayed(intercomCrackle != null && intercomCrackle.clip != null ? intercomCrackle.clip.length : 0f);
                intercomLength = Mathf.Max(intercomLength, intercomVoice.clip.length + (intercomCrackle != null && intercomCrackle.clip != null ? intercomCrackle.clip.length : 0f));
            }

            intercom?.Invoke();
            yield return Wait(intercomLength);

            // Back to the normal view and the player.
            stage = "Returning";
            if (gateCamera != null)
            {
                gateCamera.enabled = false;
            }

            player.enabled = true;
            if (driver != null)
            {
                driver.enabled = true;
            }

            ui?.SetCutscene(false);
            stage = "Done";
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until)
            {
                yield return null;
            }
        }
    }
}

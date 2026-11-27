using System.Collections;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The short cutscene at the end of the exterior. When the player enters this object's Box Collider, control is
    /// taken and the protagonist keeps running along the walkway. A facility drone comes in from behind and strikes
    /// their back; they fall flat (Mixamo "Fall Flat", the Animator's Knockdown state) and lie still while the drone
    /// rises and holds above them. Then the Ending Sequence takes over: the still wide view, the title, END, credits.
    ///
    /// The drone is the Drone object: its children are only a placeholder model, so the real asset can replace them
    /// later without touching this script. It flies on real time, so nothing here depends on the frame rate.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DroneStrikeSequence : MonoBehaviour
    {
        //==================== CAST =====================
        [Header("Cast")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The drone. Hidden until the cutscene; replace its placeholder children with the real model.")]
        [SerializeField] private Transform drone;

        [Tooltip("The ending that follows the fall.")]
        [SerializeField] private EndingSequence ending;

        //==================== RUN =====================
        [Header("Run")]
        [Tooltip("How fast the protagonist runs once control is taken, in units per second.")]
        [SerializeField, Min(1f)] private float runSpeed = 7.5f;

        [Tooltip("Real seconds of running before the drone strikes.")]
        [SerializeField, Min(0.5f)] private float runSeconds = 2.4f;

        //==================== DRONE =====================
        [Header("Drone")]
        [Tooltip("Where the drone starts, relative to the player when the cutscene begins: behind, above and a little toward the camera.")]
        [SerializeField] private Vector3 droneStartOffset = new Vector3(-16f, 6f, -1.5f);

        [Tooltip("Where the drone strikes, relative to the player's feet: the middle of the back.")]
        [SerializeField] private Vector3 strikeOffset = new Vector3(-0.35f, 1.25f, 0f);

        [Tooltip("How high the drone holds above the fallen protagonist.")]
        [SerializeField, Min(1f)] private float hoverHeight = 3.2f;

        [Tooltip("Real seconds the drone takes to rise to its hold after the strike.")]
        [SerializeField, Min(0.1f)] private float riseSeconds = 1.4f;

        //==================== FALL =====================
        [Header("Fall")]
        [Tooltip("The Animator state of the fall.")]
        [SerializeField] private string knockdownState = "Knockdown";

        [Tooltip("Real seconds from the strike until the ending begins.")]
        [SerializeField, Min(0.5f)] private float lieSeconds = 3.8f;

        [Tooltip("How quickly the run's momentum fades once struck. The fall clip already carries the body about 2.5 m forward, so this only adds a little; higher stops sooner.")]
        [SerializeField, Min(0.5f)] private float momentumFade = 20f;

        [Tooltip("Played at the strike. Optional.")]
        [SerializeField] private AudioSource strikeSound;

        //==================== DEBUG =====================
        [Header("Debug")]
        [ReadOnly, SerializeField] private string stage = "Waiting";

        private BoxCollider area;
        private bool started;

        public string Stage => stage;
        public bool Started => started;

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

            if (drone != null)
            {
                drone.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (started || player == null || !area.bounds.Contains(player.transform.position + Vector3.up))
            {
                return;
            }

            FacilityUI ui = FacilityUI.Current;
            if (ui != null && (ui.IsReturning || ui.IsPaused || ui.IsLeaving || ui.IsEnding))
            {
                return;
            }

            started = true;
            StartCoroutine(Play(ui));
        }

        private IEnumerator Play(FacilityUI ui)
        {
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
            float direction = Mathf.Sign(player.BodyFacingSign == 0f ? 1f : player.BodyFacingSign);
            animator.SetBool("IsGrounded", true);
            animator.SetFloat("Speed", runSpeed);
            animator.CrossFadeInFixedTime("Locomotion", 0.15f);

            Vector3 droneStart = player.transform.position + new Vector3(droneStartOffset.x * direction, droneStartOffset.y, droneStartOffset.z);
            if (drone != null)
            {
                drone.position = droneStart;
                drone.rotation = Quaternion.LookRotation(new Vector3(direction, 0f, 0f));
                drone.gameObject.SetActive(true);
            }

            // Run; the drone closes in on the point where the back will be at the strike.
            stage = "Running";
            float elapsed = 0f;
            while (elapsed < runSeconds)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                if (body != null && body.enabled)
                {
                    body.Move(new Vector3(direction * runSpeed * dt, -2f * dt, 0f));
                }

                animator.SetFloat("Speed", runSpeed);
                if (drone != null)
                {
                    float remaining = Mathf.Max(runSeconds - elapsed, 0f);
                    Vector3 strikePoint = player.transform.position + new Vector3(strikeOffset.x * direction, strikeOffset.y, strikeOffset.z)
                        + new Vector3(direction * runSpeed * remaining, 0f, 0f);
                    float t = Mathf.SmoothStep(0f, 1f, elapsed / runSeconds);
                    Vector3 arc = Vector3.Lerp(droneStart, strikePoint, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.2f;
                    drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation((strikePoint - drone.position).normalized + Vector3.down * 0.2f), 6f * dt);
                    drone.position = arc;
                }

                yield return null;
            }

            // The strike.
            stage = "Struck";
            if (strikeSound != null)
            {
                strikeSound.Play();
            }

            animator.CrossFadeInFixedTime(knockdownState, 0.08f);
            Vector3 strikeAt = drone != null ? drone.position : player.transform.position;

            // The fall travels in the pose, so the drone follows the body (its hips), not the player's origin.
            Transform hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform bodyMark = hips != null ? hips : player.transform;

            // The drone recoils, then rises and holds over the fallen protagonist, watching.
            elapsed = 0f;
            while (elapsed < lieSeconds)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;

                // The run's momentum carries the body on a little as it falls, fading out within a second.
                float carry = runSpeed * Mathf.Exp(-elapsed * momentumFade);
                if (body != null && body.enabled && carry > 0.05f)
                {
                    body.Move(new Vector3(direction * carry * dt, -2f * dt, 0f));
                }

                if (drone != null)
                {
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / riseSeconds));
                    Vector3 holdAt = new Vector3(bodyMark.position.x, player.transform.position.y + hoverHeight, 0.8f);
                    Vector3 position = Vector3.Lerp(strikeAt, holdAt, t) + Vector3.up * Mathf.Sin(Time.unscaledTime * 1.7f) * 0.08f * t;
                    drone.position = position;
                    Vector3 lookDown = bodyMark.position - position;
                    drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(lookDown.normalized), 3f * dt);
                }

                if (elapsed > riseSeconds && stage == "Struck")
                {
                    stage = "Lying";
                }

                yield return null;
            }

            stage = "Ending";
            if (ending != null)
            {
                ending.Begin();
            }
            else
            {
                ui?.SetCutscene(false);
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

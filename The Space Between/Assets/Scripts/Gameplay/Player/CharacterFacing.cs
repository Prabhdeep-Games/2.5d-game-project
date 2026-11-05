using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Turns a rigged character to face along the side-scrolling route. The turn always passes through facing the camera,
    /// so the character's front stays readable instead of spinning away from the player.
    ///
    /// While standing still with a swap target on the far side, the character turns to face the target, so the arm does
    /// not reach through its own body. Nothing else depends on this, so it never affects movement.
    ///
    /// Put this on the character model. The model must face its own +Z, which is how Mixamo characters are made.
    /// </summary>
    // Runs after the movement's LateUpdate has placed the body, and before SwapArmAim reads the shoulder.
    [DefaultExecutionOrder(110)]
    public class CharacterFacing : MonoBehaviour
    {
        private const float FacingRightYaw = 90f;
        private const float FacingLeftYaw = 270f;

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The movement that says which way the character should face. Empty means the first one found in the parents.")]
        [SerializeField] private PlaceholderPlayerController movement;

        [Tooltip("The swap ability, used to face a target while standing still. Empty means the first one found in the parents.")]
        [SerializeField] private SwapAbility swapAbility;

        //==================== TURNING =====================
        [Header("Turning")]
        [Tooltip("How quickly the character turns around. Higher is snappier.")]
        [SerializeField, Min(0.1f)] private float turnSpeed = 14f;

        [Tooltip("Fastest speed, in units per second, that still counts as standing still for facing a swap target.")]
        [SerializeField, Min(0f)] private float standingStillSpeed = 0.5f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Which way the character is turning toward: +1 is along +X, -1 is along -X.")]
        [ReadOnly, SerializeField] private float targetSign = 1f;

        private float yaw = FacingRightYaw;

        /// <summary>The way the character is turning toward: +1 is along +X, -1 is along -X.</summary>
        public float TargetSign => targetSign;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            if (movement == null)
            {
                movement = GetComponentInParent<PlaceholderPlayerController>();
            }

            if (swapAbility == null)
            {
                swapAbility = GetComponentInParent<SwapAbility>();
            }
        }

        private void LateUpdate()
        {
            if (movement == null)
            {
                return;
            }

            targetSign = ChooseSign();

            // Scaled time deliberately: the character holds its pose while a swap has stopped game time.
            float blend = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime);
            yaw = Mathf.Lerp(yaw, targetSign > 0f ? FacingRightYaw : FacingLeftYaw, blend);
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private float ChooseSign()
        {
            bool standingStill = movement.IsGrounded && !movement.IsSliding && movement.Speed <= standingStillSpeed;
            Swappable target = swapAbility != null ? swapAbility.FocusTarget : null;

            if (standingStill && target != null)
            {
                return Mathf.Sign(target.Center.x - transform.position.x);
            }

            return movement.BodyFacingSign;
        }
    }
}

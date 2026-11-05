using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The swap gesture on a humanoid character. The right arm rises and points at whatever the swap is aimed at, on top
    /// of whatever animation is playing. Pressing swap closes the right hand, and the fist finishes closing at the exact
    /// moment SwapAbility performs the swap. The hand opens again once the swap has played out.
    ///
    /// The fist comes from an Animator layer that holds a closed hand under a mask of the right fingers. This script only
    /// sets that layer's weight, from SwapAbility.GestureProgress, so the hand and the swap cannot drift apart. It only
    /// reads the swap; SwapAbility owns the timing, so a missing fist layer or Animator never delays a swap.
    ///
    /// Put this on the character model, next to the Animator. The Animator needs a Humanoid avatar.
    /// </summary>
    // Runs after the Animator has posed the bones and after CharacterFacing has turned the model.
    [DefaultExecutionOrder(120)]
    [RequireComponent(typeof(Animator))]
    public class SwapArmAim : MonoBehaviour
    {
        private const float SmallestDirection = 0.0001f;

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The swap ability this arm follows. Empty means the first one found in the parents.")]
        [SerializeField] private SwapAbility swapAbility;

        //==================== ARM =====================
        [Header("Arm")]
        [Tooltip("Seconds the arm takes to rise to a target. Real time, so it keeps moving while a swap has stopped game time.")]
        [SerializeField, Min(0.01f)] private float raiseSeconds = 0.14f;

        [Tooltip("Seconds the arm takes to drop back into the animation.")]
        [SerializeField, Min(0.01f)] private float lowerSeconds = 0.28f;

        [Tooltip("How straight the elbow gets while aiming. 1 locks it straight, lower keeps a little of the animation's bend.")]
        [SerializeField, Range(0f, 1f)] private float elbowStraightness = 0.9f;

        //==================== FIST =====================
        [Header("Fist")]
        [Tooltip("Name of the Animator layer that holds the closed hand. Its weight is set from here, so leave it at 0 in the controller.")]
        [SerializeField] private string fistLayerName = "Right Hand Fist";

        [Tooltip("Seconds the hand takes to open again after a swap.")]
        [SerializeField, Min(0.01f)] private float fistOpenSeconds = 0.2f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Draw the line from the shoulder to the aim point, when selected.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("How far the arm is raised right now, from 0 to 1.")]
        [ReadOnly, SerializeField] private float armWeight;

        [Tooltip("How closed the hand is right now, from 0 to 1.")]
        [ReadOnly, SerializeField] private float fistWeight;

        private Animator animator;
        private Transform upperArm;
        private Transform lowerArm;
        private Transform hand;
        private Transform middleFinger;
        private int fistLayer = -1;
        private bool bonesReady;
        private Vector3 aimDirection = Vector3.right;

        /// <summary>How far the arm is raised, from 0 to 1.</summary>
        public float ArmWeight => armWeight;

        /// <summary>How closed the right hand is, from 0 to 1.</summary>
        public float FistWeight => fistWeight;

        /// <summary>The way the arm is trying to point, in world space.</summary>
        public Vector3 AimDirection => aimDirection;

        /// <summary>The bone at the shoulder end of the right arm, or null without a humanoid rig.</summary>
        public Transform UpperArm => upperArm;

        /// <summary>The right hand bone, or null without a humanoid rig.</summary>
        public Transform Hand => hand;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            animator = GetComponent<Animator>();

            if (swapAbility == null)
            {
                swapAbility = GetComponentInParent<SwapAbility>();
            }

            if (swapAbility == null)
            {
                Debug.LogWarning($"[{nameof(SwapArmAim)}] '{name}' found no SwapAbility in its parents, so the arm stays down.", this);
            }

            CacheBones();
            CacheFistLayer();
        }

        // A missing rig or layer is reported once, instead of failing silently every frame.
        private void CacheBones()
        {
            if (!animator.isHuman)
            {
                Debug.LogWarning($"[{nameof(SwapArmAim)}] '{name}' needs a Humanoid avatar on its Animator, so the arm stays down.", this);
                return;
            }

            upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            middleFinger = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            bonesReady = upperArm != null && lowerArm != null && hand != null;
        }

        private void CacheFistLayer()
        {
            if (animator.runtimeAnimatorController == null)
            {
                return;
            }

            fistLayer = animator.GetLayerIndex(fistLayerName);
            if (fistLayer < 0)
            {
                Debug.LogWarning($"[{nameof(SwapArmAim)}] The Animator controller '{animator.runtimeAnimatorController.name}' has no layer named '{fistLayerName}', so the hand stays open. The swap itself is unaffected.", this);
            }
        }

        private void LateUpdate()
        {
            if (swapAbility == null)
            {
                return;
            }

            UpdateFist();
            UpdateArmWeight();

            if (bonesReady && armWeight > 0f)
            {
                PoseArm();
            }
        }

        //==================== FIST =====================
        private void UpdateFist()
        {
            if (swapAbility.IsGesturing)
            {
                fistWeight = Mathf.Max(fistWeight, swapAbility.GestureProgress);
            }
            else if (swapAbility.IsSwapping)
            {
                fistWeight = 1f;
            }
            else
            {
                fistWeight = Mathf.MoveTowards(fistWeight, 0f, Time.unscaledDeltaTime / fistOpenSeconds);
            }

            if (fistLayer >= 0)
            {
                animator.SetLayerWeight(fistLayer, fistWeight);
            }
        }

        //==================== ARM =====================
        private void UpdateArmWeight()
        {
            // The arm holds its direction through a swap: the target is about to trade places with the player.
            Swappable target = swapAbility.FocusTarget;
            bool holding = swapAbility.IsSwapping;

            if (target != null && bonesReady)
            {
                Vector3 toTarget = target.Center - upperArm.position;
                if (toTarget.sqrMagnitude > SmallestDirection)
                {
                    aimDirection = toTarget.normalized;
                }
            }

            bool raised = target != null || holding;
            float seconds = raised ? raiseSeconds : lowerSeconds;
            armWeight = Mathf.MoveTowards(armWeight, raised ? 1f : 0f, Time.unscaledDeltaTime / seconds);
        }

        // Straightens the elbow, then turns the whole arm so the hand lies on the line to the target. Both are measured
        // from the bones' real positions, so this works whatever the rig's bone axes are and whatever animation is playing.
        private void PoseArm()
        {
            float weight = Mathf.SmoothStep(0f, 1f, armWeight);

            Vector3 upperDirection = lowerArm.position - upperArm.position;
            Vector3 lowerDirection = hand.position - lowerArm.position;
            Quaternion straighten = Quaternion.FromToRotation(lowerDirection, upperDirection);
            lowerArm.rotation = Quaternion.Slerp(Quaternion.identity, straighten, elbowStraightness * weight) * lowerArm.rotation;

            Vector3 armDirection = hand.position - upperArm.position;
            Quaternion toAim = Quaternion.FromToRotation(armDirection, aimDirection);
            upperArm.rotation = Quaternion.Slerp(Quaternion.identity, toAim, weight) * upperArm.rotation;

            // The hand carries on along the arm, rather than hanging from the wrist as the animation left it.
            if (middleFinger != null)
            {
                Quaternion alignHand = Quaternion.FromToRotation(middleFinger.position - hand.position, aimDirection);
                hand.rotation = Quaternion.Slerp(Quaternion.identity, alignHand, weight) * hand.rotation;
            }
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !bonesReady || armWeight <= 0f)
            {
                return;
            }

            Gizmos.color = fistWeight > 0f ? Color.yellow : Color.cyan;
            Gizmos.DrawLine(upperArm.position, upperArm.position + aimDirection * 2f);
            Gizmos.DrawWireSphere(hand.position, 0.08f);
        }
    }
}

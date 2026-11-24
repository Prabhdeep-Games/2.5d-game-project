using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The swap gesture on a humanoid character. When a valid swap target is in sight, the head, neck and upper chest
    /// turn a little toward it and the right arm rises and points at it, on top of whatever animation is playing. Pressing
    /// swap closes the right hand, and the fist finishes closing at the exact moment SwapAbility performs the swap. The
    /// look grows from half strength while aiming to full strength from the press until the swap has played out.
    ///
    /// A target behind the character is only acknowledged: the head and chest turn slightly toward the camera and the arm
    /// stays in its animated pose, because a shoulder cannot point backwards. The fist still closes and the swap still happens.
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

        // Look strength while a valid target is only aimed at. Pressing swap raises it to 1.
        private const float AimedLookStrength = 0.5f;

        // Furthest the head, neck and chest together tilt up or down toward a target, in degrees.
        private const float MaxLookUp = 40f;
        private const float MaxLookDown = 30f;

        // How the tilt is shared down the spine. The head takes what is left.
        private const float ChestTiltShare = 0.15f;
        private const float NeckTiltShare = 0.3f;

        // A target behind the character: the slight turn toward the camera, in degrees, and how much of the tilt is kept.
        private const float BehindHeadTurn = 25f;
        private const float BehindChestTurn = 10f;
        private const float NeckTurnShare = 0.35f;
        private const float BehindTiltKept = 0.5f;

        // Angle between the facing and the target, in degrees, where "behind" starts and where it is complete.
        private const float BehindStartsAt = 100f;
        private const float BehindCompleteAt = 135f;

        // Angle from the facing where the arm stops pointing fully, and where it is back in the animation. A shoulder only
        // reaches a little behind the body, so the arm lets go before the target is behind.
        private const float FullReachAngle = 100f;
        private const float NoReachAngle = 125f;

        // Seconds the look takes to follow a target that moves or changes.
        private const float LookFollowSeconds = 0.12f;

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The swap ability this arm follows. Empty means the first one found in the parents.")]
        [SerializeField] private SwapAbility swapAbility;

        //==================== LOOK =====================
        [Header("Look")]
        [Tooltip("Turn the head, neck and upper chest toward a valid swap target. Off leaves them to the animation; the arm and fist still work.")]
        [SerializeField] private bool lookAtTarget = true;

        //==================== ARM =====================
        [Header("Arm")]
        [Tooltip("Seconds the arm and the look take to rise to a target. Real time, so they keep moving while a swap has stopped game time.")]
        [SerializeField, Min(0.01f)] private float raiseSeconds = 0.14f;

        [Tooltip("Seconds the arm and the look take to drop back into the animation.")]
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
        [Tooltip("Draw the lines from the shoulder and the head toward the target, when selected.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("How far the arm is raised right now, from 0 to 1.")]
        [ReadOnly, SerializeField] private float armWeight;

        [Tooltip("How closed the hand is right now, from 0 to 1.")]
        [ReadOnly, SerializeField] private float fistWeight;

        [Tooltip("How strongly the head, neck and chest follow the target right now, from 0 to 1.")]
        [ReadOnly, SerializeField] private float lookWeight;

        [Tooltip("How far behind the character the target is: 0 in front or beside, 1 fully behind.")]
        [ReadOnly, SerializeField] private float behind;

        private Animator animator;
        private Transform upperArm;
        private Transform lowerArm;
        private Transform hand;
        private Transform middleFinger;
        private Transform chest;
        private Transform neck;
        private Transform head;
        private int fistLayer = -1;
        private bool bonesReady;
        private bool lookBonesReady;
        private Vector3 aimDirection = Vector3.right;
        private Vector3 lookDirection = Vector3.right;
        private float lookTilt;
        private float lookTurn;

        /// <summary>How far the arm is raised, from 0 to 1.</summary>
        public float ArmWeight => armWeight;

        /// <summary>How closed the right hand is, from 0 to 1.</summary>
        public float FistWeight => fistWeight;

        /// <summary>How strongly the head, neck and chest follow the target, from 0 to 1.</summary>
        public float LookWeight => lookWeight;

        /// <summary>How far behind the character the target is, from 0 (in front or beside) to 1 (fully behind).</summary>
        public float Behind => behind;

        /// <summary>The way the arm is trying to point, in world space.</summary>
        public Vector3 AimDirection => aimDirection;

        /// <summary>The bone at the shoulder end of the right arm, or null without a humanoid rig.</summary>
        public Transform UpperArm => upperArm;

        /// <summary>The right hand bone, or null without a humanoid rig.</summary>
        public Transform Hand => hand;

        /// <summary>The head bone, or null without a humanoid rig.</summary>
        public Transform Head => head;

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
            // Rigs with fewer finger chains (the Protagonist has only thumb and index) line the hand up along the index.
            middleFinger = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal)
                           ?? animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            bonesReady = upperArm != null && lowerArm != null && hand != null;

            // Upper Chest and Neck are optional in a humanoid avatar, so the next bone down stands in.
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            lookBonesReady = chest != null && head != null;
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

            // The pose holds its direction through a swap: the target is about to trade places with the player.
            Swappable target = ValidTarget();
            bool holding = swapAbility.IsSwapping;
            bool reaching = target != null || holding;

            if (lookBonesReady)
            {
                UpdateLook(target, reaching);
                if (lookWeight > 0f)
                {
                    PoseLook();
                }
            }

            // Measured after the look, which has moved the shoulder with the chest.
            UpdateArmWeight(target, reaching);
            if (bonesReady && armWeight > 0f)
            {
                PoseArm();
            }
        }

        // Only a target the swap would accept is reached for. After the press the locked target counts, since it was
        // valid then and the gesture checks it again before swapping.
        private Swappable ValidTarget()
        {
            if (swapAbility.IsGesturing)
            {
                return swapAbility.FocusTarget;
            }

            Swappable aimed = swapAbility.Target;
            return aimed != null && swapAbility.TargetStatus == SwapRejection.None ? aimed : null;
        }

        // The character's facing on the ground plane, after CharacterFacing has turned the model.
        private Vector3 Facing()
        {
            Vector3 facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            return facing.sqrMagnitude > SmallestDirection ? facing.normalized : Vector3.right;
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

        //==================== LOOK =====================
        private void UpdateLook(Swappable target, bool reaching)
        {
            if (target != null)
            {
                Vector3 toTarget = target.Center - head.position;
                if (toTarget.sqrMagnitude > SmallestDirection)
                {
                    lookDirection = toTarget.normalized;
                }
            }

            bool pressed = swapAbility.IsGesturing || swapAbility.IsSwapping;
            float goal = lookAtTarget && reaching ? (pressed ? 1f : AimedLookStrength) : 0f;
            float seconds = goal > lookWeight ? raiseSeconds : lowerSeconds;
            lookWeight = Mathf.MoveTowards(lookWeight, goal, Time.unscaledDeltaTime / seconds);

            // The angles follow a moving or changing target smoothly instead of jumping with it.
            Vector3 facing = Facing();
            behind = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BehindStartsAt, BehindCompleteAt, Vector3.Angle(facing, lookDirection)));
            float elevation = Mathf.Asin(Mathf.Clamp(lookDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
            float tilt = Mathf.Clamp(elevation, -MaxLookDown, MaxLookUp) * Mathf.Lerp(1f, BehindTiltKept, behind);
            float turn = behind * BehindHeadTurn;

            float follow = 1f - Mathf.Exp(-Time.unscaledDeltaTime / LookFollowSeconds);
            lookTilt = Mathf.Lerp(lookTilt, tilt, follow);
            lookTurn = Mathf.Lerp(lookTurn, turn, follow);
        }

        // Tilts toward the target's height and, for a target behind, turns slightly toward the camera so the face stays
        // readable. Each bone turns about world axes taken from the character's facing, chest first, so its children
        // carry the turn on and the rig's own bone axes never matter.
        private void PoseLook()
        {
            float weight = Mathf.SmoothStep(0f, 1f, lookWeight);
            Vector3 facing = Facing();
            Vector3 tiltAxis = Vector3.Cross(facing, Vector3.up);
            float turnSign = TurnSignTowardCamera(facing);

            float tilt = lookTilt * weight;
            float headTurn = lookTurn * turnSign * weight;
            float chestTurn = lookTurn / BehindHeadTurn * BehindChestTurn * turnSign * weight;
            float headTilt = 1f - ChestTiltShare - (neck != null ? NeckTiltShare : 0f);
            float headTurnShare = neck != null ? 1f - NeckTurnShare : 1f;

            Turn(chest, tilt * ChestTiltShare, chestTurn, tiltAxis);
            if (neck != null)
            {
                Turn(neck, tilt * NeckTiltShare, headTurn * NeckTurnShare, tiltAxis);
            }

            Turn(head, tilt * headTilt, headTurn * headTurnShare, tiltAxis);
        }

        private static void Turn(Transform bone, float tiltDegrees, float turnDegrees, Vector3 tiltAxis)
        {
            bone.rotation = Quaternion.AngleAxis(turnDegrees, Vector3.up) * Quaternion.AngleAxis(tiltDegrees, tiltAxis) * bone.rotation;
        }

        // +1 or -1: the way round, about the vertical, that turns the facing toward the camera.
        private float TurnSignTowardCamera(Vector3 facing)
        {
            Camera view = Camera.main;
            Vector3 toCamera = view != null ? view.transform.position - head.position : Vector3.back;
            toCamera = Vector3.ProjectOnPlane(toCamera, Vector3.up);
            return Vector3.SignedAngle(facing, toCamera, Vector3.up) < 0f ? -1f : 1f;
        }

        //==================== ARM =====================
        private void UpdateArmWeight(Swappable target, bool reaching)
        {
            if (target != null && bonesReady)
            {
                Vector3 toTarget = target.Center - upperArm.position;
                if (toTarget.sqrMagnitude > SmallestDirection)
                {
                    aimDirection = toTarget.normalized;
                }
            }

            // How much of the arm can point at the target: all of it in front, none of it once the target is behind.
            float reach = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullReachAngle, NoReachAngle, Vector3.Angle(Facing(), aimDirection)));
            float goal = reaching ? reach : 0f;
            float seconds = goal > armWeight ? raiseSeconds : lowerSeconds;
            armWeight = Mathf.MoveTowards(armWeight, goal, Time.unscaledDeltaTime / seconds);
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
            if (!drawGizmos)
            {
                return;
            }

            if (bonesReady && armWeight > 0f)
            {
                Gizmos.color = fistWeight > 0f ? Color.yellow : Color.cyan;
                Gizmos.DrawLine(upperArm.position, upperArm.position + aimDirection * 2f);
                Gizmos.DrawWireSphere(hand.position, 0.08f);
            }

            if (lookBonesReady && lookWeight > 0f)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawLine(head.position, head.position + lookDirection * 1.5f);
            }
        }
    }
}

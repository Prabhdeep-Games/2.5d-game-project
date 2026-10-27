using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Marks an object as able to take part in a position swap, and knows how to move itself
    /// during one. The player has one too, so both sides of a swap are treated the same way.
    ///
    /// Size is taken from the solid (non-trigger) colliders on this object and its children.
    /// Movement uses the Rigidbody when there is one and the Transform otherwise.
    /// </summary>
    [DisallowMultipleComponent]
    public class Swappable : MonoBehaviour
    {
        //==================== CONFIGURATION =====================
        [Header("Rules")]
        [Tooltip("Whether this object may be swapped right now. Turn it off from a puzzle to lock the object in place.")]
        [SerializeField] private bool canSwap = true;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Draw the measured swap bounds when this object is selected.")]
        [SerializeField] private bool drawBounds = true;

        private Collider[] colliders;
        private Rigidbody body;
        private CharacterController characterController;

        private bool frozen;
        private bool controllerWasEnabled;
        private bool bodyWasDynamic;
        private Vector3 frozenLinearVelocity;
        private Vector3 frozenAngularVelocity;

        //==================== OUTPUTS =====================
        public bool CanSwap
        {
            get => canSwap;
            set => canSwap = value;
        }

        public Vector3 Position => transform.position;

        /// <summary>Combined world bounds of all solid colliders. Falls back to a point at the position.</summary>
        public Bounds WorldBounds
        {
            get
            {
                CacheComponents();

                bool found = false;
                Bounds combined = new Bounds(transform.position, Vector3.zero);
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] == null || colliders[i].isTrigger || !colliders[i].enabled)
                    {
                        continue;
                    }

                    if (!found)
                    {
                        combined = colliders[i].bounds;
                        found = true;
                    }
                    else
                    {
                        combined.Encapsulate(colliders[i].bounds);
                    }
                }

                return combined;
            }
        }

        public Vector3 Center => WorldBounds.center;

        /// <summary>
        /// The point this object stands on: the middle of the bottom of its bounds. Swapping aligns
        /// these points, so a tall player and a short crate each end up standing on the other's floor
        /// level instead of sinking into it.
        /// </summary>
        public Vector3 Anchor
        {
            get
            {
                Bounds bounds = WorldBounds;
                return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }
        }

        public bool IsFrozen => frozen;

        //==================== PUBLIC API =====================
        /// <summary>
        /// Holds this object still for an animation: the character controller is switched off and a
        /// dynamic Rigidbody becomes kinematic, with its velocity remembered for Unfreeze.
        /// </summary>
        public void Freeze()
        {
            CacheComponents();

            if (frozen)
            {
                return;
            }

            frozen = true;

            controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled)
            {
                characterController.enabled = false;
            }

            bodyWasDynamic = body != null && !body.isKinematic;
            if (bodyWasDynamic)
            {
                frozenLinearVelocity = body.linearVelocity;
                frozenAngularVelocity = body.angularVelocity;
                body.isKinematic = true;
            }
        }

        /// <summary>Releases a Freeze and gives a dynamic Rigidbody back the velocity it had.</summary>
        public void Unfreeze()
        {
            if (!frozen)
            {
                return;
            }

            frozen = false;

            if (controllerWasEnabled)
            {
                characterController.enabled = true;
            }

            if (bodyWasDynamic)
            {
                body.isKinematic = false;
                body.linearVelocity = frozenLinearVelocity;
                body.angularVelocity = frozenAngularVelocity;
            }
        }

        /// <summary>Moves this object to a world position and removes all of its momentum.</summary>
        public void MoveTo(Vector3 position)
        {
            MoveTo(position, false);
        }

        /// <summary>Moves this object to a world position, optionally keeping the momentum it had.</summary>
        public void MoveTo(Vector3 position, bool keepMomentum)
        {
            CacheComponents();

            // A CharacterController pushes the transform back to its own position unless it is off.
            bool hadController = characterController != null && characterController.enabled;
            if (hadController)
            {
                characterController.enabled = false;
            }

            transform.position = position;

            if (hadController)
            {
                characterController.enabled = true;
            }

            if (body != null)
            {
                body.position = position;
            }

            if (keepMomentum)
            {
                return;
            }

            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            IMotionResettable[] motion = GetComponents<IMotionResettable>();
            for (int i = 0; i < motion.Length; i++)
            {
                motion[i].ResetMotion();
            }
        }

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            CacheComponents();
        }

        //==================== PRIVATE =====================
        private void CacheComponents()
        {
            if (colliders == null)
            {
                colliders = GetComponentsInChildren<Collider>();
            }

            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawBounds)
            {
                return;
            }

            // In edit mode the cache may be stale, so measure fresh each time.
            colliders = null;
            Bounds bounds = WorldBounds;
            colliders = null;

            Gizmos.color = canSwap ? Color.cyan : Color.gray;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}

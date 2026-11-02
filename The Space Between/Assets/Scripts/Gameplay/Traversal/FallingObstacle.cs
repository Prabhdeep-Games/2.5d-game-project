using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// One falling obstacle. It falls straight down at a constant speed, so its path and timing are always the same
    /// and the player can learn them. It is solid while it falls, so it blocks the player, and its TraversalHazard
    /// sends the player back when they touch it. Once it is below its destroy height it removes itself, so obstacles
    /// never pile up during a long test.
    ///
    /// A FallingObstacleSpawner creates it and calls Launch. Put the prefab together from: this component, a
    /// kinematic Rigidbody, a solid Box Collider, a slightly larger trigger Box Collider and a TraversalHazard.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FallingObstacle : MonoBehaviour
    {
        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Current falling speed, in units per second. Set by the spawner.")]
        [ReadOnly, SerializeField] private float fallSpeed;

        [Tooltip("World height below which this obstacle destroys itself. Set by the spawner.")]
        [ReadOnly, SerializeField] private float destroyBelowY;

        private Rigidbody body;
        private bool launched;

        public float FallSpeed => fallSpeed;

        public float DestroyBelowY => destroyBelowY;

        //==================== PUBLIC API =====================
        /// <summary>Starts the fall. The obstacle destroys itself once it is below the given world height.</summary>
        public void Launch(float speed, float destroyHeight, RoomReset room)
        {
            fallSpeed = speed;
            destroyBelowY = destroyHeight;
            launched = true;

            TraversalHazard hazard = GetComponent<TraversalHazard>();
            if (hazard != null)
            {
                hazard.Room = room;
            }
        }

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            body = GetComponent<Rigidbody>();

            // Moved by script, never pushed by physics, so the fall cannot be knocked off its path.
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void FixedUpdate()
        {
            if (!launched)
            {
                return;
            }

            Vector3 next = body.position + Vector3.down * (fallSpeed * Time.fixedDeltaTime);
            body.MovePosition(next);

            if (next.y < destroyBelowY)
            {
                Destroy(gameObject);
            }
        }
    }
}

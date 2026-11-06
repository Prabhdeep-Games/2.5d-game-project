using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Drops a falling obstacle from this object's position on a fixed schedule: the first after the initial delay,
    /// then one every interval. The schedule never drifts and never varies, so the player can watch a few drops,
    /// learn the rhythm and choose when to cross.
    ///
    /// Each obstacle falls straight down and destroys itself below Destroy Height. Place this object above the route,
    /// over a gap in the floor, so the obstacles fall through the route and on into the pit below it.
    /// </summary>
    public class FallingObstacleSpawner : MonoBehaviour
    {
        //==================== OBSTACLE =====================
        [Header("Obstacle")]
        [Tooltip("The obstacle prefab to drop. It needs a FallingObstacle component.")]
        [SerializeField] private FallingObstacle obstaclePrefab;

        [Tooltip("The room that is reset when an obstacle hits the player. Empty uses the Room Reset whose area contains this spawner.")]
        [SerializeField] private RoomReset room;

        //==================== TIMING =====================
        [Header("Timing")]
        [Tooltip("Seconds after the scene starts before the first obstacle drops.")]
        [SerializeField, Min(0f)] private float initialDelay = 1f;

        [Tooltip("Time in seconds between consecutive falling obstacle drops.")]
        [SerializeField, Min(0.1f)] private float interval = 2.5f;

        //==================== FALL =====================
        [Header("Fall")]
        [Tooltip("How fast each obstacle falls, in units per second. Constant, so the timing is easy to read.")]
        [SerializeField, Min(0.1f)] private float fallSpeed = 8f;

        [Tooltip("World height below which an obstacle is destroyed. Put it below the route, at the bottom of the pit, so an obstacle is never removed while it is still part of the challenge.")]
        [SerializeField] private float destroyHeight;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each drop to the Console.")]
        [SerializeField] private bool logDrops;

        [Tooltip("Draw the fall path and the destroy height in the Scene view.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("How many obstacles have been dropped since the scene started.")]
        [ReadOnly, SerializeField] private int dropped;

        [Tooltip("How many obstacles exist right now. It should stay small: each one is destroyed below Destroy Height.")]
        [ReadOnly, SerializeField] private int activeCount;

        private readonly List<FallingObstacle> active = new List<FallingObstacle>();
        private float nextDropTime;

        public float Interval => interval;

        public float InitialDelay => initialDelay;

        public float DestroyHeight => destroyHeight;

        public int Dropped => dropped;

        /// <summary>How many of this spawner's obstacles exist right now.</summary>
        public int ActiveCount => activeCount;

        //==================== LIFECYCLE =====================
        private void Start()
        {
            if (obstaclePrefab == null)
            {
                Debug.LogWarning($"[{nameof(FallingObstacleSpawner)}] '{name}' has no Obstacle Prefab, so nothing drops.", this);
                enabled = false;
                return;
            }

            if (room == null)
            {
                room = RoomReset.FindContaining(transform.position);
            }

            nextDropTime = Time.time + initialDelay;
        }

        private void Update()
        {
            // Advancing by the interval, rather than from now, keeps the rhythm exact even after a slow frame.
            while (Time.time >= nextDropTime)
            {
                Drop();
                nextDropTime += interval;
            }

            active.RemoveAll(obstacle => obstacle == null);
            activeCount = active.Count;
        }

        //==================== DROP =====================
        private void Drop()
        {
            FallingObstacle obstacle = Instantiate(obstaclePrefab, transform.position, transform.rotation);
            obstacle.Launch(fallSpeed, destroyHeight, room);
            active.Add(obstacle);
            dropped++;

            if (logDrops)
            {
                Debug.Log($"[{nameof(FallingObstacleSpawner)}] '{name}' dropped obstacle {dropped} at {Time.time:F2} s.", this);
            }
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmos()
        {
            if (!GeneratedRoom.ShowsGizmos(this))
            {
                return;
            }

            if (!drawGizmos)
            {
                return;
            }

            // The fall path, and a red line where obstacles are destroyed.
            Vector3 start = transform.position;
            Vector3 end = new Vector3(start.x, destroyHeight, start.z);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireCube(start, Vector3.one);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(end + Vector3.left * 2f, end + Vector3.right * 2f);
        }
    }
}

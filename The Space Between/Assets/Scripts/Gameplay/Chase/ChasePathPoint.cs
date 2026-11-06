using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// One point on the route the ChaseTarget flees along. The target visits every point in the scene in order of distance
    /// along the route (X, then height). It glides to an ordinary point and blinks to one marked Blink, which is how it
    /// crosses gaps, glass and climbs the player has to work for. The Room Generator places these for a chase room.
    /// </summary>
    public class ChasePathPoint : MonoBehaviour
    {
        //==================== POINT =====================
        [Header("Point")]
        [Tooltip("The target reaches this point by blinking from the previous one instead of gliding there.")]
        [SerializeField] private bool blink;

        [Tooltip("The first point of a room. When that room is reset, the target goes back to it.")]
        [SerializeField] private bool roomStart;

        public bool Blink => blink;

        public bool RoomStart => roomStart;

        /// <summary>Sets both flags. The Room Generator uses it when it places a point.</summary>
        public void Configure(bool isBlink, bool isRoomStart)
        {
            blink = isBlink;
            roomStart = isRoomStart;
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmos()
        {
            if (!GeneratedRoom.ShowsGizmos(this))
            {
                return;
            }

            Gizmos.color = blink ? new Color(0.66f, 0.86f, 0.88f) : new Color(0.71f, 0.44f, 0.26f);
            Gizmos.DrawWireSphere(transform.position, roomStart ? 0.45f : 0.3f);
        }
    }
}

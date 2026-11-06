using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The runaway displaced object the player chases. It follows every ChasePathPoint in the scene in order, keeping ahead
    /// of the player: it hurries when the player closes in and waits, turning back, when the player falls behind, so it never
    /// leaves the frame. It glides between ordinary points and blinks to Blink points with the swap disruption, which is how
    /// it crosses what the player has to climb, jump or swap past. At the last point it is trapped and can be caught.
    ///
    /// It is moved directly, never by physics, so it cannot get stuck, and it has no collider, so it never blocks the
    /// player or a swap. When a room is reset, it goes back to that room's first point.
    /// </summary>
    public class ChaseTarget : MonoBehaviour
    {
        private const float ArriveDistance = 0.05f;
        private const float GhostScale = 1.08f;

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The visible object. It wobbles and turns, while this object follows the path.")]
        [SerializeField] private Transform body;

        [Tooltip("Material of the brief outline left where the target blinks from and to. Use the swap contour, so the two read as the same phenomenon.")]
        [SerializeField] private Material blinkMaterial;

        //==================== LEAD =====================
        [Header("Lead")]
        [Tooltip("Closest the player may get along the route, in metres, before the target hurries.")]
        [SerializeField, Min(0f)] private float minLead = 6f;

        [Tooltip("Furthest ahead the target goes, in metres. Beyond this it waits for the player.")]
        [SerializeField, Min(1f)] private float maxLead = 14f;

        [Tooltip("Speed while the player keeps pace, in units per second. Just under the player's run speed of 10, so a running player closes in.")]
        [SerializeField, Min(0f)] private float cruiseSpeed = 9f;

        [Tooltip("Speed while the player is too close, in units per second. Above the run speed, so the target can only be caught at the end.")]
        [SerializeField, Min(0f)] private float hurrySpeed = 13f;

        //==================== BLINK =====================
        [Header("Blink")]
        [Tooltip("The target blinks to the next Blink point once the player is this close behind it, in metres.")]
        [SerializeField, Min(0f)] private float blinkWhenWithin = 9f;

        [Tooltip("Seconds the target is gone between leaving one point and appearing at the next.")]
        [SerializeField, Min(0f)] private float blinkSeconds = 0.12f;

        //==================== CATCH =====================
        [Header("Catch")]
        [Tooltip("How close the player has to get to the trapped target to catch it, in metres.")]
        [SerializeField, Min(0.1f)] private float catchDistance = 1.6f;

        //==================== MOTION =====================
        [Header("Motion")]
        [Tooltip("How far the body twitches out of place, in world units. It never quite sits right in space.")]
        [SerializeField, Min(0f)] private float displacement = 0.06f;

        [Tooltip("How fast the body turns, in degrees per second.")]
        [SerializeField] private float spinSpeed = 35f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each blink, room reset and the catch to the Console.")]
        [SerializeField] private bool logChase;

        [Tooltip("Index of the point the target is heading for.")]
        [ReadOnly, SerializeField] private int nextIndex;

        [Tooltip("How far ahead of the player the target is along the route, in metres.")]
        [ReadOnly, SerializeField] private float lead;

        [Tooltip("What the target is doing right now.")]
        [ReadOnly, SerializeField] private string state = "waiting";

        private readonly List<ChasePathPoint> points = new List<ChasePathPoint>();
        private readonly List<RoomReset> rooms = new List<RoomReset>();
        private readonly List<GameObject> ghosts = new List<GameObject>();
        private float blinkEndsAt = -1f;
        private float nextTwitch;
        private bool caught;

        /// <summary>Raised once, when the player catches the trapped target.</summary>
        public event Action Caught;

        public bool IsCaught => caught;

        /// <summary>True once the target has reached the last point of the route.</summary>
        public bool IsTrapped => points.Count > 0 && nextIndex >= points.Count;

        public int NextIndex => nextIndex;

        public int PointCount => points.Count;

        public float Lead => lead;

        public string State => state;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }
        }

        private void Start()
        {
            CollectPoints();
            foreach (RoomReset room in FindObjectsByType<RoomReset>(FindObjectsSortMode.None))
            {
                RoomReset captured = room;
                rooms.Add(room);
                room.WasReset += () => OnRoomReset(captured);
            }

            if (points.Count == 0)
            {
                Debug.LogWarning($"[{nameof(ChaseTarget)}] '{name}' found no ChasePathPoint, so it stays where it is.", this);
                return;
            }

            transform.position = points[0].transform.position;
            nextIndex = 1;
        }

        private void Update()
        {
            if (player == null || points.Count == 0)
            {
                return;
            }

            lead = transform.position.x - player.transform.position.x;
            AnimateBody();

            if (blinkEndsAt > 0f)
            {
                FinishBlinkWhenDue();
                return;
            }

            if (IsTrapped)
            {
                CheckCatch();
                return;
            }

            ChasePathPoint next = points[nextIndex];
            if (next.Blink)
            {
                TryBlink(next);
            }
            else
            {
                Glide(next);
            }
        }

        //==================== ROUTE =====================
        // Every point in the scene, in order along the route: left to right, and upward where a point sits above another.
        private void CollectPoints()
        {
            points.Clear();
            points.AddRange(FindObjectsByType<ChasePathPoint>(FindObjectsSortMode.None));
            points.Sort((a, b) =>
            {
                Vector3 pa = a.transform.position;
                Vector3 pb = b.transform.position;
                return Mathf.Approximately(pa.x, pb.x) ? pa.y.CompareTo(pb.y) : pa.x.CompareTo(pb.x);
            });
        }

        private float ChooseSpeed()
        {
            if (lead > maxLead)
            {
                state = "waiting";
                return 0f;
            }

            if (lead < minLead)
            {
                state = "hurrying";
                return hurrySpeed;
            }

            state = "fleeing";
            return cruiseSpeed;
        }

        private void Glide(ChasePathPoint next)
        {
            float speed = ChooseSpeed();
            Vector3 goal = next.transform.position;
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);
            if (Vector3.Distance(transform.position, goal) <= ArriveDistance)
            {
                nextIndex++;
            }
        }

        // It waits at the edge of a gap or a climb until the player is close behind, then is simply elsewhere.
        private void TryBlink(ChasePathPoint next)
        {
            if (lead > blinkWhenWithin)
            {
                state = "waiting to blink";
                return;
            }

            LeaveGhost(transform.position);
            transform.position = next.transform.position;
            LeaveGhost(transform.position);
            SetBodyVisible(false);
            blinkEndsAt = Time.time + blinkSeconds;
            nextIndex++;
            state = "blinking";
            if (logChase)
            {
                Debug.Log($"[{nameof(ChaseTarget)}] blinked to point {nextIndex - 1} at {transform.position}.", this);
            }
        }

        private void FinishBlinkWhenDue()
        {
            if (Time.time < blinkEndsAt)
            {
                return;
            }

            blinkEndsAt = -1f;
            SetBodyVisible(true);
            ClearGhosts();
        }

        private void CheckCatch()
        {
            state = caught ? "caught" : "trapped";
            if (caught || Vector3.Distance(player.transform.position, transform.position) > catchDistance)
            {
                return;
            }

            caught = true;
            state = "caught";
            Caught?.Invoke();
            if (logChase)
            {
                Debug.Log($"[{nameof(ChaseTarget)}] caught at {Time.timeSinceLevelLoad:F1} s.", this);
            }
        }

        // A reset sends the target back to the first point of that room, so the room is retried with it ahead again.
        private void OnRoomReset(RoomReset room)
        {
            if (caught || points.Count == 0)
            {
                return;
            }

            Bounds area = room.Area;
            for (int i = 0; i < points.Count; i++)
            {
                if (area.Contains(points[i].transform.position))
                {
                    transform.position = points[i].transform.position;
                    nextIndex = Mathf.Min(i + 1, points.Count);
                    blinkEndsAt = -1f;
                    SetBodyVisible(true);
                    ClearGhosts();
                    if (logChase)
                    {
                        Debug.Log($"[{nameof(ChaseTarget)}] '{room.name}' was reset; back to point {i}.", this);
                    }

                    return;
                }
            }
        }

        //==================== LOOK =====================
        // Small snaps out of place a few times a second read as an object that does not sit properly in space.
        private void AnimateBody()
        {
            if (body == null)
            {
                return;
            }

            body.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
            if (Time.time >= nextTwitch)
            {
                nextTwitch = Time.time + UnityEngine.Random.Range(0.08f, 0.3f);
                body.localPosition = UnityEngine.Random.insideUnitSphere * displacement;
            }
        }

        private void SetBodyVisible(bool visible)
        {
            if (body != null)
            {
                body.gameObject.SetActive(visible);
            }
        }

        private void LeaveGhost(Vector3 position)
        {
            if (body == null || blinkMaterial == null)
            {
                return;
            }

            MeshFilter source = body.GetComponent<MeshFilter>();
            if (source == null || source.sharedMesh == null)
            {
                return;
            }

            var ghost = new GameObject("Blink Outline");
            ghost.transform.SetPositionAndRotation(position, body.rotation);
            ghost.transform.localScale = body.lossyScale * GhostScale;
            ghost.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            ghost.AddComponent<MeshRenderer>().sharedMaterial = blinkMaterial;
            ghosts.Add(ghost);
        }

        private void ClearGhosts()
        {
            foreach (GameObject ghost in ghosts)
            {
                if (ghost != null)
                {
                    Destroy(ghost);
                }
            }

            ghosts.Clear();
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmos()
        {
            if (!GeneratedRoom.ShowsGizmos(this))
            {
                return;
            }

            Gizmos.color = new Color(0.71f, 0.44f, 0.26f);
            Gizmos.DrawWireSphere(transform.position, catchDistance);
        }
    }
}

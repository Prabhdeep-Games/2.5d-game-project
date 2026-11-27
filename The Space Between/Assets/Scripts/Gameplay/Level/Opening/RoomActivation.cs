using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Switches rooms the player has left behind off, and back on when they return, so a long level only keeps the
    /// rooms in play around the player active: their meshes, colliders, lights and everything in them.
    ///
    /// Rooms are listed in route order. A room is switched off once the player is past the middle of the room after it,
    /// and only while no camera can see it. It is switched back on as soon as the player comes back to within Return
    /// Margin of that next room's entrance, or the moment the camera's view comes near it, so it is always whole before it
    /// can be seen or stood on. The gap between the two points means standing near a boundary never flickers a room.
    ///
    /// Nothing is destroyed: a room keeps its state while off (crates, doors, Room Resets), and comes back as it was left.
    /// It works from where the player is every frame, so a restart, a section key or Continue is handled the same way. It
    /// only acts in Play Mode; the saved scene always has every room on. Keep sequence objects, cameras and triggers that
    /// must keep running outside the listed rooms.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class RoomActivation : MonoBehaviour
    {
        //==================== ROOMS =====================
        [Header("Rooms")]
        [Tooltip("The rooms, in route order along X. Each is switched on and off as a whole.")]
        [SerializeField] private List<GameObject> rooms = new List<GameObject>();

        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private Transform player;

        [Tooltip("How far back towards a room the player may come before it is switched on again, measured from the next room's entrance, in units.")]
        [SerializeField, Min(0f)] private float returnMargin = 4f;

        [Tooltip("How far beyond what the camera sees a room still counts as visible, in units, so it is never seen appearing.")]
        [SerializeField, Min(0f)] private float viewMargin = 6f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each switch to the Console.")]
        [SerializeField] private bool logChanges;

        [ReadOnly, SerializeField] private int activeRooms;

        private readonly List<Bounds> bounds = new List<Bounds>();
        private readonly Plane[] planes = new Plane[6];
        private Camera view;

        public IReadOnlyList<GameObject> Rooms => rooms;

        /// <summary>How many times a room has been switched off or on, for tests.</summary>
        public int Switches { get; private set; }

        private void Awake()
        {
            if (player == null)
            {
                PlaceholderPlayerController controller = FindAnyObjectByType<PlaceholderPlayerController>();
                player = controller != null ? controller.transform : null;
            }

            // Each room's extent is read once, while every room is still on.
            bounds.Clear();
            foreach (GameObject room in rooms)
            {
                bounds.Add(Measure(room));
            }
        }

        private void Update()
        {
            if (player == null || rooms.Count < 2)
            {
                return;
            }

            if (view == null)
            {
                view = Camera.main;
            }

            bool hasView = view != null;
            if (hasView)
            {
                GeometryUtility.CalculateFrustumPlanes(view, planes);
            }

            float x = player.position.x;
            activeRooms = 0;
            for (int i = 0; i < rooms.Count; i++)
            {
                GameObject room = rooms[i];
                if (room == null)
                {
                    continue;
                }

                bool active = room.activeSelf;
                if (i < rooms.Count - 1)
                {
                    Bounds next = bounds[i + 1];
                    bool seen = hasView && GeometryUtility.TestPlanesAABB(planes, Expanded(bounds[i]));
                    if (active && x >= next.center.x && !seen && !Contains(bounds[i], x))
                    {
                        Switch(room, false);
                    }
                    else if (!active && (x < next.min.x + returnMargin || seen))
                    {
                        Switch(room, true);
                    }
                }
                else if (!active)
                {
                    Switch(room, true);
                }

                if (room.activeSelf)
                {
                    activeRooms++;
                }
            }
        }

        /// <summary>Switches every listed room back on, for example before a sequence that needs the whole level.</summary>
        public void ShowAll()
        {
            foreach (GameObject room in rooms)
            {
                if (room != null && !room.activeSelf)
                {
                    room.SetActive(true);
                }
            }
        }

        private void Switch(GameObject room, bool on)
        {
            room.SetActive(on);
            Switches++;
            if (logChanges)
            {
                Debug.Log($"[{nameof(RoomActivation)}] {room.name} {(on ? "on" : "off")} (player at x {player.position.x:0.0}).", this);
            }
        }

        private Bounds Expanded(Bounds b)
        {
            b.Expand(viewMargin * 2f);
            return b;
        }

        private static bool Contains(Bounds b, float x) => x >= b.min.x && x <= b.max.x;

        private static Bounds Measure(GameObject room)
        {
            var b = new Bounds(room.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
            {
                if (!any)
                {
                    b = r.bounds;
                    any = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            return b;
        }

        /// <summary>A room's measured extent, for tests and gizmos.</summary>
        public Bounds BoundsOf(int index) => bounds[index];
    }
}

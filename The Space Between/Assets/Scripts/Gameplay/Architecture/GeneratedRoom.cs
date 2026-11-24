using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Marks a room made by the Procedural Room Generator. It remembers what the generator built, so regenerating or clearing
    /// removes exactly those objects, and the gameplay elements placed in it, which are never rebuilt. Changing a value rebuilds
    /// the room straight away in the Editor; in a built game this does nothing.
    ///
    /// A room can follow another: its entrance sits at that room's exit. When the room it follows changes size, the generator
    /// moves this room (only its position) so the two stay joined, and the rooms after it in turn.
    /// </summary>
    [DisallowMultipleComponent]
    public class GeneratedRoom : MonoBehaviour
    {
        private const float MarkerRadius = 0.4f;
        private const float MarkerHeight = 1f;
        private static readonly Color RouteColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color ConnectedRouteColor = new Color(1f, 1f, 1f, 0.35f);

        /// <summary>The scene-wide lighting as it was before a room changed it.</summary>
        [Serializable]
        public class GlobalLightingSnapshot
        {
            public UnityEngine.Rendering.AmbientMode ambientMode;
            public Color ambientColor;
            public bool fog;
            public Color fogColor;
            public float fogDensity;
            public FogMode fogMode;
            public float fogStart;
            public float fogEnd;
        }

        /// <summary>The child that holds the room's elements. Anything inside it is an element and is never rebuilt.</summary>
        public const string ElementsGroupName = "Elements";

        //==================== SETTINGS =====================
        [Header("Settings")]
        [Tooltip("The values this room is built from. Change one and the room rebuilds straight away.")]
        [SerializeField] private RoomSettings settings = new RoomSettings();

        //==================== CONNECTION =====================
        [Header("Connection")]
        [Tooltip("The room this one starts after: its entrance sits at that room's exit. When that room changes size, this room moves with it (its position only) so the two stay joined. Empty for a room placed on its own. Set when a room is generated at another room's exit, or by Link Rooms in Scene.")]
        [SerializeField] private GeneratedRoom follows;

        //==================== RECORD =====================
        [Header("Record")]
        [Tooltip("Where the exit floor is, relative to this object. A room added after this one starts here.")]
        [ReadOnly, SerializeField] private Vector3 exitOffset;

        [Tooltip("Every object the generator made for this room. Only these are removed when it is regenerated or cleared.")]
        [ReadOnly, SerializeField] private List<GameObject> generatedObjects = new List<GameObject>();

        [Tooltip("Whether this room changed the scene-wide lighting, so clearing it knows to put the earlier values back.")]
        [ReadOnly, SerializeField] private bool changedGlobalLighting;

        [SerializeField, HideInInspector] private GlobalLightingSnapshot globalLightingBefore = new GlobalLightingSnapshot();

        // The values the current contents were built from. When they differ from the values above, the room needs rebuilding.
        [SerializeField, HideInInspector] private string builtSignature = string.Empty;

        [Tooltip("Whether Clear Generated Pieces has emptied this room. Rebuild Room, or changing a value, builds it again.")]
        [ReadOnly, SerializeField] private bool isCleared;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Draw this room's gizmos: the entrance (green) and exit (red) of the route while it is selected, and the gizmos of every object inside it. Off hides them all, even when something in the room is selected.")]
        [SerializeField] private bool drawGizmos = true;

        public RoomSettings Settings => settings;

        public Vector3 ExitOffset => exitOffset;

        /// <summary>The room this one starts after, or null.</summary>
        public GeneratedRoom Follows => follows;

        /// <summary>Where the exit floor is in world space, turned with the room. A room that follows this one starts here.</summary>
        public Vector3 ExitPoint => transform.position + transform.rotation * exitOffset;

        public IReadOnlyList<GameObject> GeneratedObjects => generatedObjects;

        public bool ChangedGlobalLighting => changedGlobalLighting;

        public GlobalLightingSnapshot GlobalLightingBefore => globalLightingBefore;

        public bool IsCleared => isCleared;

        public string BuiltSignature => builtSignature;

        public bool DrawGizmos => drawGizmos;

        /// <summary>
        /// Whether a component may draw its gizmos. Outside generated rooms always. Inside one, only while that room or something
        /// in it is selected, and never when its Draw Gizmos is off, so the Scene view stays clean while other things are edited.
        /// Every gizmo method in the project checks this first.
        /// </summary>
        public static bool ShowsGizmos(Component component)
        {
            GeneratedRoom room = component.GetComponentInParent<GeneratedRoom>(true);
            if (room == null)
            {
                return true;
            }

            if (!room.drawGizmos)
            {
                return false;
            }

#if UNITY_EDITOR
            return IsSelected(room);
#else
            return true;
#endif
        }

#if UNITY_EDITOR
        // The rooms that hold the current selection, worked out once per selection change rather than for every gizmo.
        private static readonly HashSet<GeneratedRoom> SelectedRooms = new HashSet<GeneratedRoom>();
        private static bool selectionChanged = true;
        private static int selectionKey;

        [UnityEditor.InitializeOnLoadMethod]
        private static void WatchSelection()
        {
            UnityEditor.Selection.selectionChanged += () => selectionChanged = true;
        }

        /// <summary>Whether this room, or anything inside it, is selected.</summary>
        public static bool IsSelected(GeneratedRoom room)
        {
            // The event can arrive after the selection is set from code, so a change of the active object also counts.
            int key = UnityEditor.Selection.activeInstanceID * 31 + UnityEditor.Selection.count;
            if (selectionChanged || key != selectionKey)
            {
                selectionKey = key;
                SelectedRooms.Clear();
                foreach (Transform selected in UnityEditor.Selection.transforms)
                {
                    GeneratedRoom holder = selected.GetComponentInParent<GeneratedRoom>(true);
                    if (holder != null)
                    {
                        SelectedRooms.Add(holder);
                    }
                }

                selectionChanged = false;
            }

            return room != null && SelectedRooms.Contains(room);
        }
#endif

        /// <summary>
        /// The room's gameplay elements: the objects in its Elements child, in order. They are ordinary objects, so moving,
        /// deleting or adding one by hand is picked up here, and Undo needs no extra record.
        /// </summary>
        public List<GameObject> FindElements()
        {
            var elements = new List<GameObject>();
            Transform group = transform.Find(ElementsGroupName);
            if (group != null)
            {
                foreach (Transform child in group)
                {
                    elements.Add(child.gameObject);
                }
            }

            return elements;
        }

        /// <summary>What the current values come to. It equals <see cref="BuiltSignature"/> when the contents are up to date.</summary>
        public string CurrentSignature => JsonUtility.ToJson(settings) + isCleared;

#if UNITY_EDITOR
        /// <summary>Raised when a value may have changed in the Inspector or through Undo. The generator listens and rebuilds.</summary>
        public static event Action<GeneratedRoom> ValuesChanged;

        private void OnValidate()
        {
            ValuesChanged?.Invoke(this);
        }
#endif

        /// <summary>Replaces the values. Called by the generator.</summary>
        public void SetSettings(RoomSettings newSettings)
        {
            settings = newSettings;
        }

        /// <summary>Sets whether the room has been emptied. Called by the generator.</summary>
        public void SetCleared(bool cleared)
        {
            isCleared = cleared;
        }

        /// <summary>Sets the room this one starts after. Called by the generator.</summary>
        public void SetFollows(GeneratedRoom room)
        {
            follows = room;
        }

        /// <summary>Stores the result of a build. Called by the generator.</summary>
        public void RecordBuild(Vector3 newExitOffset, List<GameObject> objects)
        {
            exitOffset = newExitOffset;
            generatedObjects = objects;
            builtSignature = CurrentSignature;
        }

        /// <summary>Accepts the current contents as up to date, for a room saved before signatures existed. Called by the generator.</summary>
        public void AdoptCurrentValues()
        {
            builtSignature = CurrentSignature;
        }

        /// <summary>Stores the scene lighting from before this room changed it. Called by the generator.</summary>
        public void RecordGlobalLighting(GlobalLightingSnapshot before)
        {
            globalLightingBefore = before;
            changedGlobalLighting = true;
        }

        /// <summary>Forgets the stored scene lighting after it has been put back. Called by the generator.</summary>
        public void ForgetGlobalLighting()
        {
            changedGlobalLighting = false;
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!ShowsGizmos(this))
            {
                return;
            }

            Vector3 up = Vector3.up * MarkerHeight;
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(transform.position + up, MarkerRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(ExitPoint + up, MarkerRadius);

            // The route through this room, and the connected rooms on either side: the one it follows and those following it.
            Gizmos.color = RouteColor;
            Gizmos.DrawLine(transform.position + up, ExitPoint + up);
            Gizmos.color = ConnectedRouteColor;
            if (follows != null)
            {
                Gizmos.DrawLine(follows.transform.position + up, follows.ExitPoint + up);
            }

            foreach (GeneratedRoom other in FindObjectsByType<GeneratedRoom>(FindObjectsSortMode.None))
            {
                if (other.follows == this)
                {
                    Gizmos.DrawLine(other.transform.position + up, other.ExitPoint + up);
                    Gizmos.DrawWireSphere(other.ExitPoint + up, MarkerRadius);
                }
            }
        }
    }
}

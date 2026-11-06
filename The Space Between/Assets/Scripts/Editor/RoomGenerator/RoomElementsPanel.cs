using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The Elements section of the Room Generator window and the GeneratedRoom Inspector: add gameplay prefabs to a room by
    /// category, then move and configure them. Elements are ordinary prefab instances, so the Inspector and the Scene view
    /// handles work on them too.
    /// </summary>
    [System.Serializable]
    public sealed class RoomElementsPanel
    {
        private const float SpacingBetweenNewElements = 2f;
        private const float SameSpotDistance = 1.5f;
        private const float RowButtonWidth = 52f;

        [SerializeField] private List<bool> openCategories = new List<bool>();
        [SerializeField] private bool showPlaced = true;
        [SerializeField] private GameObject otherPrefab;

        // The window selects a new element so it can be moved straight away. The room's Inspector keeps the room selected
        // and highlights the element in the Hierarchy instead, so the Inspector does not switch away from the room.
        private readonly bool keepRoomSelected;

        public RoomElementsPanel() : this(false)
        {
        }

        public RoomElementsPanel(bool keepRoomSelectedOnAdd)
        {
            keepRoomSelected = keepRoomSelectedOnAdd;
        }

        /// <summary>Draws the panel for a room. A puzzle level lists the puzzle categories first.</summary>
        public void Draw(GeneratedRoom room, bool isPuzzleLevel)
        {
            EditorGUILayout.LabelField("Elements", EditorStyles.boldLabel);
            if (room == null)
            {
                EditorGUILayout.HelpBox("Select or generate a room to add gameplay elements to it.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Adds at the Scene view's centre, or the middle of the room. Move it with the usual tools.", EditorStyles.wordWrappedMiniLabel);
            foreach (RoomElementCatalog.Category category in OrderedCategories(isPuzzleLevel))
            {
                DrawCategory(room, category);
            }

            DrawOtherPrefab(room);
            EditorGUILayout.Space();
            DrawPlaced(room);
        }

        private static IEnumerable<RoomElementCatalog.Category> OrderedCategories(bool isPuzzleLevel)
        {
            var all = (RoomElementCatalog.Category[])System.Enum.GetValues(typeof(RoomElementCatalog.Category));
            foreach (RoomElementCatalog.Category category in all)
            {
                if (RoomElementCatalog.IsPuzzleCategory(category) == isPuzzleLevel)
                {
                    yield return category;
                }
            }

            foreach (RoomElementCatalog.Category category in all)
            {
                if (RoomElementCatalog.IsPuzzleCategory(category) != isPuzzleLevel)
                {
                    yield return category;
                }
            }
        }

        private void DrawCategory(GeneratedRoom room, RoomElementCatalog.Category category)
        {
            int index = (int)category;
            while (openCategories.Count <= index)
            {
                openCategories.Add(false);
            }

            openCategories[index] = EditorGUILayout.Foldout(openCategories[index], RoomElementCatalog.Label(category), true);
            if (!openCategories[index])
            {
                return;
            }

            EditorGUI.indentLevel++;
            foreach (RoomElementCatalog.Entry entry in RoomElementCatalog.Entries)
            {
                if (entry.Category != category)
                {
                    continue;
                }

                GameObject prefab = RoomElementCatalog.Load(entry);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(entry.Name, entry.Description));
                    using (new EditorGUI.DisabledScope(prefab == null))
                    {
                        if (GUILayout.Button(new GUIContent(prefab != null ? "Add" : "Missing", prefab != null ? entry.Description : entry.Path), GUILayout.Width(RowButtonWidth)))
                        {
                            Add(room, prefab, entry);
                        }
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawOtherPrefab(GeneratedRoom room)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                otherPrefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Any Prefab", "Any prefab from the Project window, for content that is not listed above."), otherPrefab, typeof(GameObject), false);
                bool isPrefab = otherPrefab != null && PrefabUtility.IsPartOfPrefabAsset(otherPrefab);
                using (new EditorGUI.DisabledScope(!isPrefab))
                {
                    if (GUILayout.Button("Add", GUILayout.Width(RowButtonWidth)))
                    {
                        Add(room, otherPrefab, RoomElementCatalog.Find(otherPrefab));
                    }
                }
            }
        }

        private void DrawPlaced(GeneratedRoom room)
        {
            List<GameObject> elements = room.FindElements();
            showPlaced = EditorGUILayout.Foldout(showPlaced, $"In This Room ({elements.Count})", true);
            if (!showPlaced)
            {
                return;
            }

            if (elements.Count == 0)
            {
                EditorGUILayout.LabelField("No elements yet. Prefabs dragged into the room's Elements object count too.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            foreach (GameObject element in elements)
            {
                DrawElement(room, element);
            }
        }

        // Position and rotation are relative to the room, so a room can be moved without its arrangement changing.
        private static void DrawElement(GeneratedRoom room, GameObject instance)
        {
            Transform t = instance.transform;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(instance.name, EditorStyles.boldLabel);
                    if (GUILayout.Button("Select", GUILayout.Width(RowButtonWidth)))
                    {
                        Selection.activeGameObject = instance;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(RowButtonWidth + 8f)))
                    {
                        RoomBuilder.RemoveElement(room, instance);
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUI.BeginChangeCheck();
                Vector3 position = EditorGUILayout.Vector3Field("Position", room.transform.InverseTransformPoint(t.position));
                Vector3 rotation = EditorGUILayout.Vector3Field("Rotation", t.localEulerAngles);
                Vector3 scale = EditorGUILayout.Vector3Field("Scale", t.localScale);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(t, "Move Room Element");
                    t.position = room.transform.TransformPoint(position);
                    t.localEulerAngles = rotation;
                    t.localScale = scale;
                }
            }
        }

        private void Add(GeneratedRoom room, GameObject prefab, RoomElementCatalog.Entry entry)
        {
            GameObject instance = RoomBuilder.AddElement(room, prefab, FreeSpot(room, prefab, entry), Quaternion.identity);
            if (instance == null)
            {
                return;
            }

            if (keepRoomSelected)
            {
                EditorGUIUtility.PingObject(instance);
            }
            else
            {
                Selection.activeGameObject = instance;
            }
        }

        // Where the Scene view is looking, if that is inside the room, moved along until it does not land on another element.
        private static Vector3 FreeSpot(GeneratedRoom room, GameObject prefab, RoomElementCatalog.Entry entry)
        {
            float along = room.Settings.Width * 0.5f;
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                float local = room.transform.InverseTransformPoint(view.pivot).x;
                if (local > 0f && local < room.Settings.Width)
                {
                    along = local;
                }
            }

            List<GameObject> elements = room.FindElements();
            Vector3 spot = RoomBuilder.DefaultElementPosition(room, entry, prefab, along);
            for (int tries = 0; tries < elements.Count && IsTaken(room, elements, spot); tries++)
            {
                spot = RoomBuilder.DefaultElementPosition(room, entry, prefab, spot.x + SpacingBetweenNewElements);
            }

            return spot;
        }

        private static bool IsTaken(GeneratedRoom room, List<GameObject> elements, Vector3 spot)
        {
            foreach (GameObject element in elements)
            {
                if (Vector3.Distance(room.transform.InverseTransformPoint(element.transform.position), spot) < SameSpotDistance)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

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

        /// <summary>Draws the panel for a room.</summary>
        public void Draw(GeneratedRoom room)
        {
            if (room == null)
            {
                EditorGUILayout.HelpBox("Select or generate a room to add gameplay elements to it.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Add places the prefab at the Scene view's centre, or the middle of the room, on the floor or under the ceiling. Move it with the usual tools. Elements are never removed by Rebuild or Clear.", EditorStyles.wordWrappedMiniLabel);
            foreach (RoomElementCatalog.Category category in (RoomElementCatalog.Category[])System.Enum.GetValues(typeof(RoomElementCatalog.Category)))
            {
                DrawCategory(room, category);
            }

            DrawOtherPrefab(room);
            EditorGUILayout.Space();
            DrawPlaced(room);
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

        // Moving, rotating and configuring an element is done in the Scene view and the Inspector, like any object, so the list
        // only finds and removes elements, and says what an element still needs before it works.
        private static void DrawElement(GeneratedRoom room, GameObject instance)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(instance.name, "Select it to move it with the Scene view tools and change its values in the Inspector."));
                if (GUILayout.Button("Select", EditorStyles.miniButtonLeft, GUILayout.Width(RowButtonWidth)))
                {
                    Selection.activeGameObject = instance;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }

                if (GUILayout.Button("Remove", EditorStyles.miniButtonRight, GUILayout.Width(RowButtonWidth + 8f)))
                {
                    RoomBuilder.RemoveElement(room, instance);
                    GUIUtility.ExitGUI();
                }
            }

            foreach ((string text, MessageType type) in RoomElementCatalog.Advice(instance))
            {
                EditorGUILayout.HelpBox(text, type);
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

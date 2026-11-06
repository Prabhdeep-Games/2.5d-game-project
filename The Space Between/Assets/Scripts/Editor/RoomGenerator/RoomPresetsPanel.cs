using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The Custom Rooms part of the Room Generator window: save a room and its elements as a preset, and generate it again
    /// anywhere. This is what makes the Custom purpose useful: shape a room, place its elements, save it, reuse it.
    /// </summary>
    [System.Serializable]
    public sealed class RoomPresetsPanel
    {
        private const string PresetFolder = "Assets/Settings/Room Presets";
        private const float ButtonWidth = 64f;

        [SerializeField] private bool showPresets = true;

        private List<RoomPreset> presets;

        /// <summary>Finds the presets in the project again, for example after one was saved or deleted.</summary>
        public void Refresh()
        {
            presets = new List<RoomPreset>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(RoomPreset)))
            {
                var preset = AssetDatabase.LoadAssetAtPath<RoomPreset>(AssetDatabase.GUIDToAssetPath(guid));
                if (preset != null)
                {
                    presets.Add(preset);
                }
            }
        }

        /// <summary>
        /// Draws the panel. Generate makes a room at the given position; Load Values passes a preset's values back to the window.
        /// </summary>
        public void Draw(GeneratedRoom room, bool isCustom, Vector3 newRoomPosition, System.Action<RoomSettings> loadValues)
        {
            if (presets == null)
            {
                Refresh();
            }

            EditorGUILayout.LabelField("Custom Rooms", EditorStyles.boldLabel);
            if (isCustom)
            {
                EditorGUILayout.HelpBox("Custom keeps every value exactly as you set it. Shape the room above, add elements, then save it as a preset to generate it again anywhere.", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(room == null))
            {
                if (GUILayout.Button(new GUIContent("Save Selected Room As Preset...", "Saves the room's values and its elements, relative to the room, as an asset in " + PresetFolder + ".")))
                {
                    SaveAs(room);
                    GUIUtility.ExitGUI();
                }
            }

            presets.RemoveAll(preset => preset == null);
            showPresets = EditorGUILayout.Foldout(showPresets, $"Presets ({presets.Count})", true);
            if (!showPresets)
            {
                return;
            }

            if (presets.Count == 0)
            {
                EditorGUILayout.LabelField("No presets yet.", EditorStyles.miniLabel);
            }

            foreach (RoomPreset preset in presets)
            {
                DrawPreset(preset, room, newRoomPosition, loadValues);
            }
        }

        private static void DrawPreset(RoomPreset preset, GeneratedRoom room, Vector3 newRoomPosition, System.Action<RoomSettings> loadValues)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(preset.name, preset.Notes), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"{preset.Settings.Purpose}, {preset.Elements.Count} elements", EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    Vector3 position = room != null ? room.transform.position + room.ExitOffset : newRoomPosition;
                    if (GUILayout.Button(new GUIContent("Generate", "Make a new room from this preset, with its elements. With a room selected, it starts at that room's exit.")))
                    {
                        GeneratedRoom created = RoomBuilder.CreateRoomFromPreset(preset, position);
                        if (created != null)
                        {
                            Selection.activeGameObject = created.gameObject;
                        }

                        GUIUtility.ExitGUI();
                    }

                    using (new EditorGUI.DisabledScope(room == null))
                    {
                        if (GUILayout.Button(new GUIContent("Add Elements", "Add this preset's elements to the selected room, where they were in the preset.")))
                        {
                            RoomBuilder.AddPresetElements(room, preset);
                        }
                    }

                    if (GUILayout.Button(new GUIContent("Load Values", "Use this preset's values in the window. A selected room changes straight away."), GUILayout.Width(ButtonWidth + 16f)))
                    {
                        loadValues(preset.Settings.Clone());
                    }

                    if (GUILayout.Button("Show", GUILayout.Width(ButtonWidth)))
                    {
                        EditorGUIUtility.PingObject(preset);
                    }
                }
            }
        }

        private void SaveAs(GeneratedRoom room)
        {
            if (!Directory.Exists(PresetFolder))
            {
                Directory.CreateDirectory(PresetFolder);
                AssetDatabase.Refresh();
            }

            string path = EditorUtility.SaveFilePanelInProject("Save Room Preset", room.name, "asset", "Choose where to save the room preset.", PresetFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            RoomPreset preset = RoomBuilder.SavePreset(room, path);
            Refresh();
            EditorGUIUtility.PingObject(preset);
        }
    }
}

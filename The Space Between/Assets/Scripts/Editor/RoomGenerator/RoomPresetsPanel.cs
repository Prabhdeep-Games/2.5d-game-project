using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The Presets section of the Room Generator window: save a room and its elements as a preset, generate it again
    /// anywhere, load its values, and keep notes on what it is for. This is what makes the Custom purpose useful: shape a
    /// room, place its elements, save it, reuse it.
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
        /// Draws the panel. Generate makes a room at <paramref name="newRoomOrigin"/>; Load Values passes a preset's values
        /// back to the window.
        /// </summary>
        public void Draw(GeneratedRoom room, bool isCustom, Vector3 newRoomOrigin, System.Action<RoomSettings> loadValues)
        {
            if (presets == null)
            {
                Refresh();
            }

            EditorGUILayout.LabelField("A preset keeps the room's values and each element's prefab, position, rotation and scale. Changes made on an element in the scene, such as a Pressure Pad's Targets, are not saved.", EditorStyles.wordWrappedMiniLabel);
            if (isCustom)
            {
                EditorGUILayout.HelpBox("Custom keeps every value exactly as you set it. Shape the room, add elements, then save it as a preset to generate it again anywhere.", MessageType.None);
            }

            using (new EditorGUI.DisabledScope(room == null))
            {
                if (GUILayout.Button(new GUIContent("Save Selected Room As Preset...", "Saves the selected room's values and its elements as an asset in " + PresetFolder + ". Saving over an existing preset asks first.")))
                {
                    SaveAs(room);
                    GUIUtility.ExitGUI();
                }
            }

            presets.RemoveAll(preset => preset == null);
            showPresets = EditorGUILayout.Foldout(showPresets, $"Saved Presets ({presets.Count})", true);
            if (!showPresets)
            {
                return;
            }

            if (presets.Count == 0)
            {
                EditorGUILayout.LabelField("No presets yet. Select a room and save it.", EditorStyles.miniLabel);
            }

            foreach (RoomPreset preset in presets)
            {
                DrawPreset(preset, room, newRoomOrigin, loadValues);
            }
        }

        private void DrawPreset(RoomPreset preset, GeneratedRoom room, Vector3 newRoomOrigin, System.Action<RoomSettings> loadValues)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(preset.name, AssetDatabase.GetAssetPath(preset)), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"{preset.Settings.Purpose}, {preset.Elements.Count} elements", EditorStyles.miniLabel);
                }

                var serializedPreset = new SerializedObject(preset);
                serializedPreset.Update();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serializedPreset.FindProperty("notes"), new GUIContent("Notes", "What this arrangement is for. Saved with the preset."));
                if (EditorGUI.EndChangeCheck())
                {
                    serializedPreset.ApplyModifiedProperties();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Generate", "Make a new room from this preset, with its elements, at the New Room Placement in Room Setup.")))
                    {
                        GeneratedRoom created = RoomBuilder.CreateRoomFromPreset(preset, newRoomOrigin);
                        if (created != null)
                        {
                            Selection.activeGameObject = created.gameObject;
                        }

                        GUIUtility.ExitGUI();
                    }

                    if (GUILayout.Button(new GUIContent("Load Values", "Use this preset's values in the window. A selected room changes straight away; its elements stay as they are.")))
                    {
                        loadValues(preset.Settings.Clone());
                    }

                    using (new EditorGUI.DisabledScope(room == null))
                    {
                        if (GUILayout.Button(new GUIContent("Add Elements", "Add this preset's elements to the selected room, where they were in the preset.")))
                        {
                            RoomBuilder.AddPresetElements(room, preset);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Show", "Highlight the preset asset in the Project window."), EditorStyles.miniButtonLeft, GUILayout.Width(ButtonWidth)))
                    {
                        EditorGUIUtility.PingObject(preset);
                    }

                    if (GUILayout.Button(new GUIContent("Delete...", "Move the preset asset to the system's recycle bin, after asking. Rooms made from it are not affected."), EditorStyles.miniButtonRight, GUILayout.Width(ButtonWidth)))
                    {
                        Delete(preset);
                        GUIUtility.ExitGUI();
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

            if (AssetDatabase.LoadAssetAtPath<RoomPreset>(path) != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Replace Room Preset",
                    $"The preset '{Path.GetFileNameWithoutExtension(path)}' already exists.\n\nReplace its values and elements with the selected room's? Its Notes are kept.",
                    "Replace",
                    "Cancel");
                if (!replace)
                {
                    return;
                }
            }

            RoomPreset preset = RoomBuilder.SavePreset(room, path);
            Refresh();
            EditorGUIUtility.PingObject(preset);
        }

        private void Delete(RoomPreset preset)
        {
            string path = AssetDatabase.GetAssetPath(preset);
            bool delete = EditorUtility.DisplayDialog(
                "Delete Room Preset",
                $"Move the preset '{preset.name}' to the recycle bin?\n\n{path}\n\nRooms already made from it stay as they are.",
                "Delete",
                "Cancel");
            if (delete)
            {
                AssetDatabase.MoveAssetToTrash(path);
                Refresh();
            }
        }
    }
}

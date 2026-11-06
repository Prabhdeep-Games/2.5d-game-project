using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The GeneratedRoom Inspector: every value of the room, which rebuilds as it changes, the same route status and notes as
    /// the Room Generator window, and the room's Elements, so a room can be built and filled without opening the window.
    /// </summary>
    [CustomEditor(typeof(GeneratedRoom))]
    public sealed class GeneratedRoomEditor : Editor
    {
        private const string SettingsProperty = "settings";
        private const string ScriptProperty = "m_Script";
        private const int MaximumRandomSeed = 100000;

        // Shared by every room's Inspector, so open categories stay open when another room is selected.
        private static readonly RoomElementsPanel ElementsPanel = new RoomElementsPanel(true);

        private RoomPlan plan;
        private string planSignature;
        private string routeProblem;

        /// <summary>
        /// Finishes an edit already applied to a room's values. Choosing a purpose or a lighting mode loads its values, any
        /// other option gets the room it needs to work, editing a cleared room brings it back, and the room is then rebuilt.
        /// </summary>
        public static void FinishEdit(GeneratedRoom room, RoomSettings before)
        {
            RoomSettings values = room.Settings.Clone();
            if (values.Purpose != before.Purpose)
            {
                values.ApplyPurpose(values.Purpose);
            }
            else if (values.LightingMode != before.LightingMode)
            {
                values.ApplyLighting(values.LightingMode);
            }
            else
            {
                RoomFitting.MakeRoomAndLog(values, before);
            }

            RoomBuilder.RecordValues(room, values, false);
            RoomLiveUpdater.Request(room);
        }

        public override void OnInspectorGUI()
        {
            var room = (GeneratedRoom)target;
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                Application.isPlaying
                    ? "Changes rebuild this room straight away. Anything changed during Play is lost when Play stops."
                    : "Changes rebuild this room straight away, replacing any generated pieces you changed by hand. Use Keep Room in the Room Generator to stop that. Changing Width or Exit Rise moves the exit, and rooms after it do not follow.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            DrawValues();
            bool randomise = GUILayout.Button(new GUIContent("Randomise Seed", "Choose a new seed, which changes the room straight away."));
            if (randomise)
            {
                serializedObject.FindProperty(SettingsProperty + ".seed").intValue = Random.Range(1, MaximumRandomSeed);
            }

            if (EditorGUI.EndChangeCheck() || randomise)
            {
                RoomSettings before = room.Settings.Clone();
                serializedObject.ApplyModifiedProperties();
                FinishEdit(room, before);
                serializedObject.Update();
            }

            DrawStatus(room);

            EditorGUILayout.Space();
            ElementsPanel.Draw(room, RoomGeneratorWindow.IsPuzzleLevel());

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, ScriptProperty, SettingsProperty);
            serializedObject.ApplyModifiedProperties();
            if (EditorGUI.EndChangeCheck())
            {
                // Draw Gizmos is read by every gizmo inside the room, so the Scene view has to redraw them all.
                SceneView.RepaintAll();
            }
        }

        // Options that belong to a switch, such as the fog values under Scene Fog, show only while it is on.
        private void DrawValues()
        {
            ShowIfFields.DrawChildren(serializedObject.FindProperty(SettingsProperty), typeof(RoomSettings));
        }

        private void DrawStatus(GeneratedRoom room)
        {
            string signature = room.CurrentSignature;
            if (plan == null || signature != planSignature)
            {
                plan = RoomLayout.Create(room.Settings);
                routeProblem = plan.CanBuild ? RoomRouteCheck.FindProblem(plan) : null;
                planSignature = signature;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            foreach (string error in plan.Errors)
            {
                EditorGUILayout.HelpBox(error + " The room stays as it was until this is fixed.", MessageType.Error);
            }

            if (!plan.CanBuild)
            {
                return;
            }

            if (routeProblem != null)
            {
                EditorGUILayout.HelpBox("Route: " + routeProblem, MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Route: floor and headroom are clear from entrance to exit.");
            }

            foreach (string note in plan.Notes)
            {
                EditorGUILayout.HelpBox(note, MessageType.Info);
            }
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The GeneratedRoom Inspector: the room's values in the same sections as the Room Generator window (Room Setup,
    /// Architecture, Elements, Lighting, and a closed Diagnostics section), rebuilding as they change, so a room can be
    /// built and filled without opening the window.
    /// </summary>
    [CustomEditor(typeof(GeneratedRoom))]
    public sealed class GeneratedRoomEditor : Editor
    {
        private const string SettingsProperty = "settings";
        private const string ScriptProperty = "m_Script";
        private const string SectionKey = "Inspector.";
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
            RefreshPlan(room);

            EditorGUILayout.HelpBox(
                Application.isPlaying
                    ? "Changes rebuild this room straight away. Anything changed during Play is lost when Play stops."
                    : "Changes rebuild this room straight away, replacing generated pieces you changed by hand. To edit it freely, use Detach Room in the Room Generator window. Changing Width or Exit Rise moves the exit, and rooms after it do not follow.",
                MessageType.None);
            RoomGeneratorSections.DrawProblems(plan, routeProblem, null, " The room stays as it was until this is fixed.");

            SerializedProperty settings = serializedObject.FindProperty(SettingsProperty);
            if (RoomGeneratorSections.Section(SectionKey + "Setup", "Room Setup"))
            {
                EditValues(room, () => RoomGeneratorSections.DrawSetup(settings));
            }

            if (RoomGeneratorSections.Section(SectionKey + "Architecture", "Architecture"))
            {
                EditValues(room, () =>
                {
                    RoomGeneratorSections.DrawArchitecture(settings);
                    return false;
                });
                RoomGeneratorSections.DrawNotes(plan);
            }

            if (RoomGeneratorSections.Section(SectionKey + "Elements", "Elements"))
            {
                ElementsPanel.Draw(room);
            }

            if (RoomGeneratorSections.Section(SectionKey + "Lighting", "Lighting"))
            {
                EditValues(room, () =>
                {
                    RoomGeneratorSections.DrawLighting(settings);
                    RoomGeneratorSections.DrawLightingSummary(plan, room.Settings);
                    RoomGeneratorSections.DrawSceneWideLighting(settings);
                    return false;
                });
            }

            if (RoomGeneratorSections.Section(SectionKey + "Diagnostics", "Diagnostics", false))
            {
                RoomGeneratorSections.DrawDiagnostics(plan, room.Settings, routeProblem);
                EditorGUI.BeginChangeCheck();
                DrawPropertiesExcluding(serializedObject, ScriptProperty, SettingsProperty);
                serializedObject.ApplyModifiedProperties();
                if (EditorGUI.EndChangeCheck())
                {
                    // Draw Gizmos is read by every gizmo inside the room, so the Scene view has to redraw them all.
                    SceneView.RepaintAll();
                }
            }
        }

        // Draws some of the room's values. Any change, or New Seed, is applied and the room rebuilt.
        private void EditValues(GeneratedRoom room, System.Func<bool> draw)
        {
            EditorGUI.BeginChangeCheck();
            bool newSeed = draw();
            if (newSeed)
            {
                serializedObject.FindProperty(SettingsProperty + ".seed").intValue = Random.Range(1, MaximumRandomSeed);
            }

            if (EditorGUI.EndChangeCheck() || newSeed)
            {
                RoomSettings before = room.Settings.Clone();
                serializedObject.ApplyModifiedProperties();
                FinishEdit(room, before);
                serializedObject.Update();
            }
        }

        private void RefreshPlan(GeneratedRoom room)
        {
            string signature = room.CurrentSignature;
            if (plan == null || signature != planSignature)
            {
                plan = RoomLayout.Create(room.Settings);
                routeProblem = plan.CanBuild ? RoomRouteCheck.FindProblem(plan) : null;
                planSignature = signature;
            }
        }
    }
}

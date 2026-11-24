using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The Room Generator: choose a purpose, adjust the values and generate. With a room selected, changes apply to it
    /// straight away, as they do on its GeneratedRoom component. Add gameplay elements, save useful rooms as presets, and
    /// detach a finished room to carry on by hand. Open it from Tools > Room Generator; it docks like any other Editor window.
    ///
    /// Layout: a status line, then the sections Room Setup, Architecture, Elements, Lighting, Presets and Diagnostics (the
    /// last one closed), and the generation buttons at the bottom.
    /// </summary>
    public sealed class RoomGeneratorWindow : EditorWindow
    {
        /// <summary>Where Generate New Room puts the new room.</summary>
        public enum Placement
        {
            AtPosition,
            AfterSelectedRoom
        }

        private const string MenuPath = "Tools/Room Generator";
        private const string WindowTitle = "Room Generator";
        private const string InspectorWindowType = "UnityEditor.InspectorWindow,UnityEditor";
        private const string SectionKey = "Window.";
        private const float ButtonHeight = 28f;
        private const int MaximumRandomSeed = 100000;
        private static readonly Vector2 MinimumSize = new Vector2(340f, 420f);
        private static readonly Color StructureColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color FixtureColor = new Color(1f, 0.9f, 0.5f, 0.9f);
        private static readonly Color GalleryColor = new Color(0.6f, 0.85f, 1f, 0.8f);
        private static readonly Color DetailColor = new Color(1f, 1f, 1f, 0.15f);
        private const float LightMarkerRadius = 0.3f;

        [SerializeField] private RoomSettings settings = new RoomSettings();
        [SerializeField] private Placement placement = Placement.AtPosition;
        [SerializeField] private Vector3 newRoomPosition;
        [SerializeField] private bool showPreview = true;
        [SerializeField] private RoomElementsPanel elementsPanel = new RoomElementsPanel();
        [SerializeField] private RoomPresetsPanel presetsPanel = new RoomPresetsPanel();

        private SerializedObject serializedWindow;
        private GeneratedRoom selectedRoom;
        private RoomPlan plan;
        private string routeProblem;
        private List<RoomGeometryCheck.Overlap> lastFlickerCheck;
        private GeneratedRoom lastFlickerRoom;
        private Vector2 scroll;

        /// <summary>Opens the window as a normal, dockable tab beside the Inspector.</summary>
        [MenuItem(MenuPath)]
        public static void Open()
        {
            System.Type inspector = System.Type.GetType(InspectorWindowType);
            RoomGeneratorWindow window = inspector != null
                ? GetWindow<RoomGeneratorWindow>(WindowTitle, true, inspector)
                : GetWindow<RoomGeneratorWindow>(WindowTitle, true);
            window.minSize = MinimumSize;
        }

        //==================== LIFECYCLE =====================
        private void OnEnable()
        {
            serializedWindow = new SerializedObject(this);
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += DrawPreview;
            OnSelectionChanged();
            RefreshPlan();
        }

        private void OnFocus()
        {
            presetsPanel.Refresh();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= DrawPreview;
        }

        // Selecting a generated room shows its values, so it can be adjusted and regenerated.
        private void OnSelectionChanged()
        {
            GameObject active = Selection.activeGameObject;
            GeneratedRoom room = active != null ? active.GetComponentInParent<GeneratedRoom>() : null;
            if (room != selectedRoom)
            {
                selectedRoom = room;
                if (room != null)
                {
                    settings = room.Settings.Clone();
                    serializedWindow.Update();
                    RefreshPlan();
                }

                Repaint();
                SceneView.RepaintAll();
            }
        }

        // The room's values can also change on its component or through Undo, so this copy follows them.
        private void OnInspectorUpdate()
        {
            if (selectedRoom != null && JsonUtility.ToJson(selectedRoom.Settings) != JsonUtility.ToJson(settings))
            {
                settings = selectedRoom.Settings.Clone();
                serializedWindow.Update();
                RefreshPlan();
                Repaint();
            }
        }

        private void PushToSelectedRoom()
        {
            if (selectedRoom != null)
            {
                RoomBuilder.RecordValues(selectedRoom, settings.Clone(), false);
                RoomLiveUpdater.Request(selectedRoom);
            }
        }

        private void RefreshPlan()
        {
            plan = RoomLayout.Create(settings);
            routeProblem = plan.CanBuild ? RoomRouteCheck.FindProblem(plan) : null;
            SceneView.RepaintAll();
        }

        //==================== WINDOW =====================
        private void OnGUI()
        {
            serializedWindow.Update();
            List<string> missingAssets = RoomAssetLibrary.FindMissing();

            DrawStatus(missingAssets);
            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (RoomGeneratorSections.Section(SectionKey + "Setup", "Room Setup"))
            {
                DrawPlacement();
                DrawValues(RoomGeneratorSections.DrawSetup);
            }

            if (RoomGeneratorSections.Section(SectionKey + "Architecture", "Architecture"))
            {
                DrawValues(property =>
                {
                    RoomGeneratorSections.DrawArchitecture(property);
                    return false;
                });
                RoomGeneratorSections.DrawNotes(plan);
            }

            if (RoomGeneratorSections.Section(SectionKey + "Elements", "Elements"))
            {
                elementsPanel.Draw(selectedRoom);
            }

            if (RoomGeneratorSections.Section(SectionKey + "Lighting", "Lighting"))
            {
                DrawValues(property =>
                {
                    RoomGeneratorSections.DrawLighting(property);
                    RoomGeneratorSections.DrawLightingSummary(plan, settings);
                    RoomGeneratorSections.DrawSceneWideLighting(property);
                    return false;
                });
            }

            if (RoomGeneratorSections.Section(SectionKey + "Presets", "Presets"))
            {
                presetsPanel.Draw(selectedRoom, settings.Purpose == RoomPurpose.Custom, NewRoomOrigin(), LoadValues);
            }

            if (RoomGeneratorSections.Section(SectionKey + "Diagnostics", "Diagnostics", false))
            {
                DrawDiagnostics(missingAssets);
            }

            EditorGUILayout.EndScrollView();

            // Window values such as New Room Position are edited through the serialized copy, so they are written back here.
            serializedWindow.ApplyModifiedProperties();
            DrawActions(missingAssets);
        }

        // Draws a group of the room's values. Any change goes through ApplyEdit; New Seed picks a seed instead.
        private void DrawValues(System.Func<SerializedProperty, bool> draw)
        {
            SerializedProperty property = serializedWindow.FindProperty(nameof(settings));
            EditorGUI.BeginChangeCheck();
            bool newSeed = draw(property);
            bool changed = EditorGUI.EndChangeCheck();
            if (newSeed)
            {
                RandomiseSeed();
            }
            else if (changed)
            {
                ApplyEdit();
            }
        }

        // Choosing a purpose or a lighting mode loads its values. Any other option gets the room it needs to work.
        private void ApplyEdit()
        {
            RoomSettings before = settings.Clone();
            serializedWindow.ApplyModifiedProperties();
            if (settings.Purpose != before.Purpose)
            {
                settings.ApplyPurpose(settings.Purpose);
            }
            else if (settings.LightingMode != before.LightingMode)
            {
                settings.ApplyLighting(settings.LightingMode);
            }
            else
            {
                RoomFitting.MakeRoomAndLog(settings, before);
            }

            serializedWindow.Update();
            PushToSelectedRoom();
            RefreshPlan();
        }

        private void RandomiseSeed()
        {
            serializedWindow.ApplyModifiedProperties();
            Undo.RecordObject(this, "Randomise Seed");
            settings.SetSeed(Random.Range(1, MaximumRandomSeed));
            serializedWindow.Update();
            PushToSelectedRoom();
            RefreshPlan();
        }

        //==================== STATUS =====================
        // Always visible: what the window is working on, and anything that stops a room being built or walked through.
        private void DrawStatus(List<string> missingAssets)
        {
            string target = selectedRoom != null
                ? $"Editing '{selectedRoom.name}'. Changes rebuild it straight away."
                : "No room selected. The values below are for a new room.";
            EditorGUILayout.HelpBox(target, MessageType.None);
            RoomGeneratorSections.DrawProblems(plan, routeProblem, missingAssets);

            GeneratedRoom occupant = !CanGenerate(out _) ? null : RoomAt(NewRoomOrigin(), settings);
            if (occupant != null)
            {
                EditorGUILayout.HelpBox($"'{occupant.name}' is already at the new room's position, so the two would overlap and their walls would flicker.", MessageType.Warning);
            }
        }

        //==================== PLACEMENT =====================
        private void DrawPlacement()
        {
            EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(placement)), new GUIContent("New Room Placement", "Where Generate New Room puts the next room. At Position uses the position below. After Selected Room starts it at the exit of the selected room, so the two connect."));
            if (placement == Placement.AtPosition)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(newRoomPosition)), new GUIContent("Position", "Where the entrance floor of the new room goes, in world space. The route runs along +X from here."));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("After Last Room", "Set the position to the exit of the room furthest along +X."), EditorStyles.miniButtonLeft))
                    {
                        SetNewRoomPosition(EndOfLastRoom());
                    }

                    if (GUILayout.Button(new GUIContent("Scene View Centre", "Set the position to the point the Scene view is looking at."), EditorStyles.miniButtonRight))
                    {
                        SceneView view = SceneView.lastActiveSceneView;
                        if (view != null)
                        {
                            SetNewRoomPosition(view.pivot);
                        }
                    }
                }
            }
            else if (selectedRoom == null)
            {
                EditorGUILayout.HelpBox("Select a generated room. The new room will start at its exit.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField($"Starts at the exit of '{selectedRoom.name}', {(selectedRoom.transform.position + selectedRoom.ExitOffset).ToString("0.##")}.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void SetNewRoomPosition(Vector3 position)
        {
            serializedWindow.ApplyModifiedProperties();
            Undo.RecordObject(this, "Move New Room Position");
            newRoomPosition = position;
            serializedWindow.Update();
            GUI.FocusControl(null);
            SceneView.RepaintAll();
        }

        /// <summary>Where Generate New Room would put a room now.</summary>
        public Vector3 NewRoomOrigin()
        {
            if (placement == Placement.AfterSelectedRoom && selectedRoom != null)
            {
                return selectedRoom.transform.position + selectedRoom.ExitOffset;
            }

            return newRoomPosition;
        }

        private bool CanGenerate(out string reason)
        {
            reason = null;
            if (Application.isPlaying)
            {
                reason = "Rooms cannot be generated during Play.";
            }
            else if (placement == Placement.AfterSelectedRoom && selectedRoom == null)
            {
                reason = "Select a room to place the new one after it, or set New Room Placement to At Position.";
            }

            return reason == null;
        }

        /// <summary>A generated room whose space a new room at this position would overlap, or null.</summary>
        private static GeneratedRoom RoomAt(Vector3 position, RoomSettings newSettings)
        {
            var newSpace = new Bounds();
            newSpace.SetMinMax(position, position + new Vector3(newSettings.Width, newSettings.Height, newSettings.Depth));
            foreach (GeneratedRoom room in FindObjectsByType<GeneratedRoom>(FindObjectsSortMode.None))
            {
                var space = new Bounds();
                Vector3 origin = room.transform.position;
                space.SetMinMax(origin, origin + new Vector3(room.Settings.Width, room.Settings.Height, room.Settings.Depth));
                if (space.Intersects(newSpace) && Mathf.Min(space.max.x, newSpace.max.x) - Mathf.Max(space.min.x, newSpace.min.x) > 0.01f)
                {
                    return room;
                }
            }

            return null;
        }

        private static Vector3 EndOfLastRoom()
        {
            Vector3 end = Vector3.zero;
            float furthest = float.MinValue;
            foreach (GeneratedRoom room in FindObjectsByType<GeneratedRoom>(FindObjectsSortMode.None))
            {
                Vector3 exit = room.transform.position + room.ExitOffset;
                if (exit.x > furthest)
                {
                    furthest = exit.x;
                    end = exit;
                }
            }

            return end;
        }

        private void LoadValues(RoomSettings values)
        {
            Undo.RecordObject(this, "Load Room Preset Values");
            settings = values;
            serializedWindow.Update();
            PushToSelectedRoom();
            RefreshPlan();
        }

        //==================== DIAGNOSTICS =====================
        private void DrawDiagnostics(List<string> missingAssets)
        {
            if (missingAssets.Count == 0)
            {
                EditorGUILayout.LabelField($"Assets: all {RoomAssetLibrary.AssetCount} materials and prefabs the generator uses were found.", EditorStyles.wordWrappedMiniLabel);
            }

            RoomGeneratorSections.DrawDiagnostics(plan, settings, routeProblem);
            DrawFlickerCheck();

            EditorGUI.BeginChangeCheck();
            showPreview = EditorGUILayout.Toggle(new GUIContent("Scene View Preview", "Draw an outline of the new room in the Scene view while no room is selected."), showPreview);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }

        /// <summary>What in a room stops a player who is running through it.</summary>
        public static List<string> FlowBreakers(RoomSettings values, RoomPlan roomPlan)
        {
            var breakers = new List<string>();
            foreach (PrefabPlan prefab in roomPlan.Prefabs)
            {
                if (prefab.Prefab == RoomPrefab.GlassPartition)
                {
                    breakers.Add("the Glass Partition stops the run until a swap gets past it.");
                    break;
                }
            }

            if (values.Exit == RoomThreshold.PanelDoor)
            {
                breakers.Add("the exit Panel Door stops the run until its panel is used.");
            }
            else if (values.Exit == RoomThreshold.SealedDoor)
            {
                breakers.Add("the exit Sealed Door stays shut until something powers it.");
            }

            if (RoomLayout.CameraShouldLockExit(values))
            {
                breakers.Add("the camera seals the exit while it sees the player.");
            }

            return breakers;
        }

        // Checking every surface takes a moment in a large room, so it runs on request.
        private void DrawFlickerCheck()
        {
            using (new EditorGUI.DisabledScope(selectedRoom == null))
            {
                if (GUILayout.Button(new GUIContent("Check For Flickering Surfaces", "Looks for faces of different objects in the selected room that lie on top of each other, which flicker in the Scene and Game views.")))
                {
                    lastFlickerCheck = RoomGeometryCheck.FindOverlaps(selectedRoom.gameObject);
                    lastFlickerRoom = selectedRoom;
                }
            }

            if (lastFlickerCheck == null || lastFlickerRoom != selectedRoom || selectedRoom == null)
            {
                return;
            }

            if (lastFlickerCheck.Count == 0)
            {
                EditorGUILayout.LabelField("No overlapping surfaces found.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var lines = new List<string>();
            foreach (RoomGeometryCheck.Overlap overlap in lastFlickerCheck)
            {
                lines.Add(overlap.ToString());
            }

            EditorGUILayout.HelpBox($"{lastFlickerCheck.Count} pair(s) of surfaces overlap and will flicker:\n" + string.Join("\n", lines), MessageType.Warning);
        }

        //==================== ACTIONS =====================
        private void DrawActions(List<string> missingAssets)
        {
            bool canGenerate = CanGenerate(out string reason) && plan.CanBuild && missingAssets.Count == 0;
            EditorGUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canGenerate))
                {
                    string tooltip = reason ?? "Make a new room with these values, at the New Room Placement in Room Setup. Other rooms are left alone.";
                    if (GUILayout.Button(new GUIContent("Generate New Room", tooltip), GUILayout.Height(ButtonHeight)))
                    {
                        Generate(NewRoomOrigin());
                    }
                }

                using (new EditorGUI.DisabledScope(Application.isPlaying || selectedRoom == null || !plan.CanBuild || missingAssets.Count > 0))
                {
                    if (GUILayout.Button(new GUIContent("Rebuild Room", "Build the selected room again from its values. This puts back generated pieces you changed or deleted by hand. Elements and objects you placed yourself stay."), GUILayout.Height(ButtonHeight)))
                    {
                        RegenerateSelected();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(Application.isPlaying || selectedRoom == null))
            {
                if (GUILayout.Button(new GUIContent("Clear Generated Pieces", "Remove the walls, lights and other pieces the generator made for the selected room. The room object, its Elements and anything you placed stay. Rebuild Room, changing a value or Undo brings the pieces back.")))
                {
                    RoomBuilder.Clear(selectedRoom);
                }

                if (GUILayout.Button(new GUIContent("Detach Room...", "Stop generating the selected room. It stays as ordinary objects you can edit freely, and the generator never changes it again.")))
                {
                    DetachSelected();
                }
            }
        }

        private void DetachSelected()
        {
            GeneratedRoom room = selectedRoom;
            bool confirmed = EditorUtility.DisplayDialog(
                "Detach Room",
                $"Detach '{room.name}'?\n\nIts objects stay in the scene, but it can no longer be rebuilt, cleared or changed from the Room Generator. Edit > Undo reverses this.",
                "Detach",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            selectedRoom = null;
            RoomBuilder.Keep(room);
            GUIUtility.ExitGUI();
        }

        // Regenerating can remove a selected child, which would otherwise end the editing of this room.
        private void RegenerateSelected()
        {
            GeneratedRoom room = selectedRoom;
            RoomBuilder.Regenerate(room, settings.Clone());
            Selection.activeGameObject = room.gameObject;
            RefreshPlan();
        }

        private void Generate(Vector3 position)
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(settings.Clone(), position);
            if (room != null)
            {
                Selection.activeGameObject = room.gameObject;
            }
        }

        //==================== PREVIEW =====================
        private void DrawPreview(SceneView view)
        {
            // A selected room shows its changes itself, so only a room yet to be generated needs an outline.
            if (!showPreview || plan == null || !plan.CanBuild || selectedRoom != null)
            {
                return;
            }

            Vector3 origin = NewRoomOrigin();
            foreach (BlockPlan block in plan.Blocks)
            {
                Handles.color = block.Group == RoomGroup.Gallery ? GalleryColor
                    : block.Group == RoomGroup.LightFixtures ? FixtureColor
                    : block.Solid ? StructureColor
                    : DetailColor;
                Handles.DrawWireCube(origin + block.Center, block.Size);
            }

            Handles.color = FixtureColor;
            foreach (LightPlan light in plan.Lights)
            {
                Handles.DrawWireDisc(origin + light.Position, Vector3.forward, LightMarkerRadius);
            }
        }
    }
}

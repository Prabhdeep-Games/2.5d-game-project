using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The Room Generator: choose a purpose, adjust the values and generate. With a room selected, changes apply to it
    /// straight away, as they do on its GeneratedRoom component. Add gameplay elements, save useful rooms as presets, then
    /// keep the result and carry on by hand. Open it from Tools > Room Generator; it docks like any other Editor window.
    /// </summary>
    public sealed class RoomGeneratorWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Room Generator";
        private const string WindowTitle = "Room Generator";
        private const string InspectorWindowType = "UnityEditor.InspectorWindow,UnityEditor";
        private const float ButtonHeight = 28f;
        private const int MaximumRandomSeed = 100000;
        private static readonly Vector2 MinimumSize = new Vector2(320f, 400f);
        private static readonly Color StructureColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color FixtureColor = new Color(1f, 0.9f, 0.5f, 0.9f);
        private static readonly Color GalleryColor = new Color(0.6f, 0.85f, 1f, 0.8f);
        private static readonly Color DetailColor = new Color(1f, 1f, 1f, 0.15f);
        private const float LightMarkerRadius = 0.3f;

        [SerializeField] private RoomSettings settings = new RoomSettings();
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
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawLevel();
            DrawTarget();
            DrawSettings();
            EditorGUILayout.Space();
            DrawPlanSummary();
            EditorGUILayout.Space();
            elementsPanel.Draw(selectedRoom, IsPuzzleLevel());
            EditorGUILayout.Space();
            presetsPanel.Draw(selectedRoom, settings.Purpose == RoomPurpose.Custom, newRoomPosition, LoadValues);

            EditorGUILayout.EndScrollView();
            DrawActions();
        }

        //==================== LEVEL =====================
        private static LevelProfile FindLevelProfile()
        {
            return FindFirstObjectByType<LevelProfile>(FindObjectsInactive.Include);
        }

        /// <summary>Whether the open scenes are marked as a puzzle level by a Level Profile.</summary>
        public static bool IsPuzzleLevel()
        {
            LevelProfile profile = FindLevelProfile();
            return profile != null && profile.IsPuzzleLevel;
        }

        // IsPuzzleLevel belongs to the scene, not to a room, and only changes advice and the order elements are listed in.
        private void DrawLevel()
        {
            LevelProfile profile = FindLevelProfile();
            if (profile == null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("Level", "Adds a Level Profile to the scene, which records whether this is a puzzle level."), GUILayout.Width(EditorGUIUtility.labelWidth));
                    if (GUILayout.Button("Mark As Traversal Level"))
                    {
                        CreateLevelProfile(false);
                    }

                    if (GUILayout.Button("Mark As Puzzle Level"))
                    {
                        CreateLevelProfile(true);
                    }
                }

                return;
            }

            var serializedProfile = new SerializedObject(profile);
            serializedProfile.Update();
            EditorGUILayout.PropertyField(serializedProfile.FindProperty("isPuzzleLevel"), new GUIContent("Is Puzzle Level", serializedProfile.FindProperty("isPuzzleLevel").tooltip));
            serializedProfile.ApplyModifiedProperties();
        }

        private static void CreateLevelProfile(bool isPuzzleLevel)
        {
            var go = new GameObject("Level Profile");
            Undo.RegisterCreatedObjectUndo(go, "Add Level Profile");
            var serializedProfile = new SerializedObject(go.AddComponent<LevelProfile>());
            serializedProfile.FindProperty("isPuzzleLevel").boolValue = isPuzzleLevel;
            serializedProfile.ApplyModifiedPropertiesWithoutUndo();
        }

        //==================== TARGET =====================
        private void DrawTarget()
        {
            if (selectedRoom != null)
            {
                EditorGUILayout.HelpBox($"Editing '{selectedRoom.name}'. Changes apply to it straight away.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox("No generated room selected. Generate New Room makes one at the position below.", MessageType.None);
            EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(newRoomPosition)), new GUIContent("New Room Position", "Where the entrance floor of the new room goes. The route runs along +X."));

            // Two rooms in the same place put identical walls on top of each other, which flicker.
            GeneratedRoom occupant = RoomAt(newRoomPosition, settings);
            if (occupant != null)
            {
                EditorGUILayout.HelpBox($"'{occupant.name}' is already here, so a new room would overlap it and its walls would flicker.", MessageType.Warning);
                if (GUILayout.Button("Move To The End Of The Last Room"))
                {
                    serializedWindow.ApplyModifiedProperties();
                    Undo.RecordObject(this, "Move New Room Position");
                    newRoomPosition = EndOfLastRoom();
                    serializedWindow.Update();
                    SceneView.RepaintAll();
                }
            }
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

        private void DrawSettings()
        {
            SerializedProperty property = serializedWindow.FindProperty(nameof(settings));
            // Options that belong to a switch, such as the fog values under Scene Fog, show only while it is on.
            EditorGUI.BeginChangeCheck();
            ShowIfFields.DrawChildren(property, typeof(RoomSettings));

            if (EditorGUI.EndChangeCheck())
            {
                RoomSettings before = settings.Clone();
                serializedWindow.ApplyModifiedProperties();

                // Choosing a purpose or a lighting mode loads its values. Any other option gets the room it needs to work.
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

            if (GUILayout.Button(new GUIContent("Randomise Seed", "Choose a new seed. A selected room changes straight away.")))
            {
                RandomiseSeed();
            }
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

        private void DrawPlanSummary()
        {
            List<string> missing = RoomAssetLibrary.FindMissing();
            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);
            if (missing.Count > 0)
            {
                EditorGUILayout.HelpBox("Missing:\n" + string.Join("\n", missing), MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField($"All {RoomAssetLibrary.AssetCount} materials and prefabs found.");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("This Room", EditorStyles.boldLabel);
            foreach (string error in plan.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            if (!plan.CanBuild)
            {
                return;
            }

            int shadows = 0;
            foreach (LightPlan light in plan.Lights)
            {
                shadows += light.Shadows ? 1 : 0;
            }

            EditorGUILayout.LabelField($"{plan.Blocks.Count + plan.Curves.Count} pieces, {plan.Prefabs.Count} prefabs, {plan.Lights.Count} lights ({shadows} with shadows)");
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

            if (!IsPuzzleLevel())
            {
                foreach (string breaker in FlowBreakers(settings, plan))
                {
                    EditorGUILayout.HelpBox("Traversal level: " + breaker, MessageType.Warning);
                }
            }

            DrawFlickerCheck();

            EditorGUI.BeginChangeCheck();
            showPreview = EditorGUILayout.Toggle(new GUIContent("Scene View Preview", "Draw an outline of this room in the Scene view before it is generated."), showPreview);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }

        /// <summary>What in a room stops a player who is running through it. Only reported in a traversal level.</summary>
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
            if (selectedRoom == null)
            {
                return;
            }

            if (GUILayout.Button(new GUIContent("Check For Flickering Surfaces", "Looks for faces of different objects in this room that lie on top of each other, which flicker in the Scene and Game views.")))
            {
                lastFlickerCheck = RoomGeometryCheck.FindOverlaps(selectedRoom.gameObject);
                lastFlickerRoom = selectedRoom;
            }

            if (lastFlickerCheck == null || lastFlickerRoom != selectedRoom)
            {
                return;
            }

            if (lastFlickerCheck.Count == 0)
            {
                EditorGUILayout.LabelField("No overlapping surfaces found.");
                return;
            }

            var lines = new List<string>();
            foreach (RoomGeometryCheck.Overlap overlap in lastFlickerCheck)
            {
                lines.Add(overlap.ToString());
            }

            EditorGUILayout.HelpBox($"{lastFlickerCheck.Count} pair(s) of surfaces overlap and will flicker:\n" + string.Join("\n", lines), MessageType.Warning);
        }

        private void DrawActions()
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying || !plan.CanBuild || RoomAssetLibrary.FindMissing().Count > 0))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Generate New Room", "Make a new room and leave any others alone. With a room selected, the new one starts at its exit, so the two connect."), GUILayout.Height(ButtonHeight)))
                    {
                        Generate(selectedRoom != null ? selectedRoom.transform.position + selectedRoom.ExitOffset : newRoomPosition);
                    }

                    using (new EditorGUI.DisabledScope(selectedRoom == null))
                    {
                        if (GUILayout.Button(new GUIContent("Regenerate", "Rebuild the selected room now, which also replaces any generated pieces you changed by hand. Objects you placed yourself stay."), GUILayout.Height(ButtonHeight)))
                        {
                            RegenerateSelected();
                        }

                        if (GUILayout.Button(new GUIContent("Regenerate Layout", "Choose a new seed and rebuild the selected room."), GUILayout.Height(ButtonHeight)))
                        {
                            RandomiseSeed();
                            RegenerateSelected();
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(Application.isPlaying || selectedRoom == null))
            {
                if (GUILayout.Button(new GUIContent("Clear Generated Room", "Remove what the generator made for the selected room. Anything placed by hand stays. Changing a value or using Undo brings it back.")))
                {
                    RoomBuilder.Clear(selectedRoom);
                }

                if (GUILayout.Button(new GUIContent("Keep Room", "Stop tracking the selected room. It stays as ordinary objects, and the generator will never touch it again.")))
                {
                    GeneratedRoom room = selectedRoom;
                    selectedRoom = null;
                    RoomBuilder.Keep(room);
                }
            }
        }

        // Regenerating can remove a selected child, which would otherwise end the editing of this room.
        private void RegenerateSelected()
        {
            GeneratedRoom room = selectedRoom;
            RoomBuilder.Regenerate(room, settings.Clone());
            Selection.activeGameObject = room.gameObject;
            RefreshPlan();
        }

        // With a room selected, a new room starts at its exit, so the two connect.
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

            Vector3 origin = newRoomPosition;
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

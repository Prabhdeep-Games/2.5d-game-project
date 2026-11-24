using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The parts the Room Generator window and the GeneratedRoom Inspector share: collapsible sections, the room's values
    /// grouped into Room Setup, Architecture and Lighting, and the status, notes and diagnostics of a plan. Both draw the
    /// same groups with the same labels, so a room reads the same wherever it is edited.
    /// </summary>
    public static class RoomGeneratorSections
    {
        private const string StatePrefix = "TheSpaceBetween.RoomGenerator.";
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const float SeedButtonWidth = 80f;

        //==================== VALUE GROUPS =====================
        // Every serialized field of RoomSettings belongs to one group. A field added later and not listed here is still
        // drawn, at the end of Architecture, so nothing can silently disappear from the interface.
        private static readonly string[] Dimensions = { "width", "height", "depth", "wallThickness" };
        private static readonly string[] WallsAndBays = { "wallFinish", "floorFinish", "ceilingFinish", "bayWidth", "pilasterDepth", "ceilingBeams", "recessedBays", "repairPanels" };
        private static readonly string[] EntranceAndExit = { "entrance", "exit", "openingHeight", "interiorThresholds", "levelChange", "exitRise" };
        private static readonly string[] ObstaclesAndObservation = { "glassPartition", "partitionPosition", "lowBarriers", "observationGallery", "galleryBays", "gallerySill", "securityCamera", "cameraPosition", "cameraLocksExit" };
        private static readonly string[] Platforming = { "platforming", "floorGaps", "gapWidth", "crumblingBridges", "crumbleDelay", "slideHoods", "hoodLength", "debrisDrops", "debrisInterval", "roomRestart", "chasePath" };
        private static readonly string[] Fixtures = { "lightingMode", "ceilingFixtures", "wallFixtures", "fixtureEveryBays", "brightness", "fillLevel", "lightColor", "spotAngle", "shadowCasters", "failedFixtures", "brightnessVariation", "galleryBrightness", "reflectionProbe" };
        private static readonly string[] SceneWide = { "sceneFog", "fogMode", "fogColor", "fogStart", "fogEnd", "fogDensity", "sceneAmbient", "ambientColor" };
        private static readonly string[] Special = { "purpose", "seed" };

        // Shown names that differ from the field names. The fields keep their names, so saved rooms and presets still load.
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "recessedBays", "Sealed Recesses" },
            { "repairPanels", "Repair Patches" }
        };

        private static HashSet<string> listed;

        //==================== SECTIONS =====================
        /// <summary>
        /// Draws a section heading that folds, and returns whether it is open. The state is kept for the Editor session,
        /// separately for each <paramref name="key"/>.
        /// </summary>
        public static bool Section(string key, string title, bool openByDefault = true)
        {
            string stateKey = StatePrefix + key;
            bool open = SessionState.GetBool(stateKey, openByDefault);
            EditorGUILayout.Space(2f);
            bool now = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            if (now != open)
            {
                SessionState.SetBool(stateKey, now);
            }

            return now;
        }

        /// <summary>A small heading inside a section.</summary>
        public static void SubHeading(string title)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        }

        //==================== VALUES =====================
        /// <summary>Purpose, Seed with a New Seed button, and the room's size. Returns true when New Seed was pressed.</summary>
        public static bool DrawSetup(SerializedProperty settings)
        {
            Field(settings.FindPropertyRelative("purpose"));
            bool newSeed;
            using (new EditorGUILayout.HorizontalScope())
            {
                Field(settings.FindPropertyRelative("seed"));
                newSeed = GUILayout.Button(new GUIContent("New Seed", "Choose a random seed: a new arrangement of recesses, patches and failed fixtures, with the same values."), GUILayout.Width(SeedButtonWidth));
            }

            SubHeading("Size");
            Fields(settings, Dimensions);
            return newSeed;
        }

        /// <summary>Walls and bays, entrance and exit, obstacles and observation, and platforming.</summary>
        public static void DrawArchitecture(SerializedProperty settings)
        {
            SubHeading("Walls and Bays");
            Fields(settings, WallsAndBays);
            SubHeading("Entrance, Exit and Level Change");
            Fields(settings, EntranceAndExit);
            SubHeading("Obstacles and Observation");
            Fields(settings, ObstaclesAndObservation);
            SubHeading("Platforming");
            Fields(settings, Platforming);
            DrawUnlisted(settings);
        }

        /// <summary>The room's own fixtures and lights.</summary>
        public static void DrawLighting(SerializedProperty settings)
        {
            Fields(settings, Fixtures);
        }

        /// <summary>Fog and ambient light, which belong to the whole scene rather than the room.</summary>
        public static void DrawSceneWideLighting(SerializedProperty settings)
        {
            SubHeading("Scene-Wide Lighting");
            EditorGUILayout.LabelField("These change the whole scene's fog and ambient light (Window > Rendering > Lighting), not only this room. Clearing the room puts the scene's earlier values back.", EditorStyles.wordWrappedMiniLabel);
            Fields(settings, SceneWide);
        }

        /// <summary>Draws the named values that are in use. Values that depend on another one are indented under it.</summary>
        public static void Fields(SerializedProperty settings, IEnumerable<string> names)
        {
            foreach (string name in names)
            {
                Field(settings.FindPropertyRelative(name));
            }
        }

        private static void Field(SerializedProperty property)
        {
            if (property == null || !ShowIfFields.IsShown(property, typeof(RoomSettings)))
            {
                return;
            }

            int depth = DependencyDepth(property.name);
            EditorGUI.indentLevel += depth;
            string label = Labels.TryGetValue(property.name, out string renamed) ? renamed : property.displayName;
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
            EditorGUI.indentLevel -= depth;
        }

        // How many switches a value sits under: Crumble Delay sits under Crumbling Bridges, which sits under Platforming.
        private static int DependencyDepth(string name)
        {
            int depth = 0;
            ShowIfAttribute rule = typeof(RoomSettings).GetField(name, FieldFlags)?.GetCustomAttribute<ShowIfAttribute>();
            while (rule != null && depth < 4)
            {
                depth++;
                rule = typeof(RoomSettings).GetField(rule.Field, FieldFlags)?.GetCustomAttribute<ShowIfAttribute>();
            }

            return depth;
        }

        private static void DrawUnlisted(SerializedProperty settings)
        {
            if (listed == null)
            {
                listed = new HashSet<string>(Special);
                foreach (string[] group in new[] { Dimensions, WallsAndBays, EntranceAndExit, ObstaclesAndObservation, Platforming, Fixtures, SceneWide })
                {
                    listed.UnionWith(group);
                }
            }

            SerializedProperty property = settings.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enter = true;
            while (property.NextVisible(enter) && !SerializedProperty.EqualContents(property, end))
            {
                enter = false;
                if (!listed.Contains(property.name))
                {
                    Field(property.Copy());
                }
            }
        }

        //==================== STATUS =====================
        /// <summary>
        /// What stops the room being built, and what blocks its route. Always shown, above the sections.
        /// </summary>
        public static void DrawProblems(RoomPlan plan, string routeProblem, List<string> missingAssets, string errorSuffix = "")
        {
            if (missingAssets != null && missingAssets.Count > 0)
            {
                EditorGUILayout.HelpBox("The generator cannot find these assets, so it cannot build:\n" + string.Join("\n", missingAssets), MessageType.Error);
            }

            if (plan == null)
            {
                return;
            }

            foreach (string error in plan.Errors)
            {
                EditorGUILayout.HelpBox(error + errorSuffix, MessageType.Error);
            }

            if (plan.CanBuild && routeProblem != null)
            {
                EditorGUILayout.HelpBox("Route: " + routeProblem, MessageType.Warning);
            }
        }

        /// <summary>What the designer should know about the room's layout, such as a door that stays shut.</summary>
        public static void DrawNotes(RoomPlan plan)
        {
            if (plan == null || !plan.CanBuild)
            {
                return;
            }

            foreach (string note in plan.Notes)
            {
                EditorGUILayout.HelpBox(note, MessageType.Info);
            }
        }

        /// <summary>How many fixtures there are and how many give light, with any warning about the room's light.</summary>
        public static void DrawLightingSummary(RoomPlan plan, RoomSettings values)
        {
            if (plan == null || !plan.CanBuild)
            {
                return;
            }

            var lines = new List<string>();
            if (values.CeilingFixtures)
            {
                lines.Add($"Ceiling fixtures: {plan.CeilingFixturesLit} of {plan.CeilingFixtureCount} lit");
            }

            if (values.WallFixtures)
            {
                lines.Add($"Wall fixtures: {plan.WallFixturesLit} of {plan.WallFixtureCount} lit");
            }

            if (!values.CeilingFixtures && !values.WallFixtures)
            {
                lines.Add("No fixtures: only the fill light, and the gallery if there is one, light this room.");
            }

            EditorGUILayout.HelpBox(string.Join("\n", lines), MessageType.None);
            foreach (string note in plan.LightingNotes)
            {
                EditorGUILayout.HelpBox(note, MessageType.Warning);
            }
        }

        /// <summary>
        /// Which room the selected room follows and which rooms follow it, with Link Rooms in Scene for rooms placed before
        /// connections were recorded. Shown in Room Setup, in the window and the Inspector.
        /// </summary>
        public static void DrawConnections(GeneratedRoom room)
        {
            var names = new List<string>();
            foreach (GeneratedRoom follower in RoomBuilder.FollowersOf(room))
            {
                names.Add("'" + follower.name + "'");
            }

            string before = room.Follows != null ? $"Follows '{room.Follows.name}'." : "Follows no room.";
            string after = names.Count > 0 ? $" Followed by {string.Join(", ", names)}, which move with its exit." : " No room follows it.";
            EditorGUILayout.LabelField("Connection: " + before + after, EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Link Rooms in Scene", "Connect every room whose entrance sits at another room's exit, so it moves with that room when its size changes. Nothing moves now. Rooms generated after another are connected already."), EditorStyles.miniButton))
                {
                    int linked = RoomBuilder.LinkRoomsByPosition(room.gameObject.scene);
                    Debug.Log($"[Procedural Room Generator] Linked {linked} room(s) to the room before them.");
                    SceneView.RepaintAll();
                }
            }
        }

        /// <summary>The counts, the route check and what would interrupt a running player. Shown in Diagnostics.</summary>
        public static void DrawDiagnostics(RoomPlan plan, RoomSettings values, string routeProblem)
        {
            if (plan == null || !plan.CanBuild)
            {
                EditorGUILayout.LabelField("The room cannot be built with these values. See the error above.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            int shadows = 0;
            foreach (LightPlan light in plan.Lights)
            {
                shadows += light.Shadows ? 1 : 0;
            }

            EditorGUILayout.LabelField($"{plan.Blocks.Count + plan.Curves.Count} pieces, {plan.Prefabs.Count} prefabs, {plan.Lights.Count} lights ({shadows} with shadows).", EditorStyles.wordWrappedMiniLabel);
            if (routeProblem == null)
            {
                EditorGUILayout.LabelField("Route: floor and headroom are clear from entrance to exit.", EditorStyles.wordWrappedMiniLabel);
            }

            List<string> breakers = RoomGeneratorWindow.FlowBreakers(values, plan);
            if (breakers.Count > 0)
            {
                EditorGUILayout.HelpBox("Stops a running player:\n- " + string.Join("\n- ", breakers), MessageType.Info);
            }
        }
    }
}

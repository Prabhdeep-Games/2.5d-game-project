using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheSpaceBetween.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Checks the Room Generator's window, scene fog, surface overlaps, gameplay elements, presets and the puzzle level
    /// setting, in a temporary scene that is never saved.
    /// </summary>
    public class RoomGeneratorToolTests
    {
        private const float PositionTolerance = 0.01f;
        private const string TempPresetPath = "Assets/Tests/EditMode/Temp Room Preset.asset";
        private const int RenderSize = 64;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempPresetPath);
        }

        private static RoomSettings Settings(RoomPurpose purpose, int seed = 1)
        {
            var settings = new RoomSettings();
            settings.ApplyPurpose(purpose);
            settings.SetSeed(seed);
            return settings;
        }

        private static void Set(RoomSettings settings, string field, object value)
        {
            FieldInfo info = typeof(RoomSettings).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(settings, value);
        }

        // Changes a value on the room's component as the Inspector does, then lets the live updater rebuild.
        private static void ChangeOnComponent(GeneratedRoom room, string property, System.Action<SerializedProperty> change)
        {
            RoomSettings before = room.Settings.Clone();
            var so = new SerializedObject(room);
            change(so.FindProperty("settings." + property));
            so.ApplyModifiedProperties();
            GeneratedRoomEditor.FinishEdit(room, before);
            RoomLiveUpdater.Flush();
        }

        //==================== WINDOW =====================
        [Test]
        public void Window_OpensAsADockedTab_NotAFloatingUtilityWindow()
        {
            bool wasOpen = HasOpenInstances();
            RoomGeneratorWindow.Open();
            RoomGeneratorWindow window = EditorWindow.GetWindow<RoomGeneratorWindow>();
            try
            {
                Assert.IsTrue(window.docked, "The Room Generator should open docked beside the Inspector.");
            }
            finally
            {
                if (!wasOpen)
                {
                    window.Close();
                }
            }
        }

        private static bool HasOpenInstances()
        {
            return EditorWindow.HasOpenInstances<RoomGeneratorWindow>();
        }

        //==================== FOG =====================
        private static RoomSettings FoggyRoom(Color fogColor, float start, float end)
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            Set(settings, "sceneFog", true);
            Set(settings, "fogMode", FogMode.Linear);
            Set(settings, "fogColor", fogColor);
            Set(settings, "fogStart", start);
            Set(settings, "fogEnd", end);
            return settings;
        }

        [Test]
        public void Fog_SetsTheScenesLightingFog_AndFollowsChangesStraightAway()
        {
            RenderSettings.fog = false;
            var first = new Color(0.2f, 0.3f, 0.4f);
            GeneratedRoom room = RoomBuilder.CreateRoom(FoggyRoom(first, 5f, 40f), Vector3.zero);

            Assert.IsTrue(RenderSettings.fog);
            Assert.AreEqual(FogMode.Linear, RenderSettings.fogMode);
            Assert.AreEqual(first, RenderSettings.fogColor);
            Assert.AreEqual(5f, RenderSettings.fogStartDistance);
            Assert.AreEqual(40f, RenderSettings.fogEndDistance);

            var second = new Color(0.7f, 0.1f, 0.1f);
            ChangeOnComponent(room, "fogColor", p => p.colorValue = second);
            ChangeOnComponent(room, "fogEnd", p => p.floatValue = 60f);
            Assert.AreEqual(second, RenderSettings.fogColor, "A fog change should reach the scene at once.");
            Assert.AreEqual(60f, RenderSettings.fogEndDistance);

            ChangeOnComponent(room, "sceneFog", p => p.boolValue = false);
            Assert.IsFalse(RenderSettings.fog, "Turning Scene Fog off should put the scene's own fog back.");
        }

        [Test]
        public void Fog_WorksWithoutSceneAmbient_AndAmbientWithoutFog()
        {
            RenderSettings.fog = false;
            Color ambientBefore = RenderSettings.ambientLight;
            RoomBuilder.CreateRoom(FoggyRoom(Color.gray, 5f, 40f), Vector3.zero);
            Assert.IsTrue(RenderSettings.fog);
            Assert.AreEqual(ambientBefore, RenderSettings.ambientLight, "Fog alone should not change the ambient light.");
        }

        [Test]
        public void Fog_IsRenderedByTheCamera()
        {
            RenderSettings.fog = false;
            var magenta = new Color(1f, 0f, 1f);
            GeneratedRoom room = RoomBuilder.CreateRoom(FoggyRoom(magenta, 1f, 12f), Vector3.zero);
            var cameraObject = new GameObject("Test Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(room.Settings.Width * 0.5f, 6f, -11f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            // Shaders compile in the background in the Editor, which can leave the first image empty, so they compile at once here.
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            Color withFog;
            Color withoutFog;
            try
            {
                RenderAverage(camera);
                withFog = RenderAverage(camera);
                ChangeOnComponent(room, "sceneFog", p => p.boolValue = false);
                withoutFog = RenderAverage(camera);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
            }

            float tintWith = (withFog.r + withFog.b) * 0.5f - withFog.g;
            float tintWithout = (withoutFog.r + withoutFog.b) * 0.5f - withoutFog.g;
            Assert.Greater(tintWith, tintWithout + 0.1f, $"The fog colour should show in the camera image: with fog {withFog}, without {withoutFog}.");
        }

        private static Color RenderAverage(Camera camera)
        {
            var target = new RenderTexture(RenderSize, RenderSize, 24);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = target };
                Assert.IsTrue(RenderPipeline.SupportsRenderRequest(camera, request), "The render pipeline should support rendering a camera on request.");
                RenderPipeline.SubmitRenderRequest(camera, request);

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0);
                RenderTexture.active = previous;

                Color sum = Color.clear;
                foreach (Color pixel in image.GetPixels())
                {
                    sum += pixel;
                }

                Object.DestroyImmediate(image);
                return sum / (RenderSize * RenderSize);
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        //==================== SURFACES =====================
        private static IEnumerable<RoomSettings> SurfaceVariations()
        {
            RoomPurpose[] purposes = { RoomPurpose.ObservationChamber, RoomPurpose.ContainmentCell, RoomPurpose.RestrictedCorridor, RoomPurpose.MonumentalHall, RoomPurpose.VerticalConnection };
            foreach (RoomPurpose purpose in purposes)
            {
                foreach (int seed in new[] { 1, 77, 4096 })
                {
                    yield return Settings(purpose, seed);
                }
            }

            RoomSettings busy = Settings(RoomPurpose.ObservationChamber, 9);
            Set(busy, "glassPartition", true);
            Set(busy, "securityCamera", true);
            Set(busy, "lowBarriers", 2);
            Set(busy, "interiorThresholds", 1);
            Set(busy, "wallFixtures", true);
            Set(busy, "width", 48f);
            yield return busy;

            RoomSettings stairs = Settings(RoomPurpose.RestrictedCorridor, 3);
            Set(stairs, "levelChange", RoomLevelChange.Stairs);
            Set(stairs, "exitRise", 2f);
            Set(stairs, "height", 9f);
            yield return stairs;

            RoomSettings corridor = Settings(RoomPurpose.RestrictedCorridor, 5);
            Set(corridor, "entrance", RoomThreshold.SealedDoor);
            Set(corridor, "exit", RoomThreshold.SealedDoor);
            yield return corridor;

            // Thick end walls with the camera at the entrance once put the camera inside the wall.
            RoomSettings thick = Settings(RoomPurpose.RestrictedCorridor, 63102);
            Set(thick, "wallThickness", 3.4f);
            Set(thick, "height", 24f);
            Set(thick, "cameraPosition", 0f);
            yield return thick;
        }

        [Test]
        public void GeneratedRooms_HaveNoOverlappingSurfaces()
        {
            var problems = new List<string>();
            foreach (RoomSettings settings in SurfaceVariations())
            {
                GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
                Assert.IsNotNull(room, $"{settings.Purpose} seed {settings.Seed} could not be built.");
                foreach (RoomGeometryCheck.Overlap overlap in RoomGeometryCheck.FindOverlaps(room.gameObject))
                {
                    problems.Add($"{settings.Purpose} seed {settings.Seed}: {overlap}");
                }

                Object.DestroyImmediate(room.gameObject);
            }

            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void ConnectedRooms_DoNotOverlapEachOther()
        {
            GeneratedRoom first = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            GeneratedRoom second = RoomBuilder.CreateRoom(Settings(RoomPurpose.VerticalConnection), first.transform.position + first.ExitOffset);
            GeneratedRoom third = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), second.transform.position + second.ExitOffset);

            var renderers = new List<Renderer>();
            foreach (GeneratedRoom room in new[] { first, second, third })
            {
                renderers.AddRange(room.GetComponentsInChildren<MeshRenderer>());
            }

            List<RoomGeometryCheck.Overlap> overlaps = RoomGeometryCheck.FindOverlaps(renderers);
            CollectionAssert.IsEmpty(overlaps, string.Join("\n", overlaps));
        }

        [Test]
        public void CurvedWallFill_MeetsTheCurve_WithoutAGapOrAnOverlap()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.VerticalConnection), Vector3.zero);
            var fill = room.GetComponentInChildren<CurvedWallFill>();
            Assert.IsNotNull(fill, "The curve should have its fill.");
            Bounds fillBounds = fill.GetComponent<MeshRenderer>().bounds;
            CurvedWall curve = fill.Wall;
            Vector3 lowestUnderside = curve.transform.TransformPoint(curve.UndersidePoint(0f));
            Vector3 endUnderside = curve.transform.TransformPoint(curve.UndersidePoint(Mathf.PI * 0.5f));

            Assert.AreEqual(lowestUnderside.y, fillBounds.min.y, PositionTolerance, "The fill should rest at the bottom of the curve.");
            Assert.AreEqual(endUnderside.y, fillBounds.max.y, PositionTolerance, "The fill should reach the top of the curve's underside.");
            Assert.AreEqual(endUnderside.x, fillBounds.max.x, PositionTolerance, "The fill should reach the curve's back.");
            CollectionAssert.IsEmpty(RoomGeometryCheck.FindOverlaps(new List<Renderer> { fill.GetComponent<MeshRenderer>(), curve.GetComponent<MeshRenderer>() }));
        }

        //==================== ELEMENTS =====================
        private static GameObject AddCatalogElement(GeneratedRoom room, RoomElementCatalog.Entry entry, float along)
        {
            GameObject prefab = RoomElementCatalog.Load(entry);
            return RoomBuilder.AddElement(room, prefab, RoomBuilder.DefaultElementPosition(room, entry, prefab, along), Quaternion.identity);
        }

        [Test]
        public void EveryCatalogElement_Exists()
        {
            CollectionAssert.IsEmpty(RoomElementCatalog.FindMissing());
        }

        [Test]
        public void EveryCatalogElement_IsAddedAsAnEditablePrefabInstance_InTheRoom()
        {
            RoomSettings settings = Settings(RoomPurpose.MonumentalHall);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            float along = 4f;
            foreach (RoomElementCatalog.Entry entry in RoomElementCatalog.Entries)
            {
                GameObject instance = AddCatalogElement(room, entry, along);
                along += 3f;
                Assert.IsNotNull(instance, entry.Name);
                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(instance), entry.Name + " should stay a prefab instance.");
                Assert.AreEqual(RoomElementCatalog.Load(entry), PrefabUtility.GetCorrespondingObjectFromSource(instance), entry.Name);
                Assert.IsTrue(instance.transform.IsChildOf(room.transform), entry.Name + " should belong to the room.");
                Assert.AreEqual(HideFlags.None, instance.hideFlags, entry.Name + " should be editable.");
            }

            Assert.AreEqual(RoomElementCatalog.Entries.Count, room.FindElements().Count);
        }

        [Test]
        public void Elements_StartOnTheFloor_OrUnderTheCeiling()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, new Vector3(3f, 2f, 0f));
            foreach (RoomElementCatalog.Entry entry in RoomElementCatalog.Entries)
            {
                GameObject instance = AddCatalogElement(room, entry, settings.Width * 0.5f);
                Bounds bounds = RoomElementCatalog.LocalBounds(instance);
                float localBottom = instance.transform.position.y - room.transform.position.y + bounds.min.y;
                float localTop = instance.transform.position.y - room.transform.position.y + bounds.max.y;
                float pivot = instance.transform.position.y - room.transform.position.y;
                switch (entry.Placement)
                {
                    case RoomElementCatalog.Placement.Floor:
                        Assert.AreEqual(entry.HeightOffset, localBottom, PositionTolerance, entry.Name + " should rest on the floor.");
                        break;
                    case RoomElementCatalog.Placement.Ceiling:
                        Assert.AreEqual(settings.Height - entry.HeightOffset, localTop, PositionTolerance, entry.Name + " should hang from the ceiling.");
                        break;
                    default:
                        Assert.AreEqual(entry.HeightOffset, pivot, PositionTolerance, entry.Name + " should have its pivot on the floor.");
                        break;
                }

                Assert.AreEqual(room.transform.position.z, instance.transform.position.z, PositionTolerance, entry.Name + " should start on the route.");
            }
        }

        [Test]
        public void Elements_KeepTheirEdits_WhenTheRoomIsRebuilt()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            var platformEntry = RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Moving Platform (Looping).prefab"));
            GameObject platform = AddCatalogElement(room, platformEntry, 10f);

            platform.transform.position = new Vector3(12f, 5f, 0.5f);
            var so = new SerializedObject(platform.GetComponent<MovingPlatform>());
            so.FindProperty("endOffset").vector3Value = new Vector3(0f, 7f, 0f);
            so.ApplyModifiedProperties();

            ChangeOnComponent(room, "width", p => p.floatValue = 44f);
            ChangeOnComponent(room, "seed", p => p.intValue = 321);

            Assert.IsTrue(platform != null, "Rebuilding the room should not replace its elements.");
            Assert.AreEqual(new Vector3(12f, 5f, 0.5f), platform.transform.position);
            Assert.AreEqual(new Vector3(0f, 7f, 0f), new SerializedObject(platform.GetComponent<MovingPlatform>()).FindProperty("endOffset").vector3Value);
            Assert.AreEqual(1, room.FindElements().Count);
        }

        [Test]
        public void RegeneratingAndClearing_KeepElements_AndEverythingElseInTheScene()
        {
            RoomSettings settings = Settings(RoomPurpose.ContainmentCell);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            GameObject crate = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Swap Crate (Housing).prefab")), 6f);
            var unrelated = new GameObject("Unrelated");
            GeneratedRoom other = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), new Vector3(100f, 0f, 0f));
            int otherCount = other.GetComponentsInChildren<Transform>().Length;

            RoomBuilder.Regenerate(room, settings);
            RoomBuilder.Regenerate(room, settings);
            Assert.IsTrue(crate != null);
            Assert.AreEqual(1, room.transform.Find("Elements").childCount, "Regenerating should not duplicate elements.");

            RoomBuilder.Clear(room);
            Assert.IsNull(room.transform.Find("Generated_Structure"), "Clearing should remove what was generated.");
            Assert.IsTrue(crate != null, "Clearing should leave elements.");
            Assert.IsTrue(unrelated != null);
            Assert.AreEqual(otherCount, other.GetComponentsInChildren<Transform>().Length);
        }

        [Test]
        public void RemovingAnElement_RemovesOnlyIt_AndUndoBringsItBack()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            GameObject door = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Sealed Door.prefab")), 8f);
            GameObject pad = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Pressure Pad.prefab")), 12f);

            // In the Editor every click is its own Undo group; in a test everything happens at once, so the groups are made here.
            Undo.IncrementCurrentGroup();
            RoomBuilder.RemoveElement(room, door);
            Assert.IsTrue(door == null);
            Assert.IsTrue(pad != null);
            Assert.AreEqual(1, room.FindElements().Count);

            Undo.PerformUndo();
            Assert.AreEqual(2, room.FindElements().Count, "Undo should bring the removed element back.");
        }

        [Test]
        public void AddingAnElement_IsOneUndoStep()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            Undo.IncrementCurrentGroup();
            GameObject crate = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Swap Crate (Housing).prefab")), 6f);
            Undo.PerformUndo();
            Assert.IsTrue(crate == null);
            Assert.IsTrue(room != null, "Undoing the element should leave the room.");
            Assert.AreEqual(0, room.FindElements().Count);
        }

        //==================== PRESETS =====================
        [Test]
        public void Preset_SavesTheRoomAndItsElements_AndGeneratesThemAgainElsewhere()
        {
            RoomSettings settings = Settings(RoomPurpose.Custom);
            Set(settings, "width", 30f);
            Set(settings, "height", 12f);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, new Vector3(5f, 0f, 0f));
            GameObject beacon = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Swap Beacon.prefab")), 10f);
            beacon.transform.position += new Vector3(1f, 3f, 0f);
            beacon.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Glass Partition.prefab")), 18f);
            Vector3 beaconInRoom = room.transform.InverseTransformPoint(beacon.transform.position);

            RoomPreset preset = RoomBuilder.SavePreset(room, TempPresetPath);
            Assert.AreEqual(2, preset.Elements.Count);

            GeneratedRoom copy = RoomBuilder.CreateRoomFromPreset(preset, new Vector3(200f, 10f, 0f));
            Assert.AreEqual(JsonUtility.ToJson(room.Settings), JsonUtility.ToJson(copy.Settings));
            Assert.AreEqual(2, copy.FindElements().Count);
            GameObject copiedBeacon = copy.FindElements()[0];
            Assert.AreEqual(beaconInRoom, copy.transform.InverseTransformPoint(copiedBeacon.transform.position));
            Assert.AreEqual(30f, copiedBeacon.transform.eulerAngles.z, PositionTolerance);
            Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(copiedBeacon));
        }

        //==================== PUZZLE LEVEL =====================
        [Test]
        public void IsPuzzleLevel_ChangesOnlyAdvice_NeverTheRoom()
        {
            RoomSettings settings = Settings(RoomPurpose.RestrictedCorridor);
            Set(settings, "glassPartition", true);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            var profile = new GameObject("Level Profile").AddComponent<LevelProfile>();
            string signature = room.BuiltSignature;
            int objects = room.GetComponentsInChildren<Transform>().Length;

            var so = new SerializedObject(profile);
            so.FindProperty("isPuzzleLevel").boolValue = true;
            so.ApplyModifiedProperties();
            RoomLiveUpdater.Flush();

            Assert.IsTrue(profile.IsPuzzleLevel);
            Assert.AreEqual(signature, room.BuiltSignature);
            Assert.AreEqual(objects, room.GetComponentsInChildren<Transform>().Length);
            CollectionAssert.IsNotEmpty(RoomGeneratorWindow.FlowBreakers(room.Settings, RoomLayout.Create(room.Settings)), "A glass partition interrupts a traversal level.");
        }

        //==================== INSPECTOR =====================
        private static bool IsShown(SerializedObject so, string field)
        {
            return ShowIfFields.IsShown(so.FindProperty("settings." + field), typeof(RoomSettings));
        }

        [Test]
        public void RoomOptions_ShowOnlyWhileTheirSwitchIsOn()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            Set(settings, "sceneFog", false);
            Set(settings, "fogMode", FogMode.Linear);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            var so = new SerializedObject(room);

            Assert.IsTrue(IsShown(so, "sceneFog"));
            Assert.IsFalse(IsShown(so, "fogMode"), "Fog options are hidden while Scene Fog is off.");
            Assert.IsFalse(IsShown(so, "fogStart"), "An option under a hidden option is hidden too.");

            so.FindProperty("settings.sceneFog").boolValue = true;
            Assert.IsTrue(IsShown(so, "fogMode"));
            Assert.IsTrue(IsShown(so, "fogStart"));
            Assert.IsFalse(IsShown(so, "fogDensity"), "Linear fog does not use Fog Density.");

            so.FindProperty("settings.fogMode").intValue = (int)FogMode.ExponentialSquared;
            Assert.IsFalse(IsShown(so, "fogStart"));
            Assert.IsTrue(IsShown(so, "fogDensity"));

            Assert.IsFalse(IsShown(so, "exitRise"), "Exit Rise is hidden while Level Change is None.");
            so.FindProperty("settings.levelChange").intValue = (int)RoomLevelChange.CurvedWall;
            Assert.IsTrue(IsShown(so, "exitRise"));
            Assert.AreEqual(room.Settings.SecurityCamera, IsShown(so, "cameraPosition"));
        }

        [Test]
        public void DrawGizmosOff_HidesTheGizmosOfEverythingInTheRoom()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            ArchitecturalBlock block = room.GetComponentInChildren<ArchitecturalBlock>();
            GameObject element = AddCatalogElement(room, RoomElementCatalog.Entries[0], 10f);
            Component elementPart = element.GetComponentInChildren<MonoBehaviour>();
            Component outside = new GameObject("Outside").AddComponent<ArchitecturalBlock>();
            Selection.activeGameObject = room.gameObject;
            Assert.IsTrue(GeneratedRoom.ShowsGizmos(block));
            Assert.IsTrue(GeneratedRoom.ShowsGizmos(elementPart));

            var so = new SerializedObject(room);
            so.FindProperty("drawGizmos").boolValue = false;
            so.ApplyModifiedProperties();

            Assert.IsFalse(GeneratedRoom.ShowsGizmos(room), "The room's own markers are hidden.");
            Assert.IsFalse(GeneratedRoom.ShowsGizmos(block), "Generated pieces are hidden.");
            Assert.IsFalse(GeneratedRoom.ShowsGizmos(elementPart), "Elements are hidden.");
            Assert.IsTrue(GeneratedRoom.ShowsGizmos(outside), "Objects outside the room keep their gizmos.");
        }

        [Test]
        public void ControlPanel_IsOfferedUnderSecurityAndAccess()
        {
            RoomElementCatalog.Entry panel = null;
            foreach (RoomElementCatalog.Entry entry in RoomElementCatalog.Entries)
            {
                if (entry.Name == "Control Panel")
                {
                    panel = entry;
                }
            }

            Assert.IsNotNull(panel);
            Assert.AreEqual(RoomElementCatalog.Category.SecurityAndAccess, panel.Category);
            Assert.IsNotNull(RoomElementCatalog.Load(panel));
        }

        //==================== OPTIONS IN EVERY PURPOSE =====================
        private static IEnumerable<(string name, System.Action<RoomSettings> turnOn)> Options()
        {
            yield return ("Stairs", s => Set(s, "levelChange", RoomLevelChange.Stairs));
            yield return ("Curved Wall", s => Set(s, "levelChange", RoomLevelChange.CurvedWall));
            yield return ("Observation Gallery", s => Set(s, "observationGallery", true));
            yield return ("Camera Locks Exit", s => { Set(s, "securityCamera", true); Set(s, "cameraLocksExit", true); });
            yield return ("Two Low Barriers", s => Set(s, "lowBarriers", 2));
            yield return ("Wall Jump Shaft", s => Set(s, "levelChange", RoomLevelChange.WallJumpShaft));
            yield return ("Gaps and a Hood", s => { Set(s, "platforming", true); Set(s, "floorGaps", 2); Set(s, "slideHoods", 1); });
        }

        [Test]
        public void EveryOption_WorksInEveryPurpose()
        {
            foreach (RoomPurpose purpose in System.Enum.GetValues(typeof(RoomPurpose)))
            {
                if (purpose == RoomPurpose.Custom)
                {
                    continue;
                }

                foreach ((string name, System.Action<RoomSettings> turnOn) in Options())
                {
                    RoomSettings before = Settings(purpose);
                    RoomSettings values = before.Clone();
                    turnOn(values);
                    RoomFitting.MakeRoomFor(values, before);

                    RoomPlan plan = RoomLayout.Create(values);
                    string context = $"{name} in {purpose}";
                    CollectionAssert.IsEmpty(plan.Errors, context);
                    Assert.AreEqual(0, plan.MissingPieces, context);
                    Assert.IsNull(RoomRouteCheck.FindProblem(plan), context);
                    foreach (string note in plan.Notes)
                    {
                        StringAssert.DoesNotContain("did not fit", note, context);
                        StringAssert.DoesNotContain("needs a door", note, context);
                        StringAssert.DoesNotContain("stays level", note, context);
                    }
                }
            }
        }

        [Test]
        public void MakingRoom_KeepsASizeSetByHand()
        {
            RoomSettings before = Settings(RoomPurpose.ContainmentCell);
            RoomSettings values = before.Clone();
            Set(values, "levelChange", RoomLevelChange.CurvedWall);
            RoomFitting.MakeRoomFor(values, before);
            Assert.Greater(values.Height, before.Height, "Turning on a curved wall raises the ceiling to fit it.");

            before = values.Clone();
            Set(values, "height", 8f);
            RoomFitting.MakeRoomFor(values, before);
            Assert.AreEqual(8f, values.Height, "A height set by hand is kept, and the plan reports what it needs instead.");
            Assert.IsFalse(RoomLayout.Create(values).CanBuild);
        }

        //==================== BAYS, FIXTURES AND PATCHES =====================
        private static int CountBlocks(RoomPlan plan, string prefix, string suffix = "")
        {
            int count = 0;
            foreach (BlockPlan block in plan.Blocks)
            {
                if (block.Name.StartsWith(prefix) && block.Name.EndsWith(suffix))
                {
                    count++;
                }
            }

            return count;
        }

        [Test]
        public void WallFixtures_LeaveRecessesAndRepairPatchesInPlace_InEveryPurpose()
        {
            foreach (RoomPurpose purpose in System.Enum.GetValues(typeof(RoomPurpose)))
            {
                for (int seed = 1; seed <= 5; seed++)
                {
                    RoomSettings off = Settings(purpose, seed);
                    Set(off, "wallFixtures", false);
                    RoomSettings on = off.Clone();
                    Set(on, "wallFixtures", true);
                    RoomPlan planOff = RoomLayout.Create(off);
                    RoomPlan planOn = RoomLayout.Create(on);
                    string context = $"{purpose}, seed {seed}";
                    Assert.AreEqual(CountBlocks(planOff, "Recess", "Plate"), CountBlocks(planOn, "Recess", "Plate"), context);
                    Assert.AreEqual(CountBlocks(planOff, "Repair Patch"), CountBlocks(planOn, "Repair Patch"), context);
                }
            }
        }

        [Test]
        public void WallFixtures_AppearInTheObservationChamber()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            Set(settings, "wallFixtures", true);
            RoomPlan plan = RoomLayout.Create(settings);
            Assert.Greater(plan.WallFixtureCount, 0, "The chamber has a bay with a plain wall, so Wall Fixtures should add one.");
            Assert.AreEqual(plan.WallFixtureCount, CountBlocks(plan, "Wall Fixture"));
        }

        [Test]
        public void RepairPatches_NeverOverlapAWallFixture()
        {
            foreach (RoomPurpose purpose in System.Enum.GetValues(typeof(RoomPurpose)))
            {
                for (int seed = 1; seed <= 10; seed++)
                {
                    RoomSettings settings = Settings(purpose, seed);
                    Set(settings, "wallFixtures", true);
                    Set(settings, "repairPanels", 8);
                    RoomPlan plan = RoomLayout.Create(settings);
                    foreach (BlockPlan patch in plan.Blocks)
                    {
                        if (!patch.Name.StartsWith("Repair Patch"))
                        {
                            continue;
                        }

                        var patchBounds = new Bounds(patch.Center, patch.Size);
                        foreach (BlockPlan fixture in plan.Blocks)
                        {
                            if (fixture.Name.StartsWith("Wall Fixture"))
                            {
                                Assert.IsFalse(patchBounds.Intersects(new Bounds(fixture.Center, fixture.Size)), $"{patch.Name} meets {fixture.Name} in {purpose}, seed {seed}.");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void ObservationChamber_GetsARepairPatch_AndSaysWhenNotAllFit()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            RoomPlan plan = RoomLayout.Create(settings);
            int patches = CountBlocks(plan, "Repair Patch");
            Assert.Greater(patches, 0);
            if (patches < settings.RepairPanels)
            {
                Assert.IsTrue(plan.Notes.Exists(n => n.StartsWith("Repair Patches:")), "A shortfall should be reported.");
            }
        }

        //==================== LIGHTING SUMMARY =====================
        [Test]
        public void LightingSummary_CountsLitFixtures_AndWarnsWhenMostHaveFailed()
        {
            RoomSettings settings = Settings(RoomPurpose.RestrictedCorridor);
            Set(settings, "failedFixtures", 0.75f);
            RoomPlan plan = RoomLayout.Create(settings);
            int spots = 0;
            foreach (LightPlan light in plan.Lights)
            {
                spots += light.Type == LightType.Spot ? 1 : 0;
            }

            Assert.AreEqual(CountBlocks(plan, "Ceiling Fixture", "Panel"), plan.CeilingFixtureCount);
            Assert.AreEqual(spots, plan.CeilingFixturesLit);
            Assert.GreaterOrEqual(plan.CeilingFixturesLit, 1, "At least one ceiling fixture always works.");
            Assert.Less(plan.CeilingFixturesLit * 2, plan.CeilingFixtureCount);
            Assert.IsTrue(plan.LightingNotes.Exists(n => n.Contains("give light")), "Most fixtures failing should be pointed out.");
        }

        //==================== PRESET OVERWRITE =====================
        [Test]
        public void SavingOverAPreset_ReplacesItInPlace_AndKeepsItsNotes()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            RoomPreset first = RoomBuilder.SavePreset(room, TempPresetPath);
            string guid = AssetDatabase.AssetPathToGUID(TempPresetPath);
            var so = new SerializedObject(first);
            so.FindProperty("notes").stringValue = "Keep me";
            so.ApplyModifiedPropertiesWithoutUndo();

            AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Swap Beacon.prefab")), 10f);
            RoomPreset second = RoomBuilder.SavePreset(room, TempPresetPath);

            Assert.AreSame(first, second);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(TempPresetPath));
            Assert.AreEqual(1, second.Elements.Count, "The replaced preset should hold the room as it is now.");
            Assert.AreEqual("Keep me", second.Notes);
            Assert.AreEqual(1, AssetDatabase.FindAssets("Temp Room Preset", new[] { "Assets/Tests/EditMode" }).Length, "No second copy should be made.");
        }

        //==================== ELEMENT ADVICE =====================
        [Test]
        public void ElementAdvice_FlagsAPadWithoutTargets_AndAnObservationPointWithoutItsCamera()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            GameObject pad = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Pressure Pad.prefab")), 12f);
            Assert.IsTrue(RoomElementCatalog.Advice(pad).Exists(a => a.type == MessageType.Warning && a.text.Contains("Targets")));

            GameObject door = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Security Door.prefab")), 20f);
            var panel = new SerializedObject(pad.GetComponent<ControlPanel>());
            SerializedProperty targets = panel.FindProperty("targets");
            targets.arraySize = 1;
            targets.GetArrayElementAtIndex(0).objectReferenceValue = door.GetComponentInChildren<Mechanism>();
            panel.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsFalse(RoomElementCatalog.Advice(pad).Exists(a => a.text.Contains("Targets")));

            GameObject point = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Level/Observation Point.prefab")), 6f);
            Assert.IsTrue(RoomElementCatalog.Advice(point).Exists(a => a.text.Contains("Observation Camera Controller")));
            new GameObject("Observation Camera").AddComponent<ObservationCameraController>();
            Assert.IsFalse(RoomElementCatalog.Advice(point).Exists(a => a.text.Contains("Observation Camera Controller")));
        }

        //==================== SWAP CRATE =====================
        [Test]
        public void SwapCrate_InTheCatalog_IsThePaleCrateFromPlaytestFull()
        {
            RoomElementCatalog.Entry entry = null;
            foreach (RoomElementCatalog.Entry candidate in RoomElementCatalog.Entries)
            {
                if (candidate.Name == "Swap Crate")
                {
                    entry = candidate;
                }
            }

            Assert.IsNotNull(entry);
            GameObject prefab = RoomElementCatalog.Load(entry);
            Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(prefab), "It should be a variant of the crate, not a copy.");
            Assert.AreEqual("Assets/Prefabs/Environment/Swap Crate.prefab", AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(prefab)));
            Assert.AreEqual("ARC_SwapHousing", prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial.name);
            Assert.IsNotNull(prefab.GetComponent<Swappable>());
            Assert.IsNotNull(prefab.GetComponent<Rigidbody>());
        }

        //==================== NEW ROOM PLACEMENT =====================
        [Test]
        public void Window_NewRoomOrigin_FollowsThePlacement()
        {
            bool wasOpen = HasOpenInstances();
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ContainmentCell), new Vector3(10f, 2f, 0f));
            RoomGeneratorWindow.Open();
            RoomGeneratorWindow window = EditorWindow.GetWindow<RoomGeneratorWindow>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(RoomGeneratorWindow).GetField("placement", flags).SetValue(window, RoomGeneratorWindow.Placement.AtPosition);
                typeof(RoomGeneratorWindow).GetField("newRoomPosition", flags).SetValue(window, new Vector3(50f, 0f, 0f));
                Assert.AreEqual(new Vector3(50f, 0f, 0f), window.NewRoomOrigin());

                Selection.activeGameObject = room.gameObject;
                typeof(RoomGeneratorWindow).GetMethod("OnSelectionChanged", flags).Invoke(window, null);
                Assert.AreEqual(new Vector3(50f, 0f, 0f), window.NewRoomOrigin(), "At Position ignores the selection.");

                typeof(RoomGeneratorWindow).GetField("placement", flags).SetValue(window, RoomGeneratorWindow.Placement.AfterSelectedRoom);
                Assert.AreEqual(room.transform.position + room.ExitOffset, window.NewRoomOrigin());
            }
            finally
            {
                Selection.activeGameObject = null;
                if (!wasOpen)
                {
                    window.Close();
                }
            }
        }
            //==================== GIZMOS FOLLOW THE SELECTION =====================
        [Test]
        public void GizmosInARoom_ShowOnlyWhileThatRoomOrSomethingInItIsSelected()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            GeneratedRoom other = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), room.ExitPoint);
            ArchitecturalBlock block = room.GetComponentInChildren<ArchitecturalBlock>();
            GameObject element = AddCatalogElement(room, RoomElementCatalog.Entries[0], 10f);
            Component outside = new GameObject("Outside").AddComponent<ArchitecturalBlock>();

            Selection.activeGameObject = null;
            Assert.IsFalse(GeneratedRoom.ShowsGizmos(block), "Nothing selected: the room's gizmos are hidden.");
            Assert.IsTrue(GeneratedRoom.ShowsGizmos(outside), "Objects outside rooms keep their gizmos.");

            Selection.activeGameObject = element;
            Assert.IsTrue(GeneratedRoom.ShowsGizmos(block), "Selecting something in the room shows them.");

            Selection.activeGameObject = other.gameObject;
            Assert.IsFalse(GeneratedRoom.ShowsGizmos(block), "Selecting another room hides them.");
            Assert.IsTrue(GeneratedRoom.ShowsGizmos(other.GetComponentInChildren<ArchitecturalBlock>()));

            Selection.activeGameObject = outside.gameObject;
            Assert.IsFalse(GeneratedRoom.ShowsGizmos(block), "Selecting an unrelated object hides them.");
            Assert.IsTrue(GeneratedRoom.ShowsGizmos(outside));
        }

        //==================== CONNECTED ROOMS FOLLOW A RESIZE =====================
        private static string Snapshot(GeneratedRoom room)
        {
            var text = new System.Text.StringBuilder(JsonUtility.ToJson(room.Settings));
            text.Append(room.transform.rotation.eulerAngles.ToString("F3"));
            foreach (Transform child in room.GetComponentsInChildren<Transform>(true))
            {
                if (child != room.transform)
                {
                    text.Append(child.name).Append(room.transform.InverseTransformPoint(child.position).ToString("F3"));
                }
            }

            return text.ToString();
        }

        private static void SetWidth(GeneratedRoom room, float width)
        {
            ChangeOnComponent(room, "width", p => p.floatValue = width);
            Assert.AreEqual(width, room.Settings.Width, 0.001f);
        }

        [Test]
        public void WideningARoom_MovesOnlyTheRoomsConnectedAfterIt_ByTheChange_WithoutChangingThem()
        {
            RoomSettings corridor = Settings(RoomPurpose.RestrictedCorridor, 101);
            Set(corridor, "width", 40f);
            GeneratedRoom before = RoomBuilder.CreateRoom(Settings(RoomPurpose.ContainmentCell), new Vector3(-30f, 0f, 0f));
            GeneratedRoom resized = RoomBuilder.CreateRoom(corridor, before.ExitPoint);
            GeneratedRoom second = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), resized.ExitPoint);
            GeneratedRoom third = RoomBuilder.CreateRoom(Settings(RoomPurpose.VerticalConnection), second.ExitPoint);
            GeneratedRoom apart = RoomBuilder.CreateRoom(Settings(RoomPurpose.MonumentalHall), new Vector3(0f, 0f, 60f));
            var handPlaced = new GameObject("Placed By Hand");
            handPlaced.transform.SetParent(second.transform, false);
            handPlaced.transform.localPosition = new Vector3(3f, 1f, 0f);
            AddCatalogElement(second, RoomElementCatalog.Entries[0], 10f);

            Assert.AreEqual(resized, second.Follows, "A room generated at another's exit follows it.");
            Assert.AreEqual(second, third.Follows);
            Assert.IsNull(apart.Follows);
            Vector3 beforeAt = before.transform.position;
            Vector3 resizedAt = resized.transform.position;
            Vector3 secondAt = second.transform.position;
            Vector3 thirdAt = third.transform.position;
            Vector3 apartAt = apart.transform.position;
            string secondLook = Snapshot(second);
            string thirdLook = Snapshot(third);
            GameObject secondPiece = second.GeneratedObjects[0];

            SetWidth(resized, 60f);

            Assert.AreEqual(beforeAt, before.transform.position, "The room before stays.");
            Assert.AreEqual(resizedAt, resized.transform.position, "The resized room is the anchor.");
            Assert.That(Vector3.Distance(secondAt + new Vector3(20f, 0f, 0f), second.transform.position), Is.LessThan(PositionTolerance), $"Moved by the 20 added. At {second.transform.position}");
            Assert.That(Vector3.Distance(thirdAt + new Vector3(20f, 0f, 0f), third.transform.position), Is.LessThan(PositionTolerance), "The rooms after it move too.");
            Assert.AreEqual(apartAt, apart.transform.position, "A room that is not connected stays.");
            Assert.That(Vector3.Distance(resized.ExitPoint, second.transform.position), Is.LessThan(PositionTolerance), "Still joined.");
            Assert.AreEqual(secondLook, Snapshot(second), "Its values, turn, pieces, elements and hand-placed objects are unchanged.");
            Assert.AreEqual(thirdLook, Snapshot(third));
            Assert.AreEqual(secondPiece, second.GeneratedObjects[0], "The moved room is not rebuilt.");
        }

        [Test]
        public void NarrowingAndResizingAgain_PlacesTheRoomsFromTheCurrentExit_WithNoDrift()
        {
            RoomSettings corridor = Settings(RoomPurpose.RestrictedCorridor, 101);
            Set(corridor, "width", 40f);
            GeneratedRoom resized = RoomBuilder.CreateRoom(corridor, Vector3.zero);
            GeneratedRoom second = RoomBuilder.CreateRoom(Settings(RoomPurpose.ChaseRun), resized.ExitPoint);
            GeneratedRoom third = RoomBuilder.CreateRoom(Settings(RoomPurpose.ContainmentCell), second.ExitPoint);
            Vector3 secondAt = second.transform.position;
            Vector3 thirdAt = third.transform.position;

            SetWidth(resized, 30f);
            Assert.That(Vector3.Distance(secondAt - new Vector3(10f, 0f, 0f), second.transform.position), Is.LessThan(PositionTolerance), "Narrowing pulls the rooms back.");

            foreach (float width in new[] { 60f, 52.5f, 33f, 70f, 41f, 40f })
            {
                SetWidth(resized, width);
                Assert.That(Vector3.Distance(resized.ExitPoint, second.transform.position), Is.LessThan(PositionTolerance), $"Joined at width {width}.");
                Assert.That(Vector3.Distance(second.ExitPoint, third.transform.position), Is.LessThan(PositionTolerance));
            }

            Assert.That(Vector3.Distance(secondAt, second.transform.position), Is.LessThan(0.0001f), "Back at the same width, exactly where it was.");
            Assert.That(Vector3.Distance(thirdAt, third.transform.position), Is.LessThan(0.0001f));
        }

        [Test]
        public void ARiseChange_InAnotherRoomType_MovesTheRoomsAfterItUpOrDown()
        {
            GeneratedRoom climb = RoomBuilder.CreateRoom(Settings(RoomPurpose.VerticalConnection), Vector3.zero);
            GeneratedRoom after = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), climb.ExitPoint);
            Vector3 afterAt = after.transform.position;
            ChangeOnComponent(climb, "exitRise", p => p.floatValue = climb.Settings.ExitRise - 4f);
            Assert.That(Vector3.Distance(afterAt + new Vector3(0f, -4f, 0f), after.transform.position), Is.LessThan(PositionTolerance), $"At {after.transform.position}");
        }

        [Test]
        public void Branches_BothFollowTheResizedRoom()
        {
            GeneratedRoom hub = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            GeneratedRoom left = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), hub.ExitPoint);
            GeneratedRoom right = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor, 7), hub.ExitPoint + new Vector3(0f, 0f, 30f));
            right.transform.position = hub.ExitPoint;
            Assert.AreEqual(1, RoomBuilder.LinkRoomsByPosition(right.gameObject.scene), "The second branch is linked by position.");
            Assert.AreEqual(hub, right.Follows);

            SetWidth(hub, hub.Settings.Width + 8f);
            Assert.That(Vector3.Distance(hub.ExitPoint, left.transform.position), Is.LessThan(PositionTolerance));
            Assert.That(Vector3.Distance(hub.ExitPoint, right.transform.position), Is.LessThan(PositionTolerance));
        }

        [Test]
        public void ATurnedRoom_IsBuiltTurned_AndItsFollowersMoveAlongItsOwnDirection()
        {
            GeneratedRoom turned = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), Vector3.zero);
            turned.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            RoomBuilder.Regenerate(turned, turned.Settings);
            Vector3 exitDirection = (turned.ExitPoint - turned.transform.position).normalized;
            Assert.That(Vector3.Distance(new Vector3(0f, 0f, -1f), exitDirection), Is.LessThan(0.001f), $"The exit is along the room's own direction. {exitDirection}");

            GeneratedRoom straight = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), new Vector3(0f, 0f, 200f));
            ArchitecturalBlock[] turnedBlocks = turned.GetComponentsInChildren<ArchitecturalBlock>();
            ArchitecturalBlock[] straightBlocks = straight.GetComponentsInChildren<ArchitecturalBlock>();
            Assert.AreEqual(straightBlocks.Length, turnedBlocks.Length);
            for (int i = 0; i < turnedBlocks.Length; i++)
            {
                Vector3 a = turned.transform.InverseTransformPoint(turnedBlocks[i].transform.position);
                Vector3 b = straight.transform.InverseTransformPoint(straightBlocks[i].transform.position);
                Assert.That(Vector3.Distance(a, b), Is.LessThan(PositionTolerance), $"'{turnedBlocks[i].name}' is laid out along the turned room, as in an unturned one.");
            }

            GeneratedRoom follower = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), turned.ExitPoint);
            Assert.AreEqual(turned, follower.Follows);
            Assert.That(Quaternion.Angle(turned.transform.rotation, follower.transform.rotation), Is.LessThan(0.01f), "A room after a turned room takes its turn.");
            Vector3 followerAt = follower.transform.position;

            SetWidth(turned, turned.Settings.Width + 12f);
            Assert.That(Vector3.Distance(followerAt + exitDirection * 12f, follower.transform.position), Is.LessThan(PositionTolerance), $"At {follower.transform.position}");
        }

        [Test]
        public void UndoAndRedo_RestoreTheSizeAndTheConnectedPositionsTogether()
        {
            RoomSettings corridor = Settings(RoomPurpose.RestrictedCorridor, 101);
            Set(corridor, "width", 40f);
            GeneratedRoom resized = RoomBuilder.CreateRoom(corridor, Vector3.zero);
            GeneratedRoom second = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), resized.ExitPoint);
            Vector3 secondAt = second.transform.position;
            Undo.IncrementCurrentGroup();

            SetWidth(resized, 60f);
            Undo.IncrementCurrentGroup();
            Vector3 movedTo = second.transform.position;

            Undo.PerformUndo();
            RoomLiveUpdater.Flush();
            Assert.AreEqual(40f, resized.Settings.Width, 0.001f, "One Undo restores the width.");
            Assert.That(Vector3.Distance(secondAt, second.transform.position), Is.LessThan(PositionTolerance), "And the connected room's position.");

            Undo.PerformRedo();
            RoomLiveUpdater.Flush();
            Assert.AreEqual(60f, resized.Settings.Width, 0.001f, "Redo brings the width back.");
            Assert.That(Vector3.Distance(movedTo, second.transform.position), Is.LessThan(PositionTolerance), "And the move.");
        }

        [Test]
        public void ARoomThatIsNotLinked_NeverMoves_EvenIfItSitsAtTheExit()
        {
            GeneratedRoom resized = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), Vector3.zero);
            GeneratedRoom loose = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), new Vector3(0f, 0f, 50f));
            loose.transform.position = resized.ExitPoint;
            Vector3 looseAt = loose.transform.position;
            SetWidth(resized, resized.Settings.Width + 10f);
            Assert.AreEqual(looseAt, loose.transform.position, "Only linked rooms move; Link Rooms in Scene links it.");
        }

        [Test]
        public void ARoomNamedByHand_KeepsItsName_WhenRebuilt_AndAGeneratedNameFollowsTheSeed()
        {
            GeneratedRoom named = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor, 5), Vector3.zero);
            GeneratedRoom automatic = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor, 6), named.ExitPoint);
            named.gameObject.name = "Room 1 Arrival";
            ChangeOnComponent(named, "seed", p => p.intValue = 77);
            ChangeOnComponent(automatic, "seed", p => p.intValue = 78);
            Assert.AreEqual("Room 1 Arrival", named.gameObject.name);
            Assert.AreEqual("Room_RestrictedCorridor_78", automatic.gameObject.name);
        }

        [Test]
        public void Window_IsCalledProceduralRoomGenerator()
        {
            RoomGeneratorWindow.Open();
            RoomGeneratorWindow window = EditorWindow.GetWindow<RoomGeneratorWindow>();
            try
            {
                Assert.AreEqual("Procedural Room Generator", window.titleContent.text);
                Assert.IsTrue(EditorApplication.ExecuteMenuItem("Tools/Procedural Room Generator"), "The menu item has the new name.");
            }
            finally
            {
                window.Close();
            }
        }
    }
}

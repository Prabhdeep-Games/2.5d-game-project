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
            GameObject crate = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Swap Crate.prefab")), 6f);
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
            GameObject crate = AddCatalogElement(room, RoomElementCatalog.Find(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Swap Crate.prefab")), 6f);
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
    }
}

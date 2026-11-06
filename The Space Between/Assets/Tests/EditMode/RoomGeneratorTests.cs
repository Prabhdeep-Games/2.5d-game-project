using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TheSpaceBetween.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Checks the Room Generator with the project's real materials and prefabs, in a temporary scene that is never saved.
    /// How a room looks and whether it can really be played are checked separately, in the game camera and in Play Mode.
    /// </summary>
    public class RoomGeneratorTests
    {
        private const float OverlapTolerance = 0.001f;
        private const float RouteHalfDepth = 2f;

        private static readonly RoomPurpose[] Purposes =
        {
            RoomPurpose.ObservationChamber,
            RoomPurpose.ContainmentCell,
            RoomPurpose.RestrictedCorridor,
            RoomPurpose.MonumentalHall,
            RoomPurpose.VerticalConnection
        };

        private static readonly RoomLightingMode[] LightingModes =
        {
            RoomLightingMode.FunctionalInstitutional,
            RoomLightingMode.SparseFailed,
            RoomLightingMode.HighContrastObservation,
            RoomLightingMode.Emergency
        };

        private static readonly int[] Seeds = { 1, 2, 77, 4096, 99999 };

        // The test runner puts the open scenes back when the run ends, so each test can start from an empty one.
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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

        private static IEnumerable<RoomSettings> AllVariations()
        {
            foreach (RoomPurpose purpose in Purposes)
            {
                foreach (int seed in Seeds)
                {
                    foreach (RoomLightingMode mode in LightingModes)
                    {
                        RoomSettings settings = Settings(purpose, seed);
                        settings.ApplyLighting(mode);
                        yield return settings;
                    }
                }
            }
        }

        private static string Describe(RoomSettings settings)
        {
            return $"{settings.Purpose} seed {settings.Seed} {settings.LightingMode}";
        }

        private static Bounds BoundsOf(BlockPlan block)
        {
            return new Bounds(block.Center, block.Size);
        }

        private static bool Overlap(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB)
        {
            return minA.x < maxB.x - OverlapTolerance && maxA.x > minB.x + OverlapTolerance
                && minA.y < maxB.y - OverlapTolerance && maxA.y > minB.y + OverlapTolerance
                && minA.z < maxB.z - OverlapTolerance && maxA.z > minB.z + OverlapTolerance;
        }

        //==================== ASSETS =====================
        [Test]
        public void EveryAsset_IsFound()
        {
            CollectionAssert.IsEmpty(RoomAssetLibrary.FindMissing());
        }

        [Test]
        public void Window_Opens()
        {
            RoomGeneratorWindow.Open();
            RoomGeneratorWindow window = EditorWindow.GetWindow<RoomGeneratorWindow>();
            Assert.IsNotNull(window);
            window.Close();
        }

        [Test]
        public void Window_SelectingARoom_LoadsItsValues_AndRegenerateAppliesEdits()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ContainmentCell, 42), Vector3.zero);
            RoomGeneratorWindow.Open();
            RoomGeneratorWindow window = EditorWindow.GetWindow<RoomGeneratorWindow>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo settingsField = typeof(RoomGeneratorWindow).GetField("settings", flags);
            try
            {
                Selection.activeGameObject = room.transform.Find("Generated_Structure/Floor").gameObject;
                typeof(RoomGeneratorWindow).GetMethod("OnSelectionChanged", flags).Invoke(window, null);
                var loaded = (RoomSettings)settingsField.GetValue(window);
                Assert.AreEqual(RoomPurpose.ContainmentCell, loaded.Purpose);
                Assert.AreEqual(42, loaded.Seed);

                Set(loaded, "width", 27f);
                typeof(RoomGeneratorWindow).GetMethod("RegenerateSelected", flags).Invoke(window, null);
                Assert.AreEqual(27f, room.Settings.Width);
                Assert.AreEqual(27f, room.ExitOffset.x);
                Assert.AreEqual(room.gameObject, Selection.activeGameObject, "The room should stay selected, so it can still be edited.");
            }
            finally
            {
                Selection.activeGameObject = null;
                window.Close();
            }
        }

        //==================== PLANS =====================
        [Test]
        public void EveryPurpose_AtEverySeedAndLightingMode_CanBeBuilt_WithAClearRoute()
        {
            foreach (RoomSettings settings in AllVariations())
            {
                RoomPlan plan = RoomLayout.Create(settings);
                CollectionAssert.IsEmpty(plan.Errors, Describe(settings));
                Assert.IsNull(RoomRouteCheck.FindProblem(plan), Describe(settings));
            }
        }

        [Test]
        public void SolidPieces_NeverOverlap()
        {
            foreach (RoomSettings settings in AllVariations())
            {
                RoomPlan plan = RoomLayout.Create(settings);
                for (int a = 0; a < plan.Blocks.Count; a++)
                {
                    BlockPlan first = plan.Blocks[a];
                    if (!first.Solid)
                    {
                        continue;
                    }

                    for (int b = a + 1; b < plan.Blocks.Count; b++)
                    {
                        BlockPlan second = plan.Blocks[b];
                        Assert.IsFalse(second.Solid && Overlap(first.Min, first.Max, second.Min, second.Max), $"{first.Name} overlaps {second.Name} in {Describe(settings)}");
                    }
                }
            }
        }

        [Test]
        public void Walls_LeaveNoGaps_FromFloorToCeiling_AlongTheWholeRoom()
        {
            foreach (RoomPurpose purpose in Purposes)
            {
                RoomSettings settings = Settings(purpose);
                RoomPlan plan = RoomLayout.Create(settings);
                float covered = 0f;
                var edges = new List<BlockPlan>(plan.Blocks);
                edges.RemoveAll(b => !b.Solid || !(b.Name.StartsWith("Back Wall") || b.Name.StartsWith("Window Bay") || b.Name.StartsWith("Recess")));
                edges.Sort((a, b) => a.Min.x.CompareTo(b.Min.x));
                foreach (BlockPlan block in edges)
                {
                    Assert.LessOrEqual(block.Min.x, covered + OverlapTolerance, $"Gap before {block.Name} in {purpose}");
                    covered = Mathf.Max(covered, block.Max.x);
                }

                Assert.AreEqual(settings.Width, covered, 0.01f, purpose.ToString());
            }
        }

        [Test]
        public void LightsAndFixtures_StayOutOfSolidGeometry()
        {
            foreach (RoomSettings settings in AllVariations())
            {
                RoomPlan plan = RoomLayout.Create(settings);
                foreach (BlockPlan solid in plan.Blocks)
                {
                    if (!solid.Solid)
                    {
                        continue;
                    }

                    Bounds bounds = BoundsOf(solid);
                    foreach (LightPlan light in plan.Lights)
                    {
                        Assert.IsFalse(bounds.Contains(light.Position), $"{light.Name} is inside {solid.Name} in {Describe(settings)}");
                    }

                    foreach (BlockPlan fixture in plan.Blocks)
                    {
                        if (fixture.Group == RoomGroup.LightFixtures)
                        {
                            Assert.IsFalse(Overlap(fixture.Min, fixture.Max, solid.Min, solid.Max), $"{fixture.Name} is inside {solid.Name} in {Describe(settings)}");
                        }
                    }
                }
            }
        }

        [Test]
        public void Fixtures_StayOffTheRoute()
        {
            foreach (RoomSettings settings in AllVariations())
            {
                float headHeight = RoomLayout.Create(settings).ExitOffset.y + settings.OpeningHeight;
                foreach (BlockPlan block in RoomLayout.Create(settings).Blocks)
                {
                    if (block.Group == RoomGroup.LightFixtures)
                    {
                        bool onRoute = block.Min.z < RouteHalfDepth && block.Max.z > -RouteHalfDepth;
                        Assert.IsTrue(!onRoute || block.Min.y >= settings.Height - 1f || block.Min.y > headHeight, $"{block.Name} in {Describe(settings)}");
                    }
                }
            }
        }

        [Test]
        public void SameSeedAndSettings_GiveTheSamePlan_AndDifferentSeeds_DoNot()
        {
            string first = Fingerprint(RoomLayout.Create(Settings(RoomPurpose.ContainmentCell, 5)));
            string again = Fingerprint(RoomLayout.Create(Settings(RoomPurpose.ContainmentCell, 5)));
            Assert.AreEqual(first, again);

            var results = new HashSet<string>();
            foreach (int seed in Seeds)
            {
                results.Add(Fingerprint(RoomLayout.Create(Settings(RoomPurpose.ContainmentCell, seed))));
            }

            Assert.Greater(results.Count, 1);
        }

        private static string Fingerprint(RoomPlan plan)
        {
            var text = new System.Text.StringBuilder();
            foreach (BlockPlan block in plan.Blocks)
            {
                text.Append(block.Name).Append(block.Min).Append(block.Max).Append(block.Material).Append(';');
            }

            foreach (LightPlan light in plan.Lights)
            {
                text.Append(light.Name).Append(light.Position).Append(light.Intensity).Append(';');
            }

            return text.ToString();
        }

        [Test]
        public void Seed_KeepsTheStructureTheSame()
        {
            RoomPlan one = RoomLayout.Create(Settings(RoomPurpose.ObservationChamber, 1));
            RoomPlan two = RoomLayout.Create(Settings(RoomPurpose.ObservationChamber, 4096));
            Assert.AreEqual(one.ExitOffset, two.ExitOffset);
            Assert.AreEqual(one.Prefabs.Count, two.Prefabs.Count);
        }

        [Test]
        public void TooShortARoom_ForItsLevelChange_IsRefused_WithTheWidthItNeeds()
        {
            RoomSettings settings = Settings(RoomPurpose.VerticalConnection);
            Set(settings, "width", 12f);
            RoomPlan plan = RoomLayout.Create(settings);
            Assert.IsFalse(plan.CanBuild);
            StringAssert.Contains("Width needs to be at least", plan.Errors[0]);
            Assert.IsNull(RoomBuilder.CreateRoom(settings, Vector3.zero));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("RoomGenerator"));
        }

        [Test]
        public void SolidEntrance_IsCaughtByTheRouteCheck()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            Set(settings, "entrance", RoomThreshold.Solid);
            StringAssert.Contains("across the route", RoomRouteCheck.FindProblem(RoomLayout.Create(settings)));
        }

        [Test]
        public void GlassPartition_IsReportedAsBlockingTheRoute()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            Set(settings, "glassPartition", true);
            RoomPlan plan = RoomLayout.Create(settings);
            Assert.IsTrue(plan.Prefabs.Exists(p => p.Prefab == RoomPrefab.GlassPartition));
            Assert.IsTrue(plan.Notes.Exists(n => n.Contains("Glass Partition blocks the route")));
        }

        [Test]
        public void DifferentLightingModes_GiveDifferentLights()
        {
            var results = new HashSet<string>();
            foreach (RoomLightingMode mode in LightingModes)
            {
                RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
                settings.ApplyLighting(mode);
                results.Add(Fingerprint(RoomLayout.Create(settings)));
            }

            Assert.AreEqual(LightingModes.Length, results.Count);
        }

        [Test]
        public void ShadowCastingLights_StayWithinTheLimit()
        {
            foreach (RoomSettings settings in AllVariations())
            {
                int shadows = 0;
                foreach (LightPlan light in RoomLayout.Create(settings).Lights)
                {
                    shadows += light.Shadows ? 1 : 0;
                }

                Assert.LessOrEqual(shadows, settings.ShadowCasters, Describe(settings));
            }
        }

        [Test]
        public void GalleryWindow_SitsInsideItsWall_AndGalleryIsBehindTheBackWall()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            RoomPlan plan = RoomLayout.Create(settings);
            List<BlockPlan> gallery = plan.Blocks.FindAll(b => b.Group == RoomGroup.Gallery);
            Assert.IsNotEmpty(gallery);
            foreach (BlockPlan block in gallery)
            {
                Assert.GreaterOrEqual(block.Min.z, settings.Depth, block.Name + " must not reach into the room");
            }
        }

        //==================== BUILDING =====================
        [Test]
        public void GeneratedRoom_UsesTheRealMaterialsAndPrefabs_WithoutCreatingAssets()
        {
            string[] before = AssetDatabase.FindAssets("t:Material t:Prefab");
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            string[] after = AssetDatabase.FindAssets("t:Material t:Prefab");
            Assert.AreEqual(before.Length, after.Length);

            foreach (MeshRenderer renderer in room.GetComponentsInChildren<MeshRenderer>())
            {
                Assert.IsTrue(AssetDatabase.Contains(renderer.sharedMaterial), renderer.name + " uses a material that is not an asset");
            }

            foreach (ArchitecturalBlock block in room.GetComponentsInChildren<ArchitecturalBlock>())
            {
                Assert.AreEqual(Vector3.one, block.transform.lossyScale, block.name);
                Assert.AreEqual(Quaternion.identity, block.transform.rotation, block.name);
            }

            Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(room.GetComponentInChildren<AccessDoor>()));
        }

        [Test]
        public void Blocks_AreBuiltWhereThePlanSays_RelativeToTheRoom()
        {
            var position = new Vector3(100f, 7f, 0f);
            RoomSettings settings = Settings(RoomPurpose.RestrictedCorridor);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, position);
            RoomPlan plan = RoomLayout.Create(settings);
            Transform floor = room.transform.Find("Generated_Structure/Floor");
            BlockPlan expected = plan.Blocks.Find(b => b.Name == "Floor");
            Assert.AreEqual(position + expected.Center, floor.position);
            Assert.AreEqual(expected.Size, floor.GetComponent<BoxCollider>().size);
        }

        [Test]
        public void EveryWallRunSurface_IsACurvedWall()
        {
            foreach (RoomPurpose purpose in Purposes)
            {
                GeneratedRoom room = RoomBuilder.CreateRoom(Settings(purpose), Vector3.zero);
                foreach (WallRunSurface surface in room.GetComponentsInChildren<WallRunSurface>())
                {
                    Assert.IsNotNull(surface.GetComponent<CurvedWall>(), surface.name);
                }
            }

            GeneratedRoom climb = RoomBuilder.CreateRoom(Settings(RoomPurpose.VerticalConnection), Vector3.zero);
            Assert.AreEqual(1, climb.GetComponentsInChildren<CurvedWall>().Length);
        }

        [Test]
        public void PanelDoor_ComesConnected_AndPlayerIsAssigned()
        {
            var player = new GameObject("Player").AddComponent<CharacterController>().gameObject.AddComponent<PlaceholderPlayerController>();
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            var so = new SerializedObject(room.GetComponentInChildren<ControlPanel>());
            Assert.AreEqual(player.transform, so.FindProperty("player").objectReferenceValue);
            Assert.AreEqual(1, so.FindProperty("targets").arraySize);
            Assert.AreEqual(room.GetComponentInChildren<AccessDoor>(), so.FindProperty("targets").GetArrayElementAtIndex(0).objectReferenceValue);
        }

        [Test]
        public void Regenerating_DoesNotDuplicate()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            int objects = room.GetComponentsInChildren<Transform>().Length;
            int lights = room.GetComponentsInChildren<Light>().Length;
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(RoomBuilder.Regenerate(room, settings));
            }

            Assert.AreEqual(objects, room.GetComponentsInChildren<Transform>().Length);
            Assert.AreEqual(lights, room.GetComponentsInChildren<Light>().Length);
        }

        [Test]
        public void Regenerating_WithAProbe_DoesNotDuplicateTheProbe()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            Set(settings, "reflectionProbe", true);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            RoomBuilder.Regenerate(room, settings);
            Assert.AreEqual(1, room.GetComponentsInChildren<ReflectionProbe>().Length);
        }

        [Test]
        public void Clearing_LeavesEverythingElse()
        {
            var unrelated = new GameObject("Unrelated");
            var manualLight = new GameObject("Manual Light").AddComponent<Light>();
            GeneratedRoom other = RoomBuilder.CreateRoom(Settings(RoomPurpose.ContainmentCell), new Vector3(200f, 0f, 0f));
            int otherCount = other.GetComponentsInChildren<Transform>().Length;
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);

            RoomBuilder.Clear(room);

            Assert.AreEqual(1, room.GetComponentsInChildren<Transform>().Length, "Only the room object should remain");
            Assert.IsTrue(unrelated != null);
            Assert.IsTrue(manualLight != null);
            Assert.AreEqual(otherCount, other.GetComponentsInChildren<Transform>().Length);
        }

        [Test]
        public void ObjectsPlacedByHand_InsideAGeneratedRoom_SurviveRegeneratingAndClearing()
        {
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            Transform sources = room.transform.Find("Generated_Lighting/Generated_LightSources");
            var manualLight = new GameObject("Manual Light").AddComponent<Light>();
            manualLight.transform.SetParent(sources, true);
            var manualProp = new GameObject("Manual Prop");
            manualProp.transform.SetParent(room.transform, true);

            RoomBuilder.Regenerate(room, settings);
            Assert.IsTrue(manualLight != null, "Regenerating removed a hand placed light");
            Assert.IsTrue(manualProp != null, "Regenerating removed a hand placed object");

            RoomBuilder.Clear(room);
            Assert.IsTrue(manualLight != null, "Clearing removed a hand placed light");
            Assert.IsTrue(manualProp != null, "Clearing removed a hand placed object");
        }

        [Test]
        public void KeepRoom_StopsTracking_AndLeavesTheObjects()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            GameObject root = room.gameObject;
            int objects = root.GetComponentsInChildren<Transform>().Length;
            RoomBuilder.Keep(room);
            Assert.IsNull(root.GetComponent<GeneratedRoom>());
            Assert.AreEqual(objects, root.GetComponentsInChildren<Transform>().Length);
        }

        [Test]
        public void GeneratedObjects_RemainEditable()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            foreach (Transform t in room.GetComponentsInChildren<Transform>())
            {
                Assert.AreEqual(HideFlags.None, t.gameObject.hideFlags, t.name);
            }
        }

        [Test]
        public void Undo_RemovesTheWholeRoomInOneStep()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), Vector3.zero);
            GameObject root = room.gameObject;
            Undo.PerformUndo();
            Assert.IsTrue(root == null);
        }

        //==================== REAL TIME CHANGES =====================
        private static void ChangeOnComponent(GeneratedRoom room, string property, float value)
        {
            RoomSettings before = room.Settings.Clone();
            var so = new SerializedObject(room);
            so.FindProperty("settings." + property).floatValue = value;
            so.ApplyModifiedProperties();
            GeneratedRoomEditor.FinishEdit(room, before);
            RoomLiveUpdater.Flush();
        }

        private static float FloorWidth(GeneratedRoom room)
        {
            return room.transform.Find("Generated_Structure/Floor").GetComponent<BoxCollider>().size.x;
        }

        [Test]
        public void ChangingAValueOnTheComponent_RebuildsTheRoom()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            ChangeOnComponent(room, "width", 48f);
            Assert.AreEqual(48f, room.ExitOffset.x);
            Assert.AreEqual(48f, FloorWidth(room));
            Assert.AreEqual(room.CurrentSignature, room.BuiltSignature);
        }

        [Test]
        public void ChangingAValueWithoutTheInspector_IsPickedUpFromValidation()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            var so = new SerializedObject(room);
            so.FindProperty("settings.width").floatValue = 44f;
            so.ApplyModifiedProperties();
            RoomLiveUpdater.Flush();
            Assert.AreEqual(44f, FloorWidth(room));
        }

        [Test]
        public void ManyChangesInARow_LeaveOneCopyOfTheRoom_AndHandPlacedObjects()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            var manual = new GameObject("Manual Prop");
            manual.transform.SetParent(room.transform.Find("Generated_Structure"), true);
            int floors = 0;
            for (int i = 0; i < 25; i++)
            {
                ChangeOnComponent(room, "width", 30f + i);
            }

            foreach (Transform t in room.GetComponentsInChildren<Transform>())
            {
                floors += t.name == "Floor" ? 1 : 0;
            }

            Assert.AreEqual(1, floors);
            Assert.AreEqual(54f, FloorWidth(room));
            Assert.IsTrue(manual != null, "A hand placed object should survive every rebuild.");
            Assert.IsTrue(room.gameObject.activeSelf, "The room should be active again after rebuilding.");
        }

        [Test]
        public void ValuesThatCannotBeBuilt_KeepTheLastGoodRoom()
        {
            RoomSettings settings = Settings(RoomPurpose.VerticalConnection);
            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            ChangeOnComponent(room, "width", 12f);
            Assert.AreEqual(settings.Width, room.ExitOffset.x, "The last good room should stay.");
            Assert.AreEqual(1, room.GetComponentsInChildren<CurvedWall>().Length);
            ChangeOnComponent(room, "width", 44f);
            Assert.AreEqual(44f, room.ExitOffset.x, "A good value should build again.");
        }

        [Test]
        public void ChangingPurposeOrLighting_OnTheComponent_LoadsTheirValues()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            RoomSettings before = room.Settings.Clone();
            var so = new SerializedObject(room);
            so.FindProperty("settings.purpose").enumValueIndex = (int)RoomPurpose.RestrictedCorridor;
            so.ApplyModifiedProperties();
            GeneratedRoomEditor.FinishEdit(room, before);
            RoomLiveUpdater.Flush();
            Assert.AreEqual(Settings(RoomPurpose.RestrictedCorridor).Width, room.ExitOffset.x);
            StringAssert.StartsWith("Room_RestrictedCorridor", room.name);

            before = room.Settings.Clone();
            so.Update();
            so.FindProperty("settings.lightingMode").enumValueIndex = (int)RoomLightingMode.Emergency;
            so.ApplyModifiedProperties();
            GeneratedRoomEditor.FinishEdit(room, before);
            RoomLiveUpdater.Flush();
            Assert.IsTrue(room.Settings.WallFixtures, "Emergency lighting should load its wall fixtures.");
        }

        [Test]
        public void Undo_PutsTheValueAndTheRoomBack_AndRedoReappliesThem()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            float before = room.Settings.Width;
            Undo.IncrementCurrentGroup();
            ChangeOnComponent(room, "width", 48f);
            Undo.IncrementCurrentGroup();

            Undo.PerformUndo();
            RoomLiveUpdater.Flush();
            Assert.AreEqual(before, room.Settings.Width);
            Assert.AreEqual(before, FloorWidth(room));

            Undo.PerformRedo();
            RoomLiveUpdater.Flush();
            Assert.AreEqual(48f, room.Settings.Width);
            Assert.AreEqual(48f, FloorWidth(room));
        }

        [Test]
        public void Undo_AfterClearing_BringsTheRoomBack_WithoutDuplicates()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            int objects = room.GetComponentsInChildren<Transform>().Length;
            Undo.IncrementCurrentGroup();
            RoomBuilder.Clear(room);
            Undo.IncrementCurrentGroup();
            Assert.AreEqual(1, room.GetComponentsInChildren<Transform>().Length);

            Undo.PerformUndo();
            RoomLiveUpdater.Flush();
            Assert.IsFalse(room.IsCleared);
            Assert.AreEqual(objects, room.GetComponentsInChildren<Transform>().Length);
        }

        [Test]
        public void UndoingTheWholeRoom_AfterLiveChanges_RemovesEverything()
        {
            Undo.IncrementCurrentGroup();
            GeneratedRoom room = RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            GameObject root = room.gameObject;
            Undo.IncrementCurrentGroup();
            ChangeOnComponent(room, "width", 48f);
            Undo.IncrementCurrentGroup();

            Undo.PerformUndo();
            RoomLiveUpdater.Flush();
            Undo.PerformUndo();
            RoomLiveUpdater.Flush();
            Assert.IsTrue(root == null);
            Assert.IsEmpty(Object.FindObjectsByType<ArchitecturalBlock>(FindObjectsSortMode.None));
        }

        //==================== GLOBAL LIGHTING =====================
        [Test]
        public void GlobalLighting_IsLeftAlone_UnlessAskedFor()
        {
            Color ambient = RenderSettings.ambientLight;
            bool fog = RenderSettings.fog;
            RoomBuilder.CreateRoom(Settings(RoomPurpose.ObservationChamber), Vector3.zero);
            Assert.AreEqual(ambient, RenderSettings.ambientLight);
            Assert.AreEqual(fog, RenderSettings.fog);
        }

        [Test]
        public void GlobalLighting_IsApplied_WhenAskedFor_AndPutBackWhenCleared()
        {
            RenderSettings.fog = false;
            Color before = RenderSettings.ambientLight;
            RoomSettings settings = Settings(RoomPurpose.ObservationChamber);
            var ambient = new Color(0.1f, 0.2f, 0.3f);
            Set(settings, "sceneAmbient", true);
            Set(settings, "ambientColor", ambient);
            Set(settings, "sceneFog", true);

            GeneratedRoom room = RoomBuilder.CreateRoom(settings, Vector3.zero);
            Assert.AreEqual(ambient, RenderSettings.ambientLight);
            Assert.IsTrue(RenderSettings.fog);

            RoomBuilder.Clear(room);
            Assert.AreEqual(before, RenderSettings.ambientLight);
            Assert.IsFalse(RenderSettings.fog);
        }

        //==================== CONNECTIONS =====================
        [Test]
        public void ExitOfOneRoom_LinesUpWithTheEntranceOfTheNext()
        {
            RoomSettings climb = Settings(RoomPurpose.VerticalConnection);
            GeneratedRoom first = RoomBuilder.CreateRoom(climb, new Vector3(10f, 3f, 0f));
            Vector3 exit = first.transform.position + first.ExitOffset;
            Assert.AreEqual(new Vector3(10f + climb.Width, 3f + climb.ExitRise, 0f), exit);

            GeneratedRoom second = RoomBuilder.CreateRoom(Settings(RoomPurpose.RestrictedCorridor), exit);
            Transform floor = second.transform.Find("Generated_Structure/Floor");
            Assert.AreEqual(exit.y, floor.GetComponent<Renderer>().bounds.max.y, 0.01f);
        }
    }
}

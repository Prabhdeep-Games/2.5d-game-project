using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Makes a <see cref="RoomPlan"/> real in the open scene, and removes it again. Every object it makes is recorded on the
    /// room's <see cref="GeneratedRoom"/>, and only recorded objects are ever removed.
    ///
    /// A room's values decide its contents. Undo tracks the values and not the hundreds of objects a slider drag would otherwise
    /// record, and <see cref="RoomLiveUpdater"/> rebuilds the room whenever its values and contents disagree. Only creating
    /// a room is an Undo step of its own. Elements (gameplay prefabs added to a room) are placed once and never rebuilt.
    /// </summary>
    public static class RoomBuilder
    {
        private const string RootPrefix = "Room_";
        private const string LightingGroup = "Generated_Lighting";
        private const string DoorOpenOffsetProperty = "openOffset";
        private const float DoorOpenHeight = 4f;
        private const float CameraViewAngle = 26f;
        private const int IgnoreRaycastLayer = 2;
        private const float CameraZoneDepth = 4f;

        private static readonly Dictionary<RoomGroup, string> GroupNames = new Dictionary<RoomGroup, string>
        {
            { RoomGroup.Structure, "Generated_Structure" },
            { RoomGroup.Openings, "Generated_Openings" },
            { RoomGroup.LevelChange, "Generated_LevelChange" },
            { RoomGroup.Gallery, "Generated_Gallery" },
            { RoomGroup.Systems, "Generated_Systems" },
            { RoomGroup.LightFixtures, "Generated_LightFixtures" },
            { RoomGroup.LightSources, "Generated_LightSources" },
            { RoomGroup.ReflectionProbes, "Generated_ReflectionProbes" },
            { RoomGroup.Chase, "Generated_Chase" }
        };

        //==================== GENERATE =====================
        /// <summary>Makes a new room at a position. Returns null and logs why if the plan cannot be built.</summary>
        public static GeneratedRoom CreateRoom(RoomSettings settings, Vector3 position)
        {
            RoomPlan plan = RoomLayout.Create(settings);
            if (!CanBuild(plan, true))
            {
                return null;
            }

            var rootObject = new GameObject(RootName(settings));
            Undo.RegisterCreatedObjectUndo(rootObject, "Generate Room");
            rootObject.transform.position = position;
            GeneratedRoom room = rootObject.AddComponent<GeneratedRoom>();
            room.SetSettings(settings.Clone());
            Rebuild(room, plan);
            return room;
        }

        /// <summary>Gives the room these values and rebuilds it straight away, even if its values have not changed.</summary>
        public static bool Regenerate(GeneratedRoom room, RoomSettings settings)
        {
            RoomPlan plan = RoomLayout.Create(settings);
            if (!CanBuild(plan, true))
            {
                return false;
            }

            RecordValues(room, settings.Clone(), false);
            Rebuild(room, plan);
            return true;
        }

        /// <summary>
        /// Makes the room's contents match its current values. A room whose values cannot be built keeps its last good contents,
        /// and returns false. The Inspector and the window say why.
        /// </summary>
        public static bool Refresh(GeneratedRoom room)
        {
            if (room.IsCleared)
            {
                RemoveGenerated(room);
                RestoreGlobalLighting(room);
                room.RecordBuild(room.ExitOffset, new List<GameObject>());
                MarkDirty(room);
                return true;
            }

            RoomPlan plan = RoomLayout.Create(room.Settings);
            if (!CanBuild(plan, false))
            {
                return false;
            }

            Rebuild(room, plan);
            return true;
        }

        /// <summary>Changes a room's values as one Undo step, without rebuilding it.</summary>
        public static void RecordValues(GeneratedRoom room, RoomSettings values, bool cleared)
        {
            Undo.RecordObject(room, "Change Room");
            room.SetSettings(values);
            room.SetCleared(cleared);

            // What the rebuild records must not become part of this Undo step, so the step ends here.
            Undo.FlushUndoRecordObjects();
        }

        // Doors, panels and cameras read their position and connections when they wake, so the room stays inactive until they
        // are placed and connected. This is what makes a rebuild safe while the game is playing.
        private static void Rebuild(GeneratedRoom room, RoomPlan plan)
        {
            bool wasActive = room.gameObject.activeSelf;
            room.gameObject.SetActive(false);
            RemoveGenerated(room);
            room.gameObject.name = RootName(room.Settings);
            Build(room, room.Settings, plan);
            room.gameObject.SetActive(wasActive);
        }

        private static bool CanBuild(RoomPlan plan, bool logProblems)
        {
            if (!plan.CanBuild)
            {
                if (logProblems)
                {
                    Debug.LogWarning("[RoomGenerator] " + string.Join(" ", plan.Errors));
                }

                return false;
            }

            List<string> missing = RoomAssetLibrary.FindMissing();
            if (missing.Count > 0)
            {
                if (logProblems)
                {
                    Debug.LogWarning("[RoomGenerator] Missing assets: " + string.Join(", ", missing));
                }

                return false;
            }

            return true;
        }

        private static string RootName(RoomSettings settings)
        {
            return RootPrefix + settings.Purpose + "_" + settings.Seed;
        }

        // A scene cannot be marked as changed during play, and nothing done during play is saved anyway.
        private static void MarkDirty(GeneratedRoom room)
        {
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(room);
                EditorSceneManager.MarkSceneDirty(room.gameObject.scene);
            }
        }

        //==================== CLEAR AND KEEP =====================
        /// <summary>
        /// Removes everything the generator made for this room, and puts back any scene lighting it changed. The room object
        /// and anything placed by hand stay. Changing a value or using Undo brings the contents back.
        /// </summary>
        public static void Clear(GeneratedRoom room)
        {
            RecordValues(room, room.Settings, true);
            Refresh(room);
        }

        /// <summary>Stops tracking the room. It stays in the scene as ordinary objects that the generator will not touch again.</summary>
        public static void Keep(GeneratedRoom room)
        {
            Undo.DestroyObjectImmediate(room);
        }

        // Anything found under a generated object that the generator did not make was placed by hand, so it moves to the room
        // object instead of going with its parent.
        private static void RemoveGenerated(GeneratedRoom room)
        {
            var generated = new HashSet<GameObject>();
            foreach (GameObject item in room.GeneratedObjects)
            {
                if (item != null)
                {
                    generated.Add(item);
                }
            }

            var manual = new List<Transform>();
            foreach (GameObject item in generated)
            {
                foreach (Transform child in item.transform)
                {
                    if (!generated.Contains(child.gameObject) && !IsPartOfGeneratedPrefab(child.gameObject, generated))
                    {
                        manual.Add(child);
                    }
                }
            }

            foreach (Transform child in manual)
            {
                child.SetParent(room.transform, true);
            }

            foreach (GameObject item in generated)
            {
                // Removing a parent has already removed its children.
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }
        }

        private static bool IsPartOfGeneratedPrefab(GameObject child, HashSet<GameObject> generated)
        {
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(child);
            return root != null && generated.Contains(root) && !PrefabUtility.IsAddedGameObjectOverride(child);
        }

        //==================== BUILD =====================
        private static void Build(GeneratedRoom room, RoomSettings settings, RoomPlan plan)
        {
            var made = new List<GameObject>();
            var groups = new Dictionary<RoomGroup, Transform>();
            Transform lighting = null;

            Transform GroupFor(RoomGroup group)
            {
                if (groups.TryGetValue(group, out Transform existing))
                {
                    return existing;
                }

                Transform parent = room.transform;
                if (group == RoomGroup.LightFixtures || group == RoomGroup.LightSources || group == RoomGroup.ReflectionProbes)
                {
                    if (lighting == null)
                    {
                        lighting = NewChild(LightingGroup, room.transform, made);
                    }

                    parent = lighting;
                }

                Transform created = NewChild(GroupNames[group], parent, made);
                groups[group] = created;
                return created;
            }

            Vector3 origin = room.transform.position;

            foreach (BlockPlan block in plan.Blocks)
            {
                MakeBlock(block, origin, GroupFor(block.Group), made);
            }

            foreach (CurvePlan curve in plan.Curves)
            {
                MakeCurve(curve, plan, origin, GroupFor(RoomGroup.LevelChange), made);
            }

            WirePrefabs(MakePrefabs(plan, origin, GroupFor, made), settings);
            MakeChasePieces(plan, origin, GroupFor, made);

            if (plan.CameraExtraDistance > 0f)
            {
                MakeCameraZone(plan, settings, origin, GroupFor(RoomGroup.Systems), made);
            }

            foreach (LightPlan light in plan.Lights)
            {
                MakeLight(light, origin, GroupFor(RoomGroup.LightSources), made);
            }

            if (plan.HasReflectionProbe)
            {
                MakeProbe(plan.ProbeBounds, origin, GroupFor(RoomGroup.ReflectionProbes), made);
            }

            room.RecordBuild(plan.ExitOffset, made);
            ApplyGlobalLighting(room, settings);
            MarkDirty(room);
        }

        private static Transform NewChild(string name, Transform parent, List<GameObject> made)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            made.Add(go);
            return go.transform;
        }

        private static GameObject MakeBlock(BlockPlan plan, Vector3 origin, Transform parent, List<GameObject> made)
        {
            Transform t = NewChild(plan.Name, parent, made);
            t.position = origin + plan.Center;
            GameObject go = t.gameObject;
            go.AddComponent<MeshRenderer>().sharedMaterial = RoomAssetLibrary.Load(plan.Material);
            var block = go.AddComponent<ArchitecturalBlock>();
            var so = new SerializedObject(block);
            so.FindProperty("size").vector3Value = plan.Size;
            so.FindProperty("solid").boolValue = plan.Solid;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        private static void MakeCurve(CurvePlan plan, RoomPlan room, Vector3 origin, Transform parent, List<GameObject> made)
        {
            Transform t = NewChild(plan.Name, parent, made);
            t.position = origin + plan.Position;
            GameObject go = t.gameObject;
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = RoomAssetLibrary.Load(RoomMaterial.WallRun);
            go.AddComponent<MeshCollider>();
            go.AddComponent<WallRunSurface>();
            var curve = go.AddComponent<CurvedWall>();
            var so = new SerializedObject(curve);
            so.FindProperty("radius").floatValue = plan.Radius;
            so.FindProperty("arcAngle").floatValue = 90f;
            so.FindProperty("straightLength").floatValue = plan.StraightLength;
            so.FindProperty("width").floatValue = plan.Width;
            so.FindProperty("thickness").floatValue = plan.Thickness;
            so.FindProperty("segments").intValue = RoomLayout.CurveSegmentCount;
            so.FindProperty("mirror").boolValue = plan.Mirror;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Rebuilt now rather than on the delayed validation, so the fill below matches the final shape at once.
            curve.Rebuild();

            Transform fill = NewChild("Curve Fill", t, made);
            fill.gameObject.AddComponent<MeshFilter>();
            fill.gameObject.AddComponent<MeshRenderer>().sharedMaterial = RoomAssetLibrary.Load(plan.FillMaterial);
            fill.gameObject.AddComponent<CurvedWallFill>().SetWall(curve);
        }

        private static List<(PrefabPlan plan, GameObject instance)> MakePrefabs(RoomPlan plan, Vector3 origin, System.Func<RoomGroup, Transform> groupFor, List<GameObject> made)
        {
            var result = new List<(PrefabPlan, GameObject)>();
            foreach (PrefabPlan item in plan.Prefabs)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(RoomAssetLibrary.Load(item.Prefab), groupFor(item.Group));
                instance.name = item.Name;
                instance.transform.position = origin + item.Position;
                if (item.Scale.HasValue)
                {
                    instance.transform.localScale = item.Scale.Value;
                }

                made.Add(instance);
                result.Add((item, instance));
            }

            return result;
        }

        // Doors, panels and cameras that were generated together are connected here, so the room works straight away.
        private static void WirePrefabs(List<(PrefabPlan plan, GameObject instance)> prefabs, RoomSettings settings)
        {
            AccessDoor exitDoor = null;
            ControlPanel panel = null;
            SecurityCamera securityCamera = null;
            foreach ((PrefabPlan plan, GameObject instance) in prefabs)
            {
                if (plan.Prefab == RoomPrefab.SealedDoor)
                {
                    var door = instance.GetComponent<AccessDoor>();
                    var so = new SerializedObject(door);
                    so.FindProperty(DoorOpenOffsetProperty).vector3Value = new Vector3(0f, DoorOpenHeight, 0f);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    if (plan.Name.StartsWith("Exit"))
                    {
                        exitDoor = door;
                    }
                }
                else if (plan.Prefab == RoomPrefab.ControlPanel)
                {
                    panel = instance.GetComponent<ControlPanel>();
                }
                else if (plan.Prefab == RoomPrefab.SecurityCamera)
                {
                    securityCamera = instance.GetComponent<SecurityCamera>();
                }
            }

            var player = Object.FindFirstObjectByType<PlaceholderPlayerController>();
            Transform playerTransform = player != null ? player.transform : null;

            if (panel != null)
            {
                var so = new SerializedObject(panel);
                so.FindProperty("player").objectReferenceValue = playerTransform;
                SetList(so.FindProperty("targets"), exitDoor);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (securityCamera != null)
            {
                var so = new SerializedObject(securityCamera);
                so.FindProperty("viewAngle").floatValue = CameraViewAngle;
                so.FindProperty("range").floatValue = RoomLayout.CameraRangeFor(settings);
                SetList(so.FindProperty("watched"), playerTransform);
                if (RoomLayout.CameraShouldLockExit(settings) && exitDoor != null)
                {
                    SetList(so.FindProperty("locksDown"), exitDoor);
                    if (settings.Exit == RoomThreshold.SealedDoor)
                    {
                        var door = new SerializedObject(exitDoor);
                        door.FindProperty("startsLocked").boolValue = false;
                        door.FindProperty("startsOpen").boolValue = true;
                        door.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                else
                {
                    // Being seen changes nothing here, so nothing should claim that it does.
                    so.FindProperty("alarmMessage").stringValue = string.Empty;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (player == null && (panel != null || securityCamera != null))
            {
                Debug.LogWarning("[RoomGenerator] No player in this scene, so the generated panel or camera has no Player assigned. Set it in the Inspector.");
            }
        }

        private static void SetList(SerializedProperty list, Object item)
        {
            list.arraySize = item != null ? 1 : 0;
            if (item != null)
            {
                list.GetArrayElementAtIndex(0).objectReferenceValue = item;
            }
        }

        //==================== PLATFORMING =====================
        // The restart comes first, so the room's slabs, pit hazards and debris can be connected to it.
        private static void MakeChasePieces(RoomPlan plan, Vector3 origin, System.Func<RoomGroup, Transform> groupFor, List<GameObject> made)
        {
            RoomReset restart = plan.Restart != null ? MakeRestart(plan, origin, groupFor(RoomGroup.Chase), made) : null;

            foreach (CrumblePlan crumble in plan.Crumbles)
            {
                GameObject slab = MakeBlock(crumble.Block, origin, groupFor(crumble.Block.Group), made);
                var so = new SerializedObject(slab.AddComponent<CrumblingSlab>());
                so.FindProperty("crumbleDelay").floatValue = crumble.Delay;
                so.FindProperty("room").objectReferenceValue = restart;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (VolumePlan hazard in plan.Hazards)
            {
                GameObject trigger = MakeTrigger(hazard, origin, groupFor(RoomGroup.Chase), made);
                var so = new SerializedObject(trigger.AddComponent<TraversalHazard>());
                so.FindProperty("room").objectReferenceValue = restart;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (DebrisPlan debris in plan.Debris)
            {
                var spawner = (GameObject)PrefabUtility.InstantiatePrefab(RoomAssetLibrary.Load(RoomPrefab.FallingObstacleSpawner), groupFor(RoomGroup.Chase));
                spawner.name = "Falling Debris";
                spawner.transform.position = origin + debris.Position;
                made.Add(spawner);
                var so = new SerializedObject(spawner.GetComponent<FallingObstacleSpawner>());
                so.FindProperty("interval").floatValue = debris.Interval;
                so.FindProperty("initialDelay").floatValue = debris.InitialDelay;
                so.FindProperty("destroyHeight").floatValue = origin.y + debris.DestroyHeight;
                so.FindProperty("room").objectReferenceValue = restart;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            for (int i = 0; i < plan.PathPoints.Count; i++)
            {
                PathPointPlan point = plan.PathPoints[i];
                Transform t = NewChild("Chase Point " + (i + 1), groupFor(RoomGroup.Chase), made);
                t.position = origin + point.Position;
                var so = new SerializedObject(t.gameObject.AddComponent<ChasePathPoint>());
                so.FindProperty("blink").boolValue = point.Blink;
                so.FindProperty("roomStart").boolValue = point.RoomStart;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static RoomReset MakeRestart(RoomPlan plan, Vector3 origin, Transform parent, List<GameObject> made)
        {
            GameObject area = MakeTrigger(plan.Restart, origin, parent, made);
            Transform entry = NewChild("Entry Point", area.transform, made);
            entry.position = origin + plan.RestartEntry;
            var reset = area.AddComponent<RoomReset>();
            var so = new SerializedObject(reset);
            so.FindProperty("entryPoint").objectReferenceValue = entry;
            so.ApplyModifiedPropertiesWithoutUndo();
            return reset;
        }

        // Every room trigger sits on Ignore Raycast, so it cannot block the swap's sight line.
        private static GameObject MakeTrigger(VolumePlan plan, Vector3 origin, Transform parent, List<GameObject> made)
        {
            Transform t = NewChild(plan.Name, parent, made);
            t.gameObject.layer = IgnoreRaycastLayer;
            t.position = origin + plan.Center;
            var box = t.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = plan.Size;
            return t.gameObject;
        }

        // The trigger sits on Ignore Raycast, as every other room trigger does, so it cannot block the swap's sight line.
        private static void MakeCameraZone(RoomPlan plan, RoomSettings settings, Vector3 origin, Transform parent, List<GameObject> made)
        {
            // A shaft or pit below the floor is part of the room the camera has to take in.
            Transform t = NewChild("Camera Zone", parent, made);
            t.gameObject.layer = IgnoreRaycastLayer;
            float bottom = -plan.BelowFloor;
            t.position = origin + new Vector3(settings.Width * 0.5f, (bottom + settings.Height) * 0.5f, 0f);
            var box = t.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(settings.Width, settings.Height - bottom, CameraZoneDepth);
            var so = new SerializedObject(t.gameObject.AddComponent<CameraZone>());
            so.FindProperty("extraDistance").floatValue = plan.CameraExtraDistance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void MakeLight(LightPlan plan, Vector3 origin, Transform parent, List<GameObject> made)
        {
            Transform t = NewChild(plan.Name, parent, made);
            t.position = origin + plan.Position;
            var light = t.gameObject.AddComponent<Light>();
            light.type = plan.Type;
            light.color = plan.Color;
            light.intensity = plan.Intensity;
            light.range = plan.Range;
            light.shadows = plan.Shadows ? LightShadows.Soft : LightShadows.None;
            if (plan.Type == LightType.Spot)
            {
                t.rotation = Quaternion.Euler(90f, 0f, 0f);
                light.spotAngle = plan.SpotAngle;
            }
        }

        private static void MakeProbe(Bounds bounds, Vector3 origin, Transform parent, List<GameObject> made)
        {
            Transform t = NewChild("Room Reflection Probe", parent, made);
            t.position = origin + bounds.center;
            var probe = t.gameObject.AddComponent<ReflectionProbe>();
            probe.size = bounds.size;
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.boxProjection = true;
        }

        //==================== SCENE LIGHTING =====================
        // Fog and ambient light are the scene's own settings (Window > Rendering > Lighting > Environment). URP renders this
        // fog in its Lit shaders. The first change remembers the scene's values, so turning an option off puts them back.
        private static void ApplyGlobalLighting(GeneratedRoom room, RoomSettings settings)
        {
            if (!settings.ChangesSceneLighting)
            {
                RestoreGlobalLighting(room);
                return;
            }

            InRoomScene(room, () =>
            {
                if (!room.ChangedGlobalLighting)
                {
                    room.RecordGlobalLighting(CaptureSceneLighting());
                }

                GeneratedRoom.GlobalLightingSnapshot before = room.GlobalLightingBefore;
                if (settings.SceneAmbient)
                {
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = settings.AmbientColor;
                }
                else
                {
                    RestoreAmbient(before);
                }

                if (settings.SceneFog)
                {
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = settings.FogMode;
                    RenderSettings.fogColor = settings.FogColor;
                    RenderSettings.fogDensity = settings.FogDensity;
                    RenderSettings.fogStartDistance = settings.FogStart;
                    RenderSettings.fogEndDistance = Mathf.Max(settings.FogEnd, settings.FogStart);
                }
                else
                {
                    RestoreFog(before);
                }
            });
        }

        private static void RestoreGlobalLighting(GeneratedRoom room)
        {
            if (!room.ChangedGlobalLighting)
            {
                return;
            }

            InRoomScene(room, () =>
            {
                RestoreAmbient(room.GlobalLightingBefore);
                RestoreFog(room.GlobalLightingBefore);
            });
            room.ForgetGlobalLighting();
        }

        private static GeneratedRoom.GlobalLightingSnapshot CaptureSceneLighting()
        {
            return new GeneratedRoom.GlobalLightingSnapshot
            {
                ambientMode = RenderSettings.ambientMode,
                ambientColor = RenderSettings.ambientLight,
                fog = RenderSettings.fog,
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
                fogMode = RenderSettings.fogMode,
                fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance
            };
        }

        private static void RestoreAmbient(GeneratedRoom.GlobalLightingSnapshot before)
        {
            RenderSettings.ambientMode = before.ambientMode;
            RenderSettings.ambientLight = before.ambientColor;
        }

        private static void RestoreFog(GeneratedRoom.GlobalLightingSnapshot before)
        {
            RenderSettings.fog = before.fog;
            RenderSettings.fogMode = before.fogMode;
            RenderSettings.fogColor = before.fogColor;
            RenderSettings.fogDensity = before.fogDensity;
            RenderSettings.fogStartDistance = before.fogStart;
            RenderSettings.fogEndDistance = before.fogEnd;
        }

        // RenderSettings always belong to the active scene, so a room in another open scene changes its own scene's values.
        // The Lighting window and Scene view are repainted, so the change shows straight away.
        private static void InRoomScene(GeneratedRoom room, System.Action change)
        {
            UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.Scene roomScene = room.gameObject.scene;
            bool switchScene = roomScene.IsValid() && roomScene.isLoaded && roomScene != active;
            if (switchScene)
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(roomScene);
            }

            change();

            if (switchScene)
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        //==================== ELEMENTS =====================
        /// <summary>
        /// Adds a prefab to the room as an element: an ordinary prefab instance under the room's Elements group, relative to
        /// the room. Regenerating and clearing the room leave it alone. One Undo step.
        /// </summary>
        public static GameObject AddElement(GeneratedRoom room, GameObject prefab, Vector3 localPosition, Quaternion localRotation, Vector3? localScale = null)
        {
            if (room == null || prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                Debug.LogWarning("[RoomGenerator] Elements must be prefab assets from the Project window.");
                return null;
            }

            Undo.SetCurrentGroupName("Add Room Element");
            int group = Undo.GetCurrentGroup();
            Transform parent = ElementsGroup(room);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Add Room Element");
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            if (localScale.HasValue)
            {
                instance.transform.localScale = localScale.Value;
            }

            Undo.CollapseUndoOperations(group);
            MarkDirty(room);
            return instance;
        }

        /// <summary>Removes one element from the room and the scene. One Undo step.</summary>
        public static void RemoveElement(GeneratedRoom room, GameObject instance)
        {
            if (instance != null)
            {
                Undo.DestroyObjectImmediate(instance);
                MarkDirty(room);
            }
        }

        /// <summary>Where an element of this kind starts in the room: on the route at a distance along it, on the floor or under the ceiling.</summary>
        public static Vector3 DefaultElementPosition(GeneratedRoom room, RoomElementCatalog.Entry entry, GameObject prefab, float alongRoom)
        {
            RoomSettings settings = room.Settings;
            float x = Mathf.Clamp(alongRoom, settings.WallThickness + 1f, settings.Width - settings.WallThickness - 1f);
            Bounds bounds = RoomElementCatalog.LocalBounds(prefab);
            RoomElementCatalog.Placement placement = entry != null ? entry.Placement : RoomElementCatalog.Placement.Floor;
            float offset = entry != null ? entry.HeightOffset : 0f;
            float floor = RoomLayout.FloorHeightAt(settings, x);
            float y;
            switch (placement)
            {
                case RoomElementCatalog.Placement.Ceiling:
                    y = settings.Height - bounds.max.y - offset;
                    break;
                case RoomElementCatalog.Placement.PivotOnFloor:
                    y = floor + offset;
                    break;
                default:
                    y = floor - bounds.min.y + offset;
                    break;
            }

            return new Vector3(x, y, 0f);
        }

        private static Transform ElementsGroup(GeneratedRoom room)
        {
            Transform existing = room.transform.Find(GeneratedRoom.ElementsGroupName);
            if (existing != null)
            {
                return existing;
            }

            var group = new GameObject(GeneratedRoom.ElementsGroupName);
            Undo.RegisterCreatedObjectUndo(group, "Add Room Element");
            group.transform.SetParent(room.transform, false);
            return group.transform;
        }

        //==================== PRESETS =====================
        /// <summary>
        /// Saves the room's values and its elements, relative to the room, as a preset asset. Only prefab instances can be
        /// made again, so other objects in the Elements group are left out, with a warning.
        /// </summary>
        public static RoomPreset SavePreset(GeneratedRoom room, string assetPath)
        {
            var preset = ScriptableObject.CreateInstance<RoomPreset>();
            var elements = new List<RoomPreset.Element>();
            foreach (GameObject element in room.FindElements())
            {
                GameObject source = PrefabUtility.IsOutermostPrefabInstanceRoot(element) ? PrefabUtility.GetCorrespondingObjectFromSource(element) : null;
                if (source == null)
                {
                    Debug.LogWarning($"[RoomGenerator] '{element.name}' is not a prefab instance, so it is not saved in the preset.", element);
                    continue;
                }

                Transform t = element.transform;
                elements.Add(new RoomPreset.Element
                {
                    prefab = source,
                    localPosition = room.transform.InverseTransformPoint(t.position),
                    localRotation = Quaternion.Inverse(room.transform.rotation) * t.rotation,
                    localScale = t.localScale
                });
            }

            preset.Store(room.Settings.Clone(), elements);
            AssetDatabase.CreateAsset(preset, AssetDatabase.GenerateUniqueAssetPath(assetPath));
            AssetDatabase.SaveAssets();
            return preset;
        }

        /// <summary>Makes a new room from a preset, with its elements, as one Undo step.</summary>
        public static GeneratedRoom CreateRoomFromPreset(RoomPreset preset, Vector3 position)
        {
            Undo.SetCurrentGroupName("Generate Room From Preset");
            int group = Undo.GetCurrentGroup();
            GeneratedRoom room = CreateRoom(preset.Settings, position);
            if (room != null)
            {
                AddPresetElements(room, preset);
            }

            Undo.CollapseUndoOperations(group);
            return room;
        }

        /// <summary>Adds a preset's elements to a room, where they were in the preset's room.</summary>
        public static void AddPresetElements(GeneratedRoom room, RoomPreset preset)
        {
            foreach (RoomPreset.Element element in preset.Elements)
            {
                if (element.prefab != null)
                {
                    AddElement(room, element.prefab, element.localPosition, element.localRotation, element.localScale);
                }
            }
        }
    }
}

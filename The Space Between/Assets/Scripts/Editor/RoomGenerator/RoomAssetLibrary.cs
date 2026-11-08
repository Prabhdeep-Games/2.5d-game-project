using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The project's existing assets that the generator uses, by their real paths. Nothing here creates or changes an asset.
    /// The project has no modular wall meshes or light prefabs: its architecture is made of sized blocks, so the materials
    /// below are the wall kit, and fixtures are blocks in these materials with a Unity light.
    /// </summary>
    public static class RoomAssetLibrary
    {
        private const string ArchitectureFolder = "Assets/Materials/Architecture/";
        private const string EnvironmentFolder = "Assets/Prefabs/Environment/";

        private static readonly Dictionary<RoomMaterial, string> MaterialPaths = new Dictionary<RoomMaterial, string>
        {
            { RoomMaterial.Concrete, ArchitectureFolder + "ARC_Concrete.mat" },
            { RoomMaterial.ConcretePale, ArchitectureFolder + "ARC_ConcretePale.mat" },
            { RoomMaterial.ConcreteSlate, ArchitectureFolder + "ARC_ConcreteSlate.mat" },
            { RoomMaterial.Steel, ArchitectureFolder + "ARC_Steel.mat" },
            { RoomMaterial.DeepShadow, ArchitectureFolder + "ARC_DeepShadow.mat" },
            { RoomMaterial.LightPanel, ArchitectureFolder + "ARC_LightPanel.mat" },
            { RoomMaterial.Glass, ArchitectureFolder + "ARC_Glass.mat" },
            // The only tested marking for a wall run. Replacing it is still an open art decision.
            { RoomMaterial.WallRun, "Assets/Materials/Greybox/GB_WallRun.mat" }
        };

        private static readonly Dictionary<RoomPrefab, string> PrefabPaths = new Dictionary<RoomPrefab, string>
        {
            { RoomPrefab.SealedDoor, EnvironmentFolder + "Sealed Door.prefab" },
            { RoomPrefab.ControlPanel, EnvironmentFolder + "Control Panel.prefab" },
            { RoomPrefab.GlassPartition, EnvironmentFolder + "Glass Partition.prefab" },
            { RoomPrefab.IndustrialBarrier, EnvironmentFolder + "Industrial Barrier.prefab" },
            { RoomPrefab.SecurityCamera, EnvironmentFolder + "Security Camera.prefab" },
            { RoomPrefab.FallingObstacleSpawner, "Assets/Prefabs/Traversal/Falling Obstacle Spawner.prefab" }
        };

        public static Material Load(RoomMaterial material)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialPaths[material]);
        }

        public static GameObject Load(RoomPrefab prefab)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPaths[prefab]);
        }

        /// <summary>The path of every asset the generator needs that cannot be found. Empty when all are present.</summary>
        public static List<string> FindMissing()
        {
            var missing = new List<string>();
            foreach (KeyValuePair<RoomMaterial, string> entry in MaterialPaths)
            {
                if (Load(entry.Key) == null)
                {
                    missing.Add(entry.Value);
                }
            }

            foreach (KeyValuePair<RoomPrefab, string> entry in PrefabPaths)
            {
                if (Load(entry.Key) == null)
                {
                    missing.Add(entry.Value);
                }
            }

            return missing;
        }

        /// <summary>How many assets the generator draws on.</summary>
        public static int AssetCount => MaterialPaths.Count + PrefabPaths.Count;
    }
}

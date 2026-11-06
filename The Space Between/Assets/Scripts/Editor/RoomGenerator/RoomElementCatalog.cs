using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// The gameplay prefabs the Room Generator offers as elements, by category. Every entry is an existing, working prefab
    /// that also works when dragged into any scene by hand; any other prefab can still be added from the window.
    /// </summary>
    public static class RoomElementCatalog
    {
        /// <summary>The groups the window shows elements in.</summary>
        public enum Category
        {
            Traversal,
            SecurityAndAccess,
            InteractiveArchitecture,
            SwapPuzzles,
            LevelFlow
        }

        /// <summary>Where a new element starts in a room.</summary>
        public enum Placement
        {
            /// <summary>Its lowest point rests on the floor.</summary>
            Floor,

            /// <summary>Its highest point touches the ceiling.</summary>
            Ceiling,

            /// <summary>Its pivot sits on the floor, for prefabs built to reach below it.</summary>
            PivotOnFloor
        }

        /// <summary>One prefab the window offers.</summary>
        public sealed class Entry
        {
            public Category Category;
            public string Name;
            public string Path;
            public Placement Placement;

            /// <summary>Extra height above the floor, or distance below the ceiling, when the element is added.</summary>
            public float HeightOffset;

            public string Description;
        }

        private const string Environment = "Assets/Prefabs/Environment/";
        private const string Traversal = "Assets/Prefabs/Traversal/";
        private const string Level = "Assets/Prefabs/Level/";

        public static readonly IReadOnlyList<Entry> Entries = new List<Entry>
        {
            // Traversal: flowing movement through a room.
            new Entry { Category = Category.Traversal, Name = "Wall Run Section", Path = Traversal + "Wall Run Section.prefab", Placement = Placement.PivotOnFloor,
                Description = "A curve rising into a vertical wall run, on its base block. Every wall run starts from this curve." },
            new Entry { Category = Category.Traversal, Name = "Moving Platform (Looping)", Path = Environment + "Moving Platform (Looping).prefab", Placement = Placement.Floor, HeightOffset = 2f,
                Description = "Runs back and forth on its own between its start and End Offset. Change End Offset for its path." },
            new Entry { Category = Category.Traversal, Name = "Industrial Barrier", Path = Environment + "Industrial Barrier.prefab", Placement = Placement.Floor,
                Description = "A solid barrier. It blocks movement and the swap's line of sight. Scale Y for its height." },
            new Entry { Category = Category.Traversal, Name = "Vertical Shaft", Path = Environment + "Vertical Shaft.prefab", Placement = Placement.Floor,
                Description = "Two tall walls and a Camera Zone that pulls the camera back for a climb. Put a platform or wall run inside." },
            new Entry { Category = Category.Traversal, Name = "Falling Obstacle Spawner", Path = Traversal + "Falling Obstacle Spawner.prefab", Placement = Placement.Ceiling, HeightOffset = 1f,
                Description = "Drops obstacles straight down on a fixed rhythm. A hit resets the Room Reset it stands in." },

            // Security and access: being watched and kept out.
            new Entry { Category = Category.SecurityAndAccess, Name = "Security Camera", Path = Environment + "Security Camera.prefab", Placement = Placement.Ceiling,
                Description = "Watches a cone below it and finds the player by itself. Add doors to Locks Down to seal them while it sees the player." },
            new Entry { Category = Category.SecurityAndAccess, Name = "Sealed Door", Path = Environment + "Sealed Door.prefab", Placement = Placement.Floor,
                Description = "Stays shut until it is powered. Add it to the Targets of a Control Panel or Pressure Pad." },
            new Entry { Category = Category.SecurityAndAccess, Name = "Control Panel", Path = Environment + "Control Panel.prefab", Placement = Placement.Floor,
                Description = "Press E beside it to switch its Targets. Add a Sealed Door to its Targets to make an access panel." },

            // Interactive architecture: machinery the player switches.
            new Entry { Category = Category.InteractiveArchitecture, Name = "Movable Platform", Path = Environment + "Movable Platform.prefab", Placement = Placement.Floor,
                Description = "Moves to End Offset while powered. Add it to the Targets of a Control Panel or Pressure Pad." },
            new Entry { Category = Category.InteractiveArchitecture, Name = "Pressure Pad", Path = Environment + "Pressure Pad.prefab", Placement = Placement.Floor,
                Description = "Powers its Targets while the player or a crate stands on it." },

            // Swap puzzles: what can be seen, reached and traded.
            new Entry { Category = Category.SwapPuzzles, Name = "Glass Partition", Path = Environment + "Glass Partition.prefab", Placement = Placement.Floor,
                Description = "Can be seen and swapped through, not walked through. Scale Y and Z to fit the room." },
            new Entry { Category = Category.SwapPuzzles, Name = "Swap Crate", Path = Environment + "Swap Crate.prefab", Placement = Placement.Floor,
                Description = "A physics crate the player can swap with. It also weighs down a Pressure Pad." },
            new Entry { Category = Category.SwapPuzzles, Name = "Swap Beacon", Path = Environment + "Swap Beacon.prefab", Placement = Placement.Floor, HeightOffset = 2f,
                Description = "A swap target that stays where it is placed, for swaps in mid-air." },
            new Entry { Category = Category.SwapPuzzles, Name = "Swap Housing", Path = "Assets/Prefabs/Architecture/Swap Housing.prefab", Placement = Placement.Floor,
                Description = "The pale housing from the Observation Chamber, to swap with across glass." },

            // Level flow: start, retry and finish.
            new Entry { Category = Category.LevelFlow, Name = "Room Reset", Path = Level + "Room Reset.prefab", Placement = Placement.PivotOnFloor,
                Description = "R, or touching a hazard, puts the player back at its Entry Point. Resize its Box Collider to the room." },
            new Entry { Category = Category.LevelFlow, Name = "Observation Point", Path = Level + "Observation Point.prefab", Placement = Placement.PivotOnFloor, HeightOffset = 1f,
                Description = "Press E near it to look from its Camera Anchor at its Look Target. Move those two children to frame the view." },
            new Entry { Category = Category.LevelFlow, Name = "Level Goal", Path = Level + "Level Goal.prefab", Placement = Placement.Floor,
                Description = "Ends the level when the player enters its box." }
        };

        /// <summary>Categories that make puzzles rather than movement, shown first in a puzzle level.</summary>
        public static bool IsPuzzleCategory(Category category)
        {
            return category == Category.SecurityAndAccess || category == Category.InteractiveArchitecture || category == Category.SwapPuzzles;
        }

        public static string Label(Category category)
        {
            switch (category)
            {
                case Category.SecurityAndAccess: return "Security and Access";
                case Category.InteractiveArchitecture: return "Interactive Architecture";
                case Category.SwapPuzzles: return "Swap Puzzles";
                case Category.LevelFlow: return "Level Flow";
                default: return "Traversal";
            }
        }

        public static GameObject Load(Entry entry)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
        }

        /// <summary>The entry for a prefab, or null for a prefab that is not in the catalog.</summary>
        public static Entry Find(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            foreach (Entry entry in Entries)
            {
                if (entry.Path == path)
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>The path of every catalog prefab that cannot be found. Empty when all are present.</summary>
        public static List<string> FindMissing()
        {
            var missing = new List<string>();
            foreach (Entry entry in Entries)
            {
                if (Load(entry) == null)
                {
                    missing.Add(entry.Path);
                }
            }

            return missing;
        }

        /// <summary>The space a prefab's meshes and box colliders take up, relative to its pivot.</summary>
        public static Bounds LocalBounds(GameObject prefab)
        {
            Vector3 origin = prefab.transform.position;
            bool any = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);

            void Add(Bounds part)
            {
                part.center -= origin;
                if (any)
                {
                    bounds.Encapsulate(part);
                }
                else
                {
                    bounds = part;
                    any = true;
                }
            }

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                {
                    Add(renderer.bounds);
                }
            }

            // Colliders of an asset are not in the physics scene, so their bounds are worked out from their box.
            foreach (BoxCollider box in prefab.GetComponentsInChildren<BoxCollider>(true))
            {
                Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
                Add(new Bounds(box.transform.TransformPoint(box.center), new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z))));
            }

            return bounds;
        }
    }
}

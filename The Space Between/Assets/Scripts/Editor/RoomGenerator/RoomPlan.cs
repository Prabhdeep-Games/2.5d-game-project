using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>The materials a plan can ask for. The first three follow the order of <see cref="RoomFinish"/>.</summary>
    public enum RoomMaterial
    {
        Concrete,
        ConcretePale,
        ConcreteSlate,
        Steel,
        DeepShadow,
        LightPanel,
        Glass,
        WallRun
    }

    /// <summary>The existing prefabs a plan can ask for.</summary>
    public enum RoomPrefab
    {
        SealedDoor,
        ControlPanel,
        GlassPartition,
        IndustrialBarrier,
        SecurityCamera,
        FallingObstacleSpawner
    }

    /// <summary>The named groups a generated room's objects are sorted into.</summary>
    public enum RoomGroup
    {
        Structure,
        Openings,
        LevelChange,
        Gallery,
        Systems,
        LightFixtures,
        LightSources,
        ReflectionProbes,
        Chase
    }

    /// <summary>One <see cref="ArchitecturalBlock"/>, relative to the room's position.</summary>
    public sealed class BlockPlan
    {
        public string Name;
        public RoomGroup Group;
        public Vector3 Min;
        public Vector3 Max;
        public RoomMaterial Material;
        public bool Solid;

        public Vector3 Center => (Min + Max) * 0.5f;

        public Vector3 Size => Max - Min;

        public bool ContainsRouteAt(float x)
        {
            return x >= Min.x && x <= Max.x && Min.z <= 0f && Max.z >= 0f;
        }
    }

    /// <summary>One <see cref="CurvedWall"/>, the only surface in a generated room that can be wall run.</summary>
    public sealed class CurvePlan
    {
        public string Name;
        public Vector3 Position;
        public float Radius;
        public float StraightLength;
        public float Width;
        public float Thickness;

        /// <summary>Where the curve meets its ledge, so the route check knows the span that needs a wall run.</summary>
        public float EndX;

        /// <summary>Material of the fill under the curve, which follows the curve's underside exactly.</summary>
        public RoomMaterial FillMaterial;

        /// <summary>True when the curve rises toward the entrance instead of the exit, as the near wall of a shaft does.</summary>
        public bool Mirror;
    }

    /// <summary>One <see cref="CrumblingSlab"/>: a block that falls away once stood on.</summary>
    public sealed class CrumblePlan
    {
        public BlockPlan Block;
        public float Delay;
    }

    /// <summary>A trigger box, relative to the room's position, such as a pit's hazard.</summary>
    public sealed class VolumePlan
    {
        public string Name;
        public Vector3 Min;
        public Vector3 Max;

        public Vector3 Center => (Min + Max) * 0.5f;

        public Vector3 Size => Max - Min;
    }

    /// <summary>A falling debris point, relative to the room's position.</summary>
    public sealed class DebrisPlan
    {
        public Vector3 Position;
        public float Interval;
        public float InitialDelay;

        /// <summary>Height below which the debris is removed, relative to the room's entrance floor.</summary>
        public float DestroyHeight;
    }

    /// <summary>One <see cref="ChasePathPoint"/>, relative to the room's position.</summary>
    public sealed class PathPointPlan
    {
        public Vector3 Position;
        public bool Blink;
        public bool RoomStart;
    }

    /// <summary>A stretch of the route, from one distance along the room to another.</summary>
    public struct RouteSpan
    {
        public float Start;
        public float End;

        public RouteSpan(float start, float end)
        {
            Start = start;
            End = end;
        }

        public float Length => End - Start;

        public bool Contains(float x)
        {
            return x >= Start && x < End;
        }
    }

    /// <summary>One existing prefab, relative to the room's position.</summary>
    public sealed class PrefabPlan
    {
        public RoomPrefab Prefab;
        public string Name;
        public RoomGroup Group;
        public Vector3 Position;

        /// <summary>Replaces the prefab's own scale when set. Partitions are sized by their scale.</summary>
        public Vector3? Scale;
    }

    /// <summary>One light source, relative to the room's position.</summary>
    public sealed class LightPlan
    {
        public string Name;
        public LightType Type;
        public Vector3 Position;
        public Color Color;
        public float Intensity;
        public float Range;
        public float SpotAngle;
        public bool Shadows;
    }

    /// <summary>
    /// Everything a room will contain, worked out without touching the scene. The same plan feeds the Scene view preview,
    /// the route check and the builder, so what is previewed and checked is what gets built.
    /// </summary>
    public sealed class RoomPlan
    {
        public readonly List<BlockPlan> Blocks = new List<BlockPlan>();
        public readonly List<CurvePlan> Curves = new List<CurvePlan>();
        public readonly List<PrefabPlan> Prefabs = new List<PrefabPlan>();
        public readonly List<LightPlan> Lights = new List<LightPlan>();

        public readonly List<CrumblePlan> Crumbles = new List<CrumblePlan>();
        public readonly List<VolumePlan> Hazards = new List<VolumePlan>();
        public readonly List<DebrisPlan> Debris = new List<DebrisPlan>();
        public readonly List<PathPointPlan> PathPoints = new List<PathPointPlan>();

        /// <summary>The room's Room Reset area, or null when the room has none.</summary>
        public VolumePlan Restart;

        /// <summary>Where the Room Reset puts the player back: the standing point, relative to the room.</summary>
        public Vector3 RestartEntry;

        /// <summary>Gaps in the floor the route has to jump, or cross on a crumbling bridge.</summary>
        public readonly List<RouteSpan> Gaps = new List<RouteSpan>();

        /// <summary>Stretches under a low hood, which the player has to slide through.</summary>
        public readonly List<RouteSpan> SlideSpans = new List<RouteSpan>();

        /// <summary>A wall jump shaft the route climbs out of, or null.</summary>
        public RouteSpan? Shaft;

        /// <summary>How far below the entrance floor the room reaches, for a pit or a shaft. The camera has to see it.</summary>
        public float BelowFloor;

        /// <summary>Problems that stop the room being built.</summary>
        public readonly List<string> Errors = new List<string>();

        /// <summary>Things the designer should know, such as what still blocks the route.</summary>
        public readonly List<string> Notes = new List<string>();

        public Vector3 ExitOffset;

        /// <summary>How much further back the game camera sits in this room, so a tall room can be read. 0 adds no camera zone.</summary>
        public float CameraExtraDistance;

        /// <summary>The smallest Width the chosen options need, such as the run up and climb of a level change.</summary>
        public float NeededWidth;

        /// <summary>The smallest Height the chosen options need, such as the exit above a level change or the gallery window.</summary>
        public float NeededHeight;

        /// <summary>How many of the wanted low barriers, interior thresholds and recesses found no free bay. Each needs about one more bay.</summary>
        public int MissingPieces;

        public bool HasReflectionProbe;
        public Bounds ProbeBounds;

        public bool CanBuild => Errors.Count == 0;
    }
}

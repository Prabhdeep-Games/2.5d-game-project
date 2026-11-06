using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace TheSpaceBetween
{
    /// <summary>What a generated room is for. Choosing one loads a starting set of values, which can then be changed.</summary>
    public enum RoomPurpose
    {
        ObservationChamber,
        ContainmentCell,
        RestrictedCorridor,
        MonumentalHall,
        VerticalConnection,
        Custom
    }

    /// <summary>How a generated room is lit. Choosing one loads a starting set of values, which can then be changed.</summary>
    public enum RoomLightingMode
    {
        FunctionalInstitutional,
        SparseFailed,
        HighContrastObservation,
        Emergency,
        Custom
    }

    /// <summary>The concrete materials a room's main surfaces can use.</summary>
    public enum RoomFinish
    {
        Concrete,
        ConcretePale,
        ConcreteSlate
    }

    /// <summary>How a room connects at its entrance or exit.</summary>
    public enum RoomThreshold
    {
        Solid,
        Open,
        Framed,
        SealedDoor,
        PanelDoor
    }

    /// <summary>How the route rises from the entrance floor to the exit floor.</summary>
    public enum RoomLevelChange
    {
        None,
        Stairs,
        CurvedWall
    }

    /// <summary>
    /// Every value of a generated room. The Room Generator window edits this, and each generated room keeps a copy, so it
    /// can be regenerated later with the same result. It holds no behaviour apart from loading the purpose and lighting values.
    /// </summary>
    [Serializable]
    public class RoomSettings
    {
        //==================== PURPOSE =====================
        [Header("Purpose")]
        [Tooltip("What the room communicates. Changing this replaces the values below with a starting point for that purpose.")]
        [SerializeField] private RoomPurpose purpose = RoomPurpose.ObservationChamber;

        [Tooltip("Decides which bays get recesses and panels, and which fixtures have failed. The same seed and values always give the same room.")]
        [SerializeField] private int seed = 1;

        //==================== DIMENSIONS =====================
        [Header("Dimensions")]
        [Tooltip("Length of the room along the route, in world units, including both end walls.")]
        [SerializeField, Range(8f, 96f)] private float width = 32f;

        [Tooltip("Distance from the entrance floor to the ceiling. The Observation Chamber is about 19.")]
        [SerializeField, Range(4.5f, 40f)] private float height = 16f;

        [Tooltip("Distance from the route to the back wall. The route itself is 2 either side of the centre line, so 2.5 feels tight and 8 feels vast.")]
        [SerializeField, Range(2.5f, 12f)] private float depth = 6f;

        [Tooltip("Thickness of the end walls, the back wall and the ceiling.")]
        [SerializeField, Range(0.5f, 4f)] private float wallThickness = 1f;

        //==================== STRUCTURE =====================
        [Header("Structure")]
        [Tooltip("Material of the walls.")]
        [SerializeField] private RoomFinish wallFinish = RoomFinish.Concrete;

        [Tooltip("Material of the floor.")]
        [SerializeField] private RoomFinish floorFinish = RoomFinish.Concrete;

        [Tooltip("Material of the ceiling.")]
        [SerializeField] private RoomFinish ceilingFinish = RoomFinish.Concrete;

        [Tooltip("Target length of one repeated bay. The room is divided into whole bays, so the real length can differ slightly. The Observation Chamber uses 8.")]
        [SerializeField, Range(4f, 16f)] private float bayWidth = 8f;

        [Tooltip("How far the pilaster at each bay line stands out from the back wall. 0 leaves them out.")]
        [SerializeField, Range(0f, 3f)] private float pilasterDepth = 1.1f;

        [Tooltip("Adds a concrete beam under the ceiling at each bay line.")]
        [SerializeField] private bool ceilingBeams = true;

        [Tooltip("How many bays get a recess in the back wall, closed by a steel plate. They read as openings that have been sealed.")]
        [SerializeField, Range(0, 6)] private int recessedBays = 1;

        [Tooltip("How many bays get a pale replacement panel on the back wall, so the building looks maintained over time.")]
        [SerializeField, Range(0, 8)] private int repairPanels = 2;

        //==================== CIRCULATION =====================
        [Header("Circulation")]
        [Tooltip("How the player comes in. A Panel Door is not available here, because its panel would have to stand outside the room. Sealed stays shut until you connect it to something.")]
        [SerializeField] private RoomThreshold entrance = RoomThreshold.Framed;

        [Tooltip("How the player leaves. Panel Door adds a Control Panel inside the room, already connected. Sealed stays shut until you connect it to something.")]
        [SerializeField] private RoomThreshold exit = RoomThreshold.PanelDoor;

        [Tooltip("Height of an open or framed opening. Doors always use 4, their real height.")]
        [SerializeField, Range(3f, 12f)] private float openingHeight = 4.5f;

        [Tooltip("How many full height cross walls with an opening divide the room. These are the layered thresholds of a controlled space.")]
        [SerializeField, Range(0, 4)] private int interiorThresholds;

        [Tooltip("How the route reaches the exit when it is higher than the entrance. Curved Wall makes it a wall run, always starting from a curve.")]
        [SerializeField] private RoomLevelChange levelChange = RoomLevelChange.None;

        [Tooltip("How much higher the exit floor is than the entrance floor. Ignored when Level Change is None.")]
        [ShowIf(nameof(levelChange), RoomLevelChange.Stairs, RoomLevelChange.CurvedWall)]
        [SerializeField, Range(0f, 24f)] private float exitRise;

        //==================== CONTROL AND VISIBILITY =====================
        [Header("Control and Visibility")]
        [Tooltip("Adds a Glass Partition across the route: the far side can be seen and swapped to, but not walked to. This blocks the route until you place a swap object.")]
        [SerializeField] private bool glassPartition;

        [Tooltip("Where the Glass Partition stands, from the entrance (0) to the exit (1). It snaps to the nearest bay line.")]
        [ShowIf(nameof(glassPartition))]
        [SerializeField, Range(0f, 1f)] private float partitionPosition = 0.5f;

        [Tooltip("How many low Industrial Barriers stand on the route. The player jumps over them.")]
        [SerializeField, Range(0, 4)] private int lowBarriers;

        [Tooltip("Adds a raised, separately lit room behind a window in the back wall. It can be seen but never entered.")]
        [SerializeField] private bool observationGallery = true;

        [Tooltip("How many bays the gallery window spans.")]
        [ShowIf(nameof(observationGallery))]
        [SerializeField, Range(1, 6)] private int galleryBays = 2;

        [Tooltip("Height of the bottom of the gallery window above the floor. Higher means the player is looked down on.")]
        [ShowIf(nameof(observationGallery))]
        [SerializeField, Range(1f, 30f)] private float gallerySill = 8f;

        [Tooltip("Adds a Security Camera on the ceiling, looking down at the route.")]
        [SerializeField] private bool securityCamera;

        [Tooltip("Where the Security Camera hangs, from the entrance (0) to the exit (1). It snaps to the nearest bay centre.")]
        [ShowIf(nameof(securityCamera))]
        [SerializeField, Range(0f, 1f)] private float cameraPosition = 0.5f;

        [Tooltip("The exit door seals while the camera sees the player, and reopens shortly after. Needs a door at the exit.")]
        [ShowIf(nameof(securityCamera))]
        [SerializeField] private bool cameraLocksExit;

        //==================== LIGHTING =====================
        [Header("Lighting")]
        [Tooltip("The kind of light in the room. Changing this replaces the lighting values below with a starting point for that mode.")]
        [SerializeField] private RoomLightingMode lightingMode = RoomLightingMode.HighContrastObservation;

        [Tooltip("Adds light panels recessed into the ceiling, at bay centres.")]
        [SerializeField] private bool ceilingFixtures = true;

        [Tooltip("Adds fixtures on the back wall, at bay centres. Bays with a window or a recess are skipped.")]
        [SerializeField] private bool wallFixtures;

        [Tooltip("A fixture every this many bays. Higher leaves pools of light with darkness between them.")]
        [SerializeField, Range(1, 4)] private int fixtureEveryBays = 2;

        [Tooltip("How bright the floor is under a fixture. The same value looks the same in any room height, because the light's strength is worked out from its distance.")]
        [SerializeField, Range(0f, 3f)] private float brightness = 0.5f;

        [Tooltip("Soft light from the camera's side that keeps the player and the route readable. Lower gives harder contrast.")]
        [SerializeField, Range(0f, 1f)] private float fillLevel = 0.2f;

        [Tooltip("Colour of the fixtures and fill. Keep it close to white, or amber for emergency light.")]
        [SerializeField] private Color lightColor = new Color(0.863f, 0.898f, 0.906f);

        [Tooltip("Width of the pool under each ceiling fixture, in degrees.")]
        [ShowIf(nameof(ceilingFixtures))]
        [SerializeField, Range(30f, 150f)] private float spotAngle = 80f;

        [Tooltip("How many ceiling fixtures cast real time shadows, starting nearest the middle of the room. These are the most expensive lights, so keep this low.")]
        [ShowIf(nameof(ceilingFixtures))]
        [SerializeField, Range(0, 3)] private int shadowCasters = 2;

        [Tooltip("Share of the fixtures that have failed: they are dark and give no light. Which ones comes from the seed.")]
        [SerializeField, Range(0f, 0.75f)] private float failedFixtures;

        [Tooltip("How much fixtures differ in brightness, as a share of Brightness. The differences come from the seed.")]
        [SerializeField, Range(0f, 0.5f)] private float brightnessVariation = 0.1f;

        [Tooltip("How bright the observation gallery is. Well above the room's Brightness makes it read as the seat of authority.")]
        [ShowIf(nameof(observationGallery))]
        [SerializeField, Range(0f, 4f)] private float galleryBrightness = 2.4f;

        [Tooltip("Adds one reflection probe that updates when the scene loads. No scene in the project uses one so far, so leave this off unless a room has shiny surfaces.")]
        [SerializeField] private bool reflectionProbe;

        // Fog and ambient light belong to the whole scene, the values in Window > Rendering > Lighting > Environment.
        [Space]
        [Tooltip("Sets the scene's fog (Lighting window > Environment > Fog) from the values below. Turning it off, or clearing the room, puts the scene's earlier fog back. The Art Direction Bible warns against using fog as a substitute for composition.")]
        [FormerlySerializedAs("fog")]
        [SerializeField] private bool sceneFog;

        [Tooltip("Linear fades from Fog Start to Fog End, which is the easiest to control. Exponential modes use Fog Density instead.")]
        [ShowIf(nameof(sceneFog))]
        [SerializeField] private FogMode fogMode = FogMode.Linear;

        [Tooltip("Colour that distant surfaces fade into. Close to the darkest wall colour keeps depth without a visible haze.")]
        [ShowIf(nameof(sceneFog))]
        [SerializeField] private Color fogColor = new Color(0.145f, 0.176f, 0.196f);

        [Tooltip("Distance from the camera, in world units, where Linear fog begins. The game camera is about 11 units from the route.")]
        [ShowIf(nameof(fogMode), FogMode.Linear)]
        [SerializeField, Min(0f)] private float fogStart = 20f;

        [Tooltip("Distance from the camera, in world units, where Linear fog is complete.")]
        [ShowIf(nameof(fogMode), FogMode.Linear)]
        [SerializeField, Min(0f)] private float fogEnd = 90f;

        [Tooltip("How quickly Exponential and Exponential Squared fog thickens. About 0.02 is visible at room distances.")]
        [ShowIf(nameof(fogMode), FogMode.Exponential, FogMode.ExponentialSquared)]
        [SerializeField, Range(0f, 0.2f)] private float fogDensity = 0.02f;

        [Tooltip("Sets the scene's ambient light to a flat Ambient Color. Turning it off, or clearing the room, puts the scene's earlier ambient light back.")]
        [FormerlySerializedAs("applyGlobalLighting")]
        [SerializeField] private bool sceneAmbient;

        [Tooltip("Scene-wide ambient colour, used when Scene Ambient is on.")]
        [ShowIf(nameof(sceneAmbient))]
        [SerializeField] private Color ambientColor = new Color(0.29f, 0.322f, 0.337f);

        //==================== READ ACCESS =====================
        public RoomPurpose Purpose => purpose;
        public int Seed => seed;
        public float Width => width;
        public float Height => height;
        public float Depth => depth;
        public float WallThickness => wallThickness;
        public RoomFinish WallFinish => wallFinish;
        public RoomFinish FloorFinish => floorFinish;
        public RoomFinish CeilingFinish => ceilingFinish;
        public float BayWidth => bayWidth;
        public float PilasterDepth => pilasterDepth;
        public bool CeilingBeams => ceilingBeams;
        public int RecessedBays => recessedBays;
        public int RepairPanels => repairPanels;
        public RoomThreshold Entrance => entrance;
        public RoomThreshold Exit => exit;
        public float OpeningHeight => openingHeight;
        public int InteriorThresholds => interiorThresholds;
        public RoomLevelChange LevelChange => levelChange;
        public float ExitRise => exitRise;
        public bool GlassPartition => glassPartition;
        public float PartitionPosition => partitionPosition;
        public int LowBarriers => lowBarriers;
        public bool ObservationGallery => observationGallery;
        public int GalleryBays => galleryBays;
        public float GallerySill => gallerySill;
        public bool SecurityCamera => securityCamera;
        public float CameraPosition => cameraPosition;
        public bool CameraLocksExit => cameraLocksExit;
        public RoomLightingMode LightingMode => lightingMode;
        public bool CeilingFixtures => ceilingFixtures;
        public bool WallFixtures => wallFixtures;
        public int FixtureEveryBays => fixtureEveryBays;
        public float Brightness => brightness;
        public float FillLevel => fillLevel;
        public Color LightColor => lightColor;
        public float SpotAngle => spotAngle;
        public int ShadowCasters => shadowCasters;
        public float FailedFixtures => failedFixtures;
        public float BrightnessVariation => brightnessVariation;
        public float GalleryBrightness => galleryBrightness;
        public bool ReflectionProbe => reflectionProbe;
        public bool SceneFog => sceneFog;
        public FogMode FogMode => fogMode;
        public Color FogColor => fogColor;
        public float FogStart => fogStart;
        public float FogEnd => fogEnd;
        public float FogDensity => fogDensity;
        public bool SceneAmbient => sceneAmbient;
        public Color AmbientColor => ambientColor;

        /// <summary>Whether this room changes any scene-wide lighting.</summary>
        public bool ChangesSceneLighting => sceneFog || sceneAmbient;

        //==================== CHANGES =====================
        /// <summary>Sets the seed.</summary>
        public void SetSeed(int value)
        {
            seed = value;
        }

        /// <summary>Sets the room's length and height. The Room Generator uses it to make room for an option.</summary>
        public void SetSize(float newWidth, float newHeight)
        {
            width = newWidth;
            height = newHeight;
        }

        /// <summary>Sets how much higher the exit floor is than the entrance floor.</summary>
        public void SetExitRise(float value)
        {
            exitRise = value;
        }

        /// <summary>Sets how the player leaves the room.</summary>
        public void SetExit(RoomThreshold value)
        {
            exit = value;
        }

        /// <summary>An independent copy, so a generated room is not changed by later edits in the window.</summary>
        public RoomSettings Clone()
        {
            return JsonUtility.FromJson<RoomSettings>(JsonUtility.ToJson(this));
        }

        /// <summary>Loads the starting values for a purpose, including its lighting. Custom changes nothing.</summary>
        public void ApplyPurpose(RoomPurpose value)
        {
            purpose = value;
            switch (value)
            {
                // Looked down on through a bright window: visibility without access.
                case RoomPurpose.ObservationChamber:
                    SetShape(32f, 16f, 6f, 1f, 8f, 1.1f, true);
                    SetFinish(RoomFinish.Concrete, RoomFinish.Concrete, RoomFinish.Concrete);
                    SetCirculation(RoomThreshold.Framed, RoomThreshold.PanelDoor, 0, RoomLevelChange.None, 0f);
                    SetControl(true, 2, 8f, false, 0, 1, 2);
                    ApplyLighting(RoomLightingMode.HighContrastObservation);
                    break;

                // Low, thick and enclosed, with a door the player has to work for.
                case RoomPurpose.ContainmentCell:
                    SetShape(18f, 6f, 3.5f, 1.6f, 4.5f, 0.5f, true);
                    SetFinish(RoomFinish.ConcreteSlate, RoomFinish.Concrete, RoomFinish.ConcreteSlate);
                    SetCirculation(RoomThreshold.Framed, RoomThreshold.PanelDoor, 1, RoomLevelChange.None, 0f);
                    SetControl(false, 1, 3f, false, 0, 2, 1);
                    ApplyLighting(RoomLightingMode.SparseFailed);
                    break;

                // Long and narrow, watched from above, with a single way forward.
                case RoomPurpose.RestrictedCorridor:
                    SetShape(40f, 5f, 2.5f, 1f, 8f, 0.4f, true);
                    SetFinish(RoomFinish.Concrete, RoomFinish.Concrete, RoomFinish.ConcreteSlate);
                    SetCirculation(RoomThreshold.Framed, RoomThreshold.Framed, 2, RoomLevelChange.None, 0f);
                    SetControl(false, 1, 3f, true, 0, 2, 1);
                    ApplyLighting(RoomLightingMode.FunctionalInstitutional);
                    break;

                // Far too big for the person in it: scale as authority.
                case RoomPurpose.MonumentalHall:
                    SetShape(56f, 30f, 8f, 2f, 14f, 2f, false);
                    SetFinish(RoomFinish.Concrete, RoomFinish.ConcreteSlate, RoomFinish.ConcreteSlate);
                    SetCirculation(RoomThreshold.Framed, RoomThreshold.Framed, 0, RoomLevelChange.None, 0f);
                    SetControl(true, 1, 18f, false, 1, 2, 2);
                    ApplyLighting(RoomLightingMode.SparseFailed);

                    // Its bays are wide, so every second one would leave the player in the dark for too long.
                    fixtureEveryBays = 1;
                    break;

                // Up to the next level by a wall run, which always starts from a curve.
                case RoomPurpose.VerticalConnection:
                    SetShape(40f, 22f, 5f, 1f, 8f, 0.8f, false);
                    SetFinish(RoomFinish.Concrete, RoomFinish.Concrete, RoomFinish.Concrete);
                    SetCirculation(RoomThreshold.Framed, RoomThreshold.Framed, 0, RoomLevelChange.CurvedWall, 12f);
                    SetControl(false, 1, 3f, false, 0, 1, 1);
                    ApplyLighting(RoomLightingMode.FunctionalInstitutional);
                    break;
            }
        }

        /// <summary>Loads the starting lighting values for a mode. Custom changes nothing.</summary>
        public void ApplyLighting(RoomLightingMode value)
        {
            lightingMode = value;
            switch (value)
            {
                // Even and clinical: nowhere feels hidden.
                case RoomLightingMode.FunctionalInstitutional:
                    SetLighting(true, false, 1, 0.55f, 0.4f, new Color(0.89f, 0.922f, 0.929f), 110f, 1, 0f, 0.05f, 0.8f);
                    break;

                // Pools of light with dead fixtures between them, but still navigable.
                case RoomLightingMode.SparseFailed:
                    SetLighting(true, false, 2, 0.8f, 0.3f, new Color(0.851f, 0.859f, 0.824f), 90f, 1, 0.34f, 0.2f, 0.5f);
                    break;

                // A dim room under a much brighter window.
                case RoomLightingMode.HighContrastObservation:
                    SetLighting(true, false, 2, 0.5f, 0.2f, new Color(0.863f, 0.898f, 0.906f), 80f, 2, 0f, 0.1f, 2.4f);
                    break;

                // The Art Direction Bible's restrained amber, on the walls only.
                case RoomLightingMode.Emergency:
                    SetLighting(false, true, 1, 1.4f, 0.3f, new Color(0.827f, 0.631f, 0.302f), 90f, 0, 0.2f, 0.15f, 0.3f);
                    break;
            }
        }

        private void SetShape(float newWidth, float newHeight, float newDepth, float thickness, float bay, float pilaster, bool beams)
        {
            width = newWidth;
            height = newHeight;
            depth = newDepth;
            wallThickness = thickness;
            bayWidth = bay;
            pilasterDepth = pilaster;
            ceilingBeams = beams;
        }

        private void SetFinish(RoomFinish walls, RoomFinish floor, RoomFinish ceiling)
        {
            wallFinish = walls;
            floorFinish = floor;
            ceilingFinish = ceiling;
        }

        private void SetCirculation(RoomThreshold newEntrance, RoomThreshold newExit, int thresholds, RoomLevelChange change, float rise)
        {
            entrance = newEntrance;
            exit = newExit;
            interiorThresholds = thresholds;
            levelChange = change;
            exitRise = rise;
        }

        private void SetControl(bool gallery, int windowBays, float sill, bool camera, int barriers, int recesses, int panels)
        {
            observationGallery = gallery;
            galleryBays = windowBays;
            gallerySill = sill;
            securityCamera = camera;
            cameraLocksExit = false;
            glassPartition = false;
            lowBarriers = barriers;
            recessedBays = recesses;
            repairPanels = panels;
        }

        private void SetLighting(bool ceiling, bool wall, int everyBays, float level, float fill, Color color, float angle, int shadows, float failed, float variation, float gallery)
        {
            ceilingFixtures = ceiling;
            wallFixtures = wall;
            fixtureEveryBays = everyBays;
            brightness = level;
            fillLevel = fill;
            lightColor = color;
            spotAngle = angle;
            shadowCasters = shadows;
            failedFixtures = failed;
            brightnessVariation = variation;
            galleryBrightness = gallery;
        }
    }
}

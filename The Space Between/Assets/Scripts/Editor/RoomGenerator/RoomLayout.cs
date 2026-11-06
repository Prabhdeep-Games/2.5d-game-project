using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Turns <see cref="RoomSettings"/> into a <see cref="RoomPlan"/>. The room runs along X from the entrance at 0, with its
    /// entrance floor at Y 0 and the route on the centre line at Z 0. The camera looks along +Z, so the near side stays open
    /// and the room is seen as a cut through the building, as in the Observation Chamber.
    ///
    /// The structure is fixed by the settings. The seed only chooses between positions that are already valid, which keeps
    /// every result coherent: which bays are recessed, where panels sit, which fixtures have failed.
    /// </summary>
    public sealed class RoomLayout
    {
        // The depth of every existing room: floors run to the front, walls start behind the near edge of the floor.
        private const float FrontZ = -8f;
        private const float WallFrontZ = -5f;
        private const float RouteEdgeZ = 1.9f;
        private const float FloorThickness = 4f;

        // Real sizes of the prefabs, measured from their meshes.
        private const float DoorHeight = 4f;
        private const float DoorCenterHeight = 2f;
        private const float PanelCenterHeight = 1f;
        private const float PanelDistanceFromDoor = 3f;
        private const float CameraTopAbovePivot = 0.7f;
        private const float BarrierWidth = 0.6f;
        private const float BarrierHeight = 1f;
        private const float PartitionThickness = 0.3f;
        private const float PrefabRouteDepth = 4f;
        private const float CameraViewBeyondFloor = 1f;

        // Level changes. A rise of 0.25 is within the player's step, so these stairs need no jump.
        private const float StairRise = 0.25f;
        private const float StairTread = 0.5f;
        private const float StairLanding = 5f;
        private const float StairRunUp = 3f;
        private const float CurveRadius = 4f;
        private const float CurveThickness = 0.6f;
        private const float CurveLedge = 7f;
        private const float CurveRunUp = 8f;
        public const float CurveMinimumRise = 6f;
        public const float CurveMaximumRise = 16f;
        private const float CurveHeadroom = 1.5f;
        private const int CurveSegments = 24;

        // Architectural details.
        private const float BeamDepth = 0.8f;
        private const float LintelDepth = 0.35f;
        private const float RecessDepth = 1.6f;
        private const float RecessPlate = 0.3f;
        private const float RecessReturn = 0.3f;
        private const float RecessExtraHeight = 1.5f;
        private const float RecessSoffit = 0.3f;
        private const float PanelProud = 0.02f;
        private const float PanelGrid = 2f;
        private const float PanelMargin = 1.4f;
        private const int PanelMinimumRows = 1;
        private const int PanelMaximumRows = 3;
        private const float BarrierEntryClearance = 5f;
        private const float BarrierThresholdClearance = 3f;

        // What the camera sees above the ceiling is more building, not an empty void. It is only seen, so it never collides.
        private const float UpperWallHeight = 40f;

        // The game camera shows about this much of a room's height before it needs to move back.
        private const float CameraClearHeight = 9f;
        private const float CameraMaximumExtra = 20f;

        // Gallery.
        private const float WindowInset = 0.6f;
        private const float WindowMaximumHeight = 4f;
        private const float WindowMinimumHeight = 1.5f;
        private const float WindowHeadroom = 1f;
        private const float GalleryDepth = 8f;
        private const float GalleryWall = 0.6f;
        private const float GalleryAboveWindow = 1.2f;
        private const float GalleryBelowSill = 0.4f;
        private const float GlassThickness = 0.1f;
        private const float GalleryLightRange = 18f;

        // Fixtures and lights.
        private const float FixtureWidthShare = 0.3f;
        private const float FixtureMaximumWidth = 4f;
        private const float FixtureDepth = 1.6f;
        private const float HousingThickness = 0.1f;
        private const float HousingMargin = 0.12f;
        private const float PanelThickness = 0.06f;
        private const float WallFixtureWidth = 1.2f;
        private const float WallFixtureHeight = 0.3f;
        private const float WallFixtureProud = 0.14f;
        private const float WallFixtureAboveOpening = 1f;
        private const float WallFixtureBelowCeiling = 1f;
        private const float WallLightOffset = 1f;
        private const float WallLightDistance = 3f;
        private const float WallLightRange = 11f;
        private const float SpotBelowFixture = 0.25f;
        private const float SpotOffsetZ = 0.5f;
        private const float SpotRangeFactor = 2.2f;
        private const float FillSpacing = 16f;
        private const float FillZ = -10f;
        private const float FillHeightShare = 0.55f;

        // Fill is there to keep the player readable, so even in a tall room it stays near the player.
        private const float FillMaximumHeight = 8f;
        private const float FillDistance = 12f;
        private const float FillRange = 36f;

        private readonly RoomSettings settings;
        private readonly RoomPlan plan = new RoomPlan();
        private readonly System.Random random;

        private readonly float width;
        private readonly float height;
        private readonly float wall;
        private readonly float backZ;
        private readonly int bayCount;
        private readonly float bay;

        private RoomLevelChange levelChange;
        private float rise;
        private float flatEnd;
        private readonly HashSet<int> crossWallLines = new HashSet<int>();
        private readonly HashSet<int> windowBays = new HashSet<int>();
        private readonly HashSet<int> recessBays = new HashSet<int>();
        private int partitionLine = -1;
        private int cameraBay = -1;
        private bool hasExitDoor;

        private RoomLayout(RoomSettings settings)
        {
            this.settings = settings;
            random = new System.Random(settings.Seed);
            width = settings.Width;
            height = settings.Height;
            wall = settings.WallThickness;
            backZ = settings.Depth;
            bayCount = Mathf.Max(1, Mathf.RoundToInt(width / settings.BayWidth));
            bay = width / bayCount;
        }

        /// <summary>Works out a room. The same settings always give the same plan.</summary>
        public static RoomPlan Create(RoomSettings settings)
        {
            var layout = new RoomLayout(settings);
            layout.Build();
            return layout.plan;
        }

        private void Build()
        {
            DecideLevelChange();
            if (!plan.CanBuild)
            {
                return;
            }

            DecideBays();
            AddFloorAndCeiling();
            AddLevelChange();
            AddEndWalls();
            AddInteriorThresholds();
            AddBackWall();
            AddPilastersAndBeams();
            AddRepairPanels();
            AddBarriers();
            AddPartition();
            AddSecurityCamera();
            AddCeilingFixtures();
            AddWallFixtures();
            AddFill();
            AddReflectionProbe();
            AddNotes();
        }

        //==================== DECISIONS =====================
        private void DecideLevelChange()
        {
            levelChange = settings.LevelChange;
            rise = levelChange == RoomLevelChange.None ? 0f : settings.ExitRise;
            if (levelChange != RoomLevelChange.None && rise <= 0f)
            {
                levelChange = RoomLevelChange.None;
                plan.Notes.Add("Level Change is set, but Exit Rise is 0, so the floor stays level.");
            }

            float runUp = 0f;
            float needed = 0f;
            switch (levelChange)
            {
                case RoomLevelChange.None:
                    flatEnd = width - wall;
                    break;

                case RoomLevelChange.Stairs:
                    needed = Mathf.Ceil(rise / StairRise) * StairTread + StairLanding;
                    runUp = StairRunUp;
                    flatEnd = width - wall - needed;
                    break;

                case RoomLevelChange.CurvedWall:
                    if (rise < CurveMinimumRise || rise > CurveMaximumRise)
                    {
                        plan.Errors.Add($"A curved wall needs an Exit Rise from {CurveMinimumRise} to {CurveMaximumRise}, the range its wall run is tuned for.");
                    }

                    needed = CurveRadius + CurveThickness + CurveLedge;
                    runUp = CurveRunUp;
                    flatEnd = width - wall - needed;
                    break;
            }

            if (levelChange != RoomLevelChange.None)
            {
                plan.NeededWidth = Mathf.Ceil(wall * 2f + runUp + needed);
            }

            if (flatEnd < wall + runUp)
            {
                plan.Errors.Add($"The room is too short for this level change: Width needs to be at least {Mathf.Ceil(wall * 2f + runUp + needed)}.");
            }

            float opening = EffectiveOpening(settings.Exit);
            float headroom = levelChange == RoomLevelChange.CurvedWall ? CurveHeadroom : 0f;
            plan.NeededHeight = Mathf.Max(EffectiveOpening(settings.Entrance), settings.Exit != RoomThreshold.Solid ? rise + opening + headroom : 0f);
            if (settings.Exit != RoomThreshold.Solid && height < rise + opening + headroom)
            {
                plan.Errors.Add($"The ceiling is too low for the exit: Height needs to be at least {rise + opening + headroom}.");
            }

            if (height < EffectiveOpening(settings.Entrance))
            {
                plan.Errors.Add($"The ceiling is lower than the entrance: Height needs to be at least {EffectiveOpening(settings.Entrance)}.");
            }

            if (settings.Entrance == RoomThreshold.PanelDoor)
            {
                plan.Errors.Add("An entrance cannot be a Panel Door, because its panel would stand outside the room. Use Sealed Door and connect it to a panel in the room before.");
            }

            plan.ExitOffset = new Vector3(width, rise, 0f);
            plan.CameraExtraDistance = Mathf.Clamp(height - CameraClearHeight, 0f, CameraMaximumExtra);
        }

        private void DecideBays()
        {
            // Interior lines the route passes on the flat part of the floor. A line needs clear floor on both sides.
            var freeLines = new List<int>();
            for (int line = 1; line < bayCount; line++)
            {
                float x = line * bay;
                if (x > wall + 1f && x < flatEnd - 1f)
                {
                    freeLines.Add(line);
                }
            }

            if (settings.GlassPartition)
            {
                if (freeLines.Count == 0)
                {
                    plan.Notes.Add("No free bay line for the Glass Partition, so it was left out. Use a longer room or narrower bays.");
                }
                else
                {
                    partitionLine = NearestLine(freeLines, settings.PartitionPosition * width);
                    freeLines.Remove(partitionLine);
                }
            }

            for (int i = 0; i < settings.InteriorThresholds && freeLines.Count > 0; i++)
            {
                float target = width * (i + 1f) / (settings.InteriorThresholds + 1f);
                int line = NearestLine(freeLines, target);
                crossWallLines.Add(line);
                freeLines.Remove(line);
            }

            if (crossWallLines.Count < settings.InteriorThresholds)
            {
                plan.MissingPieces += settings.InteriorThresholds - crossWallLines.Count;
                plan.Notes.Add($"Only {crossWallLines.Count} of {settings.InteriorThresholds} interior thresholds fit. Use a longer room or narrower bays.");
            }

            // Bays whose back wall can be seen, not hidden behind stairs or a ledge.
            var freeBays = new List<int>();
            for (int i = 0; i < bayCount; i++)
            {
                if ((i + 1) * bay <= flatEnd + 0.01f)
                {
                    freeBays.Add(i);
                }
            }

            if (settings.ObservationGallery)
            {
                DecideWindow(freeBays);
            }

            if (settings.SecurityCamera)
            {
                cameraBay = NearestBay(freeBays, settings.CameraPosition * width);
            }

            int recesses = settings.RecessedBays;
            var candidates = new List<int>(freeBays);
            candidates.RemoveAll(i => windowBays.Contains(i) || RecessWouldHitGallery(i));
            while (recesses > 0 && candidates.Count > 0)
            {
                int pick = random.Next(candidates.Count);
                recessBays.Add(candidates[pick]);
                candidates.RemoveAt(pick);
                recesses--;
            }

            if (recesses > 0)
            {
                plan.MissingPieces += recesses;
                plan.Notes.Add($"{recesses} recess(es) did not fit, because no free bays were left.");
            }
        }

        // A recess beside the window reaches back to where the gallery's side wall is, unless it stays below the gallery.
        private bool RecessWouldHitGallery(int index)
        {
            if (!windowBays.Contains(index - 1) && !windowBays.Contains(index + 1))
            {
                return false;
            }

            float recessTop = Mathf.Min(settings.OpeningHeight + RecessExtraHeight, height) + RecessSoffit;
            float galleryBottom = settings.GallerySill - GalleryBelowSill - GalleryWall;
            return recessTop > galleryBottom && RecessDepth + RecessPlate > wall;
        }

        private void DecideWindow(List<int> freeBays)
        {
            float top = settings.GallerySill + WindowHeightFor(settings.GallerySill);
            if (freeBays.Count == 0 || WindowHeightFor(settings.GallerySill) < WindowMinimumHeight || top > height)
            {
                plan.Notes.Add("The observation gallery did not fit, so it was left out. Raise Height or lower Gallery Sill.");
                if (freeBays.Count > 0)
                {
                    plan.NeededHeight = Mathf.Max(plan.NeededHeight, settings.GallerySill + WindowMaximumHeight + WindowHeadroom);
                }

                return;
            }

            int count = Mathf.Min(settings.GalleryBays, freeBays.Count);
            int start = freeBays[(freeBays.Count - count) / 2];
            for (int i = 0; i < count; i++)
            {
                windowBays.Add(start + i);
            }
        }

        private float WindowHeightFor(float sill)
        {
            return Mathf.Min(WindowMaximumHeight, height - WindowHeadroom - sill);
        }

        private int NearestLine(List<int> lines, float x)
        {
            int best = lines[0];
            foreach (int line in lines)
            {
                if (Mathf.Abs(line * bay - x) < Mathf.Abs(best * bay - x))
                {
                    best = line;
                }
            }

            return best;
        }

        private int NearestBay(List<int> bays, float x)
        {
            if (bays.Count == 0)
            {
                return Mathf.Clamp(Mathf.FloorToInt(x / bay), 0, bayCount - 1);
            }

            int best = bays[0];
            foreach (int index in bays)
            {
                if (Mathf.Abs((index + 0.5f) * bay - x) < Mathf.Abs((best + 0.5f) * bay - x))
                {
                    best = index;
                }
            }

            return best;
        }

        private float EffectiveOpening(RoomThreshold threshold)
        {
            return threshold == RoomThreshold.SealedDoor || threshold == RoomThreshold.PanelDoor ? DoorHeight : settings.OpeningHeight;
        }

        //==================== SHELL =====================
        private void AddFloorAndCeiling()
        {
            RoomMaterial floor = (RoomMaterial)settings.FloorFinish;
            RoomMaterial ceiling = (RoomMaterial)settings.CeilingFinish;
            float floorBack = backZ + wall + (recessBays.Count > 0 ? RecessDepth + RecessPlate : 0f);

            if (levelChange == RoomLevelChange.CurvedWall)
            {
                float ledgeStart = flatEnd + CurveRadius + CurveThickness;
                Block("Floor", RoomGroup.Structure, 0f, flatEnd, -FloorThickness, 0f, FrontZ, floorBack, floor, true);
                Block("Floor Under Ledge", RoomGroup.Structure, ledgeStart, width, -FloorThickness, 0f, FrontZ, floorBack, floor, true);
            }
            else
            {
                Block("Floor", RoomGroup.Structure, 0f, width, -FloorThickness, 0f, FrontZ, floorBack, floor, true);
            }

            Block("Ceiling", RoomGroup.Structure, 0f, width, height, height + wall, FrontZ, backZ + wall, ceiling, true);
        }

        private void AddLevelChange()
        {
            RoomMaterial floor = (RoomMaterial)settings.FloorFinish;
            if (levelChange == RoomLevelChange.Stairs)
            {
                int steps = Mathf.CeilToInt(rise / StairRise);
                float stepRise = rise / steps;
                for (int i = 0; i < steps; i++)
                {
                    float x = flatEnd + i * StairTread;
                    Block("Stair " + (i + 1), RoomGroup.LevelChange, x, x + StairTread, 0f, stepRise * (i + 1), FrontZ, backZ, floor, true);
                }

                Block("Landing", RoomGroup.LevelChange, flatEnd + steps * StairTread, width - wall, 0f, rise, FrontZ, backZ, floor, true);
            }
            else if (levelChange == RoomLevelChange.CurvedWall)
            {
                float faceX = flatEnd + CurveRadius;
                float ledgeStart = faceX + CurveThickness;
                Block("Ledge", RoomGroup.LevelChange, ledgeStart, width - wall, 0f, rise, FrontZ, backZ, floor, true);
                Block("Curve Base", RoomGroup.LevelChange, flatEnd, ledgeStart, -FloorThickness, -CurveThickness, FrontZ, backZ + wall, floor, true);
                // The hollow under the curve is filled by a mesh that follows the curve's underside exactly, so the
                // wall reads as solid from the front without any surfaces lying on top of each other.
                plan.Curves.Add(new CurvePlan
                {
                    Name = "Curved Wall",
                    Position = new Vector3(flatEnd, 0f, (WallFrontZ + backZ) * 0.5f),
                    Radius = CurveRadius,
                    StraightLength = rise - CurveRadius,
                    Width = backZ - WallFrontZ,
                    Thickness = CurveThickness,
                    EndX = ledgeStart,
                    FillMaterial = floor
                });
            }
        }

        private void AddEndWalls()
        {
            AddThresholdWall("Entrance", 0f, wall, 0f, settings.Entrance);
            AddThresholdWall("Exit", width - wall, width, rise, settings.Exit);

            if (settings.Exit == RoomThreshold.SealedDoor || settings.Exit == RoomThreshold.PanelDoor)
            {
                hasExitDoor = true;
                if (settings.Exit == RoomThreshold.PanelDoor)
                {
                    float x = width - wall - PanelDistanceFromDoor;
                    plan.Prefabs.Add(new PrefabPlan { Prefab = RoomPrefab.ControlPanel, Name = "Exit Control Panel", Group = RoomGroup.Systems, Position = new Vector3(x, rise + PanelCenterHeight, 0f) });
                }
            }
        }

        // A wall across the route: a part behind the route and a part above the opening. The near side stays open to the camera.
        private void AddThresholdWall(string name, float x0, float x1, float floorY, RoomThreshold kind)
        {
            RoomMaterial finish = (RoomMaterial)settings.WallFinish;
            if (floorY > 0f)
            {
                Block(name + " Wall Base", RoomGroup.Openings, x0, x1, 0f, floorY, WallFrontZ, backZ, finish, true);
            }

            if (kind == RoomThreshold.Solid)
            {
                Block(name + " Wall", RoomGroup.Openings, x0, x1, floorY, height, WallFrontZ, backZ, finish, true);
                return;
            }

            float top = floorY + EffectiveOpening(kind);
            Block(name + " Jamb", RoomGroup.Openings, x0, x1, floorY, top, RouteEdgeZ, backZ, finish, true);
            if (top < height)
            {
                Block(name + " Header", RoomGroup.Openings, x0, x1, top, height, WallFrontZ, backZ, finish, true);
            }

            // The lintel spans only the open front of the doorway. Behind the route the jamb is already there, and a
            // lintel reaching into it would share both its side faces and flicker.
            if (kind == RoomThreshold.Framed)
            {
                Block(name + " Lintel", RoomGroup.Openings, x0, x1, top - LintelDepth, top, WallFrontZ, RouteEdgeZ, RoomMaterial.Steel, false);
            }
            else if (kind == RoomThreshold.SealedDoor || kind == RoomThreshold.PanelDoor)
            {
                plan.Prefabs.Add(new PrefabPlan { Prefab = RoomPrefab.SealedDoor, Name = name + " Door", Group = RoomGroup.Openings, Position = new Vector3((x0 + x1) * 0.5f, floorY + DoorCenterHeight, 0f) });
            }
        }

        private void AddInteriorThresholds()
        {
            int index = 1;
            foreach (int line in crossWallLines)
            {
                float half = Mathf.Min(wall, bay * 0.25f) * 0.5f;
                AddThresholdWall("Threshold " + index, line * bay - half, line * bay + half, 0f, RoomThreshold.Framed);
                index++;
            }
        }

        private void AddBackWall()
        {
            RoomMaterial finish = (RoomMaterial)settings.WallFinish;
            for (int i = 0; i < bayCount; i++)
            {
                float x0 = i * bay;
                float x1 = x0 + bay;
                if (windowBays.Contains(i))
                {
                    AddWindowBay(i, x0, x1, finish);
                }
                else if (recessBays.Contains(i))
                {
                    AddRecessBay(i, x0, x1, finish);
                }
                else
                {
                    Block("Back Wall " + (i + 1), RoomGroup.Structure, x0, x1, 0f, height, backZ, backZ + wall, finish, true);
                }
            }

            if (windowBays.Count > 0)
            {
                AddGallery();
            }

            Block("Upper Wall", RoomGroup.Structure, 0f, width, height + wall, height + wall + UpperWallHeight, backZ, backZ + wall, finish, false);
        }

        // The steel plate sits at the back of the recess: an opening that has been closed.
        private void AddRecessBay(int index, float x0, float x1, RoomMaterial finish)
        {
            string name = "Recess " + (index + 1);
            float top = Mathf.Min(settings.OpeningHeight + RecessExtraHeight, height);
            float plateFront = backZ + RecessDepth;
            float back = plateFront + RecessPlate;
            Block(name + " Plate", RoomGroup.Structure, x0 + RecessReturn, x1 - RecessReturn, 0f, top, plateFront, back, RoomMaterial.Steel, true);
            Block(name + " Side", RoomGroup.Structure, x0, x0 + RecessReturn, 0f, top, backZ, back, finish, true);
            Block(name + " Side", RoomGroup.Structure, x1 - RecessReturn, x1, 0f, top, backZ, back, finish, true);
            if (top < height)
            {
                Block(name + " Head", RoomGroup.Structure, x0, x1, top, height, backZ, backZ + wall, finish, true);
                if (back > backZ + wall)
                {
                    Block(name + " Soffit", RoomGroup.Structure, x0, x1, top, top + RecessSoffit, backZ + wall, back, finish, true);
                }
            }
            else if (back > backZ + wall)
            {
                Block(name + " Cap", RoomGroup.Structure, x0, x1, height, height + wall, backZ + wall, back, finish, true);
            }
        }

        private void AddWindowBay(int index, float x0, float x1, RoomMaterial finish)
        {
            string name = "Window Bay " + (index + 1);
            float sill = settings.GallerySill;
            float top = sill + WindowHeightFor(sill);
            Block(name + " Below", RoomGroup.Structure, x0, x1, 0f, sill, backZ, backZ + wall, finish, true);
            Block(name + " Above", RoomGroup.Structure, x0, x1, top, height, backZ, backZ + wall, finish, true);

            // The wall between two window bays stays, as a mullion, so the opening keeps to the structural rhythm.
            Block(name + " Side", RoomGroup.Structure, x0, x0 + WindowInset, sill, top, backZ, backZ + wall, finish, true);
            Block(name + " Side", RoomGroup.Structure, x1 - WindowInset, x1, sill, top, backZ, backZ + wall, finish, true);
            float glassZ = backZ + wall * 0.5f;
            Block(name + " Glass", RoomGroup.Gallery, x0 + WindowInset, x1 - WindowInset, sill, top, glassZ - GlassThickness * 0.5f, glassZ + GlassThickness * 0.5f, RoomMaterial.Glass, false);
        }

        private void AddGallery()
        {
            int first = int.MaxValue;
            int last = int.MinValue;
            foreach (int index in windowBays)
            {
                first = Mathf.Min(first, index);
                last = Mathf.Max(last, index);
            }

            float x0 = first * bay;
            float x1 = (last + 1) * bay;
            float sill = settings.GallerySill;
            float top = sill + WindowHeightFor(sill) + GalleryAboveWindow;
            float floorY = sill - GalleryBelowSill;
            float z0 = backZ + wall;
            float z1 = z0 + GalleryDepth;
            float t = GalleryWall;

            Block("Gallery Floor", RoomGroup.Gallery, x0 - t, x1 + t, floorY - t, floorY, z0, z1 + t, RoomMaterial.ConcretePale, true);
            Block("Gallery Ceiling", RoomGroup.Gallery, x0 - t, x1 + t, top, top + t, z0, z1 + t, RoomMaterial.ConcretePale, true);
            Block("Gallery Back", RoomGroup.Gallery, x0, x1, floorY, top, z1, z1 + t, RoomMaterial.ConcretePale, true);
            Block("Gallery Side", RoomGroup.Gallery, x0 - t, x0, floorY, top, z0, z1 + t, RoomMaterial.ConcretePale, true);
            Block("Gallery Side", RoomGroup.Gallery, x1, x1 + t, floorY, top, z0, z1 + t, RoomMaterial.ConcretePale, true);

            float center = (x0 + x1) * 0.5f;
            float panelWidth = Mathf.Min(x1 - x0 - WindowInset * 2f, bay);
            float panelZ = z0 + GalleryDepth * 0.5f;
            Block("Gallery Panel", RoomGroup.Gallery, center - panelWidth * 0.5f, center + panelWidth * 0.5f, top - PanelThickness, top, panelZ - FixtureDepth * 0.5f, panelZ + FixtureDepth * 0.5f, RoomMaterial.LightPanel, false);
            float distance = top - floorY;
            AddLight("Gallery Light", LightType.Point, new Vector3(center, top - distance * 0.4f, panelZ), settings.GalleryBrightness * distance * distance, GalleryLightRange, 0f, false);
        }

        private void AddPilastersAndBeams()
        {
            RoomMaterial finish = (RoomMaterial)settings.WallFinish;
            float pilasterHalf = Mathf.Max(settings.PilasterDepth * 0.55f, 0.3f);
            for (int line = 1; line < bayCount; line++)
            {
                if (crossWallLines.Contains(line))
                {
                    continue;
                }

                float x = line * bay;
                if (x < wall + pilasterHalf || x > width - wall - pilasterHalf)
                {
                    continue;
                }

                // A pilaster stops under its beam, so the two meet face to face instead of sharing their sides. None stands
                // where the floor climbs, since it would cut into the stairs or the curve.
                bool hasBeam = settings.CeilingBeams && line != partitionLine;
                bool onClimb = levelChange != RoomLevelChange.None && x + pilasterHalf > flatEnd && x - pilasterHalf < LevelChangeEnd();
                if (settings.PilasterDepth > 0f && !onClimb)
                {
                    // Pilasters stay behind the route, where doors, barriers and partitions stand.
                    float depth = Mathf.Min(settings.PilasterDepth, backZ - RouteEdgeZ, backZ - PrefabRouteDepth * 0.5f);
                    float pilasterBottom = FloorAt(x);
                    float pilasterTop = hasBeam ? height - BeamDepth : height;
                    if (depth > 0f && pilasterTop > pilasterBottom)
                    {
                        Block("Pilaster " + line, RoomGroup.Structure, x - pilasterHalf, x + pilasterHalf, pilasterBottom, pilasterTop, backZ - depth, backZ, finish, false);
                    }
                }

                if (hasBeam)
                {
                    Block("Beam " + line, RoomGroup.Structure, x - pilasterHalf, x + pilasterHalf, height - BeamDepth, height, WallFrontZ, backZ, finish, false);
                }
            }
        }

        private void AddRepairPanels()
        {
            // A wall fixture sits flat on the wall too, so the two would share their back faces.
            var free = new List<int>();
            for (int i = 0; i < bayCount; i++)
            {
                if (!windowBays.Contains(i) && !recessBays.Contains(i) && !HasWallFixture(i) && (i + 1) * bay <= flatEnd + 0.01f)
                {
                    free.Add(i);
                }
            }

            for (int count = 0; count < settings.RepairPanels && free.Count > 0; count++)
            {
                int pick = random.Next(free.Count);
                int index = free[pick];
                free.RemoveAt(pick);

                float room = bay - PanelMargin * 2f;
                if (room < PanelGrid)
                {
                    continue;
                }

                int columns = Mathf.FloorToInt(room / PanelGrid);
                int across = random.Next(1, Mathf.Min(columns, 3) + 1);
                int rows = random.Next(PanelMinimumRows, PanelMaximumRows + 1);
                float x0 = index * bay + PanelMargin + random.Next(columns - across + 1) * PanelGrid;
                int levels = Mathf.Max(1, Mathf.FloorToInt((height - 1f) / PanelGrid) - rows + 1);
                float y0 = random.Next(Mathf.Min(levels, 3)) * PanelGrid;
                Block("Repair Panel " + (index + 1), RoomGroup.Structure, x0, x0 + across * PanelGrid, y0, Mathf.Min(y0 + rows * PanelGrid, height), backZ - PanelProud, backZ, RepairMaterial(), false);
            }
        }

        // A repair is a near match, not a highlight: the closest other concrete to the wall.
        private RoomMaterial RepairMaterial()
        {
            return settings.WallFinish == RoomFinish.Concrete ? RoomMaterial.ConcreteSlate : RoomMaterial.Concrete;
        }

        private void AddBarriers()
        {
            int wanted = settings.LowBarriers;
            var free = new List<int>();
            for (int i = 0; i < bayCount; i++)
            {
                float x = (i + 0.5f) * bay;
                if (x > BarrierEntryClearance && x < flatEnd - BarrierEntryClearance && !NearThreshold(x) && i != cameraBay)
                {
                    free.Add(i);
                }
            }

            int placed = 0;
            for (; placed < wanted && free.Count > 0; placed++)
            {
                int pick = random.Next(free.Count);
                float x = (free[pick] + 0.5f) * bay;
                free.RemoveAt(pick);
                plan.Prefabs.Add(new PrefabPlan
                {
                    Prefab = RoomPrefab.IndustrialBarrier,
                    Name = "Low Barrier",
                    Group = RoomGroup.Systems,
                    Position = new Vector3(x, BarrierHeight * 0.5f, 0f),
                    Scale = new Vector3(BarrierWidth, BarrierHeight, PrefabRouteDepth)
                });
            }

            plan.MissingPieces += wanted - placed;
            if (placed < wanted)
            {
                plan.Notes.Add($"Only {placed} of {wanted} low barriers fit on the route.");
            }
        }

        private bool NearThreshold(float x)
        {
            foreach (int line in crossWallLines)
            {
                if (Mathf.Abs(line * bay - x) < BarrierThresholdClearance)
                {
                    return true;
                }
            }

            return partitionLine >= 0 && Mathf.Abs(partitionLine * bay - x) < BarrierThresholdClearance;
        }

        private void AddPartition()
        {
            if (partitionLine < 0)
            {
                return;
            }

            plan.Prefabs.Add(new PrefabPlan
            {
                Prefab = RoomPrefab.GlassPartition,
                Name = "Glass Partition",
                Group = RoomGroup.Systems,
                Position = new Vector3(partitionLine * bay, height * 0.5f, 0f),
                Scale = new Vector3(PartitionThickness, height, PrefabRouteDepth)
            });
        }

        private void AddSecurityCamera()
        {
            if (cameraBay < 0)
            {
                return;
            }

            // The part of the bay inside the room, so a thick end wall never swallows the camera.
            float x = ClearBayCenter(cameraBay);
            plan.Prefabs.Add(new PrefabPlan { Prefab = RoomPrefab.SecurityCamera, Name = "Security Camera", Group = RoomGroup.Systems, Position = new Vector3(x, height - CameraTopAbovePivot, 0f) });
        }

        /// <summary>How far a generated camera should see: to the floor and a little beyond.</summary>
        public static float CameraRangeFor(RoomSettings settings)
        {
            return settings.Height + CameraViewBeyondFloor;
        }

        /// <summary>Whether the plan's camera should seal the exit door.</summary>
        public static bool CameraShouldLockExit(RoomSettings settings)
        {
            return settings.SecurityCamera && settings.CameraLocksExit && (settings.Exit == RoomThreshold.SealedDoor || settings.Exit == RoomThreshold.PanelDoor);
        }

        //==================== LIGHTING =====================
        private void AddCeilingFixtures()
        {
            if (!settings.CeilingFixtures)
            {
                return;
            }

            // Fixtures at bay centres stay clear of the beams and walls on bay lines.
            var positions = new List<int>();
            int every = settings.FixtureEveryBays;
            for (int i = Mathf.Min(every - 1, bayCount - 1) / 2; i < bayCount; i += every)
            {
                if (i != cameraBay)
                {
                    positions.Add(i);
                }
            }

            HashSet<int> failed = PickFailed(positions);
            var lit = new List<int>(positions);
            lit.RemoveAll(failed.Contains);
            lit.Sort((a, b) => Mathf.Abs(a * 2 + 1 - bayCount).CompareTo(Mathf.Abs(b * 2 + 1 - bayCount)));
            var shadowed = new HashSet<int>();
            for (int i = 0; i < lit.Count && i < settings.ShadowCasters; i++)
            {
                shadowed.Add(lit[i]);
            }

            foreach (int index in positions)
            {
                float x = ClearBayCenter(index);
                float half = Mathf.Min(bay * FixtureWidthShare, FixtureMaximumWidth) * 0.5f;
                float halfDepth = FixtureDepth * 0.5f;
                string name = "Ceiling Fixture " + (index + 1);
                float housingBottom = height - HousingThickness;
                Block(name + " Housing", RoomGroup.LightFixtures, x - half - HousingMargin, x + half + HousingMargin, housingBottom, height, -halfDepth - HousingMargin, halfDepth + HousingMargin, RoomMaterial.Steel, false);
                bool isFailed = failed.Contains(index);
                Block(name + " Panel", RoomGroup.LightFixtures, x - half, x + half, housingBottom - PanelThickness, housingBottom, -halfDepth, halfDepth, isFailed ? RoomMaterial.DeepShadow : RoomMaterial.LightPanel, false);
                if (isFailed)
                {
                    continue;
                }

                float lightY = housingBottom - SpotBelowFixture;
                float distance = lightY - FloorAt(x);
                float intensity = Vary(settings.Brightness) * distance * distance;
                AddLight(name + " Light", LightType.Spot, new Vector3(x, lightY, SpotOffsetZ), intensity, distance * SpotRangeFactor, settings.SpotAngle, shadowed.Contains(index));
            }
        }

        private void AddWallFixtures()
        {
            if (!settings.WallFixtures)
            {
                return;
            }

            var positions = new List<int>();
            for (int i = 0; i < bayCount; i++)
            {
                if (HasWallFixture(i))
                {
                    positions.Add(i);
                }
            }

            HashSet<int> failed = PickFailed(positions);
            foreach (int index in positions)
            {
                float x = ClearBayCenter(index);
                float floorHere = FloorAt(x);
                float y = Mathf.Min(floorHere + settings.OpeningHeight + WallFixtureAboveOpening, height - WallFixtureBelowCeiling);
                string name = "Wall Fixture " + (index + 1);
                float halfWidth = WallFixtureWidth * 0.5f;
                float halfHeight = WallFixtureHeight * 0.5f;
                bool isFailed = failed.Contains(index);
                Block(name, RoomGroup.LightFixtures, x - halfWidth, x + halfWidth, y - halfHeight, y + halfHeight, backZ - WallFixtureProud, backZ, isFailed ? RoomMaterial.DeepShadow : RoomMaterial.Steel, false);
                if (!isFailed)
                {
                    AddLight(name + " Light", LightType.Point, new Vector3(x, y, backZ - WallLightOffset), Vary(settings.Brightness) * WallLightDistance * WallLightDistance, WallLightRange, 0f, false);
                }
            }
        }

        private HashSet<int> PickFailed(List<int> positions)
        {
            var failed = new HashSet<int>();
            int count = Mathf.RoundToInt(positions.Count * settings.FailedFixtures);
            var candidates = new List<int>(positions);
            for (int i = 0; i < count && candidates.Count > 1; i++)
            {
                int pick = random.Next(candidates.Count);
                failed.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }

            return failed;
        }

        private float Vary(float value)
        {
            float spread = settings.BrightnessVariation;
            return value * (1f + spread * (float)(random.NextDouble() * 2.0 - 1.0));
        }

        private void AddFill()
        {
            if (settings.FillLevel <= 0f)
            {
                return;
            }

            int count = Mathf.Max(1, Mathf.RoundToInt(width / FillSpacing));
            float intensity = settings.FillLevel * FillDistance * FillDistance;
            for (int i = 0; i < count; i++)
            {
                float x = width * (i + 0.5f) / count;
                AddLight("Fill Light " + (i + 1), LightType.Point, new Vector3(x, Mathf.Min(height * FillHeightShare, FillMaximumHeight), FillZ), intensity, FillRange, 0f, false);
            }
        }

        private void AddReflectionProbe()
        {
            if (!settings.ReflectionProbe)
            {
                return;
            }

            plan.HasReflectionProbe = true;
            var size = new Vector3(width, height, backZ - WallFrontZ);
            plan.ProbeBounds = new Bounds(new Vector3(width * 0.5f, height * 0.5f, (backZ + WallFrontZ) * 0.5f), size);
        }

        private void AddLight(string name, LightType type, Vector3 position, float intensity, float range, float spotAngle, bool shadows)
        {
            plan.Lights.Add(new LightPlan
            {
                Name = name,
                Type = type,
                Position = position,
                Color = settings.LightColor,
                Intensity = intensity,
                Range = range,
                SpotAngle = spotAngle,
                Shadows = shadows
            });
        }

        //==================== NOTES =====================
        private void AddNotes()
        {
            if (settings.Entrance == RoomThreshold.Solid)
            {
                plan.Notes.Add("The entrance is a solid wall, so this room can only be a start.");
            }

            if (settings.Exit == RoomThreshold.Solid)
            {
                plan.Notes.Add("The exit is a solid wall, so this room is a dead end.");
            }

            if (settings.Entrance == RoomThreshold.SealedDoor)
            {
                plan.Notes.Add("The entrance door stays shut until you connect it to a panel in the room before.");
            }

            if (settings.Exit == RoomThreshold.SealedDoor && !CameraShouldLockExit(settings))
            {
                plan.Notes.Add("The exit door stays shut until you connect it to something.");
            }

            if (partitionLine >= 0)
            {
                plan.Notes.Add("The Glass Partition blocks the route. Place a swap object beyond it, within reach of the swap.");
            }

            if (settings.SecurityCamera && settings.CameraLocksExit && !hasExitDoor)
            {
                plan.Notes.Add("Camera Locks Exit needs a door at the exit, so the camera only watches.");
            }

            if (CameraShouldLockExit(settings))
            {
                plan.Notes.Add("The camera seals the exit while it sees the player, so the route needs a way past its view.");
            }

            if (levelChange == RoomLevelChange.CurvedWall)
            {
                plan.Notes.Add("The way up is a wall run: hold Run and the direction, and run up the curve.");
            }
        }

        //==================== HELPERS =====================
        private bool HasWallFixture(int index)
        {
            return settings.WallFixtures && index % settings.FixtureEveryBays == 0 && !windowBays.Contains(index) && !recessBays.Contains(index);
        }

        // Where the climbing part of the floor ends: the first landing step or the start of the ledge.
        private float LevelChangeEnd()
        {
            switch (levelChange)
            {
                case RoomLevelChange.Stairs:
                    return flatEnd + Mathf.Ceil(rise / StairRise) * StairTread;
                case RoomLevelChange.CurvedWall:
                    return flatEnd + CurveRadius + CurveThickness;
                default:
                    return flatEnd;
            }
        }
        // The middle of the part of a bay that is inside the room. The first and last bays lose some to the end walls.
        private float ClearBayCenter(int index)
        {
            float left = Mathf.Max(index * bay, wall);
            float right = Mathf.Min((index + 1) * bay, width - wall);
            return (left + right) * 0.5f;
        }

        private float FloorAt(float x)
        {
            if (x <= flatEnd || levelChange == RoomLevelChange.None)
            {
                return 0f;
            }

            if (levelChange == RoomLevelChange.Stairs)
            {
                float steps = Mathf.Ceil(rise / StairRise);
                return Mathf.Min(rise, (x - flatEnd) / StairTread * (rise / steps));
            }

            // A curved wall's own span keeps the room floor. Beyond it is the ledge.
            return x < flatEnd + CurveRadius + CurveThickness ? 0f : rise;
        }

        private void Block(string name, RoomGroup group, float x0, float x1, float y0, float y1, float z0, float z1, RoomMaterial material, bool solid)
        {
            plan.Blocks.Add(new BlockPlan
            {
                Name = name,
                Group = group,
                Min = new Vector3(x0, y0, z0),
                Max = new Vector3(x1, y1, z1),
                Material = material,
                Solid = solid
            });
        }

        /// <summary>Number of segments a generated curve uses, matching the existing curves in the project.</summary>
        public static int CurveSegmentCount => CurveSegments;

        /// <summary>Height of the walkable floor at a distance along the room, relative to its entrance floor.</summary>
        public static float FloorHeightAt(RoomSettings settings, float x)
        {
            var layout = new RoomLayout(settings);
            layout.DecideLevelChange();
            return layout.FloorAt(x);
        }
    }
}

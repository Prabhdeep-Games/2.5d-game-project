using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Makes room for an option that has just been turned on, so every option works whatever the purpose. A purpose only
    /// loads starting values, and its size can be too short or too low for a level change, a gallery, barriers, thresholds or
    /// recesses. Sizes only grow, and only for a shortage the edit itself caused, so a size set by hand is kept.
    /// </summary>
    public static class RoomFitting
    {
        private const float MaximumWidth = 96f;
        private const float MaximumHeight = 40f;
        private const float DefaultStairRise = 4f;
        private const float DefaultCurveRise = 12f;
        private const float DefaultShaftRise = 4f;
        private const int MaximumSteps = 12;

        /// <summary>Adjusts the values after an edit and returns what it changed. Empty when nothing was needed.</summary>
        public static List<string> MakeRoomFor(RoomSettings values, RoomSettings before)
        {
            var changes = new List<string>();
            FitExitRise(values, before, changes);
            FitCameraLock(values, before, changes);

            bool sizedByHand = values.Width != before.Width || values.Height != before.Height;
            if (!sizedByHand)
            {
                Grow(values, RoomLayout.Create(before), changes);
            }

            return changes;
        }

        /// <summary>Makes room and logs what changed, in one line.</summary>
        public static void MakeRoomAndLog(RoomSettings values, RoomSettings before)
        {
            List<string> changes = MakeRoomFor(values, before);
            if (changes.Count > 0)
            {
                Debug.Log("Room Generator made room for the new option: " + string.Join(", ", changes) + ".");
            }
        }

        // A level change with no rise, a curve outside the rise its wall run is tuned for, or a shaft whose exit is out of
        // jumping reach, would not work.
        private static void FitExitRise(RoomSettings values, RoomSettings before, List<string> changes)
        {
            if (values.LevelChange == before.LevelChange)
            {
                return;
            }

            float rise = values.ExitRise;
            if (values.LevelChange == RoomLevelChange.Stairs && rise <= 0f)
            {
                rise = DefaultStairRise;
            }
            else if (values.LevelChange == RoomLevelChange.CurvedWall && (rise < RoomLayout.CurveMinimumRise || rise > RoomLayout.CurveMaximumRise))
            {
                rise = rise <= 0f ? DefaultCurveRise : Mathf.Clamp(rise, RoomLayout.CurveMinimumRise, RoomLayout.CurveMaximumRise);
            }
            else if (values.LevelChange == RoomLevelChange.WallJumpShaft && rise > RoomLayout.ShaftMaximumRise)
            {
                rise = DefaultShaftRise;
            }

            if (rise != values.ExitRise)
            {
                changes.Add($"Exit Rise {values.ExitRise:0.#} to {rise:0.#}");
                values.SetExitRise(rise);
            }
        }

        // Camera Locks Exit seals the exit door, so it needs one. A Panel Door can still be opened from inside the room.
        private static void FitCameraLock(RoomSettings values, RoomSettings before, List<string> changes)
        {
            bool locks = values.SecurityCamera && values.CameraLocksExit;
            bool lockedBefore = before.SecurityCamera && before.CameraLocksExit;
            bool exitIsDoor = values.Exit == RoomThreshold.SealedDoor || values.Exit == RoomThreshold.PanelDoor;
            if (locks && !lockedBefore && !exitIsDoor)
            {
                changes.Add($"Exit {values.Exit} to PanelDoor");
                values.SetExit(RoomThreshold.PanelDoor);
            }
        }

        // A shortage the room already had before the edit is left alone. Each step can uncover a new need, since a longer room
        // has other bays, so it repeats until the edit's own needs are met.
        private static void Grow(RoomSettings values, RoomPlan planBefore, List<string> changes)
        {
            bool widthWasEnough = values.Width >= planBefore.NeededWidth;
            bool heightWasEnough = values.Height >= planBefore.NeededHeight;
            float startWidth = values.Width;
            float startHeight = values.Height;
            for (int step = 0; step < MaximumSteps; step++)
            {
                RoomPlan plan = RoomLayout.Create(values);
                float width = values.Width;
                float height = values.Height;
                if (widthWasEnough)
                {
                    width = Mathf.Max(width, plan.NeededWidth);
                }

                if (heightWasEnough)
                {
                    height = Mathf.Max(height, plan.NeededHeight);
                }

                width += Mathf.Max(0, plan.MissingPieces - planBefore.MissingPieces) * values.BayWidth;
                width = Mathf.Min(width, MaximumWidth);
                height = Mathf.Min(height, MaximumHeight);
                if (width == values.Width && height == values.Height)
                {
                    break;
                }

                values.SetSize(width, height);
            }

            if (values.Width != startWidth)
            {
                changes.Add($"Width {startWidth:0.#} to {values.Width:0.#}");
            }

            if (values.Height != startHeight)
            {
                changes.Add($"Height {startHeight:0.#} to {values.Height:0.#}");
            }
        }
    }
}

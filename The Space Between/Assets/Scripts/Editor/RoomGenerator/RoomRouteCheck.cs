using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Walks a plan's route along the centre line, from the entrance floor to the exit, using its solid pieces. It answers
    /// one question before anything is built: is there floor and headroom all the way through? Doors, glass and barriers are
    /// reported separately in the plan's notes, because whether they stop the player depends on how they are used. A gap
    /// counts as crossable when a running jump clears it, a low hood when a slide fits under it, and a wall jump shaft as
    /// climbed, since its walls are the tested half-pipe.
    /// </summary>
    public static class RoomRouteCheck
    {
        private const float SampleStep = 0.1f;

        // The player's height, and the tallest step it can walk or jump onto without a run-up. Measured in Play Mode.
        private const float PlayerHeight = 2.2f;
        private const float StepLimit = 0.6f;
        private const float ExitTolerance = 0.01f;

        // A running jump clears about 7.6 and a slide needs 1.0 of headroom, both measured in Play Mode, with some margin.
        private const float LongestJump = 7f;
        private const float SlideHeadroom = 1.2f;

        /// <summary>Returns null when the route is clear, or a sentence saying where it fails.</summary>
        public static string FindProblem(RoomPlan plan)
        {
            float y = 0f;
            float end = plan.ExitOffset.x;
            for (float x = SampleStep * 0.5f; x < end; x += SampleStep)
            {
                if (plan.Shaft.HasValue && plan.Shaft.Value.Contains(x))
                {
                    x = plan.Shaft.Value.End;
                    y = plan.ExitOffset.y;
                    continue;
                }

                RouteSpan? gap = SpanAt(plan.Gaps, x);
                if (gap.HasValue)
                {
                    if (gap.Value.Length > LongestJump)
                    {
                        return $"The gap at {gap.Value.Start:0.0} along the room is {gap.Value.Length:0.0} wide, more than a running jump clears ({LongestJump}).";
                    }

                    x = gap.Value.End;
                    continue;
                }

                CurvePlan curve = CurveAt(plan, x);
                if (curve != null)
                {
                    x = curve.EndX;
                    y = curve.StraightLength + curve.Radius;
                    continue;
                }

                float floor = float.NegativeInfinity;
                float ceiling = float.PositiveInfinity;
                foreach (BlockPlan block in plan.Blocks)
                {
                    if (!block.Solid || !block.ContainsRouteAt(x))
                    {
                        continue;
                    }

                    if (block.Max.y <= y + StepLimit)
                    {
                        floor = Mathf.Max(floor, block.Max.y);
                    }
                    else if (block.Min.y > y + StepLimit)
                    {
                        ceiling = Mathf.Min(ceiling, block.Min.y);
                    }
                    else
                    {
                        return $"'{block.Name}' stands across the route at {x:0.0} along the room.";
                    }
                }

                if (float.IsNegativeInfinity(floor))
                {
                    return $"There is no floor at {x:0.0} along the room.";
                }

                float needed = SpanAt(plan.SlideSpans, x).HasValue ? SlideHeadroom : PlayerHeight;
                if (ceiling - floor < needed)
                {
                    return $"There is not enough headroom at {x:0.0} along the room.";
                }

                y = floor;
            }

            return Mathf.Abs(y - plan.ExitOffset.y) <= ExitTolerance ? null : $"The route ends at height {y:0.0}, but the exit is at {plan.ExitOffset.y:0.0}.";
        }

        private static RouteSpan? SpanAt(System.Collections.Generic.List<RouteSpan> spans, float x)
        {
            foreach (RouteSpan span in spans)
            {
                if (span.Contains(x))
                {
                    return span;
                }
            }

            return null;
        }

        private static CurvePlan CurveAt(RoomPlan plan, float x)
        {
            foreach (CurvePlan curve in plan.Curves)
            {
                if (x >= curve.Position.x && x < curve.EndX)
                {
                    return curve;
                }
            }

            return null;
        }
    }
}

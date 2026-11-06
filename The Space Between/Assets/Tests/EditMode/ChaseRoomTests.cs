using NUnit.Framework;
using TheSpaceBetween.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Checks the Room Generator's chase pieces: gaps with crumbling bridges and pit hazards, low hoods, the wall jump shaft,
    /// the restart and the runaway's path, as planned and as built in a temporary scene that is never saved.
    /// </summary>
    public class ChaseRoomTests
    {
        private const float Tolerance = 0.01f;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static RoomSettings Chase(string overrides = null)
        {
            var settings = new RoomSettings();
            settings.ApplyPurpose(RoomPurpose.ChaseRun);
            settings.SetSeed(1);
            if (!string.IsNullOrEmpty(overrides))
            {
                JsonUtility.FromJsonOverwrite("{" + overrides + "}", settings);
            }

            return settings;
        }

        [Test]
        public void ChaseRun_PlansGapsBridgesAHood_ARestartAndThePath()
        {
            RoomPlan plan = RoomLayout.Create(Chase());

            CollectionAssert.IsEmpty(plan.Errors);
            Assert.AreEqual(0, plan.MissingPieces);
            Assert.IsNull(RoomRouteCheck.FindProblem(plan));
            Assert.AreEqual(2, plan.Gaps.Count, "Two gaps.");
            Assert.AreEqual(1, plan.SlideSpans.Count, "One hood.");
            Assert.AreEqual(2, plan.Crumbles.Count, "Each gap is bridged by a crumbling slab.");
            Assert.AreEqual(2, plan.Hazards.Count, "Each gap has a pit hazard.");
            Assert.IsNotNull(plan.Restart);

            // Gaps and the hood alternate along the room, and never touch.
            Assert.Less(plan.Gaps[0].End, plan.SlideSpans[0].Start);
            Assert.Less(plan.SlideSpans[0].End, plan.Gaps[1].Start);

            // The path starts at the room start, runs left to right, and blinks across each gap.
            Assert.IsTrue(plan.PathPoints[0].RoomStart);
            for (int i = 1; i < plan.PathPoints.Count; i++)
            {
                Assert.Greater(plan.PathPoints[i].Position.x, plan.PathPoints[i - 1].Position.x);
            }

            Assert.AreEqual(plan.Gaps.Count, plan.PathPoints.FindAll(p => p.Blink).Count);
        }

        [Test]
        public void WallJumpShaft_IsTheTestedHalfPipe_AndClimbsToTheExit()
        {
            RoomPlan plan = RoomLayout.Create(Chase("\"levelChange\":3,\"exitRise\":4,\"floorGaps\":0,\"slideHoods\":0"));

            CollectionAssert.IsEmpty(plan.Errors);
            Assert.IsNull(RoomRouteCheck.FindProblem(plan));
            Assert.IsTrue(plan.Shaft.HasValue);
            Assert.AreEqual(7.2f, plan.Shaft.Value.Length, Tolerance, "Two curves of radius 3 and thickness 0.6, as in animation_Trial.");
            Assert.AreEqual(4f, plan.ExitOffset.y, Tolerance);

            CurvePlan near = plan.Curves.Find(c => c.Mirror);
            CurvePlan far = plan.Curves.Find(c => !c.Mirror);
            Assert.IsNotNull(near, "The near wall rises toward the entrance.");
            Assert.IsNotNull(far);
            Assert.AreEqual(0f, near.Position.y + near.Radius + near.StraightLength, Tolerance, "The near wall's top meets the entrance floor.");
            Assert.AreEqual(4f, far.Position.y + far.Radius + far.StraightLength, Tolerance, "The far wall's top meets the exit ledge.");
            Assert.IsTrue(plan.Blocks.Exists(b => b.Name == "Shaft Cap"), "The cap that turns the wall run into wall jumps.");
            Assert.Greater(plan.CameraExtraDistance, 0f, "The camera moves back to show the shaft.");
        }

        [Test]
        public void GapTooWideToJump_IsReported()
        {
            RoomPlan plan = RoomLayout.Create(Chase("\"gapWidth\":8,\"floorGaps\":1,\"slideHoods\":0,\"width\":60"));
            StringAssert.Contains("more than a running jump", RoomRouteCheck.FindProblem(plan));
        }

        [Test]
        public void TurningPlatformingOff_LeavesTheRoomAsBefore()
        {
            RoomSettings settings = Chase("\"platforming\":false");
            RoomPlan plan = RoomLayout.Create(settings);
            Assert.AreEqual(0, plan.Gaps.Count + plan.SlideSpans.Count + plan.Crumbles.Count + plan.Hazards.Count + plan.PathPoints.Count);
            Assert.IsNull(plan.Restart);
            Assert.AreEqual(1, plan.Blocks.FindAll(b => b.Name.StartsWith("Floor")).Count, "One unbroken floor.");
        }

        [Test]
        public void BuiltChaseRoom_ConnectsSlabsHazardsAndDebrisToItsRestart()
        {
            GeneratedRoom room = RoomBuilder.CreateRoom(Chase("\"debrisDrops\":1"), Vector3.zero);
            Assert.IsNotNull(room);

            RoomReset restart = room.GetComponentInChildren<RoomReset>(true);
            Assert.IsNotNull(restart, "The room has a restart.");

            foreach (TraversalHazard hazard in room.GetComponentsInChildren<TraversalHazard>(true))
            {
                Assert.AreEqual(restart, hazard.Room, hazard.name);
                Assert.IsTrue(hazard.GetComponent<BoxCollider>().isTrigger, hazard.name);
            }

            CrumblingSlab[] slabs = room.GetComponentsInChildren<CrumblingSlab>(true);
            Assert.AreEqual(2, slabs.Length);
            Assert.AreEqual(0.8f, slabs[0].CrumbleDelay, Tolerance);
            Assert.AreEqual(1, room.GetComponentsInChildren<FallingObstacleSpawner>(true).Length);
            Assert.Greater(room.GetComponentsInChildren<ChasePathPoint>(true).Length, 3);
            Assert.AreEqual(2, room.GetComponentsInChildren<TraversalHazard>(true).Length);
        }
    }
}

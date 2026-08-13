using NUnit.Framework;
using Rokkan.Prophecy.Presentation;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The camera's room clamp and door pan, pinned headless.
    ///
    /// <para>Both regressions this logic has shipped were invisible until someone walked the
    /// exact door that showed them: the mid-walk lurch (blending only the RECTS released the
    /// pinned camera and it chased the player — Matt's jar), and the respawn that resolved its
    /// shot under a still-sliding clamp. Neither throws, neither fails a frame; the picture is
    /// just wrong for half a second. These tests are the door, walked every run.</para>
    /// </summary>
    public class LaneCameraClampTests
    {
        // A 9 m-tall frame's numbers, rounded for legible arithmetic: the camera sees 8 m of
        // width (half 4), 9 m of height (half 4.5), feet 37.5% up the frame.
        private const float HalfW = 4f;
        private const float HalfH = 4.5f;
        private const float Focus = 1.125f;
        private const float Dt = 1f / 60f;

        // Two rooms sharing a door at x = 12. The origin room is NARROW on purpose: its east
        // clamp (maxX - HalfW = 8) pins the camera short of the door, which is the exact
        // arrangement the jar regression needed.
        private static readonly (float floor, float ceiling, float minX, float maxX)
            Room1 = (0f, 20f, 0f, 12f);

        private static readonly (float floor, float ceiling, float minX, float maxX)
            Room2 = (0f, 20f, 12f, 40f);

        /// <summary>The delivery point just past the door's far face.</summary>
        private const float Delivery = 12.6f;

        /// <summary>Where the delivery point sits under Room2's clamps:
        /// Clamp(12.6, 12 + 4, 40 - 4) = 16.</summary>
        private const float DeliveredFrame = 16f;

        private static LaneCameraClamp.Frame InRoom(
            int room, (float floor, float ceiling, float minX, float maxX) rect)
        {
            return new LaneCameraClamp.Frame
            {
                Room = room,
                RoomRect = rect,
                CameraX = 8f,
                CameraY = 5f,
                HalfVisibleWidth = HalfW,
                HalfVisibleHeight = HalfH,
                FocusOffsetY = Focus,
                SlideSeconds = 0.5f,
                DeltaSeconds = Dt,
            };
        }

        private static LaneCameraClamp.Frame Crossing(
            float progress, float cameraX,
            (float floor, float ceiling, float minX, float maxX)? to = null)
        {
            return new LaneCameraClamp.Frame
            {
                Room = 1,
                Transiting = true,
                TargetRoom = 2,
                TransitProgress = progress,
                TransitAxisX = true,
                DeliveryAxis = Delivery,
                RoomRect = Room1,
                TargetRect = to ?? Room2,
                CameraX = cameraX,
                CameraY = 5f,
                HalfVisibleWidth = HalfW,
                HalfVisibleHeight = HalfH,
                FocusOffsetY = Focus,
                SlideSeconds = 0.5f,
                DeltaSeconds = Dt,
            };
        }

        /// <summary>A clamp settled in Room1, the way an arrival leaves it.</summary>
        private static LaneCameraClamp SettledInRoom1()
        {
            var clamp = new LaneCameraClamp();
            clamp.SetBaseline(0f, 20f);
            clamp.Drive(InRoom(1, Room1), snap: true);
            return clamp;
        }

        // ---------------------------------------------------------------- the jar

        [Test]
        public void TheStepInHoldsThePinnedCameraWhereItStood()
        {
            var clamp = SettledInRoom1();

            // The camera is PINNED at the narrow room's east clamp (x = 8) while the player
            // walks the last metres to the door — the clamp holds, the body drifts ahead.
            clamp.Drive(Crossing(0f, cameraX: 8f));

            Assert.IsTrue(clamp.PanActive, "the step-in must arm the pan");
            Assert.AreEqual(8f, clamp.PanPosition, 1e-4f,
                "the pan departs from where the camera actually stood — anything else is a " +
                "cut at the door");
        }

        [Test]
        public void MidWalkThePanOwnsTheAxis_NotTheBlendedClamps()
        {
            var clamp = SettledInRoom1();
            clamp.Drive(Crossing(0f, cameraX: 8f));

            clamp.Drive(Crossing(0.5f, cameraX: 8f));

            // The shipped bug: blending only the rects put the east clamp at
            // lerp(12, 40, 0.5) = 26 by mid-walk, which released the pinned camera to lurch
            // after the player — Matt's jar. The pan must be a fixed glide instead, and at
            // half progress it is exactly halfway along it.
            float glide = Mathf.Lerp(8f, DeliveredFrame, Mathf.SmoothStep(0f, 1f, 0.5f));
            Assert.AreEqual(glide, clamp.PanPosition, 1e-4f,
                "mid-walk the camera rides the pan, not the released clamp");
            Assert.AreEqual(26f, clamp.MaxX, 1e-3f,
                "the rects DO still blend underneath — the pan is what stops that blend " +
                "reaching the shot");
        }

        [Test]
        public void ThePanIsAimedOnceAtTheStepIn()
        {
            var clamp = SettledInRoom1();
            clamp.Drive(Crossing(0f, cameraX: 8f));

            // By mid-walk the follow target has moved on. Re-aiming from it would turn the
            // fixed glide back into a chase.
            clamp.Drive(Crossing(0.4f, cameraX: 11.2f));

            Assert.AreEqual(8f, clamp.PanFrom, 1e-4f, "the departure point is frozen at the step-in");
            Assert.AreEqual(DeliveredFrame, clamp.PanTo, 1e-4f,
                "the destination is the delivery point under the new room's clamps");
        }

        [Test]
        public void TheGlideIsMonotonicAndEndsOnTheDestinationFrame()
        {
            var clamp = SettledInRoom1();
            clamp.Drive(Crossing(0f, cameraX: 8f));

            float last = clamp.PanPosition;
            for (int step = 1; step <= 10; step++)
            {
                clamp.Drive(Crossing(step / 10f, cameraX: 8f));

                Assert.GreaterOrEqual(clamp.PanPosition + 1e-4f, last,
                    "a glide that backtracks is a wobble at the door");
                Assert.LessOrEqual(clamp.PanPosition, DeliveredFrame + 1e-4f,
                    "overshooting the delivery frame shows past the destination room");
                last = clamp.PanPosition;
            }

            Assert.AreEqual(DeliveredFrame, last, 1e-3f,
                "the pan must deliver exactly the frame the new room's clamps resolve");
        }

        // ---------------------------------------------------------------- lifecycle

        [Test]
        public void TheCrossingCompletesWithThePanDownAndNothingLeftToMove()
        {
            var clamp = SettledInRoom1();
            clamp.Drive(Crossing(0f, cameraX: 8f));
            clamp.Drive(Crossing(1f, cameraX: 8f));

            // Delivery: the clamps arrived with the feet.
            Assert.AreEqual(Room2.minX, clamp.MinX, 1e-3f);
            Assert.AreEqual(Room2.maxX, clamp.MaxX, 1e-3f);

            // The crossing completes and the ordinary path takes over in the new room.
            clamp.Drive(InRoom(2, Room2));

            Assert.IsFalse(clamp.PanActive, "the pan must not outlive the crossing");
            Assert.AreEqual(Room2.maxX, clamp.MaxX, 1e-3f,
                "the room change already landed during the walk — a slide here is the camera " +
                "settling after the feet have stopped");
            Assert.AreEqual(Room2.floor, clamp.FloorY, 1e-3f);
            Assert.AreEqual(Room2.ceiling, clamp.CeilingY, 1e-3f);
        }

        [Test]
        public void TheClampsBlendByTheWalk_NotByAClock()
        {
            var clamp = SettledInRoom1();
            clamp.Drive(Crossing(0.5f, cameraX: 8f));

            float minX = clamp.MinX;
            float maxX = clamp.MaxX;

            // Frames keep rendering while the feet hold still — a body pausable mid-door
            // would drift if any clock but the walk drove the blend.
            var stalled = Crossing(0.5f, cameraX: 8f);
            stalled.DeltaSeconds = 0.25f;
            clamp.Drive(stalled);

            Assert.AreEqual(minX, clamp.MinX, 1e-4f, "no progress, no movement");
            Assert.AreEqual(maxX, clamp.MaxX, 1e-4f, "no progress, no movement");
        }

        // ---------------------------------------------------------------- the respawn

        [Test]
        public void ASnapLandsTheClampsEvenWhenTheRoomDidNotChange()
        {
            var clamp = SettledInRoom1();

            // A re-placement into another room starts the ordinary slide...
            clamp.Drive(InRoom(2, Room2));
            Assert.Less(clamp.MaxX, Room2.maxX - 1f, "sanity: the slide is genuinely in flight");

            // ...and a snap while it is still in flight must land outright. Finishing only on
            // room changes was the regression: a respawn resolved its shot under the old
            // room's still-sliding limits.
            clamp.Drive(InRoom(2, Room2), snap: true);
            Assert.AreEqual(Room2.maxX, clamp.MaxX, 1e-4f);
            Assert.AreEqual(Room2.minX, clamp.MinX, 1e-4f);

            // The slide's momentum has to die with it, or the next frame overshoots.
            clamp.Drive(InRoom(2, Room2));
            Assert.AreEqual(Room2.maxX, clamp.MaxX, 1e-4f, "a snapped clamp stays put");
        }

        // ---------------------------------------------------------------- aim rules

        [Test]
        public void ADestinationNarrowerThanTheFrameCentresThePan()
        {
            var narrow = (floor: 0f, ceiling: 20f, minX: 12f, maxX: 18f);

            var clamp = SettledInRoom1();
            clamp.Drive(Crossing(0f, cameraX: 8f, to: narrow));

            // 6 m of room against 8 m of frame: nothing to clamp to, so the whole extent is
            // centred — the same surrender as the vertical rule.
            Assert.AreEqual(15f, clamp.PanTo, 1e-4f);
        }

        [Test]
        public void AVerticalCrossingPansYToTheDeliveryUnderTheFocusOffset()
        {
            var below = (floor: -7.2f, ceiling: 3.6f, minX: 0f, maxX: 40f);

            var clamp = SettledInRoom1();
            var frame = Crossing(0f, cameraX: 8f, to: below);
            frame.TransitAxisX = false;
            frame.DeliveryAxis = -3.6f;
            clamp.Drive(frame);

            Assert.IsFalse(clamp.PanAxisX, "a vertical door pans Y");
            Assert.AreEqual(5f, clamp.PanFrom, 1e-4f, "departs from where the camera stood");

            // The FEET are delivered at -3.6; the framed centre sits FocusOffsetY above them,
            // inside the destination's vertical clamps [-7.2 + 4.5, 3.6 - 4.5].
            Assert.AreEqual(-3.6f + Focus, clamp.PanTo, 1e-4f);
        }

        // ---------------------------------------------------------------- scene edges

        [Test]
        public void ClearReleasesTheXClampsSoTheNextSceneIsNotPinned()
        {
            var clamp = SettledInRoom1();
            clamp.Drive(Crossing(0.5f, cameraX: 8f));

            clamp.Clear();

            Assert.IsFalse(clamp.PanActive, "no pan survives a scene change");
            Assert.AreEqual(-LaneCameraClamp.Unbounded, clamp.MinX,
                "a stale west clamp pins the camera in the next scene");
            Assert.AreEqual(LaneCameraClamp.Unbounded, clamp.MaxX,
                "a stale east clamp pins the camera in the next scene");
        }

        [Test]
        public void ABaselineArrivalLandsWithoutASlide()
        {
            var clamp = SettledInRoom1();
            clamp.Drive(Crossing(0.5f, cameraX: 8f));

            clamp.SetBaseline(-3.6f, 40f);

            Assert.IsFalse(clamp.PanActive);
            Assert.AreEqual(-3.6f, clamp.FloorY, 1e-4f, "a load is not a place to watch a slide");
            Assert.AreEqual(40f, clamp.CeilingY, 1e-4f);
            Assert.AreEqual(-LaneCameraClamp.Unbounded, clamp.MinX);
        }
    }
}

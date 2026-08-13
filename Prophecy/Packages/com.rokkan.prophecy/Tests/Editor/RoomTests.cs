using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using Rokkan.Prophecy.Sim.Stats;
using UnityEngine;
using static Rokkan.Prophecy.Tests.SimTestHarness;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Rooms are identities, not shapes (Matt): membership is graph state, changed ONLY by a
    /// door — and a door is a COMMITMENT, Zelda II's screens rather than Mario's checkpoints
    /// (Matt's correction of the first cut). Stepping into a doorway takes the pad, the walk
    /// carries itself through, and the room changes as the crossing completes: no halfway,
    /// no backing out. The change is the channel room-scoped state drops through — Buoyancy's
    /// float and any modifier that declared itself room-lived — while a seeded arrival stays
    /// SILENT: an arrival is not a passage.
    /// </summary>
    public class RoomTests
    {
        private static CollisionWorld DoorWorld()
        {
            var world = Ground();
            world.AddDoor(new Aabb(new Vector2(9.8f, 0f), new Vector2(10.2f, 2.6f)),
                          roomMinSide: 1, roomMaxSide: 2);
            return world;
        }

        private static CharacterSim Player(CollisionWorld world, Vector2 at, int room)
        {
            var sim = SimTestHarness.Player(world, new CombatTuningData(), null, at: at);
            sim.SetRoom(room);
            return sim;
        }

        private static InputFrame Hold(float x = 0f) => new InputFrame(new Vector2(x, 0f));

        [Test]
        public void WalkingThroughADoorChangesTheRoom()
        {
            var sim = Player(DoorWorld(), new Vector2(5f, 0f), room: 1);
            Step(sim, Hold(), 3);

            int guard = 0;
            while (sim.State.Position.x < 12f && guard++ < 600) Step(sim, Hold(x: 1f));

            Assert.AreEqual(2, sim.State.Room, "the exit side names the room");
        }

        [Test]
        public void EnteringADoorCommitsTheCrossing()
        {
            // No backing out: the moment the feet are in the doorway, the crossing belongs
            // to the door. Reverse input for the whole transit changes nothing.
            var sim = Player(DoorWorld(), new Vector2(5f, 0f), room: 1);
            Step(sim, Hold(), 3);

            int guard = 0;
            while (!sim.Get<DoorTransit>().IsTransiting && guard++ < 600)
                Step(sim, Hold(x: 1f));
            Assert.IsTrue(sim.Get<DoorTransit>().IsTransiting, "the harness must have entered");

            // Reverse held for the WHOLE crossing — and only the crossing: once control is
            // back, walking away again would be a legitimate second crossing, not a back-out.
            guard = 0;
            while (sim.Get<DoorTransit>().IsTransiting && guard++ < 300)
                Step(sim, Hold(x: -1f));

            Assert.AreEqual(2, sim.State.Room, "the crossing completed against the input");
            Assert.Greater(sim.State.Position.x, 10.2f, "delivered to the far side");
            Assert.IsFalse(sim.Get<DoorTransit>().IsTransiting, "and the pad is back");
        }

        [Test]
        public void TheCrossingTakesThePad()
        {
            var sim = Player(DoorWorld(), new Vector2(5f, 0f), room: 1);
            Step(sim, Hold(), 3);

            int guard = 0;
            while (!sim.Get<DoorTransit>().IsTransiting && guard++ < 600)
                Step(sim, Hold(x: 1f));

            Assert.IsFalse(sim.Can(LockFlags.Move), "a crossing is not steerable");
            Assert.IsFalse(sim.Can(LockFlags.Jump), "or jumpable out of");

            // Progress is the camera's sync signal, so it must be honest: monotone from the
            // step-in to the delivery, never jumping backwards for the frame to follow.
            var transit = sim.Get<DoorTransit>();
            float lastProgress = -1f;
            guard = 0;
            while (transit.IsTransiting && guard++ < 300)
            {
                Assert.GreaterOrEqual(transit.Progress, lastProgress, "progress never rewinds");
                lastProgress = transit.Progress;
                Step(sim, Hold());
            }

            Assert.IsTrue(sim.Can(LockFlags.Move), "control returns on the next screen");
        }

        [Test]
        public void AJumpIntoTheDoorwayCommitsMidAir()
        {
            // A low arc into the opening is still an entry: the commitment does not care how
            // the feet arrived, and gravity keeps running while the drive carries through —
            // the body lands mid-crossing and walks out the far side.
            var sim = Player(DoorWorld(), new Vector2(8.6f, 0f), room: 1);
            Step(sim, Hold(), 3);

            Step(sim, new InputFrame(new Vector2(1f, 0f), jump: ButtonState.Press));
            int guard = 0;
            while (sim.State.Room != 2 && guard++ < 600) Step(sim, Hold(x: 1f));

            Assert.AreEqual(2, sim.State.Room, "an airborne entry still commits and completes");
            Assert.Greater(sim.State.Position.x, 10.2f);
        }

        [Test]
        public void DoorsAreWallsToThoseWhoCannotUseThem()
        {
            // The Roc's bake: for a body whose DoorTransit is off, the baker fills the
            // doorway with plain solid — the chase ends at the frame (Matt's report: it
            // followed him through). This world is built the way that bake builds it.
            var world = DoorWorld();
            world.Add(new Aabb(new Vector2(9.8f, 0f), new Vector2(10.2f, 2.6f)));

            var sim = Player(world, new Vector2(5f, 0f), room: 1);
            sim.Get<DoorTransit>().Enabled = false;
            Step(sim, Hold(), 3);

            Step(sim, Hold(x: 1f), 400);

            Assert.Less(sim.State.Position.x, 9.85f, "the doorway is a wall to it");
            Assert.AreEqual(1, sim.State.Room, "and no crossing ever happened");
        }

        [Test]
        public void TheBarrierStopsFightsNotFeet()
        {
            // The player's bake: the doorway carries a DoorBarrier — free passage for the
            // body, a wall to every attack, death to every shot. Projectiles opt into it;
            // movement and the headroom probes never see it.
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, -2f), new Vector2(500f, 0f)));
            world.Add(new Aabb(new Vector2(9.8f, 0f), new Vector2(10.2f, 2.6f)),
                      SolidKind.DoorBarrier);

            var body = new Aabb(new Vector2(9f, 0f), new Vector2(9.9f, 1.8f));
            world.SweepHorizontal(body, 2f, out bool blocked);
            Assert.IsFalse(blocked, "movement never meets the barrier");

            Assert.IsTrue(world.IsOccluded(new Vector2(8f, 1f), new Vector2(12f, 1f)),
                          "a swing cannot cross the door");

            var shot = Aabb.FromCenterSize(new Vector2(10f, 1f), new Vector2(0.4f, 0.4f));
            Assert.IsTrue(world.OverlapsAnySolid(shot, includeDoorBarriers: true),
                          "a bolt dies at the doorway");
            Assert.IsFalse(world.OverlapsAnySolid(shot),
                           "but a body standing in it is not inside a wall");
        }

        [Test]
        public void CrossingADoorDropsTheFloat()
        {
            // Matt's rule, rooms being its final name: the cast is a toggle ended by recasting
            // or a room change. The hook fires without a drop of water in sight — the state is
            // the module's, the channel is the sim's.
            var sim = Player(DoorWorld(), new Vector2(5f, 0f), room: 1);
            Step(sim, Hold(), 3);

            Step(sim, new InputFrame(Vector2.zero, flameArt: ButtonState.Press));
            Assert.IsTrue(sim.Get<Buoyancy>().FloatOn, "armed on dry land");

            int guard = 0;
            while (sim.State.Position.x < 12f && guard++ < 600) Step(sim, Hold(x: 1f));

            Assert.AreEqual(2, sim.State.Room);
            Assert.IsFalse(sim.Get<Buoyancy>().FloatOn, "the door ends the cast");
        }

        [Test]
        public void RoomScopedModifiersDieAtTheDoor()
        {
            var sim = Player(DoorWorld(), new Vector2(5f, 0f), room: 1);
            Step(sim, Hold(), 3);

            sim.Stats.Add(new StatModifier
            {
                Kind = StatKind.Speed,
                Stage = StatStage.Percent,
                Value = -0.5f,
                ExpiresOnTick = StatModifier.Permanent,
                RoomScoped = true,
            });

            sim.Stats.Add(new StatModifier
            {
                Kind = StatKind.Speed,
                Stage = StatStage.Percent,
                Value = -0.25f,
                ExpiresOnTick = StatModifier.Permanent,
            });

            Assert.AreEqual(0.25f, sim.Stats.Effective(StatKind.Speed), 0.001f,
                            "both slows apply inside the room");

            int guard = 0;
            while (sim.State.Position.x < 12f && guard++ < 900) Step(sim, Hold(x: 1f));

            Assert.AreEqual(2, sim.State.Room);
            Assert.AreEqual(0.75f, sim.Stats.Effective(StatKind.Speed), 0.001f,
                            "the room-scoped slow died at the door; the other survived");
        }

        [Test]
        public void SeedingTheRoomIsSilent()
        {
            // An arrival is not a passage: spawns and scene loads SET the room, and the full
            // module Reset covers whatever a real crossing would have dropped. SetRoom on its
            // own must therefore clear nothing.
            var sim = Player(DoorWorld(), new Vector2(5f, 0f), room: 1);
            Step(sim, Hold(), 3);

            Step(sim, new InputFrame(Vector2.zero, flameArt: ButtonState.Press));
            Assert.IsTrue(sim.Get<Buoyancy>().FloatOn);

            sim.SetRoom(7);

            Assert.AreEqual(7, sim.State.Room);
            Assert.IsTrue(sim.Get<Buoyancy>().FloatOn, "seeding fires no hooks");
        }
    }
}

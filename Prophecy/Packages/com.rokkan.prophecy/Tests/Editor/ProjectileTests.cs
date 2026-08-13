using System.Collections.Generic;
using NUnit.Framework;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Volumes that outlive the swing that made them: projectiles, and the area attacks that are
    /// the same thing standing still.
    ///
    /// <para>The interesting assertions are about lifetime rather than geometry — the overlap maths
    /// is already <c>HitResolver</c>'s and already tested. What is new here is that a thing can be
    /// dangerous after its author has stopped being: it keeps flying, it expires on its own, a wall
    /// stops it, and a parry kills it where it stands.</para>
    /// </summary>
    public class ProjectileTests
    {
        private const float Dt = 1f / 60f;

        private const int CasterId = 7;
        private const int CasterTeam = 2;
        private const int TargetId = 1;
        private const int TargetTeam = 1;

        /// <summary>The shared recording world, answering the clean 12-damage landing every
        /// flight test here assumes unless it says otherwise.</summary>
        private static RecordingCombatWorld World() =>
            new RecordingCombatWorld { Answer = new HitResult(HitOutcome.Landed, 12) };

        // ---------------------------------------------------------------- harness

        private static ProjectileDefinition Bolt()
        {
            return new ProjectileDefinition
            {
                Id = "bolt",
                SpawnOffset = new Vector2(0.5f, 1f),
                Speed = 12f,
                HalfExtents = new Vector2(0.2f, 0.2f),
                Damage = 12,
                LifetimeTicks = 60,
                StoppedByGeometry = false,
                MaxTargets = 1,
            };
        }

        private static Attacker Caster(int facing = 1) =>
            new Attacker(CasterId, Vector2.zero, new Vector2(0f, 0.9f), facing, CasterTeam);

        private static Hurtbox TargetAt(float x, float y = 1f) =>
            new Hurtbox(TargetId, new Vector2(x, y), new Vector2(0.45f, 0.9f), 0f, TargetTeam);

        private static void Run(ProjectileSystem system, ICombatWorld world, CollisionWorld level,
                                int ticks, long from = 1)
        {
            for (int i = 0; i < ticks; i++) system.Tick(world, level, from + i, Dt);
        }

        // ---------------------------------------------------------------- flight

        [Test]
        public void ASpawnedBoltTravelsAndHits()
        {
            var world = World();
            world.Targets.Add(TargetAt(4f));

            var system = new ProjectileSystem();
            system.Spawn(Bolt(), Caster());

            Assert.AreEqual(1, system.Count);
            Assert.IsEmpty(world.Hits, "it has not travelled yet");

            Run(system, world, null, 40);

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual("bolt", world.Hits[0].AttackId);
            Assert.AreEqual(12, world.Hits[0].Damage);
        }

        [Test]
        public void ABoltFliesTheOtherWayWhenTheCasterDoes()
        {
            var world = World();
            world.Targets.Add(TargetAt(-4f));

            var system = new ProjectileSystem();
            system.Spawn(Bolt(), Caster(facing: -1));

            Run(system, world, null, 40);

            Assert.AreEqual(1, world.Hits.Count);
        }

        [Test]
        public void ABoltExpiresOnItsOwn()
        {
            var world = World();

            var definition = Bolt();
            definition.LifetimeTicks = 10;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, null, 9);
            Assert.AreEqual(1, system.Count);

            Run(system, world, null, 2, from: 10);
            Assert.AreEqual(0, system.Count, "a shot that never lands must still stop existing");
        }

        [Test]
        public void AWallStopsABolt()
        {
            var level = new CollisionWorld();
            level.Add(new Aabb(new Vector2(2f, 0f), new Vector2(2.2f, 3f)));

            var world = World();
            world.Targets.Add(TargetAt(4f));

            var definition = Bolt();
            definition.StoppedByGeometry = true;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, level, 40);

            Assert.IsEmpty(world.Hits, "the wall is between the caster and the target");
            Assert.AreEqual(0, system.Count);
        }

        [Test]
        public void APassThroughBoltIgnoresTheWall()
        {
            var level = new CollisionWorld();
            level.Add(new Aabb(new Vector2(2f, 0f), new Vector2(2.2f, 3f)));

            var world = World();
            world.Targets.Add(TargetAt(4f));

            var system = new ProjectileSystem();
            system.Spawn(Bolt(), Caster());

            Run(system, world, level, 40);

            Assert.AreEqual(1, world.Hits.Count);
        }

        [Test]
        public void GravityArcsAShot()
        {
            var world = World();

            var definition = Bolt();
            definition.Gravity = 30f;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            float startY = system.Live[0].Position.y;
            Run(system, world, null, 20);

            Assert.Less(system.Live[0].Position.y, startY);
        }

        // ---------------------------------------------------------------- areas

        [Test]
        public void AStationaryVolumeStaysWhereItLanded()
        {
            var world = World();

            var definition = Bolt();
            definition.Speed = 0f;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            var start = system.Live[0].Position;
            Run(system, world, null, 20);

            Assert.AreEqual(start.x, system.Live[0].Position.x, 0.0001f);
            Assert.IsTrue(definition.IsStationary);
        }

        [Test]
        public void AGrowingVolumeReachesFurtherThanItStarted()
        {
            // The difference between a lingering hazard and a spreading one, and the only thing
            // separating an area attack from a projectile that forgot to move.
            var world = World();
            world.Targets.Add(TargetAt(3f));

            var definition = Bolt();
            definition.Speed = 0f;
            definition.GrowthPerSecond = new Vector2(6f, 0f);
            definition.LifetimeTicks = 60;
            definition.MaxTargets = 0;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, null, 5);
            Assert.IsEmpty(world.Hits, "it has not spread that far yet");

            Run(system, world, null, 40, from: 6);
            Assert.AreEqual(1, world.Hits.Count, "and then it does");
        }

        [Test]
        public void AnAreaCatchesEveryoneStandingInIt()
        {
            // MaxTargets 0 is what makes it an area rather than a projectile that hits the first
            // person and stops.
            var world = World();
            world.Targets.Add(new Hurtbox(1, new Vector2(1f, 1f), new Vector2(0.4f, 0.9f), 0f, TargetTeam));
            world.Targets.Add(new Hurtbox(2, new Vector2(2f, 1f), new Vector2(0.4f, 0.9f), 0f, TargetTeam));
            world.Targets.Add(new Hurtbox(3, new Vector2(3f, 1f), new Vector2(0.4f, 0.9f), 0f, TargetTeam));

            var definition = Bolt();
            definition.Speed = 0f;
            definition.SpawnOffset = new Vector2(2f, 1f);
            definition.HalfExtents = new Vector2(2.5f, 1f);
            definition.MaxTargets = 0;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, null, 3);

            Assert.AreEqual(3, world.Hits.Count);
        }

        [Test]
        public void ABoltStopsAtItsTargetLimit()
        {
            var world = World();
            world.Targets.Add(new Hurtbox(1, new Vector2(1f, 1f), new Vector2(0.4f, 0.9f), 0f, TargetTeam));
            world.Targets.Add(new Hurtbox(2, new Vector2(2f, 1f), new Vector2(0.4f, 0.9f), 0f, TargetTeam));

            var definition = Bolt();
            definition.Speed = 0f;
            definition.SpawnOffset = new Vector2(1.5f, 1f);
            definition.HalfExtents = new Vector2(1.5f, 1f);
            definition.MaxTargets = 1;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, null, 5);

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual(0, system.Count, "spent, so gone");
        }

        // ---------------------------------------------------------------- answers

        [Test]
        public void OneVolumeHitsOneTargetOnce()
        {
            var world = World();
            world.Targets.Add(TargetAt(2f));

            var definition = Bolt();
            definition.Speed = 0f;
            definition.SpawnOffset = new Vector2(2f, 1f);
            definition.HalfExtents = new Vector2(1f, 1f);
            definition.MaxTargets = 0;
            definition.LifetimeTicks = 30;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, null, 30);

            Assert.AreEqual(1, world.Hits.Count, "a lingering volume must not re-hit every tick");
        }

        [Test]
        public void TheAuthoredAnswerSetReachesTheDefender()
        {
            var world = World();
            world.Targets.Add(TargetAt(2f));

            var definition = Bolt();
            definition.Speed = 0f;
            definition.SpawnOffset = new Vector2(2f, 1f);
            definition.HalfExtents = new Vector2(1f, 1f);
            definition.Defeats = DefensiveAnswer.Block | DefensiveAnswer.Parry;
            definition.Height = AttackHeight.Low;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, null, 3);

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual(DefensiveAnswer.Block | DefensiveAnswer.Parry, world.Hits[0].Defeats);
            Assert.IsTrue(world.Hits[0].CanBe(DefensiveAnswer.IFrames), "a dodge still answers it");
            Assert.IsFalse(world.Hits[0].CanBe(DefensiveAnswer.Block));
            Assert.AreEqual(AttackHeight.Low, world.Hits[0].Height);
        }

        [Test]
        public void AParriedShotDiesWhereItWasTurned()
        {
            // Nothing is reflected. Reflection is a mechanic this project has not decided on, and
            // inventing it here would make a parry behave differently against a bolt than a blade.
            var world = new RecordingCombatWorld
            {
                Answer = new HitResult(HitOutcome.Parried, 0, 40),
            };
            world.Targets.Add(TargetAt(2f));

            var definition = Bolt();
            definition.Speed = 0f;
            definition.SpawnOffset = new Vector2(2f, 1f);
            definition.HalfExtents = new Vector2(1f, 1f);
            definition.LifetimeTicks = 60;

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, null, 3);

            Assert.AreEqual(0, system.Count);
        }

        [Test]
        public void AShotNeverHitsItsOwnCaster()
        {
            var world = World();
            world.Targets.Add(new Hurtbox(CasterId, new Vector2(0.5f, 1f),
                                          new Vector2(0.9f, 0.9f), 0f, CasterTeam));

            var definition = Bolt();
            definition.Speed = 0f;
            definition.HalfExtents = new Vector2(1f, 1f);

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Run(system, world, null, 5);

            Assert.IsEmpty(world.Hits);
        }

        [Test]
        public void AShotOutlivesTheCasterBeingInterrupted()
        {
            // The point of the whole type. Nothing here holds a reference to whoever fired it, so
            // there is nothing to go stale when they are stunned or killed a tick later.
            var world = World();
            world.Targets.Add(TargetAt(4f));

            var system = new ProjectileSystem();
            system.Spawn(Bolt(), Caster());

            Run(system, world, null, 40);

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual(CasterId, world.Hits[0].AttackerId);
        }

        // ---------------------------------------------------------------- validation

        [Test]
        public void AVolumeWithNoSizeIsRejected()
        {
            var definition = Bolt();
            definition.HalfExtents = Vector2.zero;

            Assert.IsNotNull(definition.Validate());

            var system = new ProjectileSystem();
            system.Spawn(definition, Caster());

            Assert.AreEqual(0, system.Count, "and never gets into the air");
        }

        [Test]
        public void AVolumeWithNoLifetimeIsRejected()
        {
            var definition = Bolt();
            definition.LifetimeTicks = 0;

            StringAssert.Contains("lifetime", definition.Validate());
        }
    }
}

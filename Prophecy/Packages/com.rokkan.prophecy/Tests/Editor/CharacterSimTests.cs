using System.Collections.Generic;
using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Pins the character sim: registry ordering, the action-lock arbiter, and movement
    /// resolution. Built entirely in code — no scene, no prefab, no Animator.
    /// </summary>
    public class CharacterSimTests
    {
        private static CollisionWorld GroundAt(float y = 0f)
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(-100f, y - 1f), new Vector2(100f, y)));
            return w;
        }

        private static CharacterSim Standing(CollisionWorld world = null)
        {
            var sim = new CharacterSim(world ?? GroundAt());
            sim.Teleport(Vector2.zero);
            return sim;
        }

        /// <summary>Advance the sim n ticks at the fixed rate.</summary>
        private static void Step(CharacterSim sim, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
                sim.Tick(new SimTickInfo(sim.CurrentTick + 1, SimConstants.FixedDeltaSeconds));
        }

        // ---------- module registry ----------

        private sealed class OrderedModule : AbilityModule
        {
            private readonly int _order;
            private readonly List<string> _log;
            public override int Order => _order;
            public override string Name { get; }
            public override MovementSpace ValidIn { get; }
            public int Ticks;
            public int Resets;

            public OrderedModule(int order, string name, List<string> log = null,
                                 MovementSpace validIn = MovementSpace.SideScroll)
            {
                _order = order;
                Name = name;
                _log = log;
                ValidIn = validIn;
            }

            public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
            {
                Ticks++;
                _log?.Add(Name);
            }

            public override void Reset() => Resets++;
        }

        [Test]
        public void Modules_TickInOrderRegardlessOfRegistrationSequence()
        {
            var log = new List<string>();
            var sim = Standing();

            // Added deliberately out of order — ordering must come from Order, not insertion.
            sim.Add(new OrderedModule(30, "c", log));
            sim.Add(new OrderedModule(10, "a", log));
            sim.Add(new OrderedModule(20, "b", log));

            Step(sim);

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, log,
                "registration accident must never change simulation results");
        }

        [Test]
        public void DisabledModule_DoesNotTick()
        {
            var sim = Standing();
            var m = sim.Add(new OrderedModule(10, "m"));
            m.Enabled = false;

            Step(sim, 5);

            Assert.AreEqual(0, m.Ticks, "progression is a flag flip, so disabled must mean silent");
        }

        [Test]
        public void ModuleInvalidForTheCurrentSpace_DoesNotTick()
        {
            var sim = Standing();
            var sideOnly = sim.Add(new OrderedModule(10, "side", null, MovementSpace.SideScroll));
            var topOnly = sim.Add(new OrderedModule(20, "top", null, MovementSpace.TopDown));

            Step(sim);
            Assert.AreEqual(1, sideOnly.Ticks);
            Assert.AreEqual(0, topOnly.Ticks);

            sim.State.Space = MovementSpace.TopDown;
            Step(sim);
            Assert.AreEqual(1, sideOnly.Ticks, "side-scroll module must go quiet in the overworld");
            Assert.AreEqual(1, topOnly.Ticks);
        }

        [Test]
        public void Get_FindsARegisteredModule()
        {
            var sim = Standing();
            var m = sim.Add(new OrderedModule(10, "m"));
            Assert.AreSame(m, sim.Get<OrderedModule>());
        }

        [Test]
        public void Teleport_ResetsModulesAndClearsMotion()
        {
            var sim = Standing();
            var m = sim.Add(new OrderedModule(10, "m"));
            sim.State.Velocity = new Vector2(5f, 5f);

            sim.Teleport(new Vector2(10f, 0f), facing: -1);

            Assert.AreEqual(Vector2.zero, sim.State.Velocity, "stale velocity must not survive");
            Assert.AreEqual(10f, sim.State.Position.x, 1e-5f);
            Assert.AreEqual(-1, sim.State.Facing);
            Assert.AreEqual(1, m.Resets);
        }

        // ---------- action locks ----------

        [Test]
        public void UnlockedCharacter_CanDoAnything()
        {
            var sim = Standing();
            Assert.IsTrue(sim.Can(LockFlags.Move));
            Assert.IsTrue(sim.Can(LockFlags.Attack));
            Assert.IsFalse(sim.CurrentLock.IsHeld);
        }

        [Test]
        public void Lock_SuppressesOnlyItsOwnFlags()
        {
            var sim = Standing();
            var attack = new object();

            Assert.IsTrue(sim.TryLock(attack, LockFlags.Move | LockFlags.Turn, LockPriority.Attack));

            Assert.IsFalse(sim.Can(LockFlags.Move), "an attack commits your footing");
            Assert.IsFalse(sim.Can(LockFlags.Turn));
            Assert.IsTrue(sim.Can(LockFlags.Jump), "but says nothing about jumping");
        }

        [Test]
        public void HigherPriority_CannotStompACommittedAction()
        {
            var sim = Standing();
            var attack = new object();
            var dodge = new object();

            sim.TryLock(attack, LockFlags.All, LockPriority.Attack);

            Assert.IsFalse(sim.TryLock(dodge, LockFlags.All, LockPriority.Reaction),
                "cancel window closed — the swing keeps its committed frames");
            Assert.IsTrue(sim.HoldsLock(attack));
        }

        [Test]
        public void HigherPriority_TakesOverOnceTheCancelWindowOpens()
        {
            var sim = Standing();
            var attack = new object();
            var dodge = new object();

            sim.TryLock(attack, LockFlags.All, LockPriority.Attack);
            sim.SetCancelWindow(attack, true);   // recovery frames

            Assert.IsTrue(sim.TryLock(dodge, LockFlags.All, LockPriority.Reaction),
                "cancelling out of recovery is what keeps combat responsive");
            Assert.IsTrue(sim.HoldsLock(dodge));
            Assert.IsFalse(sim.HoldsLock(attack));
        }

        [Test]
        public void EqualOrLowerPriority_NeverTakesOver_EvenInACancelWindow()
        {
            var sim = Standing();
            var first = new object();
            var second = new object();

            sim.TryLock(first, LockFlags.All, LockPriority.Attack);
            sim.SetCancelWindow(first, true);

            Assert.IsFalse(sim.TryLock(second, LockFlags.All, LockPriority.Attack),
                "equal priority must not win, or two attacks would trade the lock every tick");
            Assert.IsTrue(sim.HoldsLock(first));
        }

        [Test]
        public void OwnerCanReassertItsOwnLock_ToChangeFlags()
        {
            var sim = Standing();
            var owner = new object();

            sim.TryLock(owner, LockFlags.All, LockPriority.Attack);
            Assert.IsTrue(sim.TryLock(owner, LockFlags.Move, LockPriority.Attack));

            Assert.IsTrue(sim.Can(LockFlags.Attack), "flags narrowed as the action progressed");
            Assert.IsFalse(sim.Can(LockFlags.Move));
        }

        [Test]
        public void OnlyTheOwnerCanRelease()
        {
            var sim = Standing();
            var owner = new object();
            sim.TryLock(owner, LockFlags.All, LockPriority.Attack);

            sim.ReleaseLock(new object());
            Assert.IsTrue(sim.HoldsLock(owner), "a stale reference must not free someone else's claim");

            sim.ReleaseLock(owner);
            Assert.IsFalse(sim.CurrentLock.IsHeld);
        }

        [Test]
        public void Teleport_ClearsAnyLock()
        {
            var sim = Standing();
            var owner = new object();
            sim.TryLock(owner, LockFlags.All, LockPriority.HitReact);

            sim.Teleport(Vector2.zero);

            Assert.IsFalse(sim.CurrentLock.IsHeld, "a scene transition must not carry a lock across");
        }

        // ---------- movement resolution ----------

        [Test]
        public void StartsGroundedOnGround()
        {
            var sim = Standing();
            Assert.IsTrue(sim.State.Grounded);
            Assert.AreEqual(Stance.Stand, sim.State.Stance);
        }

        [Test]
        public void FallsUnderVelocity_AndLandsOnTheSurface()
        {
            var sim = new CharacterSim(GroundAt());
            sim.Teleport(new Vector2(0f, 5f));
            Assert.IsFalse(sim.State.Grounded);

            sim.State.Velocity = new Vector2(0f, -10f);
            Step(sim, 60);   // one second: 10 units of fall, far past the 5 it needs

            Assert.IsTrue(sim.State.Grounded);
            Assert.AreEqual(0f, sim.State.Position.y, 0.01f, "rests exactly on the surface");
            Assert.AreEqual(0f, sim.State.Velocity.y, 1e-5f, "landing kills downward velocity");
        }

        [Test]
        public void LandedThisTick_FiresExactlyOnce()
        {
            var sim = new CharacterSim(GroundAt());
            sim.Teleport(new Vector2(0f, 1f));
            sim.State.Velocity = new Vector2(0f, -10f);

            int landings = 0;
            for (int i = 0; i < 60; i++)
            {
                Step(sim);
                if (sim.State.LandedThisTick) landings++;
            }

            Assert.AreEqual(1, landings, "the landing edge drives squash and dust — it must not repeat");
        }

        [Test]
        public void WalkingIntoAWall_StopsAndZeroesHorizontalVelocity()
        {
            var world = GroundAt();
            world.Add(new Aabb(new Vector2(5f, 0f), new Vector2(6f, 3f)));
            var sim = Standing(world);

            sim.State.Velocity = new Vector2(20f, 0f);
            Step(sim, 60);

            Assert.IsTrue(sim.State.Position.x < 4.6f, "stopped at the wall face");
            Assert.AreEqual(0f, sim.State.Velocity.x, 1e-5f,
                "velocity must clear or the character keeps grinding into geometry");
        }

        [Test]
        public void BlockedHorizontally_StillFallsVertically()
        {
            // Axis separation is what gives wall-sliding for free; a combined sweep would snag.
            var world = GroundAt();
            world.Add(new Aabb(new Vector2(1f, 0f), new Vector2(2f, 10f)));
            var sim = new CharacterSim(world);
            sim.Teleport(new Vector2(0f, 5f));

            sim.State.Velocity = new Vector2(10f, -10f);
            Step(sim, 10);

            Assert.Less(sim.State.Position.y, 5f, "descent continues despite the wall");
        }

        [Test]
        public void LastGroundedTick_TracksGrounding_ForCoyoteTime()
        {
            var sim = Standing();
            Step(sim, 5);
            long groundedAt = sim.State.LastGroundedTick;

            // Launch upward; grounding should stop advancing.
            sim.State.Velocity = new Vector2(0f, 12f);
            Step(sim, 10);

            Assert.IsFalse(sim.State.Grounded);
            Assert.AreEqual(groundedAt, sim.State.LastGroundedTick,
                "coyote time counts from the last grounded TICK, so it is frame-rate independent");
            Assert.Greater(sim.State.TicksSinceGrounded(sim.CurrentTick), 0);
        }

        [Test]
        public void StanceBecomesAirWhenAirborne_AndStandOnLanding()
        {
            var sim = Standing();
            sim.State.Velocity = new Vector2(0f, 12f);
            Step(sim, 5);
            Assert.AreEqual(Stance.Air, sim.State.Stance, "air stance is what gates the down-thrust");

            sim.State.Velocity = new Vector2(0f, -30f);
            Step(sim, 60);
            Assert.AreEqual(Stance.Stand, sim.State.Stance);
        }

        [Test]
        public void HasHeadroomToStand_IsFalseUnderALowCeiling()
        {
            var world = GroundAt();
            world.Add(new Aabb(new Vector2(-5f, 1.5f), new Vector2(5f, 2.5f))); // 1.5 of clearance
            var sim = new CharacterSim(world);
            sim.Teleport(Vector2.zero);
            sim.State.Stance = Stance.Crouch;

            Assert.IsFalse(sim.HasHeadroomToStand(), "a 2-tall body cannot rise into 1.5 of space");

            sim.World.Clear();
            Assert.IsTrue(sim.HasHeadroomToStand());
        }

        [Test]
        public void TopDownSpace_HasNoGravityAndIsAlwaysGrounded()
        {
            var sim = new CharacterSim(new CollisionWorld());
            sim.State.Space = MovementSpace.TopDown;
            sim.Teleport(Vector2.zero);

            sim.State.Velocity = new Vector2(3f, 4f);
            Step(sim, 60);

            Assert.IsTrue(sim.State.Grounded, "the overworld has no falling");
            Assert.AreEqual(Stance.Stand, sim.State.Stance);
            Assert.AreEqual(3f, sim.State.Position.x, 0.05f, "both axes are plain lateral motion");
            Assert.AreEqual(4f, sim.State.Position.y, 0.05f);
        }

        [Test]
        public void SimIsDeterministic_AcrossIdenticalRuns()
        {
            Vector2 RunOnce()
            {
                var world = GroundAt();
                world.Add(new Aabb(new Vector2(6f, 0f), new Vector2(7f, 3f)));
                var sim = new CharacterSim(world);
                sim.Teleport(new Vector2(0f, 4f));
                sim.State.Velocity = new Vector2(7f, -3f);
                Step(sim, 120);
                return sim.State.Position;
            }

            Assert.AreEqual(RunOnce(), RunOnce(), "same inputs, same ticks, same result — always");
        }
    }
}

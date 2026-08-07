using System.Collections.Generic;
using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.AI;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The pacing seam end to end, headless: a mashing brain (attack pressed EVERY tick)
    /// run through the press gate against a real sim and a real fight. Whatever the brain
    /// wants, attack starts obey the beat — that is the whole feature.
    /// </summary>
    public class AttackPacingSimTests
    {
        private const float Dt = SimConstants.FixedDeltaSeconds;
        private const int EnemyId = 7;
        private const int Beat = 90;

        private static CollisionWorld Ground()
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, -2f), new Vector2(500f, 0f)));
            return world;
        }

        /// <summary>Run <paramref name="ticks"/> of a mashing enemy through the gate and the
        /// sim, returning every tick an attack STARTED.</summary>
        private static List<long> MashingRun(int ticks)
        {
            var fight = new CombatState();
            var sim = PlayerCharacterFactory.Create(
                Ground(), new MovementTuningData(), MovementSpace.SideScroll, null,
                new CombatTuningData(), fight);
            sim.State.CombatId = EnemyId;
            sim.State.Team = 2;
            sim.Teleport(Vector2.zero, facing: 1);

            var intent = new EnemyIntent();
            var percept = new Percept { TargetId = 1, DistanceX = 1f, DirectionToTarget = 1 };
            var attack = sim.Get<AttackModule>();

            var starts = new List<long>();
            bool wasAttacking = false;

            for (long tick = 1; tick <= ticks; tick++)
            {
                // The runtime order: the fight ticks (grants land), the brain decides (mash),
                // the gate applies, the sim consumes.
                fight.Tick(null, tick, Dt);

                intent.PressAttack();
                AttackPacingLink.Apply(fight, intent, in percept, EnemyId, AttackPool.Melee,
                                       Beat, incapacitated: false, tick);

                sim.SetInput(intent.Consume());
                sim.Tick(new SimTickInfo(tick, Dt));

                if (attack.IsAttacking && !wasAttacking) starts.Add(tick);
                wasAttacking = attack.IsAttacking;
            }

            return starts;
        }

        [Test]
        public void AMashingBrainAttacksOnTheBeatAndNeverInsideIt()
        {
            var starts = MashingRun(2000);

            Assert.GreaterOrEqual(starts.Count, 3, "The enemy does still attack.");

            for (int i = 1; i < starts.Count; i++)
                Assert.GreaterOrEqual(starts[i] - starts[i - 1], Beat,
                    $"Attack starts {starts[i - 1]} → {starts[i]} are inside the {Beat}-tick beat.");
        }

        [Test]
        public void PacingIsAFunctionOfTicksAloneAndRepeatsExactly()
        {
            // Same seed, same everything: the schedule is deterministic. This is the
            // frame-rate-invariance claim in its testable form — nothing here can see a
            // frame, only ticks, so identical tick streams must produce identical starts.
            var first = MashingRun(1200);
            var second = MashingRun(1200);

            CollectionAssert.AreEqual(first, second,
                "The attack schedule must be a pure function of the tick stream.");
        }

        [Test]
        public void AnIncapacitatedEnemyStopsRequestingAndItsGrantLapses()
        {
            var fight = new CombatState();
            var intent = new EnemyIntent();
            var percept = new Percept { TargetId = 1 };

            // Granted while healthy…
            fight.Tick(null, 1, Dt);
            AttackPacingLink.Apply(fight, intent, in percept, EnemyId, AttackPool.Melee,
                                   Beat, incapacitated: false, 1);
            fight.Tick(null, 2, Dt);
            Assert.IsTrue(fight.Attacks.HoldsToken(EnemyId));

            // …then stunned before pressing: requests stop, the token lapses back.
            long tick = 2;
            for (; tick < 25; tick++)
            {
                AttackPacingLink.Apply(fight, intent, in percept, EnemyId, AttackPool.Melee,
                                       Beat, incapacitated: true, tick);
                fight.Tick(null, tick + 1, Dt);
            }

            Assert.IsFalse(fight.Attacks.HoldsToken(EnemyId),
                "A stunned holder's token must return to the pool.");
        }

        [Test]
        public void TheGateStripsAnUnlicensedPress()
        {
            var fight = new CombatState();
            var intent = new EnemyIntent();
            var percept = new Percept { TargetId = 1 };

            // Two contenders; the second is refused — its press must not survive the gate.
            fight.Attacks.Request(1, AttackPool.Melee, Beat, 1);
            fight.Tick(null, 1, Dt);

            intent.PressAttack();
            bool held = AttackPacingLink.Apply(fight, intent, in percept, EnemyId,
                                               AttackPool.Melee, Beat, false, 1);

            Assert.IsFalse(held);
            Assert.IsFalse(intent.HasPendingAttack, "The unlicensed press dies at the gate.");
        }
    }
}

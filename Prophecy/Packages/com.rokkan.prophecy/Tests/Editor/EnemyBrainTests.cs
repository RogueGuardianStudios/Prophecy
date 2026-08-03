using System.Collections.Generic;
using NUnit.Framework;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.AI;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The patrol / pursue / attack loop, and the senses that feed it.
    ///
    /// <para>All of it runs with no scene, no ScriptableObject and no planner, which is the point
    /// of keeping perception and decision as plain C#: an enemy's behaviour is a thing you can
    /// assert, not a thing you have to watch.</para>
    /// </summary>
    public class EnemyBrainTests
    {
        private const int Self = 1;
        private const int SelfTeam = 2;
        private const int Player = 99;

        private static readonly Vector2 Body = new Vector2(0.9f, 1.8f);

        private static CollisionWorld Ground(float from = -100f, float to = 100f)
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(from, -2f), new Vector2(to, 0f)));
            return world;
        }

        /// <summary>A fight containing the enemy and one player-team target.</summary>
        private static CombatState FightWith(Vector2 selfAt, Vector2 targetAt)
        {
            var fight = new CombatState();
            fight.Register(new Dummy { CombatId = Self, Team = SelfTeam, Position = selfAt });
            fight.Register(new Dummy { CombatId = Player, Team = 1, Position = targetAt });
            fight.RebuildHurtboxes();
            return fight;
        }

        private sealed class Dummy : ICombatant
        {
            public int CombatId { get; set; }
            public int Team { get; set; } = 2;
            public bool IsAlive { get; set; } = true;
            public int ContactDamage => 0;
            public int ContactIntervalTicks => 45;
            public DefensiveAnswer ContactDefeats => DefensiveAnswer.None;

            public Vector2 Position;

            public Hurtbox BuildHurtbox() =>
                new Hurtbox(CombatId, Position + new Vector2(0f, 0.9f), Body * 0.5f, 0f, Team);

            public HitResult ReceiveHit(in HitEvent hit) => HitResult.Ignored;
        }

        // ---------------------------------------------------------------- senses

        [Test]
        public void TheNearestHostileIsFoundAndTheDirectionIsRight()
        {
            var fight = FightWith(Vector2.zero, new Vector2(5f, 0f));
            var scratch = new List<int>();

            var percept = EnemyPerception.Sense(fight, Ground(), scratch,
                                                Self, SelfTeam, new Vector2(0f, 0.9f), range: 12f);

            Assert.IsTrue(percept.HasTarget);
            Assert.AreEqual(Player, percept.TargetId);
            Assert.AreEqual(1, percept.DirectionToTarget);
            Assert.AreEqual(5f, percept.DistanceX, 0.01f);
        }

        [Test]
        public void AlliesAreNotTargets()
        {
            // Hostility is the combat rule, not an AI rule. An enemy must not reach a different
            // conclusion about who its enemies are than the hit resolver does.
            var fight = new CombatState();
            fight.Register(new Dummy { CombatId = Self, Team = SelfTeam, Position = Vector2.zero });
            fight.Register(new Dummy { CombatId = 2, Team = SelfTeam, Position = new Vector2(3f, 0f) });
            fight.RebuildHurtboxes();

            var percept = EnemyPerception.Sense(fight, Ground(), new List<int>(),
                                                Self, SelfTeam, new Vector2(0f, 0.9f), 12f);

            Assert.IsFalse(percept.HasTarget, "same team is not prey");
        }

        [Test]
        public void NothingBeyondSightRangeIsSeen()
        {
            var fight = FightWith(Vector2.zero, new Vector2(40f, 0f));

            var percept = EnemyPerception.Sense(fight, Ground(), new List<int>(),
                                                Self, SelfTeam, new Vector2(0f, 0.9f), range: 12f);

            Assert.IsFalse(percept.HasTarget);
        }

        [Test]
        public void AWallBlocksSightTheSameWayItBlocksASwing()
        {
            // Sight is CollisionWorld.IsOccluded — the same call an attack uses for cover. So an
            // enemy cannot see through a wall it also cannot hit through, and the two can never
            // disagree.
            var world = Ground();
            world.Add(new Aabb(new Vector2(2f, 0f), new Vector2(2.5f, 4f)));

            var fight = FightWith(Vector2.zero, new Vector2(5f, 0f));

            var percept = EnemyPerception.Sense(fight, world, new List<int>(),
                                                Self, SelfTeam, new Vector2(0f, 0.9f), 12f);

            Assert.IsTrue(percept.HasTarget, "it is still there");
            Assert.IsFalse(percept.HasLineOfSight, "but it cannot be seen through the wall");
        }

        [Test]
        public void ALedgeAheadIsNoticedBeforeItIsWalkedOff()
        {
            var world = Ground(-100f, 5f);   // floor stops at x = 5

            Assert.IsTrue(EnemyPerception.GroundAhead(world, new Vector2(0f, 0f), Body, 1),
                "solid floor ahead");

            Assert.IsFalse(EnemyPerception.GroundAhead(world, new Vector2(4.9f, 0f), Body, 1),
                "the drop should be seen from the near side, not discovered mid-air");
        }

        // ---------------------------------------------------------------- the loop

        private static PatrolPursueAttack Brain(out EnemyBrainTuning tuning)
        {
            tuning = new EnemyBrainTuning();
            return new PatrolPursueAttack(tuning);
        }

        [Test]
        public void WithNothingToSeeItPatrols()
        {
            var brain = Brain(out _);
            var intent = new EnemyIntent();

            brain.Tick(default, intent, tick: 0, canTurnBack: false);

            Assert.AreEqual(EnemyBehaviour.Patrol, brain.Behaviour);
            Assert.AreNotEqual(0f, intent.MoveX, "a patrol walks");
        }

        [Test]
        public void APatrolTurnsRoundAtALedgeRatherThanWalkingOff()
        {
            var brain = Brain(out _);
            var intent = new EnemyIntent();

            brain.FacePatrol(1);
            brain.Tick(default, intent, 0, canTurnBack: false);
            Assert.AreEqual(1f, intent.MoveX, 0.001f);

            brain.Tick(default, intent, 1, canTurnBack: true);
            Assert.AreEqual(-1f, intent.MoveX, 0.001f, "the edge should have turned it");
        }

        [Test]
        public void SeeingSomethingSwitchesToPursuitAndHeadsTowardIt()
        {
            var brain = Brain(out _);
            var intent = new EnemyIntent();

            var seen = new Percept
            {
                TargetId = Player, TargetCentre = new Vector2(-8f, 0.9f),
                DistanceX = 8f, Distance = 8f, DirectionToTarget = -1, HasLineOfSight = true,
            };

            brain.Tick(in seen, intent, 0, false);

            Assert.AreEqual(EnemyBehaviour.Pursue, brain.Behaviour);
            Assert.AreEqual(-1f, intent.MoveX, 0.001f, "toward the target, not away");
        }

        [Test]
        public void InRangeItStopsClosingAndSwings()
        {
            // Walking into the target while swinging shoves it out of the reach the swing was
            // authored for, and the hit whiffs for reasons nobody can see.
            var brain = Brain(out var tuning);
            var intent = new EnemyIntent();

            var close = new Percept
            {
                TargetId = Player, TargetCentre = new Vector2(1f, 0.9f),
                DistanceX = tuning.AttackRange - 0.1f, Distance = tuning.AttackRange,
                DirectionToTarget = 1, HasLineOfSight = true,
            };

            brain.Tick(in close, intent, 0, false);

            Assert.AreEqual(EnemyBehaviour.Attack, brain.Behaviour);
            Assert.AreEqual(0f, intent.MoveX, 0.001f, "stop closing");

            var frame = intent.Consume();
            Assert.IsTrue(frame.Attack.Pressed, "and swing");
        }

        [Test]
        public void SwingsAreSpacedByTheCooldownRatherThanEveryTick()
        {
            var brain = Brain(out var tuning);
            var intent = new EnemyIntent();

            var close = new Percept
            {
                TargetId = Player, DistanceX = 0.5f, Distance = 0.5f,
                DirectionToTarget = 1, HasLineOfSight = true,
            };

            brain.Tick(in close, intent, 0, false);
            Assert.IsTrue(intent.Consume().Attack.Pressed, "the first swing");

            brain.Tick(in close, intent, 1, false);
            Assert.IsFalse(intent.Consume().Attack.Pressed, "not on the very next tick");

            brain.Tick(in close, intent, tuning.AttackCooldownTicks, false);
            Assert.IsTrue(intent.Consume().Attack.Pressed, "and again once the beat comes round");
        }

        [Test]
        public void LosingSightKeepsItChasingForAWhileRatherThanFreezing()
        {
            // An enemy that stopped dead the instant a pillar passed between you would read as
            // switched off rather than as having lost you.
            var brain = Brain(out var tuning);
            var intent = new EnemyIntent();

            var seen = new Percept
            {
                TargetId = Player, DistanceX = 6f, Distance = 6f,
                DirectionToTarget = 1, HasLineOfSight = true,
            };

            brain.Tick(in seen, intent, 0, false);
            Assert.AreEqual(EnemyBehaviour.Pursue, brain.Behaviour);

            brain.Tick(default, intent, tuning.MemoryTicks - 1, false);
            Assert.AreEqual(EnemyBehaviour.Pursue, brain.Behaviour, "still looking");
            Assert.AreEqual(1f, intent.MoveX, 0.001f, "and still heading the way it last saw them");

            brain.Tick(default, intent, tuning.MemoryTicks + 1, false);
            Assert.AreEqual(EnemyBehaviour.Patrol, brain.Behaviour, "eventually it gives up");
        }

        [Test]
        public void GivingUpResumesWalkingTheWayItWasAlreadyFacing()
        {
            // Snapping about on losing a target reads as a glitch. The patrol heading follows the
            // chase so the transition is invisible.
            var brain = Brain(out var tuning);
            var intent = new EnemyIntent();

            brain.FacePatrol(1);

            var behind = new Percept
            {
                TargetId = Player, DistanceX = 6f, Distance = 6f,
                DirectionToTarget = -1, HasLineOfSight = true,
            };

            brain.Tick(in behind, intent, 0, false);
            Assert.AreEqual(-1f, intent.MoveX, 0.001f);

            brain.Tick(default, intent, tuning.MemoryTicks + 1, false);

            Assert.AreEqual(EnemyBehaviour.Patrol, brain.Behaviour);
            Assert.AreEqual(-1f, intent.MoveX, 0.001f, "carries on the way the chase left it");
        }

        [Test]
        public void ATargetPastTheGiveUpDistanceIsAbandonedEvenInPlainSight()
        {
            var brain = Brain(out var tuning);
            var intent = new EnemyIntent();

            var far = new Percept
            {
                TargetId = Player, DistanceX = tuning.LoseInterestRange + 1f,
                Distance = tuning.LoseInterestRange + 1f,
                DirectionToTarget = 1, HasLineOfSight = true,
            };

            brain.Tick(in far, intent, 0, false);

            Assert.AreEqual(EnemyBehaviour.Patrol, brain.Behaviour);
        }

        // ---------------------------------------------------------------- intent

        [Test]
        public void APressIsSpentExactlyOnceHoweverOftenItIsAsked()
        {
            // The same latch the player's capture runs, and for the same reason: one decision must
            // not become two swings just because the brain thinks faster than the sim ticks.
            var intent = new EnemyIntent();
            intent.PressAttack();

            Assert.IsTrue(intent.Consume().Attack.Pressed);
            Assert.IsFalse(intent.Consume().Attack.Pressed, "a press survives exactly one tick");
        }

        [Test]
        public void HeldIntentPersistsAcrossTicksWhileAPressDoesNot()
        {
            var intent = new EnemyIntent { MoveX = 1f, Guard = true };
            intent.PressAttack();

            var first = intent.Consume();
            var second = intent.Consume();

            Assert.AreEqual(1f, second.Move.x, 0.001f, "held movement is a level, not an edge");
            Assert.IsTrue(second.Block.Held, "so is a raised guard");
            Assert.IsTrue(first.Attack.Pressed);
            Assert.IsFalse(second.Attack.Pressed);
        }
    }
}

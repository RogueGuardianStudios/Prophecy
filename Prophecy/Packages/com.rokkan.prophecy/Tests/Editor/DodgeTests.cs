using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;
using static Rokkan.Prophecy.Tests.SimTestHarness;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The dodge step: the only thing in the game that grants invulnerability on purpose.
    ///
    /// <para>Most of these are about what the dodge is <i>not</i> — not safe from tick zero, not
    /// spammable into permanent intangibility, not a way out of hit-stun. A dodge that fails any of
    /// those stops being a read and becomes the answer to everything, which is the failure mode
    /// worth testing for.</para>
    /// </summary>
    public class DodgeTests
    {
        private const int DefenderId = 1;
        private const int DefenderTeam = 1;
        private const int AttackerId = 9;

        // ---------------------------------------------------------------- harness

        private static CharacterSim Player(CombatTuningData combat) =>
            SimTestHarness.Player(Ground(), combat, null, combatId: DefenderId, team: DefenderTeam);

        private static InputFrame Hold(float x = 0f) => new InputFrame(new Vector2(x, 0f));

        private static InputFrame DodgeInput(float x = 0f) =>
            new InputFrame(new Vector2(x, 0f), dodge: ButtonState.Press);

        /// <summary>A blow arriving at a right-facing defender's front.</summary>
        private static HitEvent Blow(CharacterSim sim, int damage = 20) =>
            new HitEvent(AttackerId, DefenderId, damage, -1, sim.CurrentTick, "test_swing", 0);

        private static DodgeStep Module(CharacterSim sim) => sim.Get<DodgeStep>();

        // ---------------------------------------------------------------- starting

        [Test]
        public void APressStartsTheStep()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput());

            Assert.IsTrue(Module(sim).IsDodging);
        }

        [Test]
        public void ANeutralDodgeStepsBackwards()
        {
            // Away from what you are facing is the reading a player expects, and it keeps the move
            // defensive rather than turning it into a movement option.
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput());
            Step(sim, Hold(), combat.DodgeAction.StartupTicks + 1);

            Assert.AreEqual(-1, Module(sim).Direction);
            Assert.Less(sim.State.Velocity.x, 0f);
        }

        [Test]
        public void AHeldDirectionSteersTheStep()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput(x: 1f));
            Step(sim, Hold(1f), combat.DodgeAction.StartupTicks + 1);

            Assert.AreEqual(1, Module(sim).Direction);
            Assert.Greater(sim.State.Velocity.x, 0f);
        }

        [Test]
        public void TheStepDoesNotTurnYouRound()
        {
            // Turn is locked, so a back-dodge keeps the enemy in front of you. Spinning to face the
            // way you rolled would give away the read the dodge just bought.
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput(x: -1f));
            Step(sim, Hold(-1f), combat.DodgeAction.TotalTicks - 2);

            Assert.AreEqual(1, sim.State.Facing);
        }

        [Test]
        public void ADodgeCannotStartInMidAir()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, Hold(), 4);
            Assert.IsFalse(sim.State.Grounded);

            Step(sim, DodgeInput());

            Assert.IsFalse(Module(sim).IsDodging);
        }

        // ---------------------------------------------------------------- distance

        [Test]
        public void TheStepCoversItsAuthoredDistance()
        {
            // Authored as a distance so the number means something against an enemy's reach, and
            // re-asserted each tick so locomotion's friction cannot quietly shorten it.
            var combat = new CombatTuningData();
            var sim = Player(combat);

            float startX = sim.State.Position.x;

            Step(sim, DodgeInput(x: 1f));
            Step(sim, Hold(1f), combat.DodgeAction.StartupTicks + combat.DodgeAction.ActiveTicks);

            float travelled = sim.State.Position.x - startX;

            Assert.AreEqual(combat.DodgeDistance, travelled, 0.05f,
                $"the step should carry {combat.DodgeDistance} m; it carried {travelled:F2}");
        }

        [Test]
        public void TheWindUpDoesNotMoveYou()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            float startX = sim.State.Position.x;

            Step(sim, DodgeInput(x: 1f));
            Step(sim, Hold(1f), combat.DodgeAction.StartupTicks - 1);

            Assert.AreEqual(startX, sim.State.Position.x, 0.0001f,
                "the startup is a commitment, not a step");
        }

        // ---------------------------------------------------------------- i-frames

        [Test]
        public void TheStepIsIntangibleForExactlyTheAuthoredTicks()
        {
            // Tick by tick rather than at the edges: a window open for the right number of ticks in
            // the wrong place passes an edge test.
            var combat = new CombatTuningData();
            var sim = Player(combat);

            var expected = combat.DodgeAction.IFrames.BaseRange;
            var dodge = Module(sim);

            Step(sim, DodgeInput());

            for (int elapsed = 0; elapsed < combat.DodgeAction.TotalTicks; elapsed++)
            {
                Assert.AreEqual(expected.Contains(elapsed), dodge.WindowOpen,
                    $"i-frames disagreed with the authored range on tick {elapsed}");

                Step(sim, Hold());
            }
        }

        [Test]
        public void AHitDuringTheStepPassesThrough()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput());
            Step(sim, Hold(), combat.DodgeAction.IFrames.BaseStart);

            Assert.IsTrue(Module(sim).WindowOpen, "the harness must be inside the window");

            var result = sim.ReceiveHit(Blow(sim));

            Assert.AreEqual(HitOutcome.Invulnerable, result.Outcome);
            Assert.AreEqual(combat.MaxHealth, sim.Vitals.Health);
        }

        [Test]
        public void AHitDuringTheWindUpLands()
        {
            // The startup is what stops the dodge being a panic button. If it were safe from tick
            // zero, mashing it would answer everything.
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput());

            Assert.IsFalse(Module(sim).WindowOpen);
            Assert.AreEqual(HitOutcome.Landed, sim.ReceiveHit(Blow(sim)).Outcome);
        }

        [Test]
        public void AHitDuringTheRecoveryLands()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            var window = combat.DodgeAction.IFrames.BaseRange;

            Step(sim, DodgeInput());
            Step(sim, Hold(), window.End);

            Assert.IsFalse(Module(sim).WindowOpen, "the window must have closed by now");
            Assert.AreEqual(HitOutcome.Landed, sim.ReceiveHit(Blow(sim)).Outcome);
        }

        [Test]
        public void SpammingTheDodgeDoesNotGiveContinuousInvulnerability()
        {
            // The recovery is the whole safety valve. If the action were shorter than its own
            // window, holding the button would be a permanent i-frame generator.
            var combat = new CombatTuningData();
            var sim = Player(combat);

            int vulnerable = 0;
            int total = combat.DodgeAction.TotalTicks * 3;

            for (int i = 0; i < total; i++)
            {
                Step(sim, DodgeInput());
                if (!Module(sim).WindowOpen) vulnerable++;
            }

            Assert.Greater(vulnerable, total / 2,
                "a spammed dodge must leave the character exposed most of the time");
        }

        [Test]
        public void AWiderWindowStillOpensOnTheSameTick()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            var dodge = Module(sim);
            dodge.Modifiers = new AttackModifiers { IFrameScale = 2f, ParryScale = 1f, CancelScale = 1f };

            Step(sim, DodgeInput());

            Assert.AreEqual(combat.DodgeAction.IFrames.BaseStart, dodge.Timeline.IFrameRange.Start);
            Assert.Greater(dodge.Timeline.IFrameRange.Duration, combat.DodgeAction.IFrames.BaseDuration);
        }

        // ---------------------------------------------------------------- the arbiter

        [Test]
        public void TheStepCommitsTheCharacter()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput());
            Step(sim, new InputFrame(new Vector2(1f, 0f), jump: ButtonState.Press));

            Assert.IsFalse(sim.Can(LockFlags.Jump), "you cannot jump out of a dodge");
            Assert.IsFalse(sim.Can(LockFlags.Attack));
        }

        [Test]
        public void ControlComesBackWhenTheStepEnds()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput());
            Step(sim, Hold(), combat.DodgeAction.TotalTicks);

            Assert.IsFalse(Module(sim).IsDodging);
            Assert.IsTrue(sim.Can(LockFlags.Move));
        }

        [Test]
        public void ADodgeCanCancelAnAttacksRecovery()
        {
            // Reaction outranks Attack, and the cancel window is the gate — which is the whole
            // reason the arbiter models both.
            var combat = new CombatTuningData();
            var sim = Player(combat);

            var attack = combat.Moveset[combat.StandingAttackId];

            Step(sim, new InputFrame(Vector2.zero, attack: ButtonState.Press));
            Step(sim, Hold(), attack.CancelWindow.BaseStart);
            Assert.IsTrue(sim.CurrentLock.CancelWindowOpen);

            Step(sim, DodgeInput());

            Assert.IsTrue(Module(sim).IsDodging);
            Assert.IsFalse(sim.Get<AttackModule>().IsAttacking, "the swing gives way");
        }

        [Test]
        public void ADodgeCannotCancelAnAttacksActiveFrames()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, new InputFrame(Vector2.zero, attack: ButtonState.Press));
            Assert.IsFalse(sim.CurrentLock.CancelWindowOpen, "the swing must be committed here");

            Step(sim, DodgeInput());

            Assert.IsFalse(Module(sim).IsDodging);
            Assert.IsTrue(sim.Get<AttackModule>().IsAttacking);
        }

        [Test]
        public void ADodgeCanBeTakenOutOfABlock()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, new InputFrame(Vector2.zero, block: ButtonState.Holding), 3);
            Assert.IsTrue(sim.Get<Block>().IsGuarding);

            Step(sim, new InputFrame(Vector2.zero, block: ButtonState.Holding, dodge: ButtonState.Press));

            Assert.IsTrue(Module(sim).IsDodging);
            Assert.IsFalse(sim.Get<Block>().IsGuarding);
        }

        [Test]
        public void ADodgeCannotEscapeHitStun()
        {
            // Hit-react holds at priority 90. Being able to roll out of the consequence would make
            // every hit free.
            var combat = new CombatTuningData();
            var sim = Player(combat);

            sim.ReceiveHit(Blow(sim));
            Step(sim, Hold());
            Assert.IsTrue(sim.Get<HitReact>().IsReacting);

            Step(sim, DodgeInput());

            Assert.IsFalse(Module(sim).IsDodging);
        }

        [Test]
        public void AHitThatBeatsTheWindUpEndsTheStep()
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            Step(sim, DodgeInput());
            sim.ReceiveHit(Blow(sim));
            Step(sim, Hold());

            Assert.IsFalse(Module(sim).IsDodging, "a step you were hit out of is over");
            Assert.IsTrue(sim.Get<HitReact>().IsReacting);
        }

        // ---------------------------------------------------------------- frame rate

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void TheDodgeIsIdenticalAtAnyFrameRate(int fps)
        {
            Assert.AreEqual(RunAtFrameRate(60), RunAtFrameRate(fps));
        }

        /// <summary>
        /// Dodge once, take a blow every tick, and record what each turned into plus where the step
        /// finished. The outcome string is the whole i-frame profile; the position is the distance.
        /// Driven through the REAL clock — SimClock, the accumulator the game ships — with the
        /// input feed registered ahead of the sim and the probe blow behind it, exactly the shape
        /// of a real tick.
        /// </summary>
        private static string RunAtFrameRate(int fps)
        {
            var combat = new CombatTuningData();
            var sim = Player(combat);

            var outcomes = new System.Text.StringBuilder();

            var clock = new SimClock();
            clock.Register(new TickHook(info =>
                sim.SetInput(info.Tick == 0
                    ? new InputFrame(new Vector2(1f, 0f), dodge: ButtonState.Press)
                    : new InputFrame(new Vector2(1f, 0f)))));
            clock.Register(sim);
            clock.Register(new TickHook(_ =>
            {
                var result = sim.ReceiveHit(new HitEvent(
                    AttackerId, DefenderId, 5, -1, sim.CurrentTick, "probe", 0));

                // Health is restored each tick so the run cannot end early at a different point
                // on different machines — what is being compared is the answer, not how long the
                // defender survived.
                sim.Vitals.Reset();
                outcomes.Append((int)result.Outcome);
            }));

            AdvanceTicks(clock, 1.0 / fps, 45);

            return $"{outcomes}|{sim.State.Position.x:F4}";
        }
    }
}

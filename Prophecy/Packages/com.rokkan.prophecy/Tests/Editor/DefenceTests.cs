using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The three answers to being attacked, and the consequence of not finding one.
    ///
    /// <para>All of it in ticks, all of it headless. This is precisely where HopeFell stopped —
    /// their parry window was <c>0.2f</c> seconds accumulated in <c>Update()</c> and their
    /// invulnerability was two more floats — so there was nothing to port and every reason not to
    /// reach for seconds. These tests exist to keep it that way: a window asserted in ticks cannot
    /// quietly become frame-rate dependent.</para>
    /// </summary>
    public class DefenceTests
    {
        private const float Dt = 1f / 60f;

        private const int DefenderId = 1;
        private const int DefenderTeam = 1;
        private const int AttackerId = 9;
        private const int AttackerTeam = 2;

        // ---------------------------------------------------------------- harness

        private static CollisionWorld Ground(float top = 0f)
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, top - 2f), new Vector2(500f, top)));
            return world;
        }

        private static CharacterSim Defender(CombatTuningData combat)
        {
            var sim = PlayerCharacterFactory.Create(
                Ground(), new MovementTuningData(), MovementSpace.SideScroll, null, combat, null);

            sim.State.CombatId = DefenderId;
            sim.State.Team = DefenderTeam;

            // Facing +1 with blows arriving from +1 would mean being struck in the back on every
            // test. The defender faces right; the attacker stands to the right and swings left.
            sim.Teleport(Vector2.zero, facing: 1);
            return sim;
        }

        private static void Step(CharacterSim sim, InputFrame input, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                sim.SetInput(input);
                sim.Tick(new SimTickInfo(sim.CurrentTick + 1, Dt));
            }
        }

        private static InputFrame Hold(float x = 0f, float y = 0f) =>
            new InputFrame(new Vector2(x, y));

        private static InputFrame Guarding(float y = 0f) =>
            new InputFrame(new Vector2(0f, y), block: ButtonState.Holding);

        /// <summary>A blow travelling left, i.e. arriving at a right-facing defender's front.</summary>
        private static HitEvent Blow(CharacterSim sim, int damage = 20,
                                     AttackHeight height = AttackHeight.Any,
                                     bool unblockable = false, int facing = -1)
        {
            return new HitEvent(AttackerId, DefenderId, damage, facing, sim.CurrentTick,
                                "test_swing", 0, height, unblockable);
        }

        // ---------------------------------------------------------------- taking damage

        [Test]
        public void AnUnansweredHitTakesHealth()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            var result = sim.ReceiveHit(Blow(sim, 20));

            Assert.AreEqual(HitOutcome.Landed, result.Outcome);
            Assert.AreEqual(20, result.DamageApplied);
            Assert.AreEqual(combat.MaxHealth - 20, sim.Vitals.Health);
        }

        [Test]
        public void DamageIsCappedAtWhatIsLeft()
        {
            // So a damage tally adds up, and so an overkill blow does not report more than it took.
            var combat = new CombatTuningData { MaxHealth = 30 };
            var sim = Defender(combat);

            var result = sim.ReceiveHit(Blow(sim, 500));

            Assert.AreEqual(30, result.DamageApplied);
            Assert.IsFalse(sim.Vitals.IsAlive);
        }

        [Test]
        public void AHitOnTheDeadIsIgnored()
        {
            var combat = new CombatTuningData { MaxHealth = 10 };
            var sim = Defender(combat);

            sim.ReceiveHit(Blow(sim, 10));
            var second = sim.ReceiveHit(Blow(sim, 10));

            Assert.AreEqual(HitOutcome.Ignored, second.Outcome);
        }

        // ---------------------------------------------------------------- blocking

        [Test]
        public void AGuardedHitFromTheFrontIsBlocked()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());
            Assert.IsTrue(sim.Get<Block>().IsGuarding, "the harness must actually have the guard up");

            var result = sim.ReceiveHit(Blow(sim, 20));

            Assert.AreEqual(HitOutcome.Blocked, result.Outcome);
            Assert.Less(result.DamageApplied, 20, "a block must reduce the blow");
        }

        [Test]
        public void ABlockedHitStillCostsSomething()
        {
            // Chip is what keeps a turtle on a timer. A block that costs literally nothing makes
            // holding the guard the correct play against everything blockable in the game.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());
            var result = sim.ReceiveHit(Blow(sim, 20));

            Assert.Greater(result.DamageApplied, 0);
            Assert.AreEqual(combat.MaxHealth - result.DamageApplied, sim.Vitals.Health);
        }

        [Test]
        public void AWeakBlockedHitStillChipsAtLeastOne()
        {
            // 1 damage x 0.25 rounds to zero. Rounding a chip away is the same exploit as having
            // no chip at all, just harder to notice.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());

            Assert.AreEqual(1, sim.ReceiveHit(Blow(sim, 1)).DamageApplied);
        }

        [Test]
        public void AGuardDoesNotCoverYourBack()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());

            // Same direction as the defender faces: the blow catches them from behind.
            var result = sim.ReceiveHit(Blow(sim, 20, facing: 1));

            Assert.AreEqual(HitOutcome.Landed, result.Outcome);
        }

        [Test]
        public void AStandingGuardDoesNotAnswerALowAttack()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());
            Assert.AreEqual(AttackHeight.High, sim.Get<Block>().Guarding);

            Assert.AreEqual(HitOutcome.Landed,
                sim.ReceiveHit(Blow(sim, 20, AttackHeight.Low)).Outcome);
        }

        [Test]
        public void ACrouchingGuardAnswersALowAttackAndNotAHighOne()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding(y: -1f));
            Assert.AreEqual(AttackHeight.Low, sim.Get<Block>().Guarding,
                "down plus block on the same tick must produce a low guard");

            Assert.AreEqual(HitOutcome.Blocked,
                sim.ReceiveHit(Blow(sim, 20, AttackHeight.Low)).Outcome);

            Assert.AreEqual(HitOutcome.Landed,
                sim.ReceiveHit(Blow(sim, 20, AttackHeight.High)).Outcome);
        }

        [Test]
        public void TheGuardHeightCannotBeChangedWithoutDroppingIt()
        {
            // The block lock freezes stance, so committing to a height is the decision and
            // switching costs the time it takes to lower the shield. Without this, one button
            // answers everything and blocking stops being a read.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());
            Step(sim, Guarding(y: -1f), 10);

            Assert.AreEqual(AttackHeight.High, sim.Get<Block>().Guarding);
            Assert.AreEqual(HitOutcome.Landed, sim.ReceiveHit(Blow(sim, 20, AttackHeight.Low)).Outcome);
        }

        [Test]
        public void AnUnblockableAttackIgnoresTheGuard()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());

            Assert.AreEqual(HitOutcome.Landed,
                sim.ReceiveHit(Blow(sim, 20, AttackHeight.Any, unblockable: true)).Outcome);
        }

        [Test]
        public void AGuardPlantsYourFeet()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, new InputFrame(new Vector2(1f, 0f), block: ButtonState.Holding), 6);

            Assert.AreEqual(0f, sim.State.Velocity.x, 0.0001f,
                "being unable to reposition is the cost that makes blocking a read");
        }

        [Test]
        public void ReleasingTheGuardGivesMovementBack()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding(), 4);
            Step(sim, Hold(x: 1f), 4);

            Assert.IsFalse(sim.Get<Block>().IsGuarding);
            Assert.Greater(sim.State.Velocity.x, 0f);
        }

        // ---------------------------------------------------------------- parry

        [Test]
        public void AHitInsideTheParryWindowIsParried()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, new InputFrame(Vector2.zero, parry: ButtonState.Press));
            Step(sim, Hold(), combat.ParryAction.ParryWindow.BaseStart);

            var parry = sim.Get<Parry>();
            Assert.IsTrue(parry.WindowOpen, "the harness must be inside the window");

            var result = sim.ReceiveHit(Blow(sim, 20));

            Assert.AreEqual(HitOutcome.Parried, result.Outcome);
            Assert.AreEqual(0, result.DamageApplied);
            Assert.AreEqual(combat.MaxHealth, sim.Vitals.Health);
        }

        [Test]
        public void AParryPunishesTheAttacker()
        {
            // The negation is not the mechanic — the opening is. A parry that only cancelled
            // damage would be a slightly better block.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, new InputFrame(Vector2.zero, parry: ButtonState.Press));
            Step(sim, Hold(), combat.ParryAction.ParryWindow.BaseStart);

            var result = sim.ReceiveHit(Blow(sim, 20));

            Assert.AreEqual(combat.ParryStunTicks, result.AttackerStunTicks);
        }

        [Test]
        public void AHitBeforeTheWindowOpensIsNotParried()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            // Armed this tick; the window opens later. Startup is what makes a parry a read.
            Step(sim, new InputFrame(Vector2.zero, parry: ButtonState.Press));

            Assert.IsFalse(sim.Get<Parry>().WindowOpen);
            Assert.AreEqual(HitOutcome.Landed, sim.ReceiveHit(Blow(sim, 20)).Outcome);
        }

        [Test]
        public void AHitAfterTheWindowClosesIsNotParried()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            var window = combat.ParryAction.ParryWindow.BaseRange;

            Step(sim, new InputFrame(Vector2.zero, parry: ButtonState.Press));
            Step(sim, Hold(), window.End);

            Assert.IsFalse(sim.Get<Parry>().WindowOpen, "the window must have closed by now");
            Assert.AreEqual(HitOutcome.Landed, sim.ReceiveHit(Blow(sim, 20)).Outcome);
        }

        [Test]
        public void TheParryWindowIsExactlyAsLongAsAuthored()
        {
            // Asserted tick by tick rather than at the edges, because a window that is open for
            // the right number of ticks in the wrong place passes an edge test.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            var expected = combat.ParryAction.ParryWindow.BaseRange;
            var parry = sim.Get<Parry>();

            Step(sim, new InputFrame(Vector2.zero, parry: ButtonState.Press));

            for (int elapsed = 0; elapsed < combat.ParryAction.TotalTicks; elapsed++)
            {
                Assert.AreEqual(expected.Contains(elapsed), parry.WindowOpen,
                    $"parry window disagreed with the authored range on tick {elapsed}");

                Step(sim, Hold());
            }
        }

        [Test]
        public void AWiderParryWindowStillOpensOnTheSameTick()
        {
            // The one property the whole combat read depends on: gear makes an answer easier to
            // hold, never earlier to start.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            var parry = sim.Get<Parry>();
            parry.Modifiers = new AttackModifiers { IFrameScale = 1f, ParryScale = 2f, CancelScale = 1f };

            Step(sim, new InputFrame(Vector2.zero, parry: ButtonState.Press));

            Assert.AreEqual(combat.ParryAction.ParryWindow.BaseStart, parry.Timeline.ParryRange.Start);
            Assert.Greater(parry.Timeline.ParryRange.Duration, combat.ParryAction.ParryWindow.BaseDuration);
        }

        [Test]
        public void AParryCanBeRaisedStraightOutOfABlock()
        {
            // The guard holds its cancel window open for exactly this reason: a block is a stance
            // you can drop for something better, not a committed action.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding(), 3);
            Step(sim, new InputFrame(Vector2.zero, block: ButtonState.Holding, parry: ButtonState.Press));

            Assert.IsTrue(sim.Get<Parry>().IsActive);
            Assert.IsFalse(sim.Get<Block>().IsGuarding, "the guard must give way rather than fight it");
        }

        // ---------------------------------------------------------------- i-frames

        [Test]
        public void ACleanHitGrantsInvulnerabilityForTheAuthoredTicks()
        {
            // Without this a lingering hit box re-hits on the tick control returns and chains the
            // player to death with no chance to answer.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            sim.ReceiveHit(Blow(sim, 10));
            Step(sim, Hold());

            int health = sim.Vitals.Health;

            for (int i = 1; i < combat.HitInvulnerabilityTicks; i++)
            {
                Assert.AreEqual(HitOutcome.Invulnerable, sim.ReceiveHit(Blow(sim, 10)).Outcome,
                    $"took a hit on i-frame tick {i}");
                Step(sim, Hold());
            }

            Assert.AreEqual(health, sim.Vitals.Health);
        }

        [Test]
        public void InvulnerabilityRunsOut()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            sim.ReceiveHit(Blow(sim, 10));
            Step(sim, Hold(), combat.HitInvulnerabilityTicks + 2);

            Assert.AreEqual(HitOutcome.Landed, sim.ReceiveHit(Blow(sim, 10)).Outcome);
        }

        [Test]
        public void ABlockGrantsNoInvulnerability()
        {
            // Otherwise the guard is strictly better than not being there, and chip damage stops
            // meaning anything.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());
            Assert.AreEqual(HitOutcome.Blocked, sim.ReceiveHit(Blow(sim, 20)).Outcome);

            Step(sim, Guarding());
            Assert.AreEqual(HitOutcome.Blocked, sim.ReceiveHit(Blow(sim, 20)).Outcome);
        }

        // ---------------------------------------------------------------- hit react

        [Test]
        public void AHitTakesControlAway()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            sim.ReceiveHit(Blow(sim, 10));
            Step(sim, Hold(x: 1f));

            Assert.IsTrue(sim.Get<HitReact>().IsReacting);
            Assert.IsFalse(sim.Can(LockFlags.Move));
        }

        [Test]
        public void ControlComesBackAfterTheAuthoredStun()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            sim.ReceiveHit(Blow(sim, 10));

            // One tick to pick the stun up, then the authored count.
            Step(sim, Hold(), 1 + combat.HitStunTicks);

            Assert.IsFalse(sim.Get<HitReact>().IsReacting);
            Assert.IsTrue(sim.Can(LockFlags.Move));
        }

        [Test]
        public void AHitKnocksYouBackTheWayItTravelled()
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            // Travelling left, so the defender is thrown left.
            sim.ReceiveHit(Blow(sim, 10, facing: -1));
            Step(sim, Hold());

            Assert.Less(sim.State.Velocity.x, 0f);
        }

        [Test]
        public void AHitInterruptsASwingOnTheSameTickItLands()
        {
            // Hit-react force-locks precisely because TryLock would make it wait for the attack's
            // cancel window — which is shut for exactly the frames a counter-hit should punish.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, new InputFrame(Vector2.zero, attack: ButtonState.Press));
            Assert.IsTrue(sim.Get<AttackModule>().IsAttacking);
            Assert.IsFalse(sim.CurrentLock.CancelWindowOpen, "the swing must be committed here");

            sim.ReceiveHit(Blow(sim, 10));
            Step(sim, Hold());

            Assert.IsFalse(sim.Get<AttackModule>().IsAttacking, "the swing must not survive the hit");
            Assert.IsTrue(sim.Get<HitReact>().IsReacting);
        }

        [Test]
        public void ABlockedHitShovesYouWithoutTakingTheGuardDown()
        {
            // A blocked hit that parked a stun would hand the character to the hit-react module,
            // which force-locks — and taking the lock takes the shield down. That is guard-break
            // by accident, on every single blocked hit.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding());
            sim.ReceiveHit(Blow(sim, 20));

            Assert.Less(sim.State.Velocity.x, 0f, "a blocked hit must still move you");

            Step(sim, Guarding());

            Assert.IsTrue(sim.Get<Block>().IsGuarding, "the guard must survive a hit it answered");
            Assert.IsFalse(sim.Get<HitReact>().IsReacting);
        }

        [Test]
        public void TheStrongerOfTwoStunsOnTheSameTickWins()
        {
            // Two hits landing together must not leave the character reacting to the weaker one.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            sim.Stun(4, -1, sim.CurrentTick);
            sim.Stun(40, -1, sim.CurrentTick);
            sim.Stun(6, -1, sim.CurrentTick);

            Step(sim, Hold());

            Assert.AreEqual(40, sim.Get<HitReact>().TicksRemaining(sim.CurrentTick));
        }

        // ---------------------------------------------------------------- gate order

        [Test]
        public void InvulnerabilityOutranksTheGuard()
        {
            // If a block could claim a hit first, i-frames would silently become chip damage.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            sim.ReceiveHit(Blow(sim, 20));
            Step(sim, Guarding());

            Assert.AreEqual(HitOutcome.Invulnerable, sim.ReceiveHit(Blow(sim, 20)).Outcome);
        }

        [Test]
        public void AParryOutranksTheGuard()
        {
            // A parry raised out of a block must not be downgraded into the block it replaced.
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            Step(sim, Guarding(), 3);
            Step(sim, new InputFrame(Vector2.zero, block: ButtonState.Holding, parry: ButtonState.Press));
            Step(sim, new InputFrame(Vector2.zero, block: ButtonState.Holding),
                 combat.ParryAction.ParryWindow.BaseStart);

            Assert.IsTrue(sim.Get<Parry>().WindowOpen);
            Assert.AreEqual(HitOutcome.Parried, sim.ReceiveHit(Blow(sim, 20)).Outcome);
        }

        // ---------------------------------------------------------------- frame rate

        /// <summary>
        /// The acceptance test, applied to defence. A parry window is the single most frame-rate
        /// sensitive thing in the game — it is a handful of ticks wide and it is the difference
        /// between a read and a death.
        /// </summary>
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void DefenceIsIdenticalAtAnyFrameRate(int fps)
        {
            Assert.AreEqual(RunAtFrameRate(60), RunAtFrameRate(fps));
        }

        /// <summary>
        /// Press parry, then take one blow per tick for the whole action, and record what each tick
        /// turned into. The string is the entire defensive profile of the move.
        /// </summary>
        private static string RunAtFrameRate(int fps)
        {
            var combat = new CombatTuningData();
            var sim = Defender(combat);

            double fixedDelta = 1.0 / SimConstants.TicksPerSecond;
            double frameDelta = 1.0 / fps;
            double accumulator = 0.0;
            long tick = 0;

            var outcomes = new System.Text.StringBuilder();
            bool pressed = false;

            // Run until a fixed number of TICKS have been retired rather than for a fixed number
            // of frames. How many ticks a wall-clock second turns into can differ by one at the
            // tail because of accumulator rounding, and that is the clock's business — what is
            // being compared here is the tick-for-tick answer, not the clock's arithmetic.
            const int Ticks = 60;

            while (tick < Ticks)
            {
                accumulator += frameDelta;

                while (accumulator >= fixedDelta && tick < Ticks)
                {
                    accumulator -= fixedDelta;
                    tick++;

                    // The press lands on the first simulated tick at every frame rate, so the
                    // windows below are being compared from the same starting point.
                    var input = pressed
                        ? InputFrame.Empty
                        : new InputFrame(Vector2.zero, parry: ButtonState.Press);
                    pressed = true;

                    sim.SetInput(input);
                    sim.Tick(new SimTickInfo(tick, SimConstants.FixedDeltaSeconds));

                    // Health is restored each tick so the run cannot end early at a different
                    // point on different machines — what is being compared is the answer, not
                    // how long the defender survived.
                    var result = sim.ReceiveHit(new HitEvent(
                        AttackerId, DefenderId, 5, -1, tick, "probe", 0, AttackHeight.Any));

                    sim.Vitals.Reset();
                    outcomes.Append((int)result.Outcome);
                }
            }

            return outcomes.ToString();
        }
    }
}

using NUnit.Framework;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Pins the attack director — the fight's answer to WHO may swing and WHEN. Pure
    /// headless: no scene, no sim, just requests, grants and the clock.
    /// </summary>
    public class AttackDirectorTests
    {
        private static AttackDirector Fresh(int melee = 1, int ranged = 1, int globalGap = 10,
                                            int neutral = 20, int lapse = 15)
        {
            return new AttackDirector
            {
                Tuning = new AttackDirectorTuning
                {
                    MeleeBudget = melee,
                    RangedBudget = ranged,
                    GlobalMinGapTicks = globalGap,
                    MinNeutralGapTicks = neutral,
                    RequestLapseTicks = lapse,
                },
            };
        }

        [Test]
        public void TheSoleContenderIsGrantedOnItsFirstTick()
        {
            var director = Fresh();

            director.Request(7, AttackPool.Melee, beatTicks: 90, tick: 1);
            director.Tick(1);

            Assert.IsTrue(director.HoldsToken(7));
        }

        [Test]
        public void ASecondMeleeContenderWaitsWhileTheFirstHolds()
        {
            var director = Fresh();

            director.Request(7, AttackPool.Melee, 90, 1);
            director.Request(8, AttackPool.Melee, 90, 1);
            director.Tick(1);

            Assert.IsTrue(director.HoldsToken(7), "Lowest id wins the never-attacked tie…");
            Assert.IsFalse(director.HoldsToken(8), "…and the budget of one refuses the second.");
        }

        [Test]
        public void MeleeAndRangedBudgetsAreIndependent()
        {
            var director = Fresh();

            director.Request(7, AttackPool.Melee, 90, 1);
            director.Request(9, AttackPool.Ranged, 120, 1);
            director.Tick(1);

            Assert.IsTrue(director.HoldsToken(7), "A swing at arm's length…");
            Assert.IsTrue(director.HoldsToken(9), "…and a bolt from range are different pressures.");
        }

        [Test]
        public void AStartConsumesAndAnEndReturns()
        {
            var director = Fresh();

            director.Request(7, AttackPool.Melee, 90, 1);
            director.Request(8, AttackPool.Melee, 90, 1);
            director.Tick(1);

            director.OnAttackStarted(7, tick: 2, totalTicks: 40);
            Assert.IsFalse(director.HoldsToken(7), "The token is spent on the real start.");

            // In flight: the budget is still occupied.
            director.Request(8, AttackPool.Melee, 90, 3);
            director.Tick(3);
            Assert.IsFalse(director.HoldsToken(8), "In-flight attacks occupy the budget.");

            // Ended (say, parried at tick 20): the budget frees the same tick…
            director.OnAttackEnded(7, 20);
            director.Request(8, AttackPool.Melee, 90, 20);
            director.Tick(20);
            Assert.IsTrue(director.HoldsToken(8), "…and the waiting contender is served.");
        }

        [Test]
        public void AnInterruptedAttackStillSpendsItsBeat()
        {
            var director = Fresh();

            director.Request(7, AttackPool.Melee, 90, 1);
            director.Tick(1);
            director.OnAttackStarted(7, 2, totalTicks: 40);
            director.OnAttackEnded(7, 10);   // parried early

            // The parry bought the stun AND the beat — never a faster comeback.
            for (long tick = 11; tick < 92; tick++)
            {
                director.Request(7, AttackPool.Melee, 90, tick);
                director.Tick(tick);
                Assert.IsFalse(director.HoldsToken(7), $"regranted at tick {tick}, before the beat");
            }

            director.Request(7, AttackPool.Melee, 90, 92);
            director.Tick(92);
            Assert.IsTrue(director.HoldsToken(7), "Beat served (start 2 + 90), token again.");
        }

        [Test]
        public void TheBeatIsFlooredAtTheMovesOwnLengthPlusNeutral()
        {
            var director = Fresh(neutral: 20);

            // An authored beat SHORTER than the move: the drift that caused the rapid fire.
            director.Request(7, AttackPool.Melee, beatTicks: 10, tick: 1);
            director.Tick(1);
            director.OnAttackStarted(7, 2, totalTicks: 40);
            director.OnAttackEnded(7, 42);

            // Floor = 40 + 20 = 60 from the start tick, not the authored 10.
            director.Request(7, AttackPool.Melee, 10, 50);
            director.Tick(50);
            Assert.IsFalse(director.HoldsToken(7), "The derived floor outlasts a dead authored number.");

            director.Request(7, AttackPool.Melee, 10, 62);
            director.Tick(62);
            Assert.IsTrue(director.HoldsToken(7));
        }

        [Test]
        public void AGrantedButSilentTokenLapses()
        {
            var director = Fresh(lapse: 15);

            director.Request(7, AttackPool.Melee, 90, 1);
            director.Tick(1);
            Assert.IsTrue(director.HoldsToken(7));

            // The holder goes quiet — died, stunned, lost the target — and never presses.
            for (long tick = 2; tick <= 17; tick++) director.Tick(tick);

            Assert.IsFalse(director.HoldsToken(7), "A token idling in a dead pocket returns.");

            // And the pool serves someone else.
            director.Request(8, AttackPool.Melee, 90, 18);
            director.Tick(18);
            Assert.IsTrue(director.HoldsToken(8));
        }

        [Test]
        public void ContendersAlternateByLongestIdle()
        {
            var director = Fresh(globalGap: 0);

            // 7 attacks first (never-attacked tie broken by id), then 8 must go next.
            director.Request(7, AttackPool.Melee, 30, 1);
            director.Request(8, AttackPool.Melee, 30, 1);
            director.Tick(1);
            Assert.IsTrue(director.HoldsToken(7));

            director.OnAttackStarted(7, 2, 10);
            director.OnAttackEnded(7, 12);

            // Both eligible again at 7's beat end; 8 has NEVER attacked → longest idle.
            for (long tick = 2; tick <= 40; tick++)
            {
                director.Request(7, AttackPool.Melee, 30, tick);
                director.Request(8, AttackPool.Melee, 30, tick);
                director.Tick(tick);
            }

            Assert.IsTrue(director.HoldsToken(8), "The one who has waited longest goes next.");
            Assert.IsFalse(director.HoldsToken(7));
        }

        [Test]
        public void TheGlobalGapStaggersEvenSeparatePools()
        {
            var director = Fresh(globalGap: 10);

            director.Request(7, AttackPool.Melee, 90, 1);
            director.Request(9, AttackPool.Ranged, 90, 1);
            director.Tick(1);

            // Both hold — the gap gates STARTS, not grants.
            director.OnAttackStarted(7, 2, 40);

            // The ranged token was granted before the melee start; it may still fire. But a
            // NEW grant inside the gap is refused: return 9's token via lapse and re-ask.
            director.OnAttackEnded(9, 2);   // no-op guard: 9 never started
            for (long tick = 3; tick <= 20; tick++) director.Tick(tick);   // 9 lapses (silent)

            director.Request(9, AttackPool.Ranged, 90, 8);
            director.Tick(8);
            Assert.IsFalse(director.HoldsToken(9), "Inside the global gap after a start: refused.");

            director.Request(9, AttackPool.Ranged, 90, 13);
            director.Tick(13);
            Assert.IsTrue(director.HoldsToken(9), "Past the gap: granted.");
        }

        [Test]
        public void ForgetAndClearDropRecords()
        {
            var director = Fresh();

            director.Request(7, AttackPool.Melee, 90, 1);
            director.Tick(1);
            Assert.IsTrue(director.HoldsToken(7));

            director.Forget(7);
            Assert.IsFalse(director.HoldsToken(7));
            Assert.IsFalse(director.TryDescribe(7, 2, out _), "Forgotten means gone.");

            director.Request(8, AttackPool.Melee, 90, 3);
            director.Tick(3);
            director.Clear();
            Assert.IsFalse(director.HoldsToken(8));
        }
    }
}

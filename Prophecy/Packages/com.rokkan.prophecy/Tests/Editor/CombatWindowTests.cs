using NUnit.Framework;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Tick windows and the stat scaling that gear applies to them.
    ///
    /// <para>These are the numbers a parry lives or dies on, so the edges get pinned hard: a
    /// window that rounds to nothing, or that quietly moves its opening tick when a player equips
    /// a better shield, is the difference between combat that can be learned and combat that feels
    /// arbitrary.</para>
    /// </summary>
    public class CombatWindowTests
    {
        // ---------------------------------------------------------------- TickRange

        [Test]
        public void RangeIsHalfOpen_SoAdjacentWindowsTileWithoutOverlapping()
        {
            var first = new TickRange(0, 5);
            var second = new TickRange(5, 10);

            Assert.IsTrue(first.Contains(4));
            Assert.IsFalse(first.Contains(5), "the closing tick is outside");
            Assert.IsTrue(second.Contains(5), "and belongs to the next window instead");

            Assert.AreEqual(5, first.Duration, "duration is End - Start with no off-by-one");
        }

        [Test]
        public void AZeroLengthRangeIsAbsent_NotInstantaneous()
        {
            var empty = new TickRange(7, 7);

            Assert.IsFalse(empty.IsActive);
            Assert.IsFalse(empty.Contains(7), "no tick can ever be inside it");
            Assert.IsFalse(TickRange.None.IsActive);
        }

        [Test]
        public void ClampingTrimsToTheOwningAction()
        {
            Assert.AreEqual(new TickRange(2, 10), new TickRange(2, 14).ClampedTo(10));
            Assert.AreEqual(new TickRange(0, 4), new TickRange(-3, 4).ClampedTo(10));

            Assert.IsFalse(new TickRange(12, 15).ClampedTo(10).IsActive,
                "a window entirely past the end collapses rather than inverting");
        }

        // ---------------------------------------------------------------- scaling

        [Test]
        public void NoMultiplier_GivesTheAuthoredWindow()
        {
            var window = new ScalableWindow(baseStart: 10, baseDuration: 6);

            Assert.AreEqual(new TickRange(10, 16), window.Resolve());
            Assert.AreEqual(new TickRange(10, 16), window.Resolve(1f));
        }

        [Test]
        public void AStatWidensTheWindow()
        {
            var parry = new ScalableWindow(baseStart: 10, baseDuration: 8, maxTicks: 60);

            Assert.AreEqual(10, parry.Resolve(1.25f).Duration, "a 25% wider parry");
            Assert.AreEqual(4, parry.Resolve(0.5f).Duration, "and a debuff halves it");
        }

        [Test]
        public void TheOpeningTickNeverMoves()
        {
            // The property everything else rests on. The opening tick is the cue a player learns
            // off the telegraph, so no amount of gear may shift it — a stat makes an answer easier
            // to HOLD, never earlier to START.
            var parry = new ScalableWindow(baseStart: 12, baseDuration: 4, maxTicks: 60);

            Assert.AreEqual(12, parry.Resolve(1f).Start);
            Assert.AreEqual(12, parry.Resolve(2f).Start);
            Assert.AreEqual(12, parry.Resolve(0.5f).Start);
            Assert.AreEqual(12, parry.Resolve(0f).Start, "even a total debuff leaves the cue alone");
        }

        [Test]
        public void OnlyTheDurationIsEverRounded_SoThereIsNoEvenOddAsymmetry()
        {
            // A centred window cannot be symmetric at even durations — there is no half tick, so
            // it must lean early or late and the choice leaks into feel. Anchoring at the start
            // removes the question: the opening tick is exact by construction at every duration.
            var window = new ScalableWindow(baseStart: 9, baseDuration: 2, maxTicks: 60);

            for (float multiplier = 0.5f; multiplier <= 3f; multiplier += 0.25f)
                Assert.AreEqual(9, window.Resolve(multiplier).Start,
                    $"start drifted at x{multiplier}");
        }

        // ---------------------------------------------------------------- rounding

        [Test]
        public void RoundingIsHalfUp_NotBankers()
        {
            // Mathf.RoundToInt would turn 2.5 into 2 but 3.5 into 4. A +25% stat would then round
            // a 2-tick window DOWN while rounding a 3-tick window up — inconsistent in a way that
            // reads as a bug.
            var twoTick = new ScalableWindow(0, 2, maxTicks: 60);
            var threeTick = new ScalableWindow(0, 3, maxTicks: 60);

            Assert.AreEqual(3, twoTick.Resolve(1.25f).Duration, "2 x 1.25 = 2.5 rounds up to 3");
            Assert.AreEqual(4, threeTick.Resolve(1.25f).Duration, "3 x 1.25 = 3.75 rounds up to 4");
        }

        [Test]
        public void ASmallBonusNeverSilentlyDoesNothing()
        {
            // The failure this guards: a stat the player can see on their gear that rounds away.
            var window = new ScalableWindow(0, 4, maxTicks: 60);

            Assert.Greater(window.Resolve(1.2f).Duration, window.Resolve(1f).Duration,
                "a 20% bonus on a 4-tick window must be worth at least one tick");
        }

        // ---------------------------------------------------------------- clamps

        [Test]
        public void StackedGearCannotMakeAWindowFree()
        {
            var parry = new ScalableWindow(baseStart: 10, baseDuration: 6, maxTicks: 12);

            Assert.AreEqual(12, parry.Resolve(10f).Duration, "capped however much gear is stacked");
        }

        [Test]
        public void ADebuffCannotEraseAWindowEntirely()
        {
            var parry = new ScalableWindow(baseStart: 10, baseDuration: 6, minTicks: 2, maxTicks: 60);

            Assert.AreEqual(2, parry.Resolve(0.01f).Duration, "floored, so a parry stays possible");
            Assert.AreEqual(2, parry.Resolve(0f).Duration);
        }

        [Test]
        public void NegativeMultipliersAreTreatedAsZero()
        {
            var window = new ScalableWindow(baseStart: 4, baseDuration: 6, minTicks: 1, maxTicks: 60);

            Assert.IsTrue(window.Resolve(-5f).IsActive, "a nonsense stat must not invert the range");
            Assert.AreEqual(1, window.Resolve(-5f).Duration);
        }

        // ---------------------------------------------------------------- absence

        [Test]
        public void AnUnauthoredWindowStaysAbsentNoMatterTheStat()
        {
            // No amount of gear conjures a parry onto a move that never had one.
            var none = ScalableWindow.None;

            Assert.IsFalse(none.IsAuthored);
            Assert.IsFalse(none.Resolve(1f).IsActive);
            Assert.IsFalse(none.Resolve(100f).IsActive);
        }

        [Test]
        public void ScalingAlwaysDerivesFromBase_SoBuffsDoNotCompound()
        {
            // Resolve is pure: the authored window is never mutated, so applying 1.5 twice cannot
            // silently become 2.25.
            var window = new ScalableWindow(baseStart: 5, baseDuration: 8, maxTicks: 60);

            var first = window.Resolve(1.5f);
            var second = window.Resolve(1.5f);

            Assert.AreEqual(first, second);
            Assert.AreEqual(new TickRange(5, 13), window.Resolve(1f), "base is untouched");
        }
    }
}

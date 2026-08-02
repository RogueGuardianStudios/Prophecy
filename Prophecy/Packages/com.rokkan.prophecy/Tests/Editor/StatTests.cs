using NUnit.Framework;
using Rokkan.Prophecy.Sim.Stats;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Might, Flame and Heart, and the modifier pipeline over them.
    ///
    /// <para>The two properties worth most here are the ones HopeFell's version does not have:
    /// a result independent of the order modifiers arrived in, and durations counted in ticks. Both
    /// are invisible in single player and both decide whether two machines agree.</para>
    /// </summary>
    public class StatTests
    {
        private static StatBlock Fresh() => new StatBlock();

        // ---------------------------------------------------------------- extensibility

        [Test]
        public void EveryStatKindIsAddressable()
        {
            // Guards the fragility that adding a stat used to introduce: the level array was
            // written as three literals, so a fourth StatKind would have thrown on the first read
            // rather than failing to compile. Sizing from the enum fixes it; this catches any
            // future place that goes back to assuming a count.
            var block = Fresh();

            foreach (StatKind kind in System.Enum.GetValues(typeof(StatKind)))
            {
                Assert.DoesNotThrow(() => block.SetLevel(kind, 3), $"{kind} is not addressable");
                Assert.AreEqual(3, block.LevelOf(kind), $"{kind} did not keep its level");

                block.Add(StatModifier.Flat(kind, 1f));
                Assert.AreEqual(4f, block.Effective(kind), 0.0001f, $"{kind} ignored its modifier");
            }

            Assert.AreEqual(StatBlock.Count * 3, block.TotalLevels,
                "TotalLevels must count every stat, not a hard-coded three");
        }

        [Test]
        public void ModifiersOnlyTouchTheStatTheyName()
        {
            var block = Fresh();
            block.Add(StatModifier.Flat(StatKind.Might, 5f));

            Assert.AreEqual(6f, block.Effective(StatKind.Might), 0.0001f);
            Assert.AreEqual(1f, block.Effective(StatKind.Flame), 0.0001f, "Flame must be untouched");
            Assert.AreEqual(1f, block.Effective(StatKind.Heart), 0.0001f, "Heart must be untouched");
        }

        // ---------------------------------------------------------------- ordering

        [Test]
        public void ModifierOrderCannotChangeTheResult()
        {
            // The bug this design exists to prevent. Applied in arrival order, +2 then +50% gives
            // 9 while +50% then +2 gives 8 — the same two pickups in a different order producing
            // different characters. HopeFell applies through a multicast event in subscription
            // order and carries "// add prority" in the source as a note about it.
            var a = Fresh();
            a.SetLevel(StatKind.Might, 4);
            a.Add(StatModifier.Flat(StatKind.Might, 2f));
            a.Add(StatModifier.Percent(StatKind.Might, 0.5f));

            var b = Fresh();
            b.SetLevel(StatKind.Might, 4);
            b.Add(StatModifier.Percent(StatKind.Might, 0.5f));
            b.Add(StatModifier.Flat(StatKind.Might, 2f));

            Assert.AreEqual(a.Effective(StatKind.Might), b.Effective(StatKind.Might), 0.0001f);
            Assert.AreEqual(9f, a.Effective(StatKind.Might), 0.0001f, "(4 + 2) * 1.5");
        }

        [Test]
        public void PercentagesSumRatherThanCompound()
        {
            // +10% and +20% is +30%, not +32%. Compounding would reintroduce order sensitivity
            // through the back door the moment a third one appeared.
            var block = Fresh();
            block.SetLevel(StatKind.Heart, 10);   // clamped to MaxLevel
            block.SetLevel(StatKind.Heart, 4);

            block.Add(StatModifier.Percent(StatKind.Heart, 0.1f));
            block.Add(StatModifier.Percent(StatKind.Heart, 0.2f));

            Assert.AreEqual(4f * 1.3f, block.Effective(StatKind.Heart), 0.0001f);
        }

        [Test]
        public void FinalModifiersAreNotScaled()
        {
            var block = Fresh();
            block.SetLevel(StatKind.Might, 2);

            block.Add(StatModifier.Percent(StatKind.Might, 1f));   // doubles
            block.Add(StatModifier.Final(StatKind.Might, 1f));     // then plus one

            Assert.AreEqual(5f, block.Effective(StatKind.Might), 0.0001f, "(2 * 2) + 1");
        }

        [Test]
        public void ADebuffCannotInvertAStat()
        {
            // Stacked debuffs going negative would make Might heal the target, which nobody ever
            // intends and which reads as the game being broken rather than as a strong debuff.
            var block = Fresh();
            block.SetLevel(StatKind.Might, 2);
            block.Add(StatModifier.Flat(StatKind.Might, -50f));

            Assert.AreEqual(0f, block.Effective(StatKind.Might), 0.0001f);
        }

        // ---------------------------------------------------------------- ticks

        [Test]
        public void ABuffExpiresOnATickNotAClock()
        {
            // Counted in ticks so a buff lasts the same at 30 fps and 144. HopeFell's runs on a
            // wall-clock CountdownTimer, which would make its duration frame-rate dependent — the
            // one thing every other window in this project is authored in ticks to avoid.
            var block = Fresh();
            block.SetLevel(StatKind.Might, 1);
            block.Add(StatModifier.Flat(StatKind.Might, 3f, expiresOnTick: 100));

            block.PruneExpired(99);
            Assert.AreEqual(4f, block.Effective(StatKind.Might), 0.0001f, "still up on tick 99");

            block.PruneExpired(100);
            Assert.AreEqual(1f, block.Effective(StatKind.Might), 0.0001f, "gone on the tick it names");
        }

        [Test]
        public void PermanentModifiersSurviveAnyTick()
        {
            var block = Fresh();
            block.Add(StatModifier.Flat(StatKind.Heart, 2f));

            block.PruneExpired(long.MaxValue - 1);

            Assert.AreEqual(3f, block.Effective(StatKind.Heart), 0.0001f);
        }

        [Test]
        public void EverythingFromOneSourceComesOffTogether()
        {
            const int sword = 7;

            var block = Fresh();
            block.Add(StatModifier.Flat(StatKind.Might, 2f, sourceId: sword));
            block.Add(StatModifier.Percent(StatKind.Might, 0.5f, sourceId: sword));
            block.Add(StatModifier.Flat(StatKind.Might, 1f, sourceId: 99));

            block.RemoveSource(sword);

            Assert.AreEqual(2f, block.Effective(StatKind.Might), 0.0001f, "only the other source left");
        }

        // ---------------------------------------------------------------- derived numbers

        [Test]
        public void HeartSizesHealthAndMightSizesDamage()
        {
            var block = Fresh();
            var tuning = block.Tuning;

            Assert.AreEqual(tuning.BaseHealth, block.MaxHealth, "level 1 is the base");

            block.SetLevel(StatKind.Heart, 3);
            Assert.AreEqual(tuning.BaseHealth + 2 * tuning.HealthPerHeart, block.MaxHealth);

            Assert.AreEqual(1f, block.DamageScale, 0.0001f, "Might 1 deals authored damage");

            block.SetLevel(StatKind.Might, 5);
            Assert.AreEqual(1f + 4 * tuning.DamageScalePerMight, block.DamageScale, 0.0001f);
        }

        [Test]
        public void AMightDebuffWeakensAHitWithoutErasingIt()
        {
            var block = Fresh();
            block.Add(StatModifier.Percent(StatKind.Might, -0.99f));

            Assert.Less(block.ScaleDamage(10), 10, "a crushing debuff must actually weaken the hit");
            Assert.GreaterOrEqual(block.ScaleDamage(10), 1, "but a hit that lands always costs something");
            Assert.AreEqual(0, block.ScaleDamage(0), "and nothing is still nothing");
        }

        [Test]
        public void MightAloneCannotRemoveMoreThanOneScaleStepOfDamage()
        {
            // A property of the derivation worth knowing rather than rediscovering: DamageScale is
            // affine in Might and Might floors at zero, so the harshest possible Might debuff costs
            // exactly DamageScalePerMight — 25% at the current tuning, however deep the debuff.
            // Anything wanting a stronger reduction has to scale damage directly, not via Might.
            var block = Fresh();
            var tuning = block.Tuning;

            block.Add(StatModifier.Flat(StatKind.Might, -1000f));

            Assert.AreEqual(tuning.BaseDamageScale - tuning.DamageScalePerMight,
                            block.DamageScale, 0.0001f);
        }

        [Test]
        public void DamageFloorsAtOneWhenTheScaleIsLowEnoughToReachIt()
        {
            // The floor is defensive — unreachable through Might at the current tuning, which is
            // exactly why it needs a test that reaches it deliberately.
            var block = Fresh();
            block.Tuning = new StatTuningData { BaseDamageScale = 0.01f, DamageScalePerMight = 0f };

            Assert.AreEqual(1, block.ScaleDamage(10), "a hit that lands is never free");
        }

        // ---------------------------------------------------------------- resolve

        [Test]
        public void ResolveBanksIntoLevelUpsTheePlayerThenSpends()
        {
            var block = Fresh();
            int cost = block.Tuning.ResolveForNextLevel(block.TotalLevels);

            Assert.AreEqual(0, block.AwardResolve(cost - 1), "just short earns nothing");
            Assert.AreEqual(1, block.AwardResolve(1), "and the last point tips it");

            Assert.AreEqual(1, block.UnspentLevels);
            Assert.IsTrue(block.SpendLevel(StatKind.Flame));
            Assert.AreEqual(2, block.LevelOf(StatKind.Flame));
            Assert.AreEqual(0, block.UnspentLevels);
        }

        [Test]
        public void ALargeAwardCrossesSeveralThresholdsAtOnce()
        {
            // Binding a Protector is a deliberate spike (§6.2). Granting one level and discarding
            // the remainder would rob the moment the whole progression is built around.
            var block = Fresh();

            int earned = block.AwardResolve(10_000);

            Assert.Greater(earned, 1, "a huge award must grant more than one level");
            Assert.AreEqual(earned, block.UnspentLevels);
        }

        [Test]
        public void LevelsAreCappedAndSpendingRefusesAtTheCap()
        {
            var block = Fresh();
            block.SetLevel(StatKind.Might, 999);

            Assert.AreEqual(StatBlock.MaxLevel, block.LevelOf(StatKind.Might));

            block.AwardResolve(10_000);
            Assert.IsFalse(block.SpendLevel(StatKind.Might), "a capped stat refuses the level");
            Assert.Greater(block.UnspentLevels, 0, "and the level stays banked for another stat");
        }

        [Test]
        public void ResolveGetsMoreExpensivePerLevel()
        {
            var tuning = new StatTuningData();

            int first = tuning.ResolveForNextLevel(3);
            int fifth = tuning.ResolveForNextLevel(7);

            Assert.Greater(fifth, first, "later levels must cost more or the curve is pointless");
        }
    }
}

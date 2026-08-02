using System.Collections.Generic;
using NUnit.Framework;
using Rokkan.Prophecy.Sim;
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

        // ---------------------------------------------------------------- speed & restrictions

        [Test]
        public void SpeedIsAMultiplierThatStartsAtOne()
        {
            var block = Fresh();

            Assert.AreEqual(1f, block.Effective(StatKind.Speed), 0.0001f, "unmodified is normal pace");

            block.Apply(StatModifier.Percent(StatKind.Speed, -0.5f));
            Assert.AreEqual(0.5f, block.Effective(StatKind.Speed), 0.0001f, "a hobble");

            block.ClearModifiers();
            block.Apply(StatModifier.Percent(StatKind.Speed, 0.5f));
            Assert.AreEqual(1.5f, block.Effective(StatKind.Speed), 0.0001f, "a haste");
        }

        [Test]
        public void SpeedCannotBeLevelledAndDoesNotCountAsPower()
        {
            // The finale gates on total power as a measure of complicity (§6.3). A haste potion
            // must not read as having bound another Protector.
            var block = Fresh();
            block.AwardResolve(100_000);

            int before = block.TotalLevels;

            Assert.IsFalse(block.SpendLevel(StatKind.Speed), "Resolve buys the three, not Speed");
            Assert.AreEqual(before, block.TotalLevels);

            block.SetLevel(StatKind.Speed, StatBlock.MaxLevel);
            Assert.AreEqual(before, block.TotalLevels, "and Speed never counts toward the gate");
        }

        [Test]
        public void ASlowCanExpireLikeAnyOtherDebuff()
        {
            var block = Fresh();
            block.Apply(StatModifier.Percent(StatKind.Speed, -0.6f, expiresOnTick: 100));

            Assert.AreEqual(0.4f, block.Effective(StatKind.Speed), 0.0001f);

            block.PruneExpired(100);
            Assert.AreEqual(1f, block.Effective(StatKind.Speed), 0.0001f);
        }

        [Test]
        public void ASilenceBarsOneAbilityAndLeavesTheRestAlone()
        {
            var set = new RestrictionSet();
            set.Apply(AbilityRestriction.Silence(AbilityId.FlameArt, expiresOnTick: 100));

            Assert.IsTrue(set.IsBarred(AbilityId.FlameArt));
            Assert.IsFalse(set.IsBarred(AbilityId.Attack), "a silence is not a disarm");
        }

        [Test]
        public void ARootBarsMovementThroughTheSameFlagsTheLockUses()
        {
            var set = new RestrictionSet();
            set.Apply(AbilityRestriction.Lock(LockFlags.Move | LockFlags.Jump, expiresOnTick: 100));

            Assert.IsTrue(set.Blocks(LockFlags.Move));
            Assert.IsTrue(set.Blocks(LockFlags.Jump));
            Assert.IsFalse(set.Blocks(LockFlags.Attack), "rooted, not disarmed");
        }

        [Test]
        public void RestrictionsLiftWhenTheyExpire()
        {
            var set = new RestrictionSet();
            set.Apply(AbilityRestriction.Lock(LockFlags.Move, expiresOnTick: 50));

            set.PruneExpired(49);
            Assert.IsTrue(set.Blocks(LockFlags.Move));

            set.PruneExpired(50);
            Assert.IsFalse(set.Blocks(LockFlags.Move), "and the fold has to be rebuilt, not stale");
        }

        [Test]
        public void ReapplyingASilenceRefreshesItAndNeverShortensIt()
        {
            // Being silenced twice is being silenced — but a brief reapplication must not cure a
            // long one.
            var set = new RestrictionSet();
            const int key = 55;

            set.Apply(AbilityRestriction.Silence(AbilityId.FlameArt, 500, stackKey: key));
            set.Apply(AbilityRestriction.Silence(AbilityId.FlameArt, 100, stackKey: key));

            Assert.AreEqual(1, set.All.Count, "one silence, not two");

            set.PruneExpired(200);
            Assert.IsTrue(set.IsBarred(AbilityId.FlameArt), "the longer duration must win");
        }

        [Test]
        public void RestrictionsComeOffWithTheirSource()
        {
            var set = new RestrictionSet();
            set.Apply(AbilityRestriction.Lock(LockFlags.Move, StatModifier.Permanent, sourceId: 9));
            set.Apply(AbilityRestriction.Silence(AbilityId.Attack, StatModifier.Permanent, sourceId: 4));

            set.RemoveSource(9);

            Assert.IsFalse(set.Blocks(LockFlags.Move));
            Assert.IsTrue(set.IsBarred(AbilityId.Attack), "the other source is untouched");
        }

        // ---------------------------------------------------------------- stacking

        private const int Poison = 900;

        private static StatModifier PoisonDose(float value, long expires, int maxStacks = 0) =>
            new StatModifier
            {
                Kind = StatKind.Might, Stage = StatStage.Flat, Value = value,
                ExpiresOnTick = expires, StackKey = Poison, MaxStacks = maxStacks,
            };

        [Test]
        public void ReapplyingRefreshesTheDuration()
        {
            var block = Fresh();
            block.Apply(PoisonDose(-1f, expires: 100));
            block.Apply(PoisonDose(-1f, expires: 500));

            block.PruneExpired(200);

            Assert.AreEqual(0f, block.Effective(StatKind.Might), 0.0001f,
                "the refreshed dose should still be biting at tick 200");
        }

        [Test]
        public void TheStrongerDoseWinsAndTheWeakerCannotDiluteIt()
        {
            // A trash mob's feeble poison must be able to refresh a boss's, never water it down.
            var block = Fresh();
            block.SetLevel(StatKind.Might, 6);

            block.Apply(PoisonDose(-3f, expires: 100));
            block.Apply(PoisonDose(-1f, expires: 500));

            Assert.AreEqual(3f, block.Effective(StatKind.Might), 0.0001f, "6 - 3, not 6 - 1");
        }

        [Test]
        public void AStrongerReapplicationUpgradesTheStack()
        {
            var block = Fresh();
            block.SetLevel(StatKind.Might, 6);

            block.Apply(PoisonDose(-1f, expires: 100));
            block.Apply(PoisonDose(-3f, expires: 500));

            Assert.AreEqual(3f, block.Effective(StatKind.Might), 0.0001f);
        }

        [Test]
        public void TheDefaultStackLimitIsOne()
        {
            var block = Fresh();
            block.SetLevel(StatKind.Might, 8);

            var dose = PoisonDose(-1f, expires: 500);
            block.Apply(dose);
            block.Apply(dose);
            block.Apply(dose);

            Assert.AreEqual(1, block.StackCountOf(dose), "three doses, one stack");
            Assert.AreEqual(7f, block.Effective(StatKind.Might), 0.0001f);
        }

        [Test]
        public void AnEffectCanBeAllowedToStackToItsCap()
        {
            var block = Fresh();
            block.SetLevel(StatKind.Might, 8);

            for (int i = 0; i < 5; i++) block.Apply(PoisonDose(-1f, expires: 500, maxStacks: 3));

            Assert.AreEqual(3, block.StackCountOf(PoisonDose(-1f, 500, 3)), "capped at three");
            Assert.AreEqual(5f, block.Effective(StatKind.Might), 0.0001f, "8 - 3");
        }

        [Test]
        public void AWholeStackRefreshesTogether()
        {
            // One timer on screen rather than three drifting apart.
            var block = Fresh();
            block.SetLevel(StatKind.Might, 8);

            block.Apply(PoisonDose(-1f, expires: 100, maxStacks: 3));
            block.Apply(PoisonDose(-1f, expires: 100, maxStacks: 3));
            block.Apply(PoisonDose(-1f, expires: 900, maxStacks: 3));

            block.PruneExpired(500);

            Assert.AreEqual(3, block.StackCountOf(PoisonDose(-1f, 900, 3)),
                "the earlier doses should have been carried to the new expiry");
        }

        [Test]
        public void GearDoesNotStackManageAndSimplyAddsUp()
        {
            // Two rings of +1 Might give +2. StackKey stays zero for anything whose second copy is
            // genuinely a second copy.
            var block = Fresh();

            block.Apply(StatModifier.Flat(StatKind.Might, 1f));
            block.Apply(StatModifier.Flat(StatKind.Might, 1f));

            Assert.AreEqual(3f, block.Effective(StatKind.Might), 0.0001f);
        }

        [Test]
        public void DifferentStatsUnderOneEffectAreManagedSeparately()
        {
            // A poison that saps Might and Heart refreshes and caps each independently, so
            // "the strongest" always compares like with like.
            var block = Fresh();
            block.SetLevel(StatKind.Might, 5);
            block.SetLevel(StatKind.Heart, 5);

            block.Apply(new StatModifier
            {
                Kind = StatKind.Might, Stage = StatStage.Flat, Value = -2f,
                ExpiresOnTick = 500, StackKey = Poison,
            });
            block.Apply(new StatModifier
            {
                Kind = StatKind.Heart, Stage = StatStage.Flat, Value = -1f,
                ExpiresOnTick = 500, StackKey = Poison,
            });

            Assert.AreEqual(3f, block.Effective(StatKind.Might), 0.0001f);
            Assert.AreEqual(4f, block.Effective(StatKind.Heart), 0.0001f);
        }

        // ---------------------------------------------------------------- parity with HopeFell

        [Test]
        public void ModifiersCanBeAddedAndRemovedInBulk()
        {
            // The equip path. HopeFell's EquippableItem calls AddModifiers / RemoveModifiers with
            // the whole set, so the same shape has to exist here.
            var block = Fresh();
            var set = new List<StatModifier>
            {
                StatModifier.Flat(StatKind.Might, 1f, sourceId: 5),
                StatModifier.Flat(StatKind.Heart, 2f, sourceId: 5),
            };

            block.AddRange(set);
            Assert.AreEqual(2f, block.Effective(StatKind.Might), 0.0001f);
            Assert.AreEqual(3f, block.Effective(StatKind.Heart), 0.0001f);

            block.RemoveSources(new[] { 5 });
            Assert.AreEqual(1f, block.Effective(StatKind.Might), 0.0001f);
            Assert.AreEqual(1f, block.Effective(StatKind.Heart), 0.0001f);
        }

        [Test]
        public void OneModifierCanBeCancelledWithoutItsSiblings()
        {
            // HopeFell removes by object reference, which a struct cannot offer. An explicit id
            // does the same job and is unambiguous when two identical buffs are stacked.
            var block = Fresh();
            block.Add(StatModifier.Flat(StatKind.Might, 1f, sourceId: 5, id: 11));
            block.Add(StatModifier.Flat(StatKind.Might, 1f, sourceId: 5, id: 12));

            Assert.AreEqual(3f, block.Effective(StatKind.Might), 0.0001f);

            Assert.IsTrue(block.Cancel(11));
            Assert.AreEqual(2f, block.Effective(StatKind.Might), 0.0001f, "only one should go");

            Assert.IsFalse(block.Cancel(0), "an unidentified id must never match anything");
        }

        [Test]
        public void AuthoredSpecsBecomeLiveModifiersFromTheTickTheyAreApplied()
        {
            // The reason a spec exists at all: authored durations, not absolute ticks. Equipping
            // the same sword twice must give a full-length buff both times.
            var spec = new StatModifierSpec(StatKind.Might, StatStage.Flat, 2f, durationTicks: 60);

            var early = spec.Resolve(currentTick: 0);
            var late = spec.Resolve(currentTick: 1000);

            Assert.AreEqual(60, early.ExpiresOnTick);
            Assert.AreEqual(1060, late.ExpiresOnTick, "a duration is relative to when it starts");
        }

        [Test]
        public void ASpecWithNoDurationIsPermanent()
        {
            var spec = new StatModifierSpec(StatKind.Heart, StatStage.Flat, 1f, durationTicks: 0);

            Assert.IsTrue(spec.IsPermanent);
            Assert.IsTrue(spec.Resolve(500).IsPermanent, "worn gear must not quietly fall off");
        }

        [Test]
        public void ExpiryCanBeObservedWithoutPolling()
        {
            // HopeFell raises OnDispose. A caller-supplied list does the same job without
            // allocating and without letting a subscriber run inside the sim's tick.
            var block = Fresh();
            block.Add(StatModifier.Flat(StatKind.Might, 3f, expiresOnTick: 10, id: 42));

            var expired = new List<StatModifier>();
            block.PruneExpired(9, expired);
            Assert.IsEmpty(expired, "nothing has lapsed yet");

            block.PruneExpired(10, expired);
            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual(42, expired[0].Id, "and it says which one so a caller can react");
        }

        [Test]
        public void SkillsCanScaleOffSeveralStatsAtOnce()
        {
            // HopeFell's StatScaleStruct, held as a list on SkillStratagy so one skill can scale
            // off more than one stat. A flame-art costing Flame and hitting for Might is the case.
            var source = new FixedStats(might: 4, flame: 2, heart: 1);

            var scales = new List<StatScale>
            {
                new StatScale(StatKind.Might, 2f),
                new StatScale(StatKind.Flame, 3f),
            };

            Assert.AreEqual(4 * 2 + 2 * 3, scales.Evaluate(source), 0.0001f);
            Assert.AreEqual(10 + 14, scales.ApplyToDamage(source, 10));
        }

        [Test]
        public void AnUnauthoredScaleSetLeavesDamageAlone()
        {
            // An unauthored skill should do its flat damage, not zero.
            var source = new FixedStats();

            Assert.AreEqual(10, ((IReadOnlyList<StatScale>)null).ApplyToDamage(source, 10));
            Assert.AreEqual(10, new List<StatScale>().ApplyToDamage(source, 10));
        }

        [Test]
        public void AStatBlockIsAStatSource()
        {
            // So a hit box or a skill can be handed the player's block, an enemy's, or a test
            // double without caring which.
            var block = Fresh();
            block.SetLevel(StatKind.Might, 5);

            IStatSource source = block;

            Assert.AreEqual(5, source.LevelOf(StatKind.Might));
            Assert.AreEqual(5f, source.Effective(StatKind.Might), 0.0001f);
        }

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

            // Progression stats only — Speed is addressable and modifiable but is not power, so
            // raising it must not move the finale's gate.
            Assert.AreEqual(StatKinds.ProgressionCount * 3, block.TotalLevels,
                "TotalLevels must count every PROGRESSION stat, and only those");
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

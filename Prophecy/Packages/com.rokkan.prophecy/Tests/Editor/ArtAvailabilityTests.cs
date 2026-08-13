using NUnit.Framework;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Arts;
using Rokkan.Prophecy.Sim.Collision;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Art availability (Matt: you will not start with all arts): the factory seeds every
    /// art for the gray box, the loadout's toggles are the switch, the cast path refuses
    /// what the Order has not taught, and forgetting the equipped art unequips it.
    /// </summary>
    public sealed class ArtAvailabilityTests
    {
        private static CharacterSim Player() =>
            PlayerCharacterFactory.Create(new CollisionWorld(), new MovementTuningData());

        [Test]
        public void TheGrayBoxKnowsEveryArt()
        {
            var sim = Player();

            foreach (var entry in ArtCatalog.All)
                Assert.IsTrue(sim.KnownArts.Contains(entry.Id), $"{entry.Id} should be known");
        }

        [Test]
        public void CastRefusesAnUntaughtArtAndSpendsNothing()
        {
            var sim = Player();
            sim.Reserve.SetMax(100);
            sim.Reserve.Refill();

            Assert.IsTrue(CastArt.Cast(sim, ArtId.Sharpen, 0), "known casts");
            float afterFirst = sim.Reserve.Current;

            sim.KnownArts.Remove(ArtId.Sharpen);

            Assert.IsFalse(CastArt.Cast(sim, ArtId.Sharpen, 0), "untaught refuses");
            Assert.AreEqual(afterFirst, sim.Reserve.Current, "and the refusal spent NOTHING");
        }

        [Test]
        public void TheLoadoutTogglesAvailability()
        {
            var sim = Player();
            var loadout = new AbilityLoadoutData();
            loadout.ArtToggles.Add(new AbilityLoadoutData.ArtEntry(ArtId.Sharpen, false));

            loadout.Apply(sim);
            Assert.IsFalse(sim.KnownArts.Contains(ArtId.Sharpen), "toggled off is forgotten");

            loadout.ArtToggles[0] = new AbilityLoadoutData.ArtEntry(ArtId.Sharpen, true);
            loadout.Apply(sim);
            Assert.IsTrue(sim.KnownArts.Contains(ArtId.Sharpen), "toggled back on is known");
        }

        [Test]
        public void ForgettingTheEquippedArtUnequipsIt()
        {
            var sim = Player();
            sim.EquippedArt = ArtId.Sharpen;

            var loadout = new AbilityLoadoutData();
            loadout.ArtToggles.Add(new AbilityLoadoutData.ArtEntry(ArtId.Sharpen, false));
            loadout.Apply(sim);

            Assert.AreEqual(ArtId.None, sim.EquippedArt,
                            "the slot cannot hold what the Order has not taught");
        }
    }
}

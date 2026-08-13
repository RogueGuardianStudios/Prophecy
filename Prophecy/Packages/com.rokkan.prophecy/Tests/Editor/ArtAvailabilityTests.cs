using NUnit.Framework;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Arts;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;
using static Rokkan.Prophecy.Tests.SimTestHarness;

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

            foreach (var entry in sim.ArtTuning.Arts)
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
        public void DroppingARunningArtIsFreeAndRemovesItsEffect()
        {
            var sim = Player();
            sim.Reserve.SetMax(100);
            sim.Reserve.Refill();

            float baseline = sim.Stats.Effective(Sim.Stats.StatKind.Might);

            Assert.IsTrue(CastArt.Cast(sim, ArtId.Sharpen, 0));
            Assert.IsTrue(sim.ActiveArts.Contains(ArtId.Sharpen));
            Assert.Greater(sim.Stats.Effective(Sim.Stats.StatKind.Might), baseline,
                           "the cast applied its effect");

            float afterCast = sim.Reserve.Current;

            Assert.IsTrue(CastArt.Cast(sim, ArtId.Sharpen, 0), "the recast is the drop");
            Assert.IsFalse(sim.ActiveArts.Contains(ArtId.Sharpen));
            Assert.AreEqual(baseline, sim.Stats.Effective(Sim.Stats.StatKind.Might),
                            "the drop took the effect with it");
            Assert.AreEqual(afterCast, sim.Reserve.Current, "and the drop spent NOTHING");
        }

        [Test]
        public void TheVowCannotBeRecalled()
        {
            var sim = Player();
            sim.Reserve.SetMax(100);
            sim.Reserve.Refill();

            Assert.IsTrue(CastArt.Cast(sim, ArtId.GluttonForPunishment, 0));
            float afterCast = sim.Reserve.Current;

            Assert.IsFalse(CastArt.Cast(sim, ArtId.GluttonForPunishment, 0),
                           "GfP refuses to drop until the room is done");
            Assert.IsTrue(sim.ActiveArts.Contains(ArtId.GluttonForPunishment),
                          "and it is still running");
            Assert.AreEqual(afterCast, sim.Reserve.Current, "the refusal spent nothing");
        }

        [Test]
        public void TheDoubleJumpIsAscentsGift()
        {
            var tuning = new MovementTuningData();
            var sim = PlayerCharacterFactory.Create(Ground(), tuning);

            // Ground jump, then wait out the arming delay in the air.
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding),
                 tuning.AirJumpArmTicks + 2);

            // Without Ascent running the second press buys nothing — the fall continues.
            float before = sim.State.Velocity.y;
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Assert.Less(sim.State.Velocity.y, before, "no wings without the art");

            // Light Ascent mid-air: the very same press now relaunches.
            sim.ActiveArts.Add(ArtId.Ascent);
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Assert.AreEqual(tuning.AirJumpVelocity, sim.State.Velocity.y, 0.001f,
                            "Ascent gives the second jump");
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

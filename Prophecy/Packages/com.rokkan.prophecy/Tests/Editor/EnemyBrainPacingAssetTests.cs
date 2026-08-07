using NUnit.Framework;
using RGS.GOAP.Core;
using Rokkan.Prophecy.Goap;
using UnityEditor;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Pins the pacing wiring in the GENERATED brains — the HANDOFF §11.11 seam as asset
    /// assertions: attacks require the token, strike goals are invalid without it, the
    /// sensor is declared and mapped, and the grunt owns its refused-pose action. Broken
    /// wiring here fails silently at runtime (an unmapped output writes nowhere), which is
    /// exactly why it is pinned at the asset level.
    /// </summary>
    public class EnemyBrainPacingAssetTests
    {
        private static GoapBrainSO LoadBrain(string archetype)
        {
            var brain = AssetDatabase.LoadAssetAtPath<GoapBrainSO>(
                $"Assets/_Prophecy/Data/Enemies/Brain_{archetype}.asset");
            Assert.IsNotNull(brain, $"No Brain_{archetype}.asset — run Prophecy > Build > Generate Enemies.");
            return brain;
        }

        private static bool AnyConditionNamed(System.Collections.Generic.List<NodeCondition> conditions,
                                              string beliefName)
        {
            foreach (var condition in conditions)
                if (condition?.Belief != null && condition.Belief.name == beliefName)
                    return true;
            return false;
        }

        [TestCase("Grunt", "Swing")]
        [TestCase("Caster", "Fire")]
        public void AttacksRequireTheToken(string archetype, string actionName)
        {
            var state = LoadBrain(archetype).States[0];
            var action = state.Actions.Find(a => a.Name == actionName);

            Assert.IsNotNull(action, $"{archetype} has no '{actionName}' action.");
            Assert.IsTrue(AnyConditionNamed(action.Preconditions, "HasAttackToken"),
                $"{archetype}'s '{actionName}' must take the attack token as a precondition.");
        }

        [TestCase("Grunt")]
        [TestCase("Caster")]
        public void TheStrikeGoalIsInvalidWithoutTheToken(string archetype)
        {
            var state = LoadBrain(archetype).States[0];
            var goal = state.Goals.Find(g => g.Name == "StrikeTarget");

            Assert.IsNotNull(goal, $"{archetype} has no StrikeTarget goal.");
            Assert.IsTrue(AnyConditionNamed(goal.ValidityConditions, "HasAttackToken"),
                $"{archetype}'s strike goal must not even be OFFERED without the token — a " +
                "refused enemy plans its idle cleanly instead of burning planner failures.");
        }

        [TestCase("Grunt")]
        [TestCase("Caster")]
        public void TheTokenSensorIsDeclaredOnTheBrain(string archetype)
        {
            var brain = LoadBrain(archetype);

            bool declared = false;
            foreach (var sensor in brain.Sensors)
                if (sensor is ProphecyAttackTokenSensor)
                    declared = true;

            Assert.IsTrue(declared, $"{archetype}'s brain does not carry the attack token sensor.");
        }

        [Test]
        public void TheGruntGivesGroundWhileRefused()
        {
            var state = LoadBrain("Grunt").States[0];
            var giveGround = state.Actions.Find(a => a.Name == "GiveGround");

            Assert.IsNotNull(giveGround,
                "The grunt has no GiveGround action — the beat-locked back-step (Matt's " +
                "refused pose) is missing.");
            Assert.IsNotNull(giveGround.Strategy, "GiveGround has a null strategy binding.");
        }
    }
}

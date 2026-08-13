using RGS.GOAP.Core;
using Rokkan.Prophecy.Presentation;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    /// <summary>
    /// Publishes whether the fight's attack director has licensed this enemy to attack —
    /// the HANDOFF §11.11 seam: the sim grants tokens, this sensor copies the answer onto
    /// the blackboard, and Swing/Fire take it as a precondition. A refused enemy's strike
    /// goal goes invalid and the planner falls back to its idle on its own, which is the
    /// whole reason the seam is a belief rather than a busy-wait inside the action.
    ///
    /// <para>Like every Prophecy sensor it senses nothing itself:
    /// <c>EnemyBrainHost</c> asked the director once this tick and this copies the answer.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Attack Token Sensor", fileName = "Sensor_AttackToken")]
    public sealed class ProphecyAttackTokenSensor : GoapSensorSO
    {
        public const string HasAttackToken = "HasAttackToken";

        public override SensorOutputConfig[] GetOutputs() => new[]
        {
            new SensorOutputConfig(HasAttackToken, GoapKeyType.Boolean),
        };

        public override void OnUpdate(GameObject agent, GoapSensorLocalSettings settings,
                                      GoapBlackboard blackboard)
        {
            if (agent == null || !agent.TryGetComponent(out EnemyBrainHost host)) return;

            WriteBool(settings, blackboard, HasAttackToken, host.HasAttackToken);
        }
    }
}

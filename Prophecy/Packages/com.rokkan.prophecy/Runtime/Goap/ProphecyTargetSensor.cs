using RGS.GOAP.Core;
using Rokkan.Prophecy.Presentation;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    /// <summary>
    /// Publishes what the enemy can see into the blackboard, for beliefs to read.
    ///
    /// <para><b>It senses nothing itself.</b> <c>EnemyBrainHost</c> has already asked the
    /// simulation once this tick and this only copies the answer across. That split is deliberate:
    /// sensing twice would let the planner and the body disagree about where the target is, and
    /// sensing on the sensor's own interval would make an enemy's reactions depend on a cadence
    /// unrelated to the tick its combat runs on.</para>
    ///
    /// <para>It is also why nothing here raycasts. HopeFell's optical sensor casts against a live
    /// <c>PhysicsScene</c>; Prophecy's sight is <c>CollisionWorld.IsOccluded</c>, the same call an
    /// attack uses for cover, so an enemy can never see through a wall it cannot also hit
    /// through.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Target Sensor", fileName = "Sensor_Target")]
    public sealed class ProphecyTargetSensor : GoapSensorSO
    {
        public const string HasTarget = "HasTarget";
        public const string CanSeeTarget = "CanSeeTarget";
        public const string TargetDistanceX = "TargetDistanceX";
        public const string TargetDirection = "TargetDirection";

        public override SensorOutputConfig[] GetOutputs() => new[]
        {
            new SensorOutputConfig(HasTarget, GoapKeyType.Boolean),
            new SensorOutputConfig(CanSeeTarget, GoapKeyType.Boolean),
            new SensorOutputConfig(TargetDistanceX, GoapKeyType.Float),
            new SensorOutputConfig(TargetDirection, GoapKeyType.Integer),
        };

        public override void OnUpdate(GameObject agent, GoapSensorLocalSettings settings,
                                      GoapBlackboard blackboard)
        {
            if (agent == null || !agent.TryGetComponent(out EnemyBrainHost host)) return;

            var percept = host.Percept;

            WriteBool(settings, blackboard, HasTarget, percept.HasTarget);
            WriteBool(settings, blackboard, CanSeeTarget, percept.HasTarget && percept.HasLineOfSight);
            WriteFloat(settings, blackboard, TargetDistanceX,
                       percept.HasTarget ? percept.DistanceX : float.MaxValue);
            WriteInt(settings, blackboard, TargetDirection, percept.DirectionToTarget);
        }
    }
}

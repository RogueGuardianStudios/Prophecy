using RGS.GOAP.Core;
using Rokkan.Prophecy.Presentation;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    /// <summary>
    /// Publishes whether the way ahead is walkable — a wall, or a ledge to fall off.
    ///
    /// <para>Separate from the target sensor because it changes on a different rhythm and for
    /// different reasons: terrain is why a patrol turns round, and a patrolling enemy has no target
    /// to sense at all. Bundling them would make an enemy that cannot see also unable to walk.</para>
    ///
    /// <para><b>And in its own file, named for it.</b> Unity binds one MonoScript per file, to the
    /// type whose name matches it. While this class shared a file with the target sensor, an asset
    /// of it serialized with <c>m_Script: {fileID: 0}</c> — it existed, loaded as null, and was
    /// skipped by SensorController, so terrain was never sensed and nothing said so.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Terrain Sensor", fileName = "Sensor_Terrain")]
    public sealed class ProphecyTerrainSensor : GoapSensorSO
    {
        public const string BlockedAhead = "BlockedAhead";

        public override SensorOutputConfig[] GetOutputs() => new[]
        {
            new SensorOutputConfig(BlockedAhead, GoapKeyType.Boolean),
        };

        public override void OnUpdate(GameObject agent, GoapSensorLocalSettings settings,
                                      GoapBlackboard blackboard)
        {
            if (agent == null || !agent.TryGetComponent(out EnemyBrainHost host)) return;

            WriteBool(settings, blackboard, BlockedAhead, host.BlockedAhead);
        }
    }
}

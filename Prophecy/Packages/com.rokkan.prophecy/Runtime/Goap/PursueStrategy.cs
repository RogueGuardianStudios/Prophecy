using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    [Serializable]
    public sealed class PursueSettings : BaseStrategySettings
    {
        [Tooltip("Stop closing once within this horizontal distance, in metres. Should sit just " +
                 "inside the attack's reach so the swing lands rather than whiffs.")]
        public float StopWithin = 1.4f;

        [Tooltip("Hold station once close instead of finishing. For an enemy whose attack IS being " +
                 "there — a chaser — so the plan stays alive rather than completing and replanning " +
                 "every frame.")]
        public bool HoldPosition;
    }

    /// <summary>
    /// Close on the target until within reach. Succeeds when near enough, fails when there is
    /// nothing to chase — so the planner replans rather than walking at a memory forever.
    ///
    /// <para>One ScriptableObject per file, named for it — see <see cref="PatrolStrategy"/> for what
    /// happens otherwise.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Pursue", fileName = "Action_Pursue")]
    public sealed class PursueStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(PursueSettings);

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings) => TakeOver(context);

        public override GoapActionStatus OnUpdate(IGoapAgentContext context, GoapBlackboard blackboard,
                                                  float deltaTime, BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null) return GoapActionStatus.Failure;

            var percept = host.Percept;
            if (!percept.HasTarget)
            {
                host.Intent.MoveX = 0f;
                return GoapActionStatus.Failure;
            }

            var config = settings as PursueSettings;
            float stopWithin = config?.StopWithin ?? 1.4f;

            if (percept.DistanceX <= stopWithin)
            {
                // Stop moving either way. Driving at something already touching you does not push
                // it — characters do not collide in the sim — it walks THROUGH, whereupon the
                // direction to the target flips and it walks back. A chaser set to never stop
                // oscillated across its victim, landing contact damage from alternating sides.
                host.Intent.MoveX = 0f;

                return config != null && config.HoldPosition
                    ? GoapActionStatus.Running   // being here IS the attack; stay, do not replan
                    : GoapActionStatus.Success;
            }

            host.Intent.MoveX = percept.DirectionToTarget;
            return GoapActionStatus.Running;
        }

        public override void OnStop(IGoapAgentContext context, GoapBlackboard blackboard,
                                    BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host != null) host.Intent.MoveX = 0f;
        }
    }
}

using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    [Serializable]
    public sealed class KeepDistanceSettings : BaseStrategySettings
    {
        [Tooltip("Back away while the target is nearer than this, in metres. Should sit outside " +
                 "the player's own reach, or backing off just feeds them a free hit.")]
        public float PreferredRange = 6f;

        [Tooltip("Slack before it bothers moving again, in metres. Without it a caster jitters on " +
                 "the spot every time the target drifts a few centimetres.")]
        public float Hysteresis = 1.5f;
    }

    /// <summary>
    /// Backs away until there is room to shoot.
    ///
    /// <para><b>The counterpart to Pursue, and the reason a ranged enemy is not just a melee one
    /// with a longer arm.</b> Closing is the melee answer to everything; for a caster, distance
    /// <i>is</i> the weapon, so being approached is the threat and retreating is an attack of
    /// sorts. Without this a caster gets walked into and becomes a bad melee enemy.</para>
    ///
    /// <para>Succeeds once far enough, so the planner moves straight on to firing rather than
    /// treating retreat as a state to sit in.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Keep Distance", fileName = "Action_KeepDistance")]
    public sealed class KeepDistanceStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(KeepDistanceSettings);

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
                return GoapActionStatus.Success;   // nothing chasing it; the room it wanted, it has
            }

            var config = settings as KeepDistanceSettings;
            float preferred = config?.PreferredRange ?? 6f;
            float slack = Mathf.Max(0f, config?.Hysteresis ?? 1.5f);

            if (percept.DistanceX >= preferred)
            {
                host.Intent.MoveX = 0f;
                return GoapActionStatus.Success;
            }

            // Backing into a wall or off a ledge is worse than being close, so a blocked retreat
            // gives up and lets the planner find something else to do — usually firing anyway.
            if (host.BlockedAhead && Mathf.Abs(host.Intent.MoveX) > 0.01f)
            {
                host.Intent.MoveX = 0f;
                return GoapActionStatus.Failure;
            }

            host.Intent.MoveX = -percept.DirectionToTarget;

            // The slack is only read here, on the way out: once retreating it keeps going until it
            // is comfortably clear, rather than stopping the instant it crosses the line and being
            // back inside it a tick later.
            return percept.DistanceX >= preferred + slack
                ? GoapActionStatus.Success
                : GoapActionStatus.Running;
        }

        public override void OnStop(IGoapAgentContext context, GoapBlackboard blackboard,
                                    BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host != null) host.Intent.MoveX = 0f;
        }
    }
}

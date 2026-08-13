using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using Rokkan.Prophecy.Presentation;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    [Serializable]
    public sealed class SpringSettings : BaseStrategySettings
    {
        [Tooltip("Ticks the lunge drives forward before the action lets go. Short — a spring is a " +
                 "commitment, and a long one is just a chase with extra steps.")]
        public int LungeTicks = 20;

        [Tooltip("Jump as it springs. What makes an ambush read as a pounce rather than a walk.")]
        public bool Leap = true;
    }

    /// <summary>
    /// The ambush: sit still until something is close, then commit.
    ///
    /// <para><b>Why this is not Pursue with a shorter range.</b> Pursue steers — it re-reads the
    /// target every tick and follows. A spring is thrown once and does not correct, so moving out
    /// of its path beats it. That difference is the entire fantasy of an ambusher, and it lives in
    /// the fact that this action stops asking where the target is after the first tick.</para>
    ///
    /// <para>It succeeds when the lunge is spent rather than when it arrives, so a spring that
    /// misses ends and the planner decides what to do next — chase, or go back to lying in wait —
    /// instead of the action pretending it is still going somewhere.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Spring", fileName = "Action_Spring")]
    public sealed class SpringStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(SpringSettings);

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings)
        {
            var host = TakeOver(context);

            if (host == null) return;

            host.Scratch.StartedTick = long.MinValue;

            // Aimed once, here, and never revised. See the class note. Held on the host because
            // the strategy asset is shared by every ambusher in the scene.
            host.Scratch.Direction = host.Percept.HasTarget ? host.Percept.DirectionToTarget : 0;
        }

        public override GoapActionStatus OnUpdate(IGoapAgentContext context, GoapBlackboard blackboard,
                                                  float deltaTime, BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null) return GoapActionStatus.Failure;

            var config = settings as SpringSettings;
            int lunge = config?.LungeTicks ?? 20;
            bool leap = config?.Leap ?? true;

            long tick = host.CurrentTick;

            if (host.Scratch.StartedTick == long.MinValue)
            {
                if (host.Scratch.Direction == 0) return GoapActionStatus.Failure;   // nothing to spring at

                host.Scratch.StartedTick = tick;
                if (leap) host.Intent.PressJump();
            }

            host.Intent.MoveX = host.Scratch.Direction;

            return tick - host.Scratch.StartedTick >= lunge
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

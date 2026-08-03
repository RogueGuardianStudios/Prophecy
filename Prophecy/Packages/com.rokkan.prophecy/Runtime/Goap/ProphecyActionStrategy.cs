using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Burst;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim.AI;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    /// <summary>
    /// Shared plumbing for every Prophecy action: reach the body, and write intent.
    ///
    /// <para><b>The one rule these all obey.</b> A strategy writes <see cref="EnemyIntent"/> and
    /// nothing else. It never sets a position, never moves a transform, never calls an ability. The
    /// intent becomes an <c>InputFrame</c> and the simulation decides what that means — so a
    /// planned attack is subject to the same action lock, cancel window, cover check and i-frames a
    /// player's attack is.</para>
    ///
    /// <para>This is the line that erodes without a compiler to defend it. A strategy that moved
    /// the transform directly would look correct on screen and be outside every combat rule, with
    /// nothing failing to say so. If you are writing a new action and reaching for anything other
    /// than intent, that is the moment to stop.</para>
    /// </summary>
    public abstract class ProphecyActionStrategy : BaseGoapActionStrategy
    {
        /// <summary>The body's brain host, or null if this agent is not a Prophecy character.</summary>
        protected static EnemyBrainHost HostOf(IGoapAgentContext context)
        {
            var cached = context?.GetCapability<EnemyBrainHost>();
            if (cached != null) return cached;

            // Fall back to a lookup: an agent assembled at runtime may not have pre-cached.
            var go = context?.gameObject;
            return go != null && go.TryGetComponent(out EnemyBrainHost host) ? host : null;
        }

        /// <summary>
        /// Announce that a planner is driving, so the host's built-in loop stands down. Called on
        /// start rather than once at wake-up because a brain can be swapped or disabled at runtime,
        /// and an enemy whose planner stopped should fall back to patrolling rather than freeze.
        /// </summary>
        protected static EnemyBrainHost TakeOver(IGoapAgentContext context)
        {
            var host = HostOf(context);
            host?.DriveExternally(true);
            return host;
        }
    }

    // ---------------------------------------------------------------- patrol

    [Serializable]
    public sealed class PatrolSettings : BaseStrategySettings
    {
        [Tooltip("Which way to set off. Reversed whenever the way ahead is blocked.")]
        public int StartDirection = 1;
    }

    /// <summary>
    /// Walk until something stops you, then walk the other way. Never completes on its own — a
    /// patrol ends because a goal with a better score took over, which is the planner's business.
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Patrol", fileName = "Action_Patrol")]
    public sealed class PatrolStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(PatrolSettings);

        private int _direction = 1;

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings)
        {
            TakeOver(context);
            _direction = settings is PatrolSettings s && s.StartDirection < 0 ? -1 : 1;
        }

        public override GoapActionStatus OnUpdate(IGoapAgentContext context, GoapBlackboard blackboard,
                                                  float deltaTime, BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null) return GoapActionStatus.Failure;

            if (host.BlockedAhead) _direction = -_direction;

            host.Intent.MoveX = _direction;
            host.Intent.Guard = false;

            return GoapActionStatus.Running;
        }

        public override void OnStop(IGoapAgentContext context, GoapBlackboard blackboard,
                                    BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host != null) host.Intent.MoveX = 0f;
        }
    }

    // ---------------------------------------------------------------- pursue

    [Serializable]
    public sealed class PursueSettings : BaseStrategySettings
    {
        [Tooltip("Stop closing once within this horizontal distance, in metres. Should sit just " +
                 "inside the attack's reach so the swing lands rather than whiffs.")]
        public float StopWithin = 1.4f;
    }

    /// <summary>
    /// Close on the target until within reach. Succeeds when near enough, fails when there is
    /// nothing to chase — so the planner replans rather than walking at a memory forever.
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

            float stopWithin = settings is PursueSettings s ? s.StopWithin : 1.4f;

            if (percept.DistanceX <= stopWithin)
            {
                host.Intent.MoveX = 0f;
                return GoapActionStatus.Success;
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

    // ---------------------------------------------------------------- attack

    [Serializable]
    public sealed class SwingSettings : BaseStrategySettings
    {
        [Tooltip("Horizontal reach, in metres. Beyond this the action fails and the planner closes " +
                 "again rather than swinging at air.")]
        public float Reach = 1.4f;

        [Tooltip("Ticks to wait after the swing before this action reports success. Long enough " +
                 "that the planner does not immediately queue another mid-recovery.")]
        public int RecoveryTicks = 45;
    }

    /// <summary>
    /// Swing once, then wait out the recovery.
    ///
    /// <para>It presses the button and lets the simulation own everything after that. Whether the
    /// swing connects, is blocked, is parried or is interrupted by a hit-react is not this action's
    /// business — which is the point of pressing buttons rather than dealing damage.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Swing", fileName = "Action_Swing")]
    public sealed class SwingStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(SwingSettings);

        private long _swungOnTick = long.MinValue;

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings)
        {
            TakeOver(context);
            _swungOnTick = long.MinValue;
        }

        public override GoapActionStatus OnUpdate(IGoapAgentContext context, GoapBlackboard blackboard,
                                                  float deltaTime, BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null) return GoapActionStatus.Failure;

            var config = settings as SwingSettings;
            float reach = config?.Reach ?? 1.4f;
            int recovery = config?.RecoveryTicks ?? 45;

            var percept = host.Percept;
            long tick = CurrentTick(host);

            if (_swungOnTick == long.MinValue)
            {
                if (!percept.HasTarget || percept.DistanceX > reach)
                    return GoapActionStatus.Failure;   // it moved; close again rather than whiff

                // Stop dead first. Walking into the target while swinging shoves it out of the
                // reach the attack was authored for, and the hit misses invisibly.
                host.Intent.MoveX = 0f;
                host.Intent.PressAttack();
                _swungOnTick = tick;

                return GoapActionStatus.Running;
            }

            host.Intent.MoveX = 0f;

            return tick - _swungOnTick >= recovery
                ? GoapActionStatus.Success
                : GoapActionStatus.Running;
        }

        private static long CurrentTick(EnemyBrainHost host)
        {
            var body = host.GetComponent<PlayerCharacterHost>();
            return body?.Sim?.CurrentTick ?? 0L;
        }
    }
}

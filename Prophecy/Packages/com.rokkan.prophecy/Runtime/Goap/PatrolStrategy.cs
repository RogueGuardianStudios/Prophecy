using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    [Serializable]
    public sealed class PatrolSettings : BaseStrategySettings
    {
        [Tooltip("Which way to set off. Reversed whenever the way ahead is blocked.")]
        public int StartDirection = 1;
    }

    /// <summary>
    /// Walk until something stops you, then walk the other way. Never completes on its own — a
    /// patrol ends because a goal with a better score took over, which is the planner's business.
    ///
    /// <para>Alone in this file, and named for it, because Unity binds one MonoScript per file — to
    /// the type whose name matches. A ScriptableObject sharing a file with another type gets
    /// <c>m_Script: {fileID: 0}</c> when an asset of it is created: the asset exists, loads as null,
    /// and is dropped from the planner's action graph without a word. See
    /// <see cref="ProphecyActionStrategy"/> for the shared base, which is abstract and so needs no
    /// binding of its own.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Patrol", fileName = "Action_Patrol")]
    public sealed class PatrolStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(PatrolSettings);

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings)
        {
            var host = TakeOver(context);

            // Per agent, not per asset: one _direction field here turned every patrol in the level
            // into a single shared heading.
            if (host != null)
                host.Scratch.Direction = settings is PatrolSettings s && s.StartDirection < 0 ? -1 : 1;
        }

        public override GoapActionStatus OnUpdate(IGoapAgentContext context, GoapBlackboard blackboard,
                                                  float deltaTime, BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null) return GoapActionStatus.Failure;

            if (host.BlockedAhead) host.Scratch.Direction = -host.Scratch.Direction;

            host.Intent.MoveX = host.Scratch.Direction;
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
}

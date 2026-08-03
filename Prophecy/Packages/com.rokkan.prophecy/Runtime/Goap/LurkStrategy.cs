using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    [Serializable]
    public sealed class LurkSettings : BaseStrategySettings
    {
        [Tooltip("Face the target while waiting. Off for something that should look inert until it " +
                 "moves — a trap rather than a creature.")]
        public bool WatchTarget = true;
    }

    /// <summary>
    /// Waits. Deliberately.
    ///
    /// <para><b>Doing nothing has to be an action, or the planner has nothing to plan.</b> A goal
    /// with no reachable action is a planning failure, and an enemy whose only behaviour is
    /// conditional — an ambusher before anything walks past — would spend its whole dormant life
    /// reporting that it cannot make a plan. This gives idling a name so that idling is a choice.</para>
    ///
    /// <para>It never completes on its own; a lurk ends because something better outranked it.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Lurk", fileName = "Action_Lurk")]
    public sealed class LurkStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(LurkSettings);

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings) => TakeOver(context);

        public override GoapActionStatus OnUpdate(IGoapAgentContext context, GoapBlackboard blackboard,
                                                  float deltaTime, BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null) return GoapActionStatus.Failure;

            host.Intent.MoveX = 0f;
            host.Intent.Guard = false;

            return GoapActionStatus.Running;
        }
    }
}

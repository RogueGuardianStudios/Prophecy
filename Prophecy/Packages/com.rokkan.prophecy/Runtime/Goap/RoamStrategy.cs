using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim.AI;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    [Serializable]
    public sealed class RoamSettings : BaseStrategySettings
    {
        [Tooltip("Ticks one heading is held before re-rolling. Shorter is jitterier, longer is " +
                 "more purposeful. 90 is a beat and a half of walking.")]
        public int RepickTicks = 90;

        [Tooltip("0..1: how much each roll bends toward a seen target. Zero is a screensaver, " +
                 "one is a homing missile; Zelda II lives in the middle.")]
        public float BiasTowardTarget = 0.55f;

        [Tooltip("Fraction of full speed. Below 1 so the player can outrun what is menacing " +
                 "them — an overworld you cannot cross is a corridor with scenery.")]
        public float MoveScale = 0.75f;
    }

    /// <summary>
    /// Wander the plane, weighted toward whatever can be seen. The overworld's whole vocabulary.
    ///
    /// <para><b>The first two-axis action.</b> Everything else here steers with <c>MoveX</c>
    /// because a lane only has left and right; a roamer writes <c>MoveY</c> too, and the sim's
    /// <c>TopDownMove</c> does the rest. The heading itself comes from
    /// <see cref="RoamSteering"/> — pure, stateless, hash-seeded — because a strategy is a shared
    /// asset and per-agent wander state on it would be the isolation trap with a new face.</para>
    ///
    /// <para>It never succeeds, like the chaser's held pursuit: roaming is not a task with an end,
    /// it is what this creature does. The plan stays alive until something preempts it.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Roam", fileName = "Action_Roam")]
    public sealed class RoamStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(RoamSettings);

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings) => TakeOver(context);

        public override GoapActionStatus OnUpdate(IGoapAgentContext context, GoapBlackboard blackboard,
                                                  float deltaTime, BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null) return GoapActionStatus.Failure;

            var body = host.GetComponent<PlayerCharacterHost>();
            var sim = body != null ? body.Sim : null;
            if (sim == null) return GoapActionStatus.Failure;

            var config = settings as RoamSettings;
            int repick = config?.RepickTicks ?? 90;
            float bias = config?.BiasTowardTarget ?? 0.55f;
            float scale = Mathf.Clamp01(config?.MoveScale ?? 0.75f);

            var percept = host.Percept;
            var toTarget = percept.HasTarget
                ? percept.TargetCentre - sim.State.Position
                : Vector2.zero;

            var heading = RoamSteering.Heading(sim.State.CombatId, sim.CurrentTick, repick,
                                               toTarget, bias);

            host.Intent.MoveX = heading.x * scale;
            host.Intent.MoveY = heading.y * scale;

            return GoapActionStatus.Running;   // roaming is what it does, not a task it finishes
        }

        public override void OnStop(IGoapAgentContext context, GoapBlackboard blackboard,
                                    BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null) return;

            host.Intent.MoveX = 0f;
            host.Intent.MoveY = 0f;
        }
    }
}

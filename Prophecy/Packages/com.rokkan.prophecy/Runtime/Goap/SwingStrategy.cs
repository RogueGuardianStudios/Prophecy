using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using Rokkan.Prophecy.Presentation;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
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
    ///
    /// <para>One ScriptableObject per file, named for it — see <see cref="PatrolStrategy"/> for what
    /// happens otherwise.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Swing", fileName = "Action_Swing")]
    public sealed class SwingStrategy : ProphecyActionStrategy
    {
        public override Type GetSettingsType() => typeof(SwingSettings);

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings)
        {
            var host = TakeOver(context);

            // On the HOST, not on this asset. See EnemyBrainHost.ActionScratch: a field here is
            // shared by every enemy in the game, and the second one to swing inherits the first
            // one's timestamp.
            if (host != null) host.Scratch.StartedTick = long.MinValue;
        }

        public override GoapActionStatus OnUpdate(IGoapAgentContext context, GoapBlackboard blackboard,
                                                  float deltaTime, BaseStrategySettings settings)
        {
            var host = HostOf(context);
            if (host == null)
            {
#if UNITY_EDITOR
                Debug.Log("[Prophecy][GOAP] Swing refused: no EnemyBrainHost reachable from the context.");
#endif
                return GoapActionStatus.Failure;
            }

            var config = settings as SwingSettings;
            float reach = config?.Reach ?? 1.4f;
            int recovery = config?.RecoveryTicks ?? 45;

            var percept = host.Percept;
            long tick = CurrentTick(host);

            if (host.Scratch.StartedTick == long.MinValue)
            {
                if (!percept.HasTarget || percept.DistanceX > reach)
                {
#if UNITY_EDITOR
                    // "An action returned Failure" names no reason, and the two reasons here are
                    // very different problems. Tagged GOAP so the trace probe picks it up.
                    Debug.Log($"[Prophecy][GOAP] Swing refused: hasTarget={percept.HasTarget} " +
                              $"dist={percept.DistanceX:F2} reach={reach:F2} " +
                              $"settings={(settings == null ? "NULL" : settings.GetType().Name)}");
#endif
                    return GoapActionStatus.Failure;   // it moved; close again rather than whiff
                }

                // Stop dead first. Walking into the target while swinging shoves it out of the
                // reach the attack was authored for, and the hit misses invisibly.
                host.Intent.MoveX = 0f;
                host.Intent.PressAttack();
                host.Scratch.StartedTick = tick;

                return GoapActionStatus.Running;
            }

            host.Intent.MoveX = 0f;


            return tick - host.Scratch.StartedTick >= recovery
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

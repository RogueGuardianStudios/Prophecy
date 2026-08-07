using System;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim.Abilities;
using UnityEngine;

namespace Rokkan.Prophecy.Goap
{
    [Serializable]
    public sealed class SwingSettings : BaseStrategySettings
    {
        [Tooltip("Horizontal reach, in metres. Beyond this the action fails and the planner closes " +
                 "again rather than swinging at air.")]
        public float Reach = 1.4f;
    }

    /// <summary>
    /// Swing once — for real. Press the button, then WATCH the simulation's attack module
    /// until the swing it started ends; success means "it swung", never "a timer ran out".
    ///
    /// <para><b>The blind timer this replaces was the rapid fire.</b> The old settings carried
    /// a RecoveryTicks reasoned against the player's 22-tick move; the enemy's move later grew
    /// to 40 ticks and the number never moved, leaving ~5 ticks of neutral per cycle. The
    /// PACING now belongs to the fight's <c>AttackDirector</c> — the beat between attack
    /// starts is granted as a token, floored at the move's own length — and this action's only
    /// timing job is honesty: report what the body actually did.</para>
    ///
    /// <para>Whether the swing connects, is blocked or is parried is still not this action's
    /// business. But whether it RAN is — a press stripped by the pacing gate or eaten by a
    /// stun fails here, so the planner falls back instead of believing a swing it never threw.</para>
    ///
    /// <para>One ScriptableObject per file, named for it — see <see cref="PatrolStrategy"/> for what
    /// happens otherwise.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/GOAP/Action - Swing", fileName = "Action_Swing")]
    public sealed class SwingStrategy : ProphecyActionStrategy
    {
        /// <summary>Ticks to keep believing in a pressed-but-unseen attack: the sim's input
        /// buffer (10) plus handoff slack. Past this, the press was stripped or refused.</summary>
        private const int PressPatienceTicks = 12;

        public override Type GetSettingsType() => typeof(SwingSettings);

        public override void OnStart(IGoapAgentContext context, GoapBlackboard blackboard,
                                     BaseStrategySettings settings)
        {
            var host = TakeOver(context);

            // On the HOST, not on this asset. See EnemyBrainHost.ActionScratch: a field here is
            // shared by every enemy in the game, and the second one to swing inherits the first
            // one's timestamp.
            if (host == null) return;
            host.Scratch.StartedTick = long.MinValue;
            host.Scratch.AttackObserved = false;
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

                // The plan was made with a token; the token can have lapsed between planning
                // and this frame. The press gate downstream is the authoritative veto — this
                // is just failing fast so the planner re-decides instead of waiting out a
                // press that cannot survive.
                if (!host.HasAttackToken) return GoapActionStatus.Failure;

                // Face the target BEFORE pressing.
                //
                // An attack is aimed by the attacker's facing — a hit box is mirrored by it and a
                // projectile is launched along it — and facing follows the movement axis, which
                // means whichever way the body last walked. A caster that has just backed away is
                // facing away from what it is shooting at, so its bolt leaves in the direction it
                // retreated. The grunt never showed this because closing and swinging point the
                // same way; the moment an action moves AWAY and then attacks, they do not.
                //
                // Turning costs one tick and about seven centimetres of drift, because the only
                // lever the AI has is the same axis a player would push. That is the price of the
                // AI pressing buttons rather than setting state, and it is worth paying.
                int toTarget = percept.DirectionToTarget;

                if (toTarget != 0 && FacingOf(host) != toTarget)
                {
                    host.Intent.MoveX = toTarget;
                    return GoapActionStatus.Running;
                }

                // Stop dead first. Walking into the target while swinging shoves it out of the
                // reach the attack was authored for, and the hit misses invisibly.
                host.Intent.MoveX = 0f;
                host.Intent.PressAttack();
                host.Scratch.StartedTick = tick;

                return GoapActionStatus.Running;
            }

            host.Intent.MoveX = 0f;

            // The closed loop: watch the module, not a clock.
            var attack = AttackOf(host);
            if (attack != null && attack.IsAttacking)
            {
                host.Scratch.AttackObserved = true;
                return GoapActionStatus.Running;
            }

            if (host.Scratch.AttackObserved)
                return GoapActionStatus.Success;   // it swung and the swing has ended

            // Pressed but nothing ever ran: the pacing gate stripped it, a lock refused it,
            // or a stun ate it. Fail so the planner falls back — never count a swing that
            // was not thrown.
            return tick - host.Scratch.StartedTick > PressPatienceTicks
                ? GoapActionStatus.Failure
                : GoapActionStatus.Running;
        }

        /// <summary>Which way the body is pointing, or 0 if it has no simulation yet.</summary>
        private static int FacingOf(EnemyBrainHost host)
        {
            var body = host.GetComponent<PlayerCharacterHost>();
            return body?.Sim?.State?.Facing ?? 0;
        }

        private static long CurrentTick(EnemyBrainHost host)
        {
            var body = host.GetComponent<PlayerCharacterHost>();
            return body?.Sim?.CurrentTick ?? 0L;
        }

        private static AttackModule AttackOf(EnemyBrainHost host)
        {
            var body = host.GetComponent<PlayerCharacterHost>();
            return body?.Sim?.Get<AttackModule>();
        }
    }
}

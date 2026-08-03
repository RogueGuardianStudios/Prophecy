using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Shows the wind-up of a simulated attack, so an incoming swing can be read and answered.
    ///
    /// <para><b>The information already existed; nothing was showing it.</b>
    /// <see cref="AttackTimeline.Phase.Startup"/> is documented as "the telegraph — nothing is
    /// dangerous yet", and every attack in <c>CombatTuning</c> is authored with
    /// <c>Defeats: None</c>, meaning block, parry and i-frames all work against it. But a defence
    /// you cannot time is not a defence: without a visible startup the only way to block is to
    /// guess, and an enemy that hits you looks identical to one that was unblockable.</para>
    ///
    /// <para><b>Reads the sim, decides nothing.</b> The phase comes from the fixed-tick timeline
    /// that already governs when the hit lands, so the colour cannot drift out of step with the
    /// danger — which is exactly what would happen if this were driven from an animation event.
    /// This component only picks a colour.</para>
    ///
    /// <para>Colours match <see cref="TrainingAttacker"/> on purpose: the arena's scripted
    /// attackers and a real planning enemy should read the same way, or the training does not
    /// transfer.</para>
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    public sealed class AttackTelegraph : MonoBehaviour
    {
        [SerializeField, Tooltip("The simulated body whose attacks this shows. Found on this " +
                                 "object when empty.")]
        private PlayerCharacterHost _host;

        [SerializeField, Tooltip("Whose tint to drive. Found on this object when empty.")]
        private Combatant _combatant;

        [SerializeField, Tooltip("Wind-up. The window where a block or parry can still be timed.")]
        private Color _windUpColour = new Color(1f, 0.78f, 0.25f);

        [SerializeField, Tooltip("Committed. Hit boxes are live this frame.")]
        private Color _activeColour = new Color(1f, 0.2f, 0.12f);

        private AttackModule _attack;

        private void Awake()
        {
            if (_host == null) _host = GetComponent<PlayerCharacterHost>();
            if (_combatant == null) _combatant = GetComponent<Combatant>();
        }

        // Late, so the tint is chosen from the state this frame's tick left behind rather than
        // from the previous one.
        private void LateUpdate()
        {
            if (_combatant == null) return;

            var timeline = ResolveTimeline();
            if (timeline == null || !timeline.IsArmed)
            {
                _combatant.TelegraphTint = null;
                return;
            }

            switch (timeline.CurrentPhase)
            {
                case AttackTimeline.Phase.Startup:
                    // Ramps toward the active colour rather than holding flat, so the wind-up reads
                    // as time running out instead of as a state that might last forever.
                    var definition = timeline.Definition;
                    int startup = Mathf.Max(1, definition.StartupRange.Duration);
                    float through = Mathf.Clamp01(timeline.ElapsedTicks / (float)startup);

                    _combatant.TelegraphTint = Color.Lerp(_windUpColour, _activeColour,
                                                          through * through);
                    break;

                case AttackTimeline.Phase.Active:
                    _combatant.TelegraphTint = _activeColour;
                    break;

                default:
                    // Recovery is the punish window, and colouring it would suggest otherwise.
                    _combatant.TelegraphTint = null;
                    break;
            }
        }

        /// <summary>
        /// The attack module's timeline, or null when this body has no simulation yet.
        ///
        /// <para>Looked up once and cached: the module list is fixed after the sim is built, and
        /// this runs every frame.</para>
        /// </summary>
        private AttackTimeline ResolveTimeline()
        {
            var sim = _host != null ? _host.Sim : null;
            if (sim == null) return null;

            if (_attack == null)
            {
                var modules = sim.Modules;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (modules[i] is AttackModule module)
                    {
                        _attack = module;
                        break;
                    }
                }
            }

            return _attack?.Runner?.Timeline;
        }
    }
}

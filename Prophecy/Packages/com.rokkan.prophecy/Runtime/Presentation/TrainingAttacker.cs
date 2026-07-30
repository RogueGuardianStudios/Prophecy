using System.Collections.Generic;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// A dummy that swings back, on a fixed cycle, so defence can be practised rather than
    /// described.
    ///
    /// <para><b>The whole point is the telegraph.</b> A defensive system is untestable by feel
    /// until something is attacking you on a rhythm you can learn — and learning it means the
    /// startup phase has to be long enough to read and identical every time. So the cycle is in
    /// ticks, the attack is an ordinary <see cref="AttackDefinition"/>, and it runs on the same
    /// <see cref="AttackTimeline"/> and <see cref="HitResolver"/> the player does. There is no
    /// second combat implementation here to drift out of step with the first.</para>
    ///
    /// <para><b>No <see cref="CharacterSim"/>, deliberately.</b> This does not move, fall, or
    /// decide anything — it swings on a timer. Giving it a full character sim would be ceremony,
    /// and everything that makes the swing correct is plain C# already. When real enemies arrive
    /// they will own a sim and drive the same timeline from an AI decision instead of a
    /// counter.</para>
    ///
    /// <para>It is a combat participant, so it takes its identity from the sibling
    /// <see cref="Combatant"/> — one id for the thing that hits and the thing that is hit.</para>
    /// </summary>
    [DefaultExecutionOrder(-90)]
    [RequireComponent(typeof(Combatant))]
    public sealed class TrainingAttacker : MonoBehaviour, ISimSystem
    {
        [SerializeField, Tooltip("Leave empty to find one in the scene.")]
        private SimClockDriver _clockDriver;

        [Header("Cycle")]
        [SerializeField, Tooltip("Ticks of stillness between swings. The rhythm the player learns.")]
        private int _restTicks = 90;

        [SerializeField, Tooltip("Ticks before the first swing, so several attackers in one arena " +
                                 "do not all fire on the same tick.")]
        private int _openingDelayTicks;

        [SerializeField, Tooltip("Turn to face the nearest thing it can hit before each swing.")]
        private bool _faceNearestTarget = true;

        [SerializeField, Tooltip("Which way it swings when it is not turning to face anyone.")]
        private int _facing = -1;

        [Header("Telegraph")]
        [SerializeField, Tooltip("Body colour during the wind-up. Ramps toward the active colour " +
                                 "as the swing approaches, so the tell is a build rather than a flick.")]
        private Color _windUpColour = new Color(1f, 0.78f, 0.25f);

        [SerializeField, Tooltip("Body colour on the ticks the attack is actually dangerous.")]
        private Color _activeColour = new Color(1f, 0.2f, 0.12f);

        [Header("The swing")]
        [SerializeField]
        private AttackDefinition _attack = new AttackDefinition
        {
            Id = "dummy_swing",
            StartupTicks = 34,
            ActiveTicks = 6,
            RecoveryTicks = 26,
        };

        private Combatant _self;
        private readonly AttackTimeline _timeline = new AttackTimeline();
        private readonly HitSweep _sweep = new HitSweep();

        private SimClockDriver _registeredWith;
        private long _nextArmTick = long.MinValue;
        private int _resolvedFacing = -1;

        /// <summary>The swing being run, for the overlay.</summary>
        public AttackTimeline Timeline => _timeline;

        public AttackDefinition Attack => _attack;

        public bool IsSwinging => _timeline.IsArmed;

        /// <summary>Ticks until the next swing begins. Zero while one is running.</summary>
        public int TicksUntilSwing(long currentTick) =>
            !_timeline.IsArmed && _nextArmTick > currentTick ? (int)(_nextArmTick - currentTick) : 0;

        private void Awake()
        {
            _self = GetComponent<Combatant>();
            _resolvedFacing = _facing < 0 ? -1 : 1;
        }

        private void Start()
        {
            _registeredWith = SimClockDriver.RegisterWithScene(_clockDriver, this, this);
            _nextArmTick = (_registeredWith != null ? _registeredWith.Clock.CurrentTick : 0) +
                           Mathf.Max(0, _openingDelayTicks);
        }

        private void OnDestroy()
        {
            if (_registeredWith != null && _registeredWith.Clock != null)
                _registeredWith.Clock.Unregister(this);
        }

        public void Tick(in SimTickInfo info)
        {
            var director = CombatDirector.Instance;
            if (director == null || _self == null) return;

            // A dead dummy stops swinging and restarts its cycle when it revives, rather than
            // resuming mid-wind-up into an attack the player never saw begin.
            if (!_self.IsAlive)
            {
                _timeline.Disarm();
                _self.TelegraphTint = null;
                _nextArmTick = info.Tick + _restTicks;
                return;
            }

            if (!_timeline.IsArmed)
            {
                if (_self != null) _self.TelegraphTint = null;

                if (info.Tick < _nextArmTick) return;

                if (_faceNearestTarget) FaceNearest(director);

                _sweep.Begin();
                _timeline.Arm(_attack);
                _timeline.Advance();
                Telegraph();
                return;
            }

            _timeline.Advance();
            Telegraph();

            if (_timeline.IsComplete)
            {
                _timeline.Disarm();
                _nextArmTick = info.Tick + Mathf.Max(1, _restTicks);
                return;
            }

            LaunchSpawns(director, in info);
            Resolve(director, in info);
        }

        /// <summary>
        /// Colour the body by what the swing is doing.
        ///
        /// <para><b>The only tell that survives the overlay being off.</b> Hit volumes are drawn by
        /// a debug overlay, which is exactly the wrong place for the one piece of information a
        /// player has to read to survive — so the wind-up ramps the body toward the attack colour
        /// and the active frames snap to it. Placeholder art doing the job real art will: an enemy
        /// that gives no sign it is about to hit you is not a fight, it is a coin toss.</para>
        /// </summary>
        private void Telegraph()
        {
            if (_self == null) return;

            switch (_timeline.CurrentPhase)
            {
                case AttackTimeline.Phase.Startup:
                    float through = _attack.StartupTicks > 0
                        ? Mathf.Clamp01(_timeline.ElapsedTicks / (float)_attack.StartupTicks)
                        : 1f;
                    _self.TelegraphTint = Color.Lerp(_windUpColour, _activeColour, through * through);
                    break;

                case AttackTimeline.Phase.Active:
                    _self.TelegraphTint = _activeColour;
                    break;

                default:
                    _self.TelegraphTint = null;
                    break;
            }
        }

        private void FaceNearest(CombatDirector director)
        {
            var targets = director.Hurtboxes;
            var here = SpaceMapping.ToPlane(transform.position, director.Space);

            float best = float.MaxValue;
            int facing = _resolvedFacing;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.OwnerId == _self.CombatId) continue;
                if (target.Team != 0 && target.Team == _self.Team) continue;

                float dx = target.Centre.x - here.x;
                float distance = Mathf.Abs(dx);
                if (distance >= best) continue;

                best = distance;
                facing = dx < 0f ? -1 : 1;
            }

            _resolvedFacing = facing;
        }

        /// <summary>
        /// Put this tick's projectiles and areas in the air.
        ///
        /// <para>The same five lines the attack module has, for the same reason it has them — and
        /// a dummy that could swing but not cast would make half the arena impossible to build.
        /// When enemies own a real <c>CharacterSim</c> this goes away with the rest of this class.</para>
        /// </summary>
        private void LaunchSpawns(CombatDirector director, in SimTickInfo info)
        {
            if (_attack?.Spawns == null || _attack.Spawns.Length == 0) return;

            int elapsed = _timeline.ElapsedTicks;
            var here = SpaceMapping.ToPlane(transform.position, director.Space);

            for (int i = 0; i < _attack.Spawns.Length; i++)
            {
                if (_attack.Spawns[i].Tick != elapsed) continue;
                if (_attack.Spawns[i].Projectile == null) continue;

                var attacker = Attacker.FromBody(_self.CombatId, here, new Vector2(0.9f, 1.8f),
                                                 _resolvedFacing, _self.Team);

                director.State.Spawn(_attack.Spawns[i].Projectile, attacker);
            }
        }

        private void Resolve(CombatDirector director, in SimTickInfo info)
        {
            int boxes = _timeline.HitBoxCount;
            if (boxes == 0) return;

            if (director.Hurtboxes.Count == 0) return;

            var here = SpaceMapping.ToPlane(transform.position, director.Space);
            var attacker = Attacker.FromBody(_self.CombatId, here, new Vector2(0.9f, 1.8f),
                                             _resolvedFacing, _self.Team);

            for (int i = 0; i < boxes; i++)
            {
                if (!_timeline.IsHitBoxLive(i)) continue;

                var result = _sweep.Sweep(i, _timeline.GetHitBox(i), attacker, director.State, null,
                                          info.Tick, _attack.Id);

                if (!result.Punished) continue;

                // Parried. The swing dies here and the next one is pushed back by the stun, which
                // is the reward the player just earned — the opening is the point, not the damage
                // they did not take.
                _timeline.Disarm();
                _nextArmTick = info.Tick + result.AttackerStunTicks + Mathf.Max(1, _restTicks);
                return;
            }
        }
    }
}

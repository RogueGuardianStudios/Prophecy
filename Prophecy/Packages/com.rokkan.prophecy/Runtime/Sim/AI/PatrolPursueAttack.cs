using System;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.AI
{
    /// <summary>What the brain is currently trying to do.</summary>
    public enum EnemyBehaviour
    {
        Patrol,
        Pursue,
        Attack,
    }

    /// <summary>Numbers a designer turns. Plain data so the sim can hold it.</summary>
    [Serializable]
    public class EnemyBrainTuning
    {
        [Tooltip("How far the enemy can notice anything, in metres.")]
        public float SightRange = 12f;

        [Tooltip("Horizontal distance at which it stops closing and starts swinging. Should be a " +
                 "little inside the attack's actual reach so the swing lands rather than whiffs.")]
        public float AttackRange = 1.4f;

        [Tooltip("Distance at which a pursuing enemy gives up and goes back to patrol. Larger than " +
                 "SightRange on purpose — equal values make an enemy at the edge of vision flicker " +
                 "between chasing and patrolling every time the target twitches.")]
        public float LoseInterestRange = 16f;

        [Tooltip("Ticks between swings once in range. The rhythm a player learns to read.")]
        public int AttackCooldownTicks = 90;

        [Tooltip("Ticks to keep chasing after losing sight, before giving up. Zero forgets the " +
                 "instant a pillar passes between you, which reads as blindness rather than as AI.")]
        public int MemoryTicks = 120;
    }

    /// <summary>
    /// Patrol until something is seen, close on it, swing when near enough.
    ///
    /// <para><b>Pure, and deliberately not a GOAP plan.</b> This is the behaviour every enemy needs
    /// and the one a planner would spend its whole search rediscovering. Keeping it as a plain
    /// state machine means it can be stepped in a test with no scene, no ScriptableObjects and no
    /// planner — and it gives GOAP something already working to be measured against, which is the
    /// only honest way to answer whether the planner earns its place for a given enemy.</para>
    ///
    /// <para>It writes <see cref="EnemyIntent"/> and nothing else. It never moves the character,
    /// never calls an ability, never touches a transform — see <c>IInputSource</c> for why that
    /// line matters and how quietly it erodes.</para>
    /// </summary>
    public sealed class PatrolPursueAttack
    {
        private readonly EnemyBrainTuning _tuning;

        private int _patrolDirection = 1;
        private long _lastAttackTick = long.MinValue;
        private long _lastSeenTick = long.MinValue;
        private int _rememberedDirection;

        public PatrolPursueAttack(EnemyBrainTuning tuning = null)
        {
            _tuning = tuning ?? new EnemyBrainTuning();
        }

        /// <summary>What it decided last tick. For the overlay and for tests.</summary>
        public EnemyBehaviour Behaviour { get; private set; } = EnemyBehaviour.Patrol;

        /// <summary>Which way a patrol is currently walking.</summary>
        public int PatrolDirection => _patrolDirection;

        /// <summary>Start a patrol facing this way.</summary>
        public void FacePatrol(int direction) => _patrolDirection = direction < 0 ? -1 : 1;

        /// <summary>
        /// Decide one tick, writing the result into <paramref name="intent"/>.
        /// </summary>
        /// <param name="canTurnBack">
        /// Whether a wall or a ledge blocks the way ahead. Supplied rather than sensed here so this
        /// stays free of the collision world and therefore testable on its own.
        /// </param>
        public void Tick(in Percept percept, EnemyIntent intent, long tick, bool canTurnBack)
        {
            if (intent == null) return;

            if (percept.HasTarget && percept.HasLineOfSight)
            {
                _lastSeenTick = tick;
                _rememberedDirection = percept.DirectionToTarget;
            }

            bool remembers = _lastSeenTick != long.MinValue &&
                             tick - _lastSeenTick <= _tuning.MemoryTicks;

            // Out of mind entirely, or wandered past the give-up distance. The two are separate:
            // one is "I cannot see you", the other "you are not worth following".
            bool tooFar = percept.HasTarget && percept.Distance > _tuning.LoseInterestRange;

            if (!remembers || tooFar)
            {
                Patrol(intent, canTurnBack);
                return;
            }

            if (percept.HasTarget && percept.DistanceX <= _tuning.AttackRange && percept.HasLineOfSight)
            {
                Attack(percept, intent, tick);
                return;
            }

            Pursue(percept, intent);
        }

        private void Patrol(EnemyIntent intent, bool canTurnBack)
        {
            Behaviour = EnemyBehaviour.Patrol;

            if (canTurnBack) _patrolDirection = -_patrolDirection;

            intent.MoveX = _patrolDirection;
            intent.Guard = false;
        }

        private void Pursue(in Percept percept, EnemyIntent intent)
        {
            Behaviour = EnemyBehaviour.Pursue;

            // Chase the remembered direction when sight is lost, rather than stopping dead. An
            // enemy that froze the instant you broke line of sight would read as switched off.
            int direction = percept.HasTarget ? percept.DirectionToTarget : _rememberedDirection;
            if (direction == 0) direction = _patrolDirection;

            intent.MoveX = direction;
            intent.Guard = false;

            // Keep the patrol heading in step, so giving up resumes walking the way it was already
            // going instead of snapping about.
            _patrolDirection = direction;
        }

        private void Attack(in Percept percept, EnemyIntent intent, long tick)
        {
            Behaviour = EnemyBehaviour.Attack;

            // Stop closing. Walking into the target while swinging pushes it out of the reach the
            // swing was authored for, and the hit whiffs for reasons nobody can see.
            intent.MoveX = 0f;
            _patrolDirection = percept.DirectionToTarget;

            bool ready = _lastAttackTick == long.MinValue ||
                         tick - _lastAttackTick >= _tuning.AttackCooldownTicks;

            if (!ready) return;

            _lastAttackTick = tick;
            intent.PressAttack();
        }

        /// <summary>Forget everything. On death, respawn, or being pooled.</summary>
        public void Reset()
        {
            Behaviour = EnemyBehaviour.Patrol;
            _lastAttackTick = long.MinValue;
            _lastSeenTick = long.MinValue;
            _rememberedDirection = 0;
        }
    }
}

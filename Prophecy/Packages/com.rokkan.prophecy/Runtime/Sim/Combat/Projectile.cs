using System;
using System.Collections.Generic;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// A hit volume that outlives the swing that made it: an arrow, a shockwave, a lingering
    /// flame.
    ///
    /// <para><b>One type serves projectiles and area attacks, because they differ by two numbers.</b>
    /// A bolt is a small box with speed and no growth; a shockwave is a wide box with no speed that
    /// grows; an expanding wave of fire is both. Splitting them would mean two lifetimes, two
    /// resolution loops and two places for the dedup rule to drift — and the interesting authoring
    /// space is the middle, which a split would make unreachable.</para>
    ///
    /// <para><b>It is not attached to whoever fired it.</b> That is the whole point: the attacker
    /// can be hit, stunned or killed while it is in the air, and the attack still lands. The owner
    /// is carried as an id and a team so friendly fire and self-hits stay impossible, not as a
    /// reference to something that may already be gone.</para>
    /// </summary>
    [Serializable]
    public class ProjectileDefinition
    {
        [Tooltip("Reported on every hit. Shows up in the log and in anything that cares which " +
                 "attack did the damage.")]
        public string Id = "bolt";

        [Header("Spawn")]
        [Tooltip("Where it appears, relative to the attacker's feet. +X is forward; mirrored by facing.")]
        public Vector2 SpawnOffset = new Vector2(0.8f, 1.0f);

        [Header("Motion")]
        [Tooltip("Metres per second along the attacker's facing. Zero stands still — which is what " +
                 "makes this an area attack rather than a projectile.")]
        public float Speed = 12f;

        [Tooltip("Downward acceleration in m/s². Zero flies flat; positive arcs.")]
        public float Gravity;

        [Header("Volume")]
        [Tooltip("Half-extents at spawn, in metres.")]
        public Vector2 HalfExtents = new Vector2(0.25f, 0.25f);

        [Tooltip("Metres per second the half-extents grow. This is what makes a shockwave expand, " +
                 "and it is the only difference between a lingering hazard and a spreading one.")]
        public Vector2 GrowthPerSecond;

        [Header("Damage")]
        public int Damage = 12;
        public AttackHeight Height = AttackHeight.Any;

        [Tooltip("Answers this DEFEATS. Empty means every answer works. A shot you cannot block " +
                 "but can dodge reads very differently from one you can only parry.")]
        public DefensiveAnswer Defeats;

        [Header("Lifetime")]
        [Tooltip("Ticks before it expires on its own.")]
        public int LifetimeTicks = 120;

        [Tooltip("Level geometry stops it. Off for something that should pass through walls.")]
        public bool StoppedByGeometry = true;

        [Tooltip("How many targets it can hit before expiring. 0 means unlimited, which is what an " +
                 "area attack wants — it should catch everyone standing in it, not the first one.")]
        public int MaxTargets = 1;

        /// <summary>True if this stands still, i.e. is an area attack rather than a projectile.</summary>
        public bool IsStationary => Mathf.Approximately(Speed, 0f) && Mathf.Approximately(Gravity, 0f);

        /// <summary>Describes anything malformed, or null. A volume with no size or no lifetime is
        /// an attack that silently never happens.</summary>
        public string Validate()
        {
            if (HalfExtents.x <= 0f || HalfExtents.y <= 0f)
                return $"{Id}: the volume has no size";

            if (LifetimeTicks <= 0)
                return $"{Id}: the lifetime is zero, so it expires before it can resolve";

            if (MaxTargets < 0)
                return $"{Id}: MaxTargets cannot be negative — use 0 for unlimited";

            return null;
        }
    }

    /// <summary>One volume currently in flight. Read by the overlay; owned by the system.</summary>
    public sealed class LiveProjectile
    {
        public ProjectileDefinition Definition;
        public readonly HitSweep Sweep = new HitSweep();

        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 HalfExtents;

        public int OwnerId;
        public int Team;
        public int Facing;

        public int TicksLeft;
        public int TargetsHit;
        public bool Expired;
    }

    /// <summary>
    /// Everything currently in the air, ticked once per fixed step.
    ///
    /// <para>Plain C# with no engine coupling, so a whole volley resolves in a headless test. It
    /// reuses <see cref="HitSweep"/> for resolution rather than growing a second one: a projectile
    /// hitting a target is the same event as a sword doing it, and the moment they are two code
    /// paths is the moment a parry starts behaving differently depending on what parried it.</para>
    ///
    /// <para>Resolution builds an <see cref="Attacker"/> standing at the projectile's own position
    /// with a zero offset, so the shared resolver places the volume exactly where the projectile
    /// is. Cover is deliberately <i>not</i> traced from the original attacker — the shot has
    /// travelled, and asking whether a wall stands between the archer and the target would refuse
    /// hits the arrow had already flown past.</para>
    /// </summary>
    public sealed class ProjectileSystem
    {
        private readonly List<LiveProjectile> _live = new List<LiveProjectile>();
        private readonly List<LiveProjectile> _pool = new List<LiveProjectile>();

        public IReadOnlyList<LiveProjectile> Live => _live;

        public int Count => _live.Count;

        /// <summary>Put one in the air on behalf of <paramref name="owner"/>.</summary>
        public void Spawn(ProjectileDefinition definition, in Attacker owner)
        {
            if (definition == null || definition.Validate() != null) return;

            var projectile = Take();

            projectile.Definition = definition;
            projectile.Sweep.Begin();

            int facing = owner.Facing < 0 ? -1 : 1;

            projectile.Position = new Vector2(
                owner.Feet.x + definition.SpawnOffset.x * facing,
                owner.Feet.y + definition.SpawnOffset.y);

            projectile.Velocity = new Vector2(definition.Speed * facing, 0f);
            projectile.HalfExtents = definition.HalfExtents;

            projectile.OwnerId = owner.Id;
            projectile.Team = owner.Team;
            projectile.Facing = facing;

            projectile.TicksLeft = definition.LifetimeTicks;
            projectile.TargetsHit = 0;
            projectile.Expired = false;

            _live.Add(projectile);
        }

        /// <summary>Advance everything by one fixed tick: move, grow, resolve, expire.</summary>
        public void Tick(ICombatWorld world, CollisionWorld level, long tick, float deltaSeconds)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var projectile = _live[i];

                Advance(projectile, level, deltaSeconds);

                if (!projectile.Expired) Resolve(projectile, world, tick);

                if (--projectile.TicksLeft <= 0) projectile.Expired = true;
            }

            Sweep();
        }

        public void Clear()
        {
            for (int i = 0; i < _live.Count; i++) _pool.Add(_live[i]);
            _live.Clear();
        }

        // ---------------------------------------------------------------- internals

        private void Advance(LiveProjectile projectile, CollisionWorld level, float deltaSeconds)
        {
            var definition = projectile.Definition;

            projectile.Velocity.y -= definition.Gravity * deltaSeconds;
            projectile.Position += projectile.Velocity * deltaSeconds;

            projectile.HalfExtents += definition.GrowthPerSecond * deltaSeconds;

            // A wall stops a shot. Checked against the volume rather than a point, so a wide
            // shockwave is stopped by the same geometry that stops the character who cast it.
            if (definition.StoppedByGeometry && level != null &&
                level.OverlapsAnySolid(Aabb.FromCenterSize(projectile.Position, projectile.HalfExtents * 2f)))
            {
                projectile.Expired = true;
            }
        }

        private void Resolve(LiveProjectile projectile, ICombatWorld world, long tick)
        {
            if (world == null) return;

            var definition = projectile.Definition;

            var box = new AttackHitBox
            {
                OpenTick = 0,
                CloseTick = 1,
                Offset = Vector2.zero,
                HalfExtents = projectile.HalfExtents,
                Damage = definition.Damage,
                Height = definition.Height,
                Defeats = definition.Defeats,
                StoppedByGeometry = false,
            };

            // The projectile is its own attacker: it stands where it is, facing the way it flew.
            // Cover from the original caster is meaningless once the shot has left them.
            var attacker = new Attacker(projectile.OwnerId, projectile.Position, projectile.Position,
                                        projectile.Facing, projectile.Team);

            // The remaining budget, not the total: a shot that has already spent two of three
            // pierces must stop after one more, and the sweep is the only place that can count
            // targets standing in the same volume on the same tick.
            int remaining = definition.MaxTargets > 0
                ? Mathf.Max(1, definition.MaxTargets - projectile.TargetsHit)
                : 0;

            var result = projectile.Sweep.Sweep(0, box, attacker, world, null, tick, definition.Id,
                                                remaining);

            if (result.Connected <= 0 && result.LastOutcome == HitOutcome.Ignored) return;

            projectile.TargetsHit += Mathf.Max(result.Connected, 0);

            // A parried shot dies where it was turned. Nothing is reflected — that is a mechanic
            // this project has not decided on, and inventing it here would make parry silently
            // different against projectiles than against blades.
            if (result.Punished)
            {
                projectile.Expired = true;
                return;
            }

            if (definition.MaxTargets > 0 && projectile.TargetsHit >= definition.MaxTargets)
                projectile.Expired = true;
        }

        private void Sweep()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (!_live[i].Expired) continue;

                _pool.Add(_live[i]);
                _live.RemoveAt(i);
            }
        }

        /// <summary>Recycled rather than allocated. A volley should not produce garbage every
        /// tick, and the sim is the one place that has to stay allocation-quiet.</summary>
        private LiveProjectile Take()
        {
            if (_pool.Count == 0) return new LiveProjectile();

            var projectile = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            return projectile;
        }
    }
}

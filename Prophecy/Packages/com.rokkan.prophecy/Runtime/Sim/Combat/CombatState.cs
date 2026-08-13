using System.Collections.Generic;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>Anything that can be hit, from the fight's point of view.</summary>
    public interface ICombatant
    {
        /// <summary>Unique, and stable across machines. What hit routing keys on.</summary>
        int CombatId { get; }

        int Team { get; }

        bool IsAlive { get; }

        /// <summary>Damage dealt by touching this. Zero for anything only dangerous when it swings.</summary>
        int ContactDamage { get; }

        int ContactIntervalTicks { get; }

        DefensiveAnswer ContactDefeats { get; }

        /// <summary>This tick's hittable volume.</summary>
        Hurtbox BuildHurtbox();

        /// <summary>Take a hit and say what it turned into.</summary>
        HitResult ReceiveHit(in HitEvent hit);
    }

    /// <summary>
    /// The fight: who is in it, what is in the air, and what happens when those meet.
    ///
    /// <para><b>Plain C#, and that is the point of it existing.</b> All of this used to live on the
    /// <c>CombatDirector</c> MonoBehaviour — the registry, the projectiles, the contact cooldowns,
    /// the hit routing. That put the authoritative state of a fight in presentation, which is
    /// exactly the wrong side of the line for a host that has to simulate it and clients that have
    /// to predict against it. Here it is testable headlessly, and later it is replicable.</para>
    ///
    /// <para><b>It is the seam, not the rules.</b> <see cref="OnHit"/> hands the hit to the target
    /// and the target decides what it means. Damage numbers, i-frames and blocking are the
    /// receiving end's business.</para>
    /// </summary>
    public sealed class CombatState : ICombatWorld, IAttackObserver
    {
        /// <summary>How many recent hits to keep. A diagnostic, not a game system.</summary>
        public const int HitLogLength = 12;

        /// <summary>
        /// Where runtime-allocated ids begin. Authored ids live below it, so a scene-placed
        /// combatant and a spawned one can never collide.
        ///
        /// <para>Under multiplayer this counter belongs to the host, which hands ids out — a client
        /// inventing its own would name entities nobody else could find. Left local until there is
        /// a host to own it.</para>
        /// </summary>
        public const int FirstRuntimeId = 100000;

        /// <summary>A hit and what the defender made of it. "10 damage" and "10 damage, blocked"
        /// are different stories and only the pair tells either.</summary>
        public readonly struct LoggedHit
        {
            public readonly HitEvent Hit;
            public readonly HitResult Result;

            public LoggedHit(in HitEvent hit, in HitResult result)
            {
                Hit = hit;
                Result = result;
            }
        }

        /// <summary>
        /// A body that hurts to touch, paired with where it was in this tick's snapshot.
        ///
        /// <para>The index rather than a second <c>BuildHurtbox</c> call: contact then tests against
        /// the same volume everything else resolved against, instead of a freshly built one that
        /// only happens to agree.</para>
        /// </summary>
        private readonly struct ContactSource
        {
            public readonly ICombatant Combatant;
            public readonly int BoxIndex;

            public ContactSource(ICombatant combatant, int boxIndex)
            {
                Combatant = combatant;
                BoxIndex = boxIndex;
            }
        }

        private readonly List<ICombatant> _combatants = new List<ICombatant>();
        private readonly List<ContactSource> _contactSources = new List<ContactSource>();
        private readonly Dictionary<int, ICombatant> _byId = new Dictionary<int, ICombatant>();
        private readonly Dictionary<long, long> _contactReady = new Dictionary<long, long>();
        private readonly List<long> _expired = new List<long>();
        private readonly List<int> _candidates = new List<int>();
        private readonly List<LoggedHit> _hitLog = new List<LoggedHit>();

        private readonly HurtboxSet _hurtboxes = new HurtboxSet();
        private readonly ProjectileSystem _projectiles = new ProjectileSystem();

        private int _nextRuntimeId = FirstRuntimeId;

        public HurtboxSet Hurtboxes => _hurtboxes;

        public IReadOnlyList<ICombatant> Combatants => _combatants;

        public IReadOnlyList<LiveProjectile> Projectiles => _projectiles.Live;

        public IReadOnlyList<LoggedHit> HitLog => _hitLog;

        /// <summary>The fight's attack arbiter — who may swing, and when. Enemies READ it;
        /// the writers are the enemy input path and <see cref="AttackModule"/>'s real
        /// start/end reports, forwarded through this world's <see cref="IAttackObserver"/>.</summary>
        public AttackDirector Attacks { get; } = new AttackDirector();

        /// <summary>An id no authored combatant can have. See <see cref="FirstRuntimeId"/>.</summary>
        public int AllocateId() => _nextRuntimeId++;

        /// <summary>
        /// Where this fight reports a wiring mistake — a duplicate id, and whatever joins it.
        ///
        /// <para>A sink rather than a log call, because how a mistake surfaces belongs to whoever
        /// owns the fight: the game's director logs to the console, a headless harness asserts on
        /// it, and a future server routes it wherever servers route things. Null swallows nothing —
        /// the refusal itself still happens either way.</para>
        /// </summary>
        public System.Action<string> OnProblem;

        // ------------------------------------------------------------- attack observation

        /// <summary>Ids that never requested pacing — the player — pass through harmlessly:
        /// the director ignores records it does not hold.</summary>
        public void OnAttackStarted(int combatId, long tick, int totalTicks) =>
            Attacks.OnAttackStarted(combatId, tick, totalTicks);

        public void OnAttackEnded(int combatId, long tick) =>
            Attacks.OnAttackEnded(combatId, tick);

        // ---------------------------------------------------------------- registry

        /// <summary>
        /// Add a participant. Refuses a duplicate id rather than accepting it, because a duplicate
        /// does not fail loudly — it silently routes someone else's hits to the wrong body.
        /// </summary>
        public bool Register(ICombatant combatant)
        {
            if (combatant == null) return false;

            if (_byId.TryGetValue(combatant.CombatId, out var existing))
            {
                if (ReferenceEquals(existing, combatant)) return true;

                OnProblem?.Invoke($"Two combatants share id {combatant.CombatId}. Hits will " +
                                  "route to whichever registered first — give one of them a distinct id.");
                return false;
            }

            _combatants.Add(combatant);
            _byId[combatant.CombatId] = combatant;
            return true;
        }

        public void Unregister(ICombatant combatant)
        {
            if (combatant == null) return;

            if (_combatants.Remove(combatant))
            {
                _byId.Remove(combatant.CombatId);
                Attacks.Forget(combatant.CombatId);
            }
        }

        public void Clear()
        {
            _combatants.Clear();
            _contactSources.Clear();
            _byId.Clear();
            _contactReady.Clear();
            _hitLog.Clear();
            _hurtboxes.Clear();
            _projectiles.Clear();
            Attacks.Clear();
        }

        // ---------------------------------------------------------------- tick

        /// <summary>
        /// Advance the fight by one fixed tick: snapshot everyone, move what is in the air, and
        /// resolve bodies touching.
        ///
        /// <para>The snapshot happens first so every attacker resolving this tick sees the same
        /// world. Rebuilding per attack would let two simultaneous swings disagree about where a
        /// target was — the sort of thing that shows up once in fifty fights and never
        /// reproduces.</para>
        /// </summary>
        public void Tick(CollisionWorld level, long tick, float deltaSeconds)
        {
            // Grants land FIRST, so every enemy consuming input later this tick sees them.
            Attacks.Tick(tick);

            RebuildHurtboxes();

            _projectiles.Tick(this, level, tick, deltaSeconds);

            ResolveContact(tick);
        }

        /// <summary>
        /// Snapshot every live combatant's volume and bucket it.
        ///
        /// <para>A dead combatant contributes nothing rather than being removed, so a dummy that
        /// revives does not have to re-register.</para>
        ///
        /// <para><b>The one pass over everyone.</b> Somebody has to ask each body where it is, so
        /// this loop is irreducible — which is exactly why the bodies that hurt to touch are
        /// collected here too rather than by a second walk of the same list.</para>
        /// </summary>
        public void RebuildHurtboxes()
        {
            _hurtboxes.Clear();
            _contactSources.Clear();

            for (int i = 0; i < _combatants.Count; i++)
            {
                var combatant = _combatants[i];
                if (combatant == null || !combatant.IsAlive) continue;

                if (combatant.ContactDamage > 0)
                    _contactSources.Add(new ContactSource(combatant, _hurtboxes.Count));

                _hurtboxes.Add(combatant.BuildHurtbox());
            }

            _hurtboxes.Build();
        }

        // ---------------------------------------------------------------- contact

        /// <summary>
        /// Bodies that hurt to touch.
        ///
        /// <para><b>Nothing here walks the roster.</b> The sources were picked out during the
        /// snapshot, and each one queries the broadphase. The first version tested every combatant
        /// against every combatant every tick — a hundred of them is ten thousand overlap tests a
        /// tick, for a rule that usually applies to two or three of them. The version after that
        /// still swept the whole list looking for those two or three.</para>
        ///
        /// <para>Resolved centrally rather than by each participant, because contact is symmetric
        /// and needs exactly one place to decide it: two bodies each running their own test would
        /// deal the damage twice. It goes through the same <see cref="OnHit"/> as everything else,
        /// so a dodge answers a body the same way it answers a blade.</para>
        /// </summary>
        private void ResolveContact(long tick)
        {
            for (int i = 0; i < _contactSources.Count; i++)
            {
                var source = _contactSources[i].Combatant;
                var body = _hurtboxes[_contactSources[i].BoxIndex];

                int found = _hurtboxes.Query(body.Centre.x - body.HalfExtents.x,
                                             body.Centre.x + body.HalfExtents.x,
                                             _candidates);

                for (int c = 0; c < found; c++)
                {
                    var target = _hurtboxes[_candidates[c]];

                    if (target.OwnerId == source.CombatId) continue;
                    if (target.Team != 0 && target.Team == source.Team) continue;
                    if (!Touching(body, target)) continue;

                    long key = ContactKey(source.CombatId, target.OwnerId);
                    if (_contactReady.TryGetValue(key, out long ready) && tick < ready) continue;

                    _contactReady[key] = tick + Mathf.Max(1, source.ContactIntervalTicks);

                    // Pushed away from the body that touched them, so being caught between two
                    // enemies does not read as being shoved by nothing.
                    int facing = target.Centre.x < body.Centre.x ? -1 : 1;

                    OnHit(new HitEvent(source.CombatId, target.OwnerId, source.ContactDamage,
                                       facing, tick, "contact", 0,
                                       AttackHeight.Any, source.ContactDefeats));
                }
            }

            PruneContactCooldowns(tick);
        }

        /// <summary>
        /// Plain axis-aligned overlap. Contact is bodies touching and neither body rotates, so the
        /// separating-axis path would be answering a question nobody asked.
        /// </summary>
        private static bool Touching(in Hurtbox a, in Hurtbox b)
        {
            return Mathf.Abs(a.Centre.x - b.Centre.x) < a.HalfExtents.x + b.HalfExtents.x &&
                   Mathf.Abs(a.Centre.y - b.Centre.y) < a.HalfExtents.y + b.HalfExtents.y;
        }

        /// <summary>
        /// Two ids packed into one key. Explicit packing rather than <c>source * 1000 + victim</c>,
        /// which collides — (1, 2000) and (2, 1000) both give 3000 — and fails by silently skipping
        /// a hit rather than by throwing.
        /// </summary>
        private static long ContactKey(int source, int victim) =>
            ((long)source << 32) | (uint)victim;

        /// <summary>
        /// Drop cooldowns that have lapsed. Without this the table keeps an entry for every pair
        /// that has ever touched, which for a hundred combatants is ten thousand entries that
        /// never go away.
        /// </summary>
        private void PruneContactCooldowns(long tick)
        {
            if (_contactReady.Count < 64) return;

            _expired.Clear();

            foreach (var pair in _contactReady)
                if (tick >= pair.Value) _expired.Add(pair.Key);

            for (int i = 0; i < _expired.Count; i++) _contactReady.Remove(_expired[i]);
        }

        // ---------------------------------------------------------------- hits

        public HitResult OnHit(in HitEvent hit)
        {
            // A dictionary rather than a linear scan: this is called once per connecting hit, and
            // a swing into a crowd calls it many times in one tick.
            if (!_byId.TryGetValue(hit.TargetId, out var target) || target == null)
                return HitResult.Ignored;

            var result = target.ReceiveHit(in hit);

            _hitLog.Add(new LoggedHit(in hit, in result));
            if (_hitLog.Count > HitLogLength) _hitLog.RemoveAt(0);

            return result;
        }

        public void Spawn(ProjectileDefinition definition, in Attacker owner) =>
            _projectiles.Spawn(definition, in owner);
    }
}

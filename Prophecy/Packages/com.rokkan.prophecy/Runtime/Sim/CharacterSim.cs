using System.Collections.Generic;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// One character's simulation: owns <see cref="CharacterState"/>, a priority-ordered registry
    /// of <see cref="AbilityModule"/>s, and the action-lock arbiter they coordinate through.
    ///
    /// <para>Runs headless. No MonoBehaviour, Transform, Animator or Camera appears anywhere in
    /// its call graph — collision comes from the sim's own <see cref="CollisionWorld"/>, and time
    /// from the fixed tick. That is what lets a test assert a jump apex or a coyote window with
    /// no scene at all, which is the whole point of locking movement numbers before building
    /// levels from them.</para>
    ///
    /// <para>Per-tick order is fixed and deliberate:</para>
    /// <list type="number">
    ///   <item>refresh grounded/stance from geometry</item>
    ///   <item>tick modules in <see cref="AbilityModule.Order"/></item>
    ///   <item>integrate velocity and resolve it against the world</item>
    ///   <item>refresh grounded again and publish landing edges</item>
    /// </list>
    /// <para>Modules therefore always see grounded state that matches the geometry they are about
    /// to move through, and always run before resolution rather than fighting it.</para>
    /// </summary>
    public sealed class CharacterSim : ISimSystem, Combat.IDefendable
    {
        private readonly List<AbilityModule> _modules = new List<AbilityModule>();
        private readonly List<IDamageGate> _gates = new List<IDamageGate>();
        private ActionLock _lock = ActionLock.None;
        private InputFrame _input = InputFrame.Empty;
        private PendingStun _pendingStun;
        private PendingImpulse _pendingImpulse;

        public CharacterState State { get; } = new CharacterState();
        public CollisionWorld World { get; }

        /// <summary>Health. Owned here so a headless test can kill a character and assert what
        /// follows.</summary>
        public Vitals Vitals { get; } = new Vitals();

        /// <summary>The pool arts cast from — kindling, never labelled Flame (spec §2.2).</summary>
        public FlameReserve Reserve { get; } = new FlameReserve();

        /// <summary>The flask row. Capacity never shrinks; the world's refills do.</summary>
        public Flasks Flasks { get; } = new Flasks();

        /// <summary>What the Bearer wears and wields — the pack sheet's loadout panel reads
        /// this. Slots hold plain <see cref="Items.ItemData"/>; factories do the seeding.</summary>
        public Equipment Worn { get; } = new Equipment();

        /// <summary>The bag — slot-stacked item state, read by the pack's grid. The flask
        /// row stays its own tuned system (<see cref="Flasks"/>); the grid's first cell
        /// mirrors it rather than a bag slot.</summary>
        public Items.Inventory Bag { get; } = new Items.Inventory();

        /// <summary>The ONE equipped art (spec §3.1 — no in-combat selection exists). Changed
        /// only from the arts volume. Buoyancy by default, so the cast button keeps doing
        /// exactly what Matt tuned it to do until the volume says otherwise.</summary>
        public Arts.ArtId EquippedArt = Arts.ArtId.Buoyancy;

        /// <summary>The art table and its feel numbers. The factory swaps in the authored
        /// asset's data when one is wired; the code defaults otherwise, so every headless
        /// test runs the shipped table.</summary>
        public Arts.ArtTuningData ArtTuning = new Arts.ArtTuningData();

        /// <summary>Arts running in this room — the "running" marks for the HUD and the arts
        /// volume. Room-scoped: cleared when a crossing completes, alongside the room-scoped
        /// stat modifiers those arts applied. GfP will except itself when it becomes real.</summary>
        public readonly System.Collections.Generic.List<Arts.ArtId> ActiveArts =
            new System.Collections.Generic.List<Arts.ArtId>();

        /// <summary>The arts the Order has taught. The volume lists ONLY these — compacted,
        /// no holes — and the cast path refuses the rest (Matt: you will not start with all
        /// arts). The factory seeds every art for the gray box; the loadout's art toggles
        /// are the availability switch, live like every loadout edit.</summary>
        public readonly System.Collections.Generic.HashSet<Arts.ArtId> KnownArts =
            new System.Collections.Generic.HashSet<Arts.ArtId>();

        /// <summary>The ONE question "is this art on" — for the HUD's emblem, the volume's
        /// running marks, and any module gated on its art. Buoyancy keeps its float's entry
        /// in step with the toggle it owns, so no consumer needs to know which module an
        /// art lives in.</summary>
        public bool IsArtRunning(Arts.ArtId id) => ActiveArts.Contains(id);

        // Page casts arrive here as REQUESTS: the arts volume runs on render frames with the
        // clock paused, and sim state must not mutate between ticks. CastArt consumes the
        // queue at the top of the next tick, so a page cast and a button cast land through
        // the identical path, on a tick, replayable from the record like everything else.
        private readonly List<Arts.ArtId> _pendingCasts = new List<Arts.ArtId>();

        /// <summary>Park a cast for the next tick. The volume's channel; see the field note.</summary>
        public void RequestCast(Arts.ArtId id)
        {
            if (id != Arts.ArtId.None) _pendingCasts.Add(id);
        }

        /// <summary>Take the oldest parked cast, if any. <see cref="Abilities.CastArt"/> owns
        /// the consuming; nothing else should drain this.</summary>
        public bool TryDequeueCastRequest(out Arts.ArtId id)
        {
            if (_pendingCasts.Count == 0)
            {
                id = Arts.ArtId.None;
                return false;
            }

            id = _pendingCasts[0];
            _pendingCasts.RemoveAt(0);
            return true;
        }

        /// <summary>
        /// Might, Flame and Heart, and everything modifying them. Design bible §6.2.
        ///
        /// <para>On the sim rather than beside it, because damage dealt and health carried are
        /// both simulation outcomes. Presentation may read it; nothing in presentation writes it.</para>
        /// </summary>
        public Stats.StatBlock Stats { get; } = new Stats.StatBlock();

        /// <summary>
        /// What the character temporarily may not do — silences, disarms, roots.
        ///
        /// <para>Beside the stats rather than inside them: a restriction has no value to scale and
        /// no strongest to compare. It shares only the lifecycle.</para>
        /// </summary>
        public Stats.RestrictionSet Restrictions { get; } = new Stats.RestrictionSet();

        // Max health is derived from Heart, so it is re-applied when the derivation could have
        // changed rather than every tick — recomputing constantly would fight anything that
        // deliberately sets MaxHealth, and would hide that this is a derived number.
        private int _healthFromHeart = -1;
        private int _flameFromRank = -1;

        /// <summary>
        /// Ticks of stun an <i>unanswered</i> hit costs. Stamped from tuning when the character is
        /// built, the same way <see cref="Vitals.MaxHealth"/> is.
        ///
        /// <para>Here rather than on the hit-react module because this is the fallback: gates that
        /// answer a hit park their own stun, with their own number — a block staggers less than a
        /// clean hit, and that difference belongs to the block. This is only what happens when
        /// nothing answered at all.</para>
        /// </summary>
        public int HitStunTicks = 18;

        /// <summary>
        /// The fight this character is in — who they can hit and where their hits go. Null is
        /// legitimate: a character with nobody to hit still swings and still commits.
        ///
        /// <para>On the character rather than on each attacking module, because it is the same
        /// answer for all of them and because it changes: the player is a persistent prefab and the
        /// arena arrives with a scene load. One place to re-point beats one per module, and a new
        /// module that swings gets it for free.</para>
        /// </summary>
        public Combat.ICombatWorld CombatWorld { get; set; }

        /// <summary>The tick currently being simulated. Modules stamp timers against this.</summary>
        public long CurrentTick { get; private set; }

        /// <summary>Which way the body faces: -1 or +1. The damage gates' view of it.</summary>
        public int Facing => State.Facing;

        public IReadOnlyList<AbilityModule> Modules => _modules;

        public CharacterSim(CollisionWorld world)
        {
            World = world ?? new CollisionWorld();
            _mover = new CharacterMover(World);
        }

        // The motor. Its own class so a physics change and a lock-arbitration change can never
        // land in the same file — see CharacterMover for the integrator's actual rules.
        private readonly CharacterMover _mover;

        // ---------------------------------------------------------------- modules

        /// <summary>
        /// Register a module. Insertion keeps the list sorted by <see cref="AbilityModule.Order"/>,
        /// so tick order is a property of the modules themselves and never of the order someone
        /// happened to add them in — registration accident must not change simulation results.
        /// </summary>
        public T Add<T>(T module) where T : AbilityModule
        {
            if (module == null) return null;

            int i = 0;
            while (i < _modules.Count && _modules[i].Order <= module.Order) i++;
            _modules.Insert(i, module);

            // Gate precedence is its own ordering and deliberately not the tick order. When a hit
            // is answered has nothing to do with when a module runs, and tying them together would
            // mean a tick-order change silently letting a block outrank invulnerability.
            if (module is IDamageGate gate)
            {
                int g = 0;
                while (g < _gates.Count && _gates[g].GateOrder <= gate.GateOrder) g++;
                _gates.Insert(g, gate);
            }

            return module;
        }

        public T Get<T>() where T : AbilityModule
        {
            for (int i = 0; i < _modules.Count; i++)
                if (_modules[i] is T typed) return typed;
            return null;
        }

        public bool Remove(AbilityModule module)
        {
            if (module is IDamageGate gate) _gates.Remove(gate);
            return _modules.Remove(module);
        }

        // ---------------------------------------------------------------- taking hits

        /// <summary>
        /// Answer an incoming hit: walk the damage gates in precedence order, and if none of them
        /// claims it, take the damage.
        ///
        /// <para>Called from the <i>attacker's</i> tick, so the defensive state it reads is as of
        /// this character's most recent completed tick. That is a deliberate at-most-one-tick lag
        /// rather than a bug: blocking is held for many ticks so it never notices, and a parry
        /// window lagging by a fixed tick is still a fixed window. Resolving it any other way would
        /// make the answer depend on which character happened to register first.</para>
        /// </summary>
        public HitResult ReceiveHit(in HitEvent hit)
        {
            if (!Vitals.IsAlive) return HitResult.Ignored;

            var answered = DamageGateChain.Answer(_gates, this, in hit, Vitals);
            if (answered.Outcome != HitOutcome.Continue) return answered;

            int landed = Vitals.ApplyDamage(hit.Damage, hit.Tick);
            Stun(HitStunTicks, hit.Facing, hit.Tick, HitOutcome.Landed);

            return new HitResult(HitOutcome.Landed, landed);
        }

        /// <summary>
        /// Park a stun for the hit-react module to pick up on its next tick. Called by whatever
        /// decided the character should lose control — a gate, a parry answer, a script.
        ///
        /// <para>The longest pending stun wins rather than the latest. Two hits landing on the same
        /// tick should not leave the character reacting to the weaker one.</para>
        /// </summary>
        public void Stun(int ticks, int direction, long tick, HitOutcome cause = HitOutcome.Landed)
        {
            if (ticks <= 0) return;
            if (_pendingStun.IsSet && _pendingStun.Ticks >= ticks) return;

            _pendingStun = new PendingStun(ticks, direction, tick, cause);
        }

        /// <summary>Take the pending stun, if there is one. Clears it.</summary>
        public bool TryConsumeStun(out PendingStun stun)
        {
            stun = _pendingStun;
            if (!stun.IsSet) return false;

            _pendingStun = default;
            return true;
        }

        /// <summary>True while a stun is waiting to be picked up. Debug overlay.</summary>
        public bool HasPendingStun => _pendingStun.IsSet;

        /// <summary>
        /// Park a shove for this character's own tick — a blocked hit's pushback, or anything else
        /// that moves a body it does not control. The strongest pending shove wins, the same law
        /// <see cref="Stun"/> applies to durations.
        /// </summary>
        public void Impulse(float velocityX, long tick)
        {
            if (_pendingImpulse.IsSet &&
                System.Math.Abs(_pendingImpulse.VelocityX) >= System.Math.Abs(velocityX)) return;

            _pendingImpulse = new PendingImpulse(velocityX, tick);
        }

        // ---------------------------------------------------------------- input

        /// <summary>
        /// Hand the sim this tick's input. Called by the capture layer before the clock steps.
        /// The frame persists until replaced, so a dropped presentation frame repeats the last
        /// known input rather than reading as "everything released".
        /// </summary>
        public void SetInput(in InputFrame input) => _input = input;

        // ---------------------------------------------------------------- locks

        public ActionLock CurrentLock => _lock;

        /// <summary>
        /// Ask to claim the character. Granted when nothing holds a lock, when the caller already
        /// holds it (re-asserting updates the flags), or when the existing holder is strictly
        /// lower priority AND has opened its cancel window.
        ///
        /// <para>Requiring both conditions for a takeover is what stops higher-priority actions
        /// stomping mid-swing: an attack keeps its committed frames, and only opens itself to
        /// cancellation during recovery.</para>
        /// </summary>
        public bool TryLock(object owner, LockFlags flags, int priority)
        {
            if (owner == null) return false;

            if (!_lock.IsHeld || ReferenceEquals(_lock.Owner, owner))
            {
                _lock = new ActionLock(owner, flags, priority);
                return true;
            }

            if (priority > _lock.Priority && _lock.CancelWindowOpen)
            {
                _lock = new ActionLock(owner, flags, priority);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Claim the character regardless of the holder's cancel window.
        ///
        /// <para><b>For things that are not a choice</b> — being hit, being parried, dying, a scene
        /// transition. <see cref="TryLock"/>'s "higher priority AND an open cancel window" is what
        /// makes an attack a commitment, and every voluntary action must go through it. But a
        /// hit-react that waited for the cancel window would never interrupt the swing it is a
        /// reaction to, which is the one thing it exists to do.</para>
        ///
        /// <para>Still refuses a strictly higher-priority holder, so a cutscene cannot be stomped
        /// by a stray hit.</para>
        /// </summary>
        public bool ForceLock(object owner, LockFlags flags, int priority)
        {
            if (owner == null) return false;
            if (_lock.IsHeld && _lock.Priority > priority) return false;

            _lock = new ActionLock(owner, flags, priority);
            return true;
        }

        /// <summary>Release the lock. Only the owner may; anyone else is ignored, so a stale
        /// reference cannot free someone else's claim.</summary>
        public void ReleaseLock(object owner)
        {
            if (_lock.IsHeld && ReferenceEquals(_lock.Owner, owner))
                _lock = ActionLock.None;
        }

        /// <summary>Open or close the current owner's cancel window.</summary>
        public void SetCancelWindow(object owner, bool open)
        {
            if (_lock.IsHeld && ReferenceEquals(_lock.Owner, owner))
                _lock = _lock.WithCancelWindow(open);
        }

        /// <summary>
        /// True if <paramref name="action"/> is currently permitted. The question every module
        /// asks before acting — and the reason none of them need to know each other exists.
        /// </summary>
        /// <summary>
        /// Whether an action is available: not locked out by a committed move, and not barred by a
        /// restriction.
        ///
        /// <para>Both consulted here rather than at each call site, because there are dozens of
        /// call sites and one of them would eventually check only the lock. A root that a module
        /// forgot to ask about is a root that does nothing.</para>
        /// </summary>
        public bool Can(LockFlags action)
        {
            if (Restrictions.Blocks(action)) return false;

            return !_lock.IsHeld || (_lock.Flags & action) == 0;
        }

        /// <summary>True if <paramref name="owner"/> is the current lock holder.</summary>
        public bool HoldsLock(object owner) => _lock.IsHeld && ReferenceEquals(_lock.Owner, owner);

        // ---------------------------------------------------------------- tick

        /// <summary>
        /// Keep <see cref="Vitals.MaxHealth"/> equal to what Heart says it should be.
        ///
        /// <para><b>Raising the cap does not heal.</b> Levelling Heart mid-fight grants headroom,
        /// not health — the alternative makes a level-up an emergency heal and turns the
        /// progression into a combat resource. Zelda II gave you the full bar on level-up because
        /// it levelled you out of combat; this can happen at any time, so the choice matters.</para>
        /// </summary>
        private void SyncMaxHealthToHeart()
        {
            int wanted = Stats.MaxHealth;
            if (wanted == _healthFromHeart) return;

            _healthFromHeart = wanted;
            Vitals.MaxHealth = wanted;
        }

        /// <summary>
        /// The same law for the reserve: Flame RANK sets the pool's capacity (UI spec §2.2)
        /// and the stat sheet is the only author of it. The reserve briefly had its own flat
        /// max, which is exactly the two-sources-of-truth split that let the Heart sync stomp
        /// a renumbered <see cref="Vitals.MaxHealth"/> — one authority, or the quiet fight.
        /// Raising the cap does not refill, for the same reason levelling Heart does not heal.
        /// </summary>
        private void SyncReserveToFlame()
        {
            int wanted = Stats.MaxFlame;
            if (wanted == _flameFromRank) return;

            bool wasFull = Reserve.Current >= Reserve.Max;
            _flameFromRank = wanted;
            Reserve.SetMax(wanted);

            // A pool that has never been spent tracks its cap — the factory fills it before
            // the first tick, and the first sync must not strand it below a raised max.
            if (wasFull) Reserve.Refill();
        }

        public void Tick(in SimTickInfo info)
        {
            CurrentTick = info.Tick;

            // Once a tick, before anything reads a stat, so every read within the tick agrees.
            Stats.PruneExpired(info.Tick);
            Restrictions.PruneExpired(info.Tick);
            SyncMaxHealthToHeart();
            SyncReserveToFlame();

            State.HitWallThisTick = false;
            State.HitCeilingThisTick = false;
            State.LandedThisTick = false;

            // Shoves parked by the attacker's tick land here, before any module runs — the same
            // moment for every character, whatever order the clock ticked them in.
            if (_pendingImpulse.IsSet)
            {
                State.Velocity.x = _pendingImpulse.VelocityX;
                _pendingImpulse = default;
            }

            bool wasGrounded = State.Grounded;
            RefreshGrounded();

            for (int i = 0; i < _modules.Count; i++)
            {
                var m = _modules[i];
                if (!m.Enabled) continue;
                if ((m.ValidIn & State.Space) == 0) continue;

                // A silenced ability does not tick at all, rather than ticking and refusing to
                // start. Modules hold state across ticks — a charge, a combo window — and letting
                // one advance while forbidden to act is how a silence ends with the attack it was
                // meant to prevent already half-wound.
                if (Restrictions.IsBarred(m.Id)) continue;

                m.Tick(this, in _input, in info);
            }

            _mover.Integrate(State, Ground, info.DeltaSeconds);

            RefreshGrounded();
            if (!wasGrounded && State.Grounded) State.LandedThisTick = true;
            if (State.Grounded) State.LastGroundedTick = info.Tick;

            _mover.UpdateStance(State);
        }

        /// <summary>
        /// The room-change broadcast, fired by <see cref="Abilities.DoorTransit"/> as a
        /// committed crossing COMPLETES (and by any future scripted passage): modifiers that
        /// declared themselves room-lived die, then every module's hook runs — the channel
        /// "a section change clears X" travels through, with Buoyancy's float as its first
        /// passenger.
        /// </summary>
        public void CommitRoomChange(int room)
        {
            if (room == State.Room) return;

            State.Room = room;
            Stats.ClearRoomScoped();
            ActiveArts.Clear();   // the room is the timer (spec §3.2); GfP will except itself
            for (int i = 0; i < _modules.Count; i++) _modules[i].OnRoomChanged(this);
        }

        /// <summary>
        /// Place the body in a room without a crossing — spawns and scene arrivals, where the
        /// full module Reset has already cleared everything a door crossing would. Silent on
        /// purpose: an arrival is not a passage.
        /// </summary>
        public void SetRoom(int room)
        {
            State.Room = room;
        }

        /// <summary>
        /// The overworld's walkability oracle, or null for free top-down movement. Re-pointed by
        /// the host each tick, exactly as <see cref="CombatWorld"/> is — the character is a
        /// persistent prefab and the ground it stands on arrives with a scene load.
        /// </summary>
        public ITopDownGround Ground;

        private void RefreshGrounded() => _mover.RefreshGrounded(State);

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// True if the character can stand up where they are — false under a low ceiling. Kept
        /// here rather than in the crouch module so that anything needing clearance (crouch,
        /// crawl, ledge pull-up) asks the same question the same way.
        /// </summary>
        public bool HasHeadroomToStand()
        {
            var standing = Aabb.FromFootSize(State.Position, State.StandSize);
            return !World.OverlapsAnySolid(standing);
        }

        /// <summary>
        /// Put the character back to starting condition: full health, nothing pending.
        ///
        /// <para>Deliberately separate from <see cref="Teleport"/>. Moving a character and
        /// restoring one are different events — walking through a door should not heal you, and
        /// Zelda II's own rule is that a screen transition keeps your health while a death does
        /// not. Respawning is both, which is why the host has one call that does them together and
        /// this one only does the stats.</para>
        ///
        /// <para>This is the single place that has to grow when there is more to a character than
        /// health. Anything that respawns goes through here rather than listing stats itself.</para>
        /// </summary>
        public void Revive()
        {
            Vitals.Reset();
            Restrictions.Clear();
            _pendingStun = default;
            _pendingImpulse = default;
        }

        /// <summary>Place the character, clearing motion and history. Used on spawn and on scene
        /// transitions so no stale velocity or coyote credit survives the move.</summary>
        public void Teleport(Vector2 footPosition, int facing = 0, int groundLayer = 0)
        {
            State.Position = footPosition;
            State.Velocity = Vector2.zero;
            State.GroundLayer = groundLayer;   // callers that know the arrival surface say so;
                                               // everyone else lands on the base surface
            State.LastGroundedTick = long.MinValue;
            State.AirRefreshTick = long.MinValue;
            State.JumpConsumedTick = long.MinValue;
            State.DropThrough = false;
            State.HasFloatFloor = false;
            State.Attachment = AttachmentKind.None;
            State.AttachmentAnchor = Vector2.zero;
            if (facing != 0) State.Facing = facing < 0 ? -1 : 1;

            _lock = ActionLock.None;
            _pendingStun = default;
            _pendingImpulse = default;
            _pendingCasts.Clear();
            for (int i = 0; i < _modules.Count; i++) _modules[i].Reset();

            RefreshGrounded();
            _mover.UpdateStance(State);
        }
    }
}

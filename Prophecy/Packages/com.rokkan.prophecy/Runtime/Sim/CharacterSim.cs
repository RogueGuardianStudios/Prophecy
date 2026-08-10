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
    public sealed class CharacterSim : ISimSystem
    {
        private readonly List<AbilityModule> _modules = new List<AbilityModule>();
        private readonly List<IDamageGate> _gates = new List<IDamageGate>();
        private ActionLock _lock = ActionLock.None;
        private InputFrame _input = InputFrame.Empty;
        private PendingStun _pendingStun;

        public CharacterState State { get; } = new CharacterState();
        public CollisionWorld World { get; }

        /// <summary>Health. Owned here so a headless test can kill a character and assert what
        /// follows.</summary>
        public Vitals Vitals { get; } = new Vitals();

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

        public IReadOnlyList<AbilityModule> Modules => _modules;

        public CharacterSim(CollisionWorld world)
        {
            World = world ?? new CollisionWorld();
        }

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

            for (int i = 0; i < _gates.Count; i++)
            {
                var answer = _gates[i].Evaluate(this, in hit);
                if (answer.Outcome == HitOutcome.Continue) continue;

                // A gate that lets damage through in reduced form — a block — still deals it.
                if (answer.DamageApplied > 0)
                {
                    int applied = Vitals.ApplyDamage(answer.DamageApplied, hit.Tick);
                    return new HitResult(answer.Outcome, applied, answer.AttackerStunTicks);
                }

                return answer;
            }

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

        public void Tick(in SimTickInfo info)
        {
            CurrentTick = info.Tick;

            // Once a tick, before anything reads a stat, so every read within the tick agrees.
            Stats.PruneExpired(info.Tick);
            Restrictions.PruneExpired(info.Tick);
            SyncMaxHealthToHeart();

            State.HitWallThisTick = false;
            State.HitCeilingThisTick = false;
            State.LandedThisTick = false;

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

            Integrate(info.DeltaSeconds);

            RefreshGrounded();
            if (!wasGrounded && State.Grounded) State.LandedThisTick = true;
            if (State.Grounded) State.LastGroundedTick = info.Tick;

            UpdateStance();
        }

        /// <summary>
        /// The overworld's walkability oracle, or null for free top-down movement. Re-pointed by
        /// the host each tick, exactly as <see cref="CombatWorld"/> is — the character is a
        /// persistent prefab and the ground it stands on arrives with a scene load.
        /// </summary>
        public ITopDownGround Ground;

        /// <summary>
        /// Whether the body's leading edge may make this single-axis move.
        ///
        /// <para>Three points across the leading face — centre and both corners — because a feet
        /// point alone lets half a body hang over a cliff edge before anything objects. The
        /// corners pull in slightly so brushing a wall while walking parallel to it does not
        /// read as a collision.</para>
        /// </summary>
        private bool GroundPermits(Vector2 delta)
        {
            var from = State.Position;
            var to = from + delta;

            float half = State.BodySize.x * 0.5f;
            float lead = Mathf.Sign(delta.x != 0f ? delta.x : delta.y) * half;
            float across = half * 0.8f;

            Vector2 centre, cornerA, cornerB;

            if (delta.x != 0f)
            {
                centre = new Vector2(to.x + lead, to.y);
                cornerA = new Vector2(to.x + lead, to.y - across);
                cornerB = new Vector2(to.x + lead, to.y + across);
            }
            else
            {
                centre = new Vector2(to.x, to.y + lead);
                cornerA = new Vector2(to.x - across, to.y + lead);
                cornerB = new Vector2(to.x + across, to.y + lead);
            }

            // The edge probes VALIDATE; only a feet-to-feet probe RESOLVES the layer. The
            // leading edge runs up to half a body ahead of the feet, and committing its layer
            // flipped the token one cell early — walking a bridge deck toward its junction
            // dropped the body through the deck, and leaving a cave popped it onto the roof,
            // for every frame until the feet caught up. The token must track where the feet
            // ARE, not where the toes point.
            int edgeLayerA = State.GroundLayer;
            int edgeLayerB = State.GroundLayer;
            int edgeLayerC = State.GroundLayer;

            bool permitted = Ground.CanStep(from, centre, ref edgeLayerA) &&
                             Ground.CanStep(from, cornerA, ref edgeLayerB) &&
                             Ground.CanStep(from, cornerB, ref edgeLayerC);
            if (!permitted) return false;

            int feetLayer = State.GroundLayer;
            Ground.CanStep(from, to, ref feetLayer);
            State.GroundLayer = feetLayer;
            return true;
        }

        /// <summary>
        /// Move by velocity, resolving each axis separately against the world.
        ///
        /// <para>Axis separation is the standard platformer approach and it is what makes wall
        /// sliding fall out for free: blocked horizontally, vertical motion still proceeds. A
        /// single combined sweep would instead snag the character on any surface it brushed.
        /// Horizontal resolves first so that landing is evaluated at the position actually
        /// arrived at.</para>
        /// </summary>
        private void Integrate(float dt)
        {
            var delta = State.Velocity * dt;
            if (delta == Vector2.zero) return;

            if (State.Space == MovementSpace.TopDown)
            {
                // No gravity and no one-ways overhead; both axes are plain lateral motion,
                // resolved against the ground seam when a scene supplies one. No ground means
                // free movement — every scene before the overworld, and every old test.
                if (Ground == null)
                {
                    State.Position += delta;
                    return;
                }

                // Axis separation, exactly as side-scroll below: blocked one way, the other axis
                // still proceeds, which is what makes walls slide-alongable rather than sticky.
                if (delta.x != 0f)
                {
                    if (GroundPermits(new Vector2(delta.x, 0f)))
                        State.Position += new Vector2(delta.x, 0f);
                    else
                    {
                        State.HitWallThisTick = true;
                        State.Velocity.x = 0f;
                    }
                }

                if (delta.y != 0f)
                {
                    if (GroundPermits(new Vector2(0f, delta.y)))
                        State.Position += new Vector2(0f, delta.y);
                    else
                    {
                        State.HitWallThisTick = true;
                        State.Velocity.y = 0f;
                    }
                }

                return;
            }

            if (delta.x != 0f)
            {
                float allowedX = World.SweepHorizontal(State.Body, delta.x, out bool hitX);
                State.Position += new Vector2(allowedX, 0f);
                if (hitX)
                {
                    State.HitWallThisTick = true;
                    State.Velocity.x = 0f;   // stop pushing into geometry
                }
            }

            if (delta.y != 0f)
            {
                float allowedY = World.SweepVertical(State.Body, delta.y, out bool hitY, State.DropThrough);

                // The float floor stops a downward crossing from above, exactly as a one-way
                // platform would — a body already beneath it is not touched, which is what
                // lets a submerged body rise up through its own waterline.
                if (State.HasFloatFloor && delta.y < 0f && State.Position.y >= State.FloatFloorY)
                {
                    float toFloor = State.FloatFloorY - State.Position.y;
                    if (allowedY < toFloor)
                    {
                        allowedY = toFloor;
                        hitY = true;
                    }
                }

                State.Position += new Vector2(0f, allowedY);
                if (hitY)
                {
                    if (delta.y > 0f) State.HitCeilingThisTick = true;
                    State.Velocity.y = 0f;
                }
            }
        }

        /// <summary>
        /// Recompute support from geometry.
        ///
        /// <para>Deliberately passes <see cref="CharacterState.DropThrough"/> along. Grounding is
        /// the thing that stops a fall starting, so a character dropping through a platform must
        /// stop counting as standing on it the moment the drop is permitted — otherwise gravity
        /// keeps being zeroed and the input looks ignored.</para>
        /// </summary>
        private void RefreshGrounded()
        {
            State.Grounded = State.Space == MovementSpace.TopDown ||
                             World.IsGrounded(State.Body, dropThrough: State.DropThrough) ||
                             OnFloatFloor();
        }

        /// <summary>Standing on the temporary surface an ability maintains (Buoyancy's
        /// water-walk): feet at the floor, or within the same probe distance the solid
        /// grounding uses. Never true from beneath — the float floor is one-way.</summary>
        private bool OnFloatFloor() =>
            State.HasFloatFloor &&
            State.Position.y >= State.FloatFloorY - 0.001f &&
            State.Position.y <= State.FloatFloorY + 0.02f;

        /// <summary>
        /// Airborne always wins, because the down-thrust is gated on it. Crouch is otherwise a
        /// module decision — this only forces the character out of Air when they land, and never
        /// silently un-crouches someone under a low ceiling.
        /// </summary>
        private void UpdateStance()
        {
            if (State.Space == MovementSpace.TopDown)
            {
                State.Stance = Stance.Stand;
                return;
            }

            if (!State.Grounded)
            {
                State.Stance = Stance.Air;
            }
            else if (State.Stance == Stance.Air)
            {
                State.Stance = Stance.Stand;
            }
        }

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
            for (int i = 0; i < _modules.Count; i++) _modules[i].Reset();

            RefreshGrounded();
            UpdateStance();
        }
    }
}

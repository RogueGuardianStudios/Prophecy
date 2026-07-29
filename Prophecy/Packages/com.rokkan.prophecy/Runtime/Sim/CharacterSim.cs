using System.Collections.Generic;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Collision;
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
        private ActionLock _lock = ActionLock.None;
        private InputFrame _input = InputFrame.Empty;

        public CharacterState State { get; } = new CharacterState();
        public CollisionWorld World { get; }

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
            return module;
        }

        public T Get<T>() where T : AbilityModule
        {
            for (int i = 0; i < _modules.Count; i++)
                if (_modules[i] is T typed) return typed;
            return null;
        }

        public bool Remove(AbilityModule module) => _modules.Remove(module);

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
        public bool Can(LockFlags action) => !_lock.IsHeld || (_lock.Flags & action) == 0;

        /// <summary>True if <paramref name="owner"/> is the current lock holder.</summary>
        public bool HoldsLock(object owner) => _lock.IsHeld && ReferenceEquals(_lock.Owner, owner);

        // ---------------------------------------------------------------- tick

        public void Tick(in SimTickInfo info)
        {
            CurrentTick = info.Tick;

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
                m.Tick(this, in _input, in info);
            }

            Integrate(info.DeltaSeconds);

            RefreshGrounded();
            if (!wasGrounded && State.Grounded) State.LandedThisTick = true;
            if (State.Grounded) State.LastGroundedTick = info.Tick;

            UpdateStance();
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
                // No gravity and no one-ways overhead; both axes are plain lateral motion.
                State.Position += delta;
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
                State.Position += new Vector2(0f, allowedY);
                if (hitY)
                {
                    if (delta.y > 0f) State.HitCeilingThisTick = true;
                    State.Velocity.y = 0f;
                }
            }
        }

        private void RefreshGrounded()
        {
            State.Grounded = State.Space == MovementSpace.TopDown || World.IsGrounded(State.Body);
        }

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

        /// <summary>Place the character, clearing motion and history. Used on spawn and on scene
        /// transitions so no stale velocity or coyote credit survives the move.</summary>
        public void Teleport(Vector2 footPosition, int facing = 0)
        {
            State.Position = footPosition;
            State.Velocity = Vector2.zero;
            State.LastGroundedTick = long.MinValue;
            State.DropThrough = false;
            if (facing != 0) State.Facing = facing < 0 ? -1 : 1;

            _lock = ActionLock.None;
            for (int i = 0; i < _modules.Count; i++) _modules[i].Reset();

            RefreshGrounded();
            UpdateStance();
        }
    }
}

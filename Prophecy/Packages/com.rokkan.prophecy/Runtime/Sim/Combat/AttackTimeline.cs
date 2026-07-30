using System;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>Stat multipliers applied to an attack's scalable windows when it is armed.</summary>
    [Serializable]
    public struct AttackModifiers
    {
        public float IFrameScale;
        public float ParryScale;
        public float CancelScale;

        /// <summary>No gear, no buffs — every window at its authored length.</summary>
        public static AttackModifiers None => new AttackModifiers
        {
            IFrameScale = 1f,
            ParryScale = 1f,
            CancelScale = 1f,
        };
    }

    /// <summary>
    /// Runs one attack, one tick at a time, and answers what is true right now: which phase, which
    /// hit boxes are live, whether the character is invulnerable, parrying, or cancellable.
    ///
    /// <para>Plain C#, no engine types beyond maths structs, so an entire attack can be stepped in
    /// a unit test with no scene, no animation and no frame rate. That is the point: combat timing
    /// is the thing this project most needs to reproduce identically at 30, 60 and 144 fps, and
    /// integers stepped one at a time are the only way to be sure.</para>
    ///
    /// <para><b>It decides nothing about damage.</b> The timeline says "hit box 2 is live this
    /// tick"; something else asks the world who is inside it. Keeping resolution behind that seam
    /// is what lets the geometry backend change — and it is the one genuinely portable idea from
    /// HopeFell's version.</para>
    ///
    /// <para><b>Stat scaling is resolved once, at <see cref="Arm"/>.</b> A parry window must not
    /// change length midway because a buff expired — the player committed to a timing and the game
    /// owes them that timing. Recomputing per tick would also mean the same attack behaving
    /// differently depending on when you asked.</para>
    /// </summary>
    public sealed class AttackTimeline
    {
        public enum Phase
        {
            /// <summary>Nothing armed.</summary>
            Idle,

            /// <summary>Winding up. The telegraph — nothing is dangerous yet.</summary>
            Startup,

            /// <summary>Committed. Hit boxes may be live.</summary>
            Active,

            /// <summary>The vulnerable tail, where cancels and chains live.</summary>
            Recovery,
        }

        private AttackDefinition _definition;

        private TickRange _iFrames;
        private TickRange _parry;
        private TickRange _cancel;

        /// <summary>Ticks elapsed since arming. The first ticked tick is 0.</summary>
        public int ElapsedTicks { get; private set; } = -1;

        public bool IsArmed => _definition != null;

        public AttackDefinition Definition => _definition;

        /// <summary>The attack has run its full length and should be disarmed by its owner.</summary>
        public bool IsComplete => IsArmed && ElapsedTicks >= _definition.TotalTicks;

        public Phase CurrentPhase
        {
            get
            {
                if (!IsArmed || ElapsedTicks < 0 || IsComplete) return Phase.Idle;
                if (_definition.StartupRange.Contains(ElapsedTicks)) return Phase.Startup;
                if (_definition.ActiveRange.Contains(ElapsedTicks)) return Phase.Active;
                return Phase.Recovery;
            }
        }

        /// <summary>Invulnerable this tick. Feeds a damage gate.</summary>
        public bool IsInvulnerable => IsArmed && _iFrames.Contains(ElapsedTicks);

        /// <summary>An incoming attack this tick would be parried.</summary>
        public bool IsParrying => IsArmed && _parry.Contains(ElapsedTicks);

        /// <summary>
        /// A higher-priority action, or the next combo link, may take over this tick. Drive
        /// <c>CharacterSim.SetCancelWindow</c> from this and the arbiter does the rest — the lock
        /// already refuses takeover unless the window is open.
        /// </summary>
        public bool IsCancellable => IsArmed && _cancel.Contains(ElapsedTicks);

        /// <summary>Effective windows after stat scaling. Exposed for the debug overlay.</summary>
        public TickRange IFrameRange => _iFrames;
        public TickRange ParryRange => _parry;
        public TickRange CancelRange => _cancel;

        /// <summary>
        /// Begin an attack. Windows are scaled now and held for the whole action.
        /// </summary>
        public void Arm(AttackDefinition definition, in AttackModifiers modifiers)
        {
            _definition = definition;
            ElapsedTicks = -1;

            if (definition == null)
            {
                _iFrames = _parry = _cancel = TickRange.None;
                return;
            }

            int total = definition.TotalTicks;

            // Clamped to the action: a stat that lengthens a window past the move's own end would
            // otherwise leave the character invulnerable into the next action.
            _iFrames = definition.IFrames.Resolve(modifiers.IFrameScale).ClampedTo(total);
            _parry = definition.ParryWindow.Resolve(modifiers.ParryScale).ClampedTo(total);
            _cancel = definition.CancelWindow.Resolve(modifiers.CancelScale).ClampedTo(total);
        }

        public void Arm(AttackDefinition definition) => Arm(definition, AttackModifiers.None);

        public void Disarm()
        {
            _definition = null;
            ElapsedTicks = -1;
            _iFrames = _parry = _cancel = TickRange.None;
        }

        /// <summary>Advance exactly one fixed tick.</summary>
        public void Advance()
        {
            if (!IsArmed) return;
            ElapsedTicks++;
        }

        /// <summary>True if <paramref name="index"/>'s hit box can connect this tick.</summary>
        public bool IsHitBoxLive(int index)
        {
            if (!IsArmed || _definition.HitBoxes == null) return false;
            if (index < 0 || index >= _definition.HitBoxes.Length) return false;

            return _definition.HitBoxes[index].Window.Contains(ElapsedTicks);
        }

        public int HitBoxCount => IsArmed && _definition.HitBoxes != null ? _definition.HitBoxes.Length : 0;

        /// <summary>
        /// True on the exact tick a hit box opens. Useful for one-shot effects — a swing sound, a
        /// trail — that should fire once rather than every tick the box is live.
        /// </summary>
        public bool DidHitBoxOpenThisTick(int index)
        {
            if (!IsArmed || _definition.HitBoxes == null) return false;
            if (index < 0 || index >= _definition.HitBoxes.Length) return false;

            return _definition.HitBoxes[index].Window.Start == ElapsedTicks;
        }

        public AttackHitBox GetHitBox(int index) => _definition.HitBoxes[index];

        /// <summary>Progress through the action, 0..1. Presentation only — never drive gameplay
        /// off this, or timing stops being integers.</summary>
        public float NormalisedProgress =>
            IsArmed && _definition.TotalTicks > 0
                ? Mathf.Clamp01(ElapsedTicks / (float)_definition.TotalTicks)
                : 0f;
    }
}

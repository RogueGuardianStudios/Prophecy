using UnityEngine;

namespace Rokkan.Prophecy.Sim.AI
{
    /// <summary>
    /// What a brain wants the body to do. The buffer between a planner that thinks at frame rate
    /// and a simulation that acts at a fixed 60 Hz.
    ///
    /// <para><b>Why a buffer at all.</b> GOAP updates in <c>Update</c>, on a variable delta; the
    /// sim consumes input on a tick. Those cadences do not line up, and the mismatch matters
    /// differently for the two kinds of intent. A <i>held</i> axis is a level — asked for twice, it
    /// is still just held. A <i>press</i> is an edge, and an edge read twice starts two attacks
    /// from one decision.</para>
    ///
    /// <para>So presses are latched and cleared on consumption, held values simply persist. This is
    /// the same latch <c>PlayerInputCapture</c> runs for the player, for the same reason and with
    /// the same failure mode if it is skipped — which is the point: a brain and a gamepad are
    /// subject to identical rules on the way in.</para>
    ///
    /// <para>Plain C#, in the sim assembly, so a brain can be exercised headlessly.</para>
    /// </summary>
    public sealed class EnemyIntent
    {
        /// <summary>Held. -1 left, +1 right, 0 still. Clamped.</summary>
        public float MoveX;

        /// <summary>
        /// Held. The stick's vertical axis, for brains that live on a two-axis plane — the
        /// overworld. -1 south, +1 north, 0 still. Clamped.
        ///
        /// <para>Side-scroll brains leave it at zero and nothing changes for them: in that space
        /// the axis means crouch, which has its own explicit flag below so a planner can never
        /// crouch by accident while trying to walk somewhere.</para>
        /// </summary>
        public float MoveY;

        /// <summary>Held. Crouch, for a low guard or a low attack. Wins over <see cref="MoveY"/>,
        /// because ducking is a decision and drifting south is not.</summary>
        public bool Crouch;

        /// <summary>Held. Raise the guard; whether it becomes a parry is the sim's business.</summary>
        public bool Guard;

        private bool _attack;
        private bool _jump;
        private bool _dodge;

        /// <summary>Swing once. Latched until a tick consumes it.</summary>
        public void PressAttack() => _attack = true;

        /// <summary>Jump once.</summary>
        public void PressJump() => _jump = true;

        /// <summary>Dodge once.</summary>
        public void PressDodge() => _dodge = true;

        /// <summary>True while any press is waiting to be spent. For diagnostics.</summary>
        public bool HasPendingPress => _attack || _jump || _dodge;

        /// <summary>
        /// Turn this into one tick of input, spending every latched press.
        ///
        /// <para>Held values survive; presses do not. A brain that decided to attack three frames
        /// ago and has been overruled since still gets exactly one swing, which is what a player
        /// mashing the button gets.</para>
        /// </summary>
        public InputFrame Consume()
        {
            var frame = new InputFrame(
                new Vector2(Mathf.Clamp(MoveX, -1f, 1f),
                            Crouch ? -1f : Mathf.Clamp(MoveY, -1f, 1f)),
                jump: _jump ? ButtonState.Press : ButtonState.None,
                attack: _attack ? ButtonState.Press : ButtonState.None,
                block: Guard ? ButtonState.Holding : ButtonState.None,
                dodge: _dodge ? ButtonState.Press : ButtonState.None);

            _attack = false;
            _jump = false;
            _dodge = false;

            return frame;
        }

        /// <summary>Forget everything. On losing a target, being staggered, or despawning.</summary>
        public void Clear()
        {
            MoveX = 0f;
            MoveY = 0f;
            Crouch = false;
            Guard = false;
            _attack = false;
            _jump = false;
            _dodge = false;
        }
    }
}

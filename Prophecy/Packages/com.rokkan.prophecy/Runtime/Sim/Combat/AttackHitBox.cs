using System;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// Where a hit is aimed, for the purpose of answering it.
    ///
    /// <para><b>Authored, not derived from the box's height.</b> Deriving it would be free but
    /// fuzzy — a volume that straddles the waist would flip category with a 5 cm tuning nudge, and
    /// the whole point is that the player learns which answer a telegraph demands. An explicit
    /// value is legible in the asset, testable, and cannot drift.</para>
    ///
    /// <para>This is only about <i>blocking</i>. Ducking under a high swing needs no category at
    /// all: a crouched body is a smaller hurtbox and the geometry already misses.</para>
    /// </summary>
    public enum AttackHeight
    {
        /// <summary>Answered by a standing block.</summary>
        High,

        /// <summary>Answered by a crouching block.</summary>
        Low,

        /// <summary>Answered by either. Most attacks.</summary>
        Any,
    }

    /// <summary>
    /// One damaging volume within an attack: when it is live, where it is, and what it does.
    ///
    /// <para>Window and volume are paired rather than kept in separate lists because a multi-hit
    /// move rarely swings the same box twice — a whirl's second sweep is somewhere else. Keeping
    /// them together means a hit is authored as one thing and the two halves cannot drift out of
    /// step.</para>
    ///
    /// <para><b>Local space, facing-relative.</b> The offset is from the character's feet with +X
    /// meaning "forward"; the sim mirrors it by facing. Authoring in world space would need every
    /// attack duplicated for each direction.</para>
    ///
    /// <para>The window is a plain <see cref="TickRange"/>, not a
    /// <see cref="ScalableWindow"/> — hit timing is the move's identity and should not drift with
    /// equipment. Gear widens what you can <i>survive</i> (parry, i-frames), not how long your
    /// sword is dangerous.</para>
    /// </summary>
    [Serializable]
    public struct AttackHitBox
    {
        /// <summary>
        /// The window as two authored ints rather than a <see cref="TickRange"/> field.
        ///
        /// <para><b>Because Unity does not serialize readonly fields.</b> A <c>TickRange</c> here
        /// vanished from the asset silently: it round-tripped as <c>[0..0)</c>, which reads as "no
        /// window", so every hit box in the game would have loaded inert on the next fresh Editor
        /// launch with nothing logged. Keeping the authored form as primitives and
        /// <see cref="Window"/> as the derived value keeps <c>TickRange</c> a genuinely immutable
        /// value type everywhere it is used in logic — and gives the inspector two labelled ints
        /// instead of a nested struct.</para>
        ///
        /// <para>Half-open like every other range here: the box is live on <c>OpenTick</c> and not
        /// on <c>CloseTick</c>, so <c>CloseTick - OpenTick</c> is the duration.</para>
        /// </summary>
        [Tooltip("First tick, counted from the start of the action, on which this volume connects.")]
        public int OpenTick;

        [Tooltip("First tick on which it no longer connects. Duration is Close - Open.")]
        public int CloseTick;

        /// <summary>Ticks during which this volume can connect.</summary>
        public TickRange Window => new TickRange(OpenTick, CloseTick);

        [Tooltip("Offset from the feet, in metres. +X is forward; the sim mirrors it by facing.")]
        public Vector2 Offset;

        [Tooltip("Half-extents of the box, in metres.")]
        public Vector2 HalfExtents;

        [Tooltip("Rotation in degrees about the depth axis. The reason hit volumes are not AABBs.")]
        public float RotationDegrees;

        [Tooltip("Damage dealt on connect.")]
        public int Damage;

        [Tooltip("Which block answers this. Only matters to blocking — crouching under a high " +
                 "swing works because the body is smaller, not because of this.")]
        public AttackHeight Height;

        [Tooltip("No block answers this one. What stops holding the shield down from being the " +
                 "correct play forever, and what makes a telegraph worth reading.")]
        public bool Unblockable;

        [Tooltip("If set, level geometry between attacker and target stops this hit — a spear " +
                 "thrust through a grate. Leave off for anything that should reach regardless.")]
        public bool StoppedByGeometry;

        public bool IsAuthored => Window.IsActive && HalfExtents.x > 0f && HalfExtents.y > 0f;

        /// <summary>Centre of the volume in world space, given the attacker's feet and facing.</summary>
        public Vector2 ResolveCentre(Vector2 feet, int facing)
        {
            int sign = facing < 0 ? -1 : 1;
            return new Vector2(feet.x + Offset.x * sign, feet.y + Offset.y);
        }

        /// <summary>Rotation in world space. Mirrored with facing, or a diagonal slash would
        /// point the wrong way when the character turns around.</summary>
        public float ResolveRotation(int facing) => facing < 0 ? -RotationDegrees : RotationDegrees;
    }
}

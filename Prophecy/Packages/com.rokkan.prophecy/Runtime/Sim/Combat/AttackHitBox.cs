using System;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
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
        [Tooltip("Ticks from the start of the action during which this volume can connect.")]
        public TickRange Window;

        [Tooltip("Offset from the feet, in metres. +X is forward; the sim mirrors it by facing.")]
        public Vector2 Offset;

        [Tooltip("Half-extents of the box, in metres.")]
        public Vector2 HalfExtents;

        [Tooltip("Rotation in degrees about the depth axis. The reason hit volumes are not AABBs.")]
        public float RotationDegrees;

        [Tooltip("Damage dealt on connect.")]
        public int Damage;

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

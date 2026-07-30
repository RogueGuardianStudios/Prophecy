using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// A volume that can be hit, belonging to some combatant.
    ///
    /// <para>Plain data with a stable <see cref="OwnerId"/> rather than an object reference. The
    /// resolver runs inside the tick and must stay headless, so it cannot hold a
    /// <c>MonoBehaviour</c> — and an integer id is also what a hit-dedup set and, later, a save
    /// file can key on.</para>
    ///
    /// <para><see cref="Team"/> exists so an attack can be told who it may hurt without the
    /// resolver knowing anything about factions. Zero is "no team" and is hit by everyone, which
    /// is what a breakable crate wants.</para>
    /// </summary>
    public readonly struct Hurtbox
    {
        public readonly int OwnerId;
        public readonly int Team;

        /// <summary>World-space centre, on the sim's movement plane.</summary>
        public readonly Vector2 Centre;

        public readonly Vector2 HalfExtents;

        /// <summary>Rotation in degrees about the depth axis.</summary>
        public readonly float RotationDegrees;

        public Hurtbox(int ownerId, Vector2 centre, Vector2 halfExtents,
                       float rotationDegrees = 0f, int team = 0)
        {
            OwnerId = ownerId;
            Team = team;
            Centre = centre;
            HalfExtents = halfExtents;
            RotationDegrees = rotationDegrees;
        }

        public bool IsValid => HalfExtents.x > 0f && HalfExtents.y > 0f;

        /// <summary>Build one from a character's feet position and body size.</summary>
        public static Hurtbox ForBody(int ownerId, Vector2 feet, Vector2 bodySize, int team = 0)
        {
            var half = bodySize * 0.5f;
            return new Hurtbox(ownerId, new Vector2(feet.x, feet.y + half.y), half, 0f, team);
        }
    }

    /// <summary>
    /// Who is swinging, and from where.
    ///
    /// <para><see cref="Origin"/> is separate from <see cref="Feet"/> and it matters: hit volumes
    /// are placed relative to the feet, but <b>cover is measured from the body</b>. Casting the
    /// occlusion ray from the hit box instead would start it wherever the attack reaches to —
    /// which for a thrust is already past the wall that should have stopped it.</para>
    /// </summary>
    public readonly struct Attacker
    {
        public readonly int Id;
        public readonly int Team;

        /// <summary>Foot position. Hit volumes are offset from here.</summary>
        public readonly Vector2 Feet;

        /// <summary>Body centre. Cover is traced from here to the target.</summary>
        public readonly Vector2 Origin;

        /// <summary>-1 or +1. Mirrors hit volume offsets and rotations.</summary>
        public readonly int Facing;

        public Attacker(int id, Vector2 feet, Vector2 origin, int facing, int team = 0)
        {
            Id = id;
            Team = team;
            Feet = feet;
            Origin = origin;
            Facing = facing < 0 ? -1 : 1;
        }

        /// <summary>Build one from a character's feet and body size, tracing cover from the
        /// midpoint of the body.</summary>
        public static Attacker FromBody(int id, Vector2 feet, Vector2 bodySize, int facing, int team = 0)
        {
            return new Attacker(id, feet, new Vector2(feet.x, feet.y + bodySize.y * 0.5f), facing, team);
        }
    }
}

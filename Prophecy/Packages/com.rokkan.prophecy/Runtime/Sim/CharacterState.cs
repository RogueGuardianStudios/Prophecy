using UnityEngine;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// Everything the sim knows about one character. Plain mutable C# — presentation reads this
    /// and renders it, and never writes to it.
    ///
    /// <para><see cref="Position"/> is the FEET, not the centre. Level geometry, ledge heights
    /// and jump clearances are all authored against the ground the character stands on, and a
    /// foot anchor keeps those numbers directly readable instead of forever offset by half a
    /// body height. It also means a crouch can shrink the body without sliding the character
    /// downward.</para>
    /// </summary>
    public sealed class CharacterState
    {
        /// <summary>Foot position in the movement plane.</summary>
        public Vector2 Position;

        /// <summary>Units per second. Modules write this; the sim integrates and resolves it.</summary>
        public Vector2 Velocity;

        public bool Grounded;
        public Stance Stance = Stance.Stand;

        /// <summary>-1 for left, +1 for right. Never zero — a character always faces somewhere.</summary>
        public int Facing = 1;

        public MovementSpace Space = MovementSpace.SideScroll;

        /// <summary>
        /// Who this character is in a fight. An integer rather than an object reference because
        /// hit resolution runs inside the tick and must stay headless — and because it is what a
        /// hit-dedup set keys on and what a save file could store.
        ///
        /// <para>Lives here rather than on the attack module so that the character's hurtbox and
        /// the character's attacks cannot disagree about who they belong to.</para>
        /// </summary>
        public int CombatId;

        /// <summary>
        /// Faction. Attacks skip their own team; zero is neutral and hit by everyone, which is
        /// what a breakable crate wants.
        /// </summary>
        public int Team;

        /// <summary>Body size while standing.</summary>
        public Vector2 StandSize = new Vector2(1f, 2f);

        /// <summary>Body size while crouching. Drives both the collider and crawl-space clearance.</summary>
        public Vector2 CrouchSize = new Vector2(1f, 1f);

        /// <summary>
        /// The most recent tick on which the character was grounded. Coyote time reads this —
        /// keeping it as a tick rather than an accumulated float is what makes the window
        /// exactly reproducible instead of frame-rate dependent.
        /// </summary>
        public long LastGroundedTick = long.MinValue;

        /// <summary>True while the player is deliberately dropping through a one-way platform.</summary>
        public bool DropThrough;

        /// <summary>
        /// What the character is currently holding onto, if anything.
        ///
        /// <para>Shared state rather than a flag owned by one module, for the same reason
        /// <see cref="Grounded"/> is: several abilities need the answer and none of them may ask
        /// each other. Ledge pull-up reads <c>Ledge</c> without knowing a hang module exists, and
        /// the hang releases its lock when it sees the attachment end — whoever ended it.</para>
        /// </summary>
        public AttachmentKind Attachment;

        /// <summary>Where the attachment holds the character — the hang or climb anchor.</summary>
        public Vector2 AttachmentAnchor;

        /// <summary>Set by the sim when the last resolve was stopped by geometry.</summary>
        public bool HitWallThisTick;
        public bool HitCeilingThisTick;

        /// <summary>True on the tick the character transitions from airborne to grounded — the
        /// hook for landing squash, dust and the down-thrust's impact.</summary>
        public bool LandedThisTick;

        public Vector2 BodySize => Stance == Stance.Crouch ? CrouchSize : StandSize;

        /// <summary>The character's collision box at its current position and stance.</summary>
        public Collision.Aabb Body => Collision.Aabb.FromFootSize(Position, BodySize);

        /// <summary>The collision box the character would occupy at <paramref name="foot"/>.</summary>
        public Collision.Aabb BodyAt(Vector2 foot) => Collision.Aabb.FromFootSize(foot, BodySize);

        /// <summary>Ticks since the character was last grounded (0 while grounded).</summary>
        public long TicksSinceGrounded(long currentTick) =>
            LastGroundedTick == long.MinValue ? long.MaxValue : currentTick - LastGroundedTick;
    }
}

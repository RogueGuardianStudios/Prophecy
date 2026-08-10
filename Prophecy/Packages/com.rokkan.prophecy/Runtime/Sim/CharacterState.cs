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

        /// <summary>
        /// Which of the ground's surfaces the feet are on, where one plane position carries more
        /// than one (a bridge deck, a cave floor). An opaque token owned by
        /// <see cref="ITopDownGround"/> — the sim stores and threads it, never interprets it.
        /// 0 everywhere a ground has only one surface.
        /// </summary>
        public int GroundLayer;

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

        /// <summary>
        /// The tick air abilities were last restored without touching the ground.
        ///
        /// <para>Shared state rather than one module calling another, for the same reason
        /// <see cref="Grounded"/> is: a down-thrust bouncing off an enemy should hand the air jump
        /// back, and neither module may know the other exists. The down-thrust stamps this; the
        /// air jump watches it. Anything else that earns a mid-air reset — a wall scramble, a
        /// pick-up — writes the same field and works immediately.</para>
        /// </summary>
        public long AirRefreshTick = long.MinValue;

        /// <summary>
        /// The tick on which a jump press was spent by something that ticks early. Later jump
        /// modules skip a press already claimed.
        ///
        /// <para><b>Why a stamp and not a wall probe.</b> The air jump used to defer next to a
        /// wall, on the assumption that the wall jump would take the press — but that assumption is
        /// a reference to another module in disguise, and it is wrong for any loadout with the air
        /// jump unlocked and the wall jump not. In that combination the press was eaten by nobody:
        /// the wall jump was not registered, and the air jump stood aside for it anyway. With
        /// progression as the stated core mechanism, that combination will exist.</para>
        ///
        /// <para>Same shape as <see cref="AirRefreshTick"/>, and the same reason: the modules stay
        /// ignorant of each other and the fact travels through state.</para>
        /// </summary>
        public long JumpConsumedTick = long.MinValue;

        /// <summary>True while the player is deliberately dropping through a one-way platform.</summary>
        public bool DropThrough;

        /// <summary>
        /// A temporary supporting surface at a height, owned by whichever ability maintains it —
        /// Buoyancy's water-walk today. One-way from above: it stops downward crossings and
        /// grounds feet standing on it, while a body beneath rises through it freely. The sim's
        /// own grounding and vertical sweep consult it, which is what makes the waterline act
        /// like any other platform — landing there lands, jumps off it are ground jumps, and
        /// the air refreshes the ordinary way.
        ///
        /// <para>Same shape as <see cref="AirRefreshTick"/>, and the same reason: the modules
        /// stay ignorant of each other and the fact travels through state.</para>
        /// </summary>
        public bool HasFloatFloor;
        public float FloatFloorY;

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

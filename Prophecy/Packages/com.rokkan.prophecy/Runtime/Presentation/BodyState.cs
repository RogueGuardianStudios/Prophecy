namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// What the body should be seen doing.
    ///
    /// <para><b>Named for the body, not the animation, and not by accident.</b> The obvious name
    /// collides with <c>UnityEngine.AnimationState</c>, which every file with a <c>using
    /// UnityEngine</c> pulls in — so the obvious name makes every future consumer disambiguate.
    /// The name also draws the right line: <c>CharacterState</c> is what the simulation says is
    /// true, this is what the body is showing about it.</para> One entry per distinct piece of animation the game
    /// needs, which makes this enum the authoritative animation shopping list —
    /// <c>Plans/Animation-Requirements.md</c> is written from it and not the other way round.
    ///
    /// <para><b>These are presentation states, not sim states.</b> The sim has three stances and a
    /// lock; this has twenty-odd entries because <i>rising</i> and <i>falling</i> look nothing
    /// alike while being the same stance. Nothing here feeds back into the simulation.</para>
    /// </summary>
    public enum BodyState
    {
        // ---- grounded, neutral
        Idle,
        Walk,
        Run,

        // ---- crouch. A separate idle and walk because the crouch is a real stance with its own
        // attack, not a pose the character passes through.
        CrouchIdle,
        CrouchWalk,

        // ---- airborne. Split at the apex: rise and fall are the two halves of every jump and a
        // single "air" clip reads as floating.
        JumpRise,
        Fall,
        Land,

        // ---- traversal
        WallSlide,
        WallJump,
        LedgeHang,
        LedgeClimb,
        LadderIdle,
        LadderClimb,

        // ---- defence
        Block,
        Parry,
        HitReact,
        Dodge,

        // ---- offence. The chain link is its own state: a follow-up that replayed the opener
        // makes a combo look like a stutter rather than a sequence.
        AttackStandA,
        AttackStandB,
        AttackCrouch,
        DownThrust,
        UpThrust,

        // ---- terminal
        Death,
    }
}

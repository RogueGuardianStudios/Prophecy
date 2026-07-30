using System;
using UnityEngine;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// Every number that decides how the character moves, as plain C#.
    ///
    /// <para><b>Why this is not the ScriptableObject.</b> The gate forbids any
    /// <c>UnityEngine.Object</c> under <c>Rokkan.Prophecy.Sim</c>, and a ScriptableObject is one —
    /// a module holding a <c>MovementTuning</c> asset directly would fail
    /// <c>NoSimType_HoldsAUnityObjectField</c> and, worse, would mean movement could not be
    /// constructed in a headless test. So the numbers live here and
    /// <see cref="Rokkan.Prophecy.Core.MovementTuning"/> is a thin asset wrapper around an
    /// instance of this class.</para>
    ///
    /// <para>It is a <b>class, not a struct</b>, on purpose. Modules hold the same reference the
    /// asset serialises, so dragging a slider in the inspector during play mode changes movement
    /// on the next tick — which is the entire point of "SO edits persist through play mode, so
    /// this is the live tuning surface".</para>
    ///
    /// <para><b>Timing is authored in ticks, never seconds.</b> Coyote and buffer windows are
    /// counted in fixed ticks so they are exactly reproducible at any frame rate; a float
    /// accumulator would make them drift with the renderer.</para>
    /// </summary>
    [Serializable]
    public class MovementTuningData
    {
        // ------------------------------------------------------------------ body

        [Header("Body")]
        [Tooltip("Collision width in metres. 1 unit = 1 metre.")]
        public float BodyWidth = 0.9f;

        [Tooltip("Standing collision height. Ledge and ceiling clearances are authored against this.")]
        public float StandHeight = 1.8f;

        [Tooltip("Crouching collision height. Crawl spaces are sized from it.")]
        public float CrouchHeight = 0.95f;

        // ------------------------------------------------------------------ level grid

        /// <summary>
        /// The height of one <b>lane</b> — the floor-to-floor module levels are composed from.
        ///
        /// <para>Expressed as a multiple of the hero rather than an absolute, because that is what
        /// makes it a unit at all: every doorway, ceiling and drop is legible in relation to the
        /// character standing next to it. Roughly twice the hero's height gives a corridor with a
        /// clear head of headroom — tall enough to feel like architecture, tight enough that a
        /// full jump meets the ceiling, which is what stops every room being a shaft.</para>
        ///
        /// <para>Deriving it means the grid follows if the hero is ever resized, instead of a
        /// hundred hand-placed floors quietly becoming the wrong proportion.</para>
        /// </summary>
        [Header("Level grid")]
        [Tooltip("Lane height as a multiple of the standing hero. 2 = a lane is twice the hero's height.")]
        [Range(1.5f, 3f)]
        public float LaneHeightMultiplier = 2f;

        /// <summary>Floor-to-floor height of one level lane, in metres.</summary>
        public float LaneHeight => StandHeight * LaneHeightMultiplier;

        // ------------------------------------------------------------------ ground

        [Header("Ground movement")]
        public float WalkSpeed = 4f;
        public float RunSpeed = 7.5f;

        [Tooltip("Metres per second squared while input is pushing.")]
        public float GroundAcceleration = 60f;

        [Tooltip("Metres per second squared while input is neutral. Higher = snappier stops.")]
        public float GroundFriction = 70f;

        [Range(0f, 1f)]
        public float CrouchSpeedMultiplier = 0.4f;

        [Tooltip("Below this magnitude, stick input reads as neutral.")]
        [Range(0f, 0.9f)]
        public float MoveDeadzone = 0.2f;

        /// <summary>
        /// Open knob #1 from the plan. Ships as <see cref="RunMode.Toggle"/> — Zelda II had no
        /// analog stick, and a toggle keeps top speed a decision rather than a thumb position —
        /// but all three models are one dropdown apart so they can be A/B'd on the same build.
        /// </summary>
        [Header("Run model")]
        public RunMode RunMode = RunMode.Toggle;

        // ------------------------------------------------------------------ air

        [Header("Air movement")]
        [Tooltip("Air acceleration. Below ground accel gives committed jumps; equal gives full air control.")]
        public float AirAcceleration = 35f;

        [Tooltip("Air friction with no input. Low, so momentum carries across a gap.")]
        public float AirFriction = 8f;

        // ------------------------------------------------------------------ gravity & jump

        [Header("Jump")]
        [Tooltip("Apex height in metres under pure rise gravity. Apex hang adds a little on top.")]
        public float JumpHeight = 2.4f;

        [Tooltip("Gravity while rising. Jump velocity is derived from this and JumpHeight.")]
        public float RiseGravity = 45f;

        [Tooltip("Gravity while falling. Higher than rise gravity is what stops jumps feeling floaty.")]
        public float FallGravity = 70f;

        [Tooltip("Gravity multiplier near the apex — the 'hang' that makes airborne aiming readable.")]
        [Range(0.1f, 1f)]
        public float ApexGravityScale = 0.55f;

        [Tooltip("Vertical speed below which the apex scale applies, in m/s.")]
        public float ApexVelocityThreshold = 2f;

        [Tooltip("Terminal velocity. Must be >= DownThrustSpeed or the thrust would be clamped.")]
        public float MaxFallSpeed = 26f;

        [Tooltip("Rising velocity is multiplied by this when jump is released — variable jump height.")]
        [Range(0f, 1f)]
        public float JumpCutMultiplier = 0.4f;

        [Tooltip("Ticks after leaving ground during which a jump still counts. 6 ticks = 0.1 s at 60 Hz.")]
        public int CoyoteTicks = 6;

        [Tooltip("Ticks a jump press is remembered while airborne. 8 ticks = 0.133 s at 60 Hz.")]
        public int JumpBufferTicks = 8;

        // ------------------------------------------------------------------ air jumps

        [Header("Double jump")]
        [Tooltip("Extra jumps available before touching ground again. 0 disables the ability.")]
        public int AirJumps = 1;

        [Tooltip("Height of an air jump, in metres. Usually below the ground jump so it reads as a recovery.")]
        public float AirJumpHeight = 1.9f;

        [Tooltip("Ticks airborne before an air jump arms. Stops one press spending both jumps, and " +
                 "keeps the coyote window from being cashed in as a free double jump.")]
        public int AirJumpArmTicks = 8;

        // ------------------------------------------------------------------ walls

        [Header("Wall slide & wall jump")]
        [Tooltip("Terminal velocity while sliding down a wall. Well below MaxFallSpeed, or the slide reads as a fall.")]
        public float WallSlideSpeed = 4.5f;

        [Tooltip("How far ahead to probe for a wall, in metres.")]
        public float WallProbeDistance = 0.08f;

        [Tooltip("Vertical launch of a wall jump, in metres of height.")]
        public float WallJumpHeight = 2.2f;

        [Tooltip("Horizontal launch away from the wall, in m/s.")]
        public float WallJumpPushSpeed = 6.5f;

        [Tooltip("Ticks during which steering back toward the wall is ignored, so the jump actually " +
                 "leaves. Without it, holding toward the wall re-sticks instantly and the player " +
                 "climbs the same wall forever.")]
        public int WallJumpControlLockTicks = 10;

        [Tooltip("Require holding toward the wall to slide. Off means any wall contact slides.")]
        public bool WallSlideNeedsInput = true;

        // ------------------------------------------------------------------ ledges

        [Header("Ledge hang & pull-up")]
        [Tooltip("Lowest grab height above the feet. Below this the character would just step up.")]
        public float LedgeGrabMinHeight = 1f;

        [Tooltip("Highest grab height above the feet — roughly where the hands are when standing.")]
        public float LedgeGrabMaxHeight = 1.7f;

        [Tooltip("How far ahead to look for a ledge, in metres.")]
        public float LedgeProbeDistance = 0.35f;

        [Tooltip("Where the feet hang relative to the grabbed surface, in metres below it.")]
        public float LedgeHangDrop = 1.55f;

        [Tooltip("Only grab while falling. Grabbing on the way up feels like being caught by the level.")]
        public bool LedgeGrabRequiresFalling = true;

        [Tooltip("Ticks the pull-up takes. Movement is locked throughout.")]
        public int LedgePullUpTicks = 12;

        [Tooltip("Ticks after releasing a ledge before it can be grabbed again, so dropping works.")]
        public int LedgeRegrabCooldownTicks = 12;

        // ------------------------------------------------------------------ ladders

        [Header("Ladders & ropes")]
        [Tooltip("Climb speed in m/s, up and down.")]
        public float ClimbSpeed = 3.2f;

        [Tooltip("Horizontal shuffle speed while on a rope or wide ladder.")]
        public float ClimbLateralSpeed = 1.6f;

        [Tooltip("Stick deflection needed to mount a ladder you are standing in front of.")]
        [Range(0.1f, 0.9f)]
        public float ClimbMountThreshold = 0.5f;

        [Tooltip("Height of a jump pushed off a ladder, in metres.")]
        public float ClimbDismountJumpHeight = 1.6f;

        // ------------------------------------------------------------------ crouch

        [Header("Crouch")]
        [Tooltip("Stick Y below this negative magnitude means crouch.")]
        [Range(0.1f, 0.9f)]
        public float CrouchInputThreshold = 0.5f;

        // ------------------------------------------------------------------ drop-through

        [Header("Drop through platforms")]
        [Tooltip("Ticks down must be held before dropping. Long enough that a tap still crouches — " +
                 "the two share a button, and crouching is the far more common intent.")]
        public int DropThroughHoldTicks = 10;

        [Tooltip("Ticks the pass-through permission lasts. Must outlast the fall clear of the " +
                 "platform, or the character snaps back on top of it.")]
        public int DropThroughTicks = 16;

        // ------------------------------------------------------------------ down-thrust

        [Header("Down-thrust")]
        [Tooltip("Downward speed of the dive. Bible §6.1: jump + hold down + attack. Non-negotiable move.")]
        public float DownThrustSpeed = 22f;

        [Tooltip("How high the pop off a connect carries, in metres. Authored as a height like " +
                 "every other launch, so it is directly comparable to JumpHeight — and set equal " +
                 "to it by default, because a bounce that lifts you less than a jump makes " +
                 "descending a column a losing race with gravity.")]
        public float DownThrustBounceHeight = 2.4f;

        [Tooltip("Minimum ticks the dive commits for before it can be cancelled.")]
        public int DownThrustMinTicks = 4;

        // ------------------------------------------------------------------ landing

        [Header("Landing")]
        [Tooltip("Impact speed above which a landing counts as hard.")]
        public float HardLandingSpeed = 18f;

        [Tooltip("Ticks of movement lock on a hard landing. 0 disables landing lag entirely.")]
        public int HardLandingTicks = 0;

        // ------------------------------------------------------------------ top-down

        [Header("Top-down overworld")]
        public float TopDownSpeed = 5f;
        public float TopDownAcceleration = 50f;
        public float TopDownFriction = 60f;

        // ------------------------------------------------------------------ interact

        [Header("Interact")]
        [Tooltip("Reach of the interact probe, in metres from the body.")]
        public float InteractRange = 1.2f;

        [Tooltip("Ticks an interact press stays live while looking for something to talk to. Its " +
                 "own number rather than the jump buffer's, which it used to borrow — retuning " +
                 "how forgiving a jump is should not quietly change how forgiving a door is.")]
        public int InteractBufferTicks = 8;

        // ------------------------------------------------------------------ derived

        /// <summary>
        /// Launch speed that reaches <see cref="JumpHeight"/> under <see cref="RiseGravity"/>:
        /// v = sqrt(2gh). Deriving it means authoring a height you can measure against level
        /// geometry, instead of a velocity you can only discover by trial.
        /// </summary>
        public float JumpVelocity => Mathf.Sqrt(2f * RiseGravity * JumpHeight);

        /// <summary>Every launch is authored as a height and converted the same way, so the
        /// numbers stay comparable — an air jump at 1.9 m is visibly shorter than a 2.4 m one,
        /// and you can read that off the asset instead of deriving it.</summary>
        public float AirJumpVelocity => Mathf.Sqrt(2f * RiseGravity * AirJumpHeight);

        public float WallJumpVelocity => Mathf.Sqrt(2f * RiseGravity * WallJumpHeight);

        public float ClimbDismountJumpVelocity => Mathf.Sqrt(2f * RiseGravity * ClimbDismountJumpHeight);

        /// <summary>Pop off a connecting down-thrust. Same derivation as every other launch, so a
        /// bounce height equal to <see cref="JumpHeight"/> really does carry you as far as a
        /// jump.</summary>
        public float DownThrustBounceVelocity => Mathf.Sqrt(2f * RiseGravity * DownThrustBounceHeight);

        public Vector2 StandSize => new Vector2(BodyWidth, StandHeight);
        public Vector2 CrouchSize => new Vector2(BodyWidth, CrouchHeight);

        /// <summary>Stamp the authored body dimensions onto a character. Called at build time, not
        /// per tick — body size is tuning, not state.</summary>
        public void ApplyBody(CharacterState state)
        {
            if (state == null) return;
            state.StandSize = StandSize;
            state.CrouchSize = CrouchSize;
        }
    }
}

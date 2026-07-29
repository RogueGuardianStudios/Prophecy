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
        /// Open knob #1 from the plan: run is a <b>toggle</b> first (Zelda II had no analog stick,
        /// and a toggle keeps top speed a decision rather than a thumb position). Turning
        /// <see cref="AnalogSpeedBlend"/> on A/Bs the modern alternative without a code change.
        /// </summary>
        [Header("Run model — A/B these, do not ship both")]
        public bool RunIsToggle = true;

        [Tooltip("When on, stick magnitude blends walk..run and the run toggle is ignored.")]
        public bool AnalogSpeedBlend = false;

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

        // ------------------------------------------------------------------ crouch

        [Header("Crouch")]
        [Tooltip("Stick Y below this negative magnitude means crouch.")]
        [Range(0.1f, 0.9f)]
        public float CrouchInputThreshold = 0.5f;

        // ------------------------------------------------------------------ down-thrust

        [Header("Down-thrust")]
        [Tooltip("Downward speed of the dive. Bible §6.1: jump + hold down + attack. Non-negotiable move.")]
        public float DownThrustSpeed = 22f;

        [Tooltip("Upward pop on connecting. The bounce is what makes the move chainable.")]
        public float DownThrustBounceSpeed = 9f;

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

        // ------------------------------------------------------------------ derived

        /// <summary>
        /// Launch speed that reaches <see cref="JumpHeight"/> under <see cref="RiseGravity"/>:
        /// v = sqrt(2gh). Deriving it means authoring a height you can measure against level
        /// geometry, instead of a velocity you can only discover by trial.
        /// </summary>
        public float JumpVelocity => Mathf.Sqrt(2f * RiseGravity * JumpHeight);

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

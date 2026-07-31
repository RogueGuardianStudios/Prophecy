using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Everything the resolver needs to decide what the body looks like, gathered into one struct.
    ///
    /// <para>Explicit inputs rather than a <c>CharacterSim</c> reference so the decision is a pure
    /// function of values — which is what lets it be tested exhaustively with no scene, no
    /// <c>Animator</c>, and no play mode, the same way every other rule in this project is.</para>
    /// </summary>
    public struct BodyStateInputs
    {
        public bool Alive;
        public bool Grounded;
        public Stance Stance;
        public Vector2 Velocity;
        public AttachmentKind Attachment;

        public bool InHitReact;
        public bool Dodging;
        public bool Guarding;
        public bool ParryWindowOpen;
        public bool WallSliding;
        public bool ClimbingLedge;
        public bool DownThrusting;

        /// <summary>Null when not attacking; otherwise the running attack's authored id.</summary>
        public string AttackId;

        /// <summary>Landed on this tick. One frame of truth, so the state is edge-triggered.</summary>
        public bool LandedThisTick;

        /// <summary>Speed above which the walk becomes a run, in metres per second.</summary>
        public float RunThreshold;

        /// <summary>Speed below which the character reads as standing still.</summary>
        public float MoveThreshold;
    }

    /// <summary>
    /// Turns simulation state into the one thing the body should be seen doing.
    ///
    /// <para><b>The order of these checks is not arbitrary and must not be rearranged casually.</b>
    /// It mirrors the arbitration the sim already performs — <c>LockPriority</c> for what beats
    /// what, <c>ModuleOrder</c> for what resolves first. Death outranks being hit, being hit
    /// outranks acting, acting outranks moving. When the two orders disagree the animation shows a
    /// character doing something the simulation says they are not, which is the exact class of bug
    /// that is hardest to trace: nothing errors, the picture is just wrong.</para>
    ///
    /// <para>Pure and static on purpose. It reads no component, allocates nothing, and can be
    /// tested one case per assertion.</para>
    /// </summary>
    public static class BodyStateResolver
    {
        /// <summary>Default run threshold if tuning does not say otherwise, in m/s.</summary>
        public const float DefaultRunThreshold = 4.5f;

        /// <summary>Default standing-still threshold, in m/s.</summary>
        public const float DefaultMoveThreshold = 0.15f;

        public static BodyState Resolve(in BodyStateInputs input)
        {
            // 1. Terminal. Nothing overrides being dead, including being hit again.
            if (!input.Alive) return BodyState.Death;

            // 2. Reactions, which the sim force-locks through everything voluntary.
            if (input.InHitReact) return BodyState.HitReact;

            // 3. Committed actions. Ordered among themselves the way the modules are: the
            //    down-thrust is checked before ordinary attacks because it is an attack that also
            //    decides what the airborne body looks like, and a generic air-attack pose would
            //    hide the dive that is the whole point of it.
            if (input.DownThrusting) return BodyState.DownThrust;

            if (!string.IsNullOrEmpty(input.AttackId)) return ForAttack(input.AttackId, input.Stance);

            if (input.Dodging) return BodyState.Dodge;

            // 4. Held defence. Parry is the same button and the same module, distinguished only by
            //    whether its window is still open — so the pose is chosen the same way.
            if (input.Guarding) return input.ParryWindowOpen ? BodyState.Parry : BodyState.Block;

            // 5. Attachments. Position is held by the sim, so these are poses, not motion.
            if (input.Attachment == AttachmentKind.Ledge)
                return input.ClimbingLedge ? BodyState.LedgeClimb : BodyState.LedgeHang;

            if (input.Attachment == AttachmentKind.Ladder)
            {
                return Mathf.Abs(input.Velocity.y) > input.MoveThreshold
                    ? BodyState.LadderClimb
                    : BodyState.LadderIdle;
            }

            // A climb can outlive the attachment: LedgePullUp clears Attachment on its first tick
            // and then lerps for eleven more, so keying only off the attachment would drop the
            // character into a fall pose halfway up the wall.
            if (input.ClimbingLedge) return BodyState.LedgeClimb;

            // 6. Airborne.
            if (!input.Grounded)
            {
                if (input.WallSliding) return BodyState.WallSlide;

                return input.Velocity.y > 0f ? BodyState.JumpRise : BodyState.Fall;
            }

            // 7. Grounded. The landing is edge-triggered off the sim's own one-tick flag rather
            //    than inferred from velocity, so it cannot double-fire on a bumpy floor.
            if (input.LandedThisTick) return BodyState.Land;

            float speed = Mathf.Abs(input.Velocity.x);
            bool moving = speed > input.MoveThreshold;

            if (input.Stance == Stance.Crouch)
                return moving ? BodyState.CrouchWalk : BodyState.CrouchIdle;

            if (!moving) return BodyState.Idle;

            return speed >= input.RunThreshold ? BodyState.Run : BodyState.Walk;
        }

        /// <summary>
        /// Which swing is on screen. Keyed off the authored attack id, so adding an attack to
        /// <c>CombatTuning</c> shows up here as an unmapped id rather than as silence.
        /// </summary>
        private static BodyState ForAttack(string attackId, Stance stance)
        {
            switch (attackId)
            {
                case "slash_high":   return BodyState.AttackStandA;
                case "slash_high_2": return BodyState.AttackStandB;
                case "thrust_low":   return BodyState.AttackCrouch;
                case "down_thrust":  return BodyState.DownThrust;

                // An id nobody has mapped yet. Falling back on stance keeps the character swinging
                // something rather than sliding about in idle while an attack is plainly running.
                default:
                    return stance == Stance.Crouch
                        ? BodyState.AttackCrouch
                        : BodyState.AttackStandA;
            }
        }
    }
}

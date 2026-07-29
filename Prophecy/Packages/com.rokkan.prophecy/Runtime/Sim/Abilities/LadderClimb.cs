using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Climbing ladders and ropes.
    ///
    /// <para>Gravity is not "turned off" while climbing — nothing here reaches into the gravity
    /// module. This module simply writes the velocity it wants every tick, and since it runs
    /// later in the tick order, that is the velocity that gets integrated. The same result, with
    /// no ability holding a switch that belongs to another one.</para>
    ///
    /// <para>Ladders are <b>climbable volumes</b> rather than solids: you stand in front of one,
    /// not on it, and it must never block walking past. The collision world keeps them in a
    /// separate list for exactly that reason, and the baker fills it from trigger colliders — the
    /// things Unity already treats as non-blocking.</para>
    ///
    /// <para>Dismount has three doors, because a ladder you can get stuck on is worse than no
    /// ladder: step off sideways, jump off, or climb past the end. The jump reads
    /// <c>ClimbDismountJumpHeight</c> rather than the full jump, so leaving is a hop rather than
    /// a launch.</para>
    /// </summary>
    public sealed class LadderClimb : AbilityModule
    {
        private const LockFlags ClimbLock = LockFlags.Move | LockFlags.Jump;

        /// <summary>How far below the feet the climbable probe reaches.</summary>
        private const float ReachDown = 0.4f;

        /// <summary>
        /// The volume used to ask "is there something to climb here".
        ///
        /// <para>Reaches below the feet as well as covering the body, because a climbable ends
        /// level with the surface it serves. A rope tied to the platform you are standing on, or a
        /// ladder topping out at its walkway, does not overlap a body that extends purely upward
        /// from the feet — so the one spot where you most obviously want to step onto it is the
        /// one spot a body-sized test says there is nothing there. It matters twice: for mounting,
        /// and for staying attached at the very top.</para>
        /// </summary>
        private static Collision.Aabb ClimbProbe(CharacterState state) =>
            Collision.Aabb.FromFootSize(
                state.Position - new Vector2(0f, ReachDown),
                state.BodySize + new Vector2(0f, ReachDown));

        private readonly MovementTuningData _tuning;

        public LadderClimb(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.LadderClimb;
        public override int Order => ModuleOrder.LadderClimb;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (sim.HoldsLock(this))
            {
                if (sim.State.Attachment != AttachmentKind.Ladder)
                {
                    sim.ReleaseLock(this);
                    return;
                }

                Climb(sim, in input);
                return;
            }

            TryMount(sim, in input);
        }

        private void TryMount(CharacterSim sim, in InputFrame input)
        {
            var state = sim.State;

            if (state.Attachment != AttachmentKind.None) return;
            if (!sim.Can(LockFlags.Move)) return;
            if (Mathf.Abs(input.Move.y) < _tuning.ClimbMountThreshold) return;

            if (!sim.World.TryGetClimbable(ClimbProbe(state), out var climbable)) return;

            // Mounting downward is only meaningful if there is climbable below the feet to descend
            // onto. At the foot of a wall ladder there is not, so crouching there walks and
            // crouches as usual; on a rope hanging through the platform you are standing on, there
            // is, so holding down takes you onto it. One rule, and it reads correctly at both ends.
            if (input.Move.y < 0f && climbable.Box.Min.y >= state.Position.y - 0.05f) return;

            if (!sim.TryLock(this, ClimbLock, LockPriority.Movement)) return;

            state.Attachment = AttachmentKind.Ladder;
            state.AttachmentAnchor = new Vector2(climbable.Box.Center.x, state.Position.y);
            state.Velocity = Vector2.zero;
            state.Stance = Stance.Stand;

            // Descending onto a rope means leaving the platform overhead behind.
            if (input.Move.y < 0f) state.DropThrough = true;
        }

        private void Climb(CharacterSim sim, in InputFrame input)
        {
            var state = sim.State;

            // Held for as long as the climb lasts. A rope hangs through the platform it is tied
            // to, so descending one means passing down through that platform every tick — not
            // once at the moment of mounting.
            state.DropThrough = true;

            if (!sim.World.TryGetClimbable(ClimbProbe(state), out var climbable))
            {
                Dismount(sim);
                return;
            }

            if (input.Jump.Pressed)
            {
                Dismount(sim);
                state.Velocity.y = _tuning.ClimbDismountJumpVelocity;
                state.Velocity.x = state.Facing * _tuning.WalkSpeed * 0.5f;
                state.Stance = Stance.Air;
                return;
            }

            float lateral = Mathf.Abs(input.Move.x) < _tuning.MoveDeadzone ? 0f : Mathf.Sign(input.Move.x);

            // Stepping off sideways only counts with the ladder actually left behind, so brushing
            // the stick does not drop you down a shaft.
            if (lateral != 0f && !sim.World.TryGetClimbable(
                    state.BodyAt(state.Position + new Vector2(lateral * state.BodySize.x, 0f)), out _))
            {
                Dismount(sim);
                state.Velocity.x = lateral * _tuning.WalkSpeed;
                state.Facing = lateral < 0f ? -1 : 1;
                return;
            }

            float vertical = Mathf.Abs(input.Move.y) < _tuning.MoveDeadzone ? 0f : Mathf.Sign(input.Move.y);

            state.Velocity = new Vector2(lateral * _tuning.ClimbLateralSpeed,
                                         vertical * _tuning.ClimbSpeed);

            // Climbing off the top is a dismount, not a stop — the climbable has run out.
            if (vertical > 0f && state.Body.Min.y >= climbable.Box.Max.y - 0.05f)
                Dismount(sim);
        }

        private void Dismount(CharacterSim sim)
        {
            // Must be cleared here, or stepping off the top of a rope drops the player straight
            // back through the platform they just climbed onto.
            sim.State.DropThrough = false;
            sim.State.Attachment = AttachmentKind.None;
            sim.ReleaseLock(this);
        }
    }
}

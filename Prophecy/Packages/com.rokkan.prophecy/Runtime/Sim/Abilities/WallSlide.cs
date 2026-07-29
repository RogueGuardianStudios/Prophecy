using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Slows a fall to a controlled scrape while pressed against a wall.
    ///
    /// <para>Mechanically tiny — it clamps downward velocity — but it is what makes a wall jump
    /// usable. Without it the player arrives at the wall already falling at terminal velocity and
    /// has a couple of ticks to react; with it, the wall becomes somewhere you can pause and aim
    /// from. It also reads as an intention: a character scraping down a wall is obviously about
    /// to do something with it.</para>
    ///
    /// <para>Requiring input toward the wall (the default) means brushing past a surface mid-jump
    /// does not silently halve your fall speed. Turning that off gives the stickier, more forgiving
    /// feel some platformers prefer, which is why it is a knob and not a decision.</para>
    ///
    /// <para>It runs after the jumps in the tick order, and defers to the movement lock. Both
    /// matter: clamping before the jumps would eat a wall jump's launch on the tick it happens,
    /// and ignoring the lock would let the player re-stick to the wall during the wall jump's
    /// control lock, which is precisely what that lock exists to prevent.</para>
    /// </summary>
    public sealed class WallSlide : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private bool _sliding;

        public WallSlide(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.WallSlide;
        public override int Order => ModuleOrder.WallSlide;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>True while scraping. Debug overlay, and later the dust and the pose.</summary>
        public bool IsSliding => _sliding;

        /// <summary>Which side the wall is on while sliding: -1, +1, or 0 when not.</summary>
        public int WallSide { get; private set; }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            _sliding = false;
            WallSide = 0;

            var state = sim.State;

            if (state.Grounded) return;
            if (state.Attachment != AttachmentKind.None) return;
            if (state.Velocity.y > 0f) return;          // still rising: nothing to slow yet
            if (!sim.Can(LockFlags.Move)) return;       // a wall jump's control lock is in force

            int wall = WallDirection(sim);
            if (wall == 0) return;

            if (_tuning.WallSlideNeedsInput)
            {
                float axis = input.Move.x;
                if (Mathf.Abs(axis) < _tuning.MoveDeadzone) return;
                if (axis > 0f != wall > 0) return;
            }

            _sliding = true;
            WallSide = wall;
            state.Facing = wall;

            if (state.Velocity.y < -_tuning.WallSlideSpeed)
                state.Velocity.y = -_tuning.WallSlideSpeed;
        }

        public override void Reset()
        {
            _sliding = false;
            WallSide = 0;
        }

        private int WallDirection(CharacterSim sim)
        {
            var body = sim.State.Body;

            if (sim.World.HasWall(body, 1, _tuning.WallProbeDistance)) return 1;
            if (sim.World.HasWall(body, -1, _tuning.WallProbeDistance)) return -1;
            return 0;
        }
    }
}

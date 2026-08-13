using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// The committed door crossing — Zelda II's screens, not Mario's checkpoints (Matt's
    /// correction of the first build, which only WATCHED the feet cross a line). Stepping
    /// into a doorway is a choice the body then keeps: an Absolute lock takes the pad away,
    /// the walk carries itself through to the far side, the room changes as the crossing
    /// COMPLETES, and control returns on the next screen. There is no backing out, because
    /// there is no halfway — a door was entered or it was not.
    ///
    /// <para>The camera's half of the same statement is the per-room horizontal clamp: you
    /// cannot see past a door you have not committed to. Between them the door is a real
    /// gate, not a checkpoint flag.</para>
    ///
    /// <para>The drive re-asserts its axis speed every tick — the dive's last-word pattern —
    /// and gravity keeps running, so a doorway onto a drop is walked through and then fallen
    /// from, exactly like walking off any ledge.</para>
    /// </summary>
    public sealed class DoorTransit : AbilityModule
    {
        private const LockFlags TransitLock =
            LockFlags.Move | LockFlags.Turn | LockFlags.Jump | LockFlags.Attack | LockFlags.Defend;

        private readonly MovementTuningData _tuning;

        private bool _transiting;
        private int _doorIndex;
        private int _targetRoom;
        private int _direction;
        private float _startAxis;
        private float _endAxis;

        public DoorTransit(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.DoorTransit;
        public override int Order => ModuleOrder.DoorTransit;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>Mid-crossing. The overlay's lock line names this module anyway; this is
        /// for the camera and tests.</summary>
        public bool IsTransiting => _transiting;

        /// <summary>Where the crossing is going. Meaningful only while transiting.</summary>
        public int TargetRoom => _targetRoom;

        /// <summary>True when the crossing runs along X (a walked doorway); false for a
        /// vertical one. The camera pans the same axis.</summary>
        public bool TransitAxisX => _axisX;

        /// <summary>The axis value the body will be delivered at — known from the step-in,
        /// which is what lets the camera aim its pan at a fixed destination.</summary>
        public float DeliveryAxis => _endAxis;

        private bool _axisX;

        /// <summary>
        /// How far through the crossing the body is, 0 at the step-in, 1 at delivery. THE
        /// CAMERA'S SYNC SIGNAL (Matt: the transition must move with the walk, not on its own
        /// clock): the rig blends its clamps by this, so the frame arrives exactly when the
        /// feet do.
        /// </summary>
        public float Progress
        {
            get
            {
                if (!_transiting) return 0f;
                float span = _endAxis - _startAxis;
                if (Mathf.Approximately(span, 0f)) return 1f;
                return Mathf.Clamp01((_currentAxis - _startAxis) / span);
            }
        }

        private float _currentAxis;

        public override void Reset()
        {
            _transiting = false;
        }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_transiting)
            {
                Continue(sim);
                return;
            }

            TryEnter(sim);
        }

        private void TryEnter(CharacterSim sim)
        {
            var state = sim.State;
            var doors = sim.World.Doors;

            for (int i = 0; i < doors.Count; i++)
            {
                var door = doors[i];
                if (!door.Box.Contains(state.Position)) continue;

                // Just inside the near face, so the half we stand in is the side we came
                // from — the crossing aims at the other one.
                int hereSide = door.SideOf(state.Position);
                _targetRoom = hereSide == door.RoomMinSide ? door.RoomMaxSide : door.RoomMinSide;
                _direction = _targetRoom == door.RoomMaxSide ? 1 : -1;
                _doorIndex = i;
                _transiting = true;

                // The exit distance is half the beat (speed is the other half): it must at
                // least clear the volume, or the release point would re-enter the door.
                float carry = Mathf.Max(0.2f, _tuning.DoorExitDistance);

                _axisX = door.CrossesX;
                _startAxis = door.CrossesX ? state.Position.x : state.Position.y;
                _currentAxis = _startAxis;
                _endAxis = _direction > 0
                    ? (door.CrossesX ? door.Box.Max.x : door.Box.Max.y) + carry
                    : (door.CrossesX ? door.Box.Min.x : door.Box.Min.y) - carry;

                sim.ForceLock(this, TransitLock, LockPriority.Absolute);
                if (door.CrossesX) state.Facing = _direction;
                return;
            }
        }

        private void Continue(CharacterSim sim)
        {
            var state = sim.State;
            var door = sim.World.Doors[_doorIndex];

            if (door.CrossesX) state.Velocity.x = _direction * _tuning.DoorWalkSpeed;
            else state.Velocity.y = _direction * _tuning.DoorWalkSpeed;

            _currentAxis = door.CrossesX ? state.Position.x : state.Position.y;

            if (_direction > 0 ? _currentAxis < _endAxis : _currentAxis > _endAxis) return;

            _transiting = false;
            sim.ReleaseLock(this);
            sim.CommitRoomChange(_targetRoom);
        }
    }
}

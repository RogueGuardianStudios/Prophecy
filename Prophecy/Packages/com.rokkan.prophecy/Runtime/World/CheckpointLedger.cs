using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// Where the player may be put back: the departure point of every scene they have left,
    /// and the entrance of the room they are in. Plain data, deliberately apart from the
    /// scene-swap machinery — this is the state a save file will want, and it was invisible
    /// inside a coroutine-owning scene director.
    ///
    /// <para><b>Departures are keyed by scene, not a single slot.</b> Every transition records
    /// a departure, so by the time a return arrival is resolved the "last" departure is the
    /// scene being left, not the one being returned to. That bug shipped for about a minute.</para>
    ///
    /// <para><b>The room entry is Zelda II's screen-entrance checkpoint</b> (Matt's fall
    /// rule): where the player last legitimately entered the current room — a scene arrival's
    /// placement, or the landing pad of the last door crossed. A fall costs its toll and puts
    /// you back here, not at the scene's spawn half a level away.</para>
    /// </summary>
    public sealed class CheckpointLedger
    {
        /// <summary>A placement: the feet (surface height included — a departure from a
        /// bridge deck or a cave floor returns to that surface), facing, and room.</summary>
        public readonly struct Placement
        {
            public readonly Vector3 Feet;
            public readonly int Facing;
            public readonly int Room;

            public Placement(Vector3 feet, int facing, int room)
            {
                Feet = feet;
                Facing = facing;
                Room = room;
            }
        }

        private readonly Dictionary<string, Placement> _departures =
            new Dictionary<string, Placement>();

        private Placement _roomEntry;

        /// <summary>True once a room entry has been recorded this run.</summary>
        public bool HasRoomEntry { get; private set; }

        /// <summary>The current fall checkpoint. Meaningless until <see cref="HasRoomEntry"/>.</summary>
        public Placement RoomEntry => _roomEntry;

        public void RecordDeparture(string sceneName, Vector3 feet, int facing, int room)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            _departures[sceneName] = new Placement(feet, facing, room);
        }

        public bool TryGetDeparture(string sceneName, out Placement departure) =>
            _departures.TryGetValue(sceneName, out departure);

        public void RecordRoomEntry(Vector3 feet, int facing, int room)
        {
            _roomEntry = new Placement(feet, facing, room);
            HasRoomEntry = true;
        }
    }
}

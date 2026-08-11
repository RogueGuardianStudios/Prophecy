using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// The camera's framing limits for one room. Purely presentation — the sim's rooms are
    /// graph identities with no shape, and this is the one place a room gets a spatial
    /// opinion: how far the frame may travel while the body is inside it.
    /// <see cref="LaneCameraRig"/> finds these and SLIDES its clamp to the current room's
    /// values when a door is crossed; a room with no bounds entry keeps the scene's global
    /// ones from the descriptor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomBounds : MonoBehaviour
    {
        [SerializeField, Tooltip("Which room these bounds frame.")]
        private int _room = 1;

        [SerializeField, Tooltip("The camera never shows below this.")]
        private float _floorY = -3.6f;

        [SerializeField, Tooltip("The camera never shows above this.")]
        private float _ceilingY = 40f;

        [SerializeField, Tooltip("The camera never shows left of this. Put it at the room's " +
                                 "west door: not seeing past a door you have not committed " +
                                 "to is what makes a door a door.")]
        private float _minX = -100000f;

        [SerializeField, Tooltip("The camera never shows right of this — the room's east door.")]
        private float _maxX = 100000f;

        public int Room => _room;
        public float FloorY => _floorY;
        public float CeilingY => _ceilingY;
        public float MinX => _minX;
        public float MaxX => _maxX;
    }
}

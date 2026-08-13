using Rokkan.Prophecy.Sim;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Accumulates one button's edges between sim ticks.
    ///
    /// <para>Presentation samples at render rate; the sim consumes at a fixed 60 Hz. Those rates do
    /// not divide evenly, and the mismatch destroys input in both directions if nothing bridges it:
    /// at high frame rates a press and release can happen entirely inside one tick and vanish, and
    /// at low frame rates a frame can retire two ticks or none at all, so a press sampled during a
    /// zero-tick frame would be gone before anything looked at it.</para>
    ///
    /// <para>So edges are <b>sticky until consumed</b>, not until the next frame.
    /// <see cref="Sample"/> ORs in whatever happened this frame; <see cref="Consume"/> hands the
    /// accumulated picture to one tick and clears only the edges, leaving the held state alone
    /// because holding is a level, not an event.</para>
    ///
    /// <para>Plain struct with no engine types, so the rule it implements is unit-testable without
    /// a device, a scene or a frame — which matters, because "a fast tap must still register" is
    /// exactly the kind of thing that silently rots.</para>
    /// </summary>
    public struct ButtonLatch
    {
        private bool _held;
        private bool _pressed;
        private bool _released;

        /// <summary>Record this frame's raw button level, deriving edges from the previous one.</summary>
        public void Sample(bool isDown)
        {
            if (isDown && !_held) _pressed = true;
            else if (!isDown && _held) _released = true;

            _held = isDown;
        }

        /// <summary>
        /// Hand the accumulated state to a tick and reset the edges. The held level survives,
        /// because a button that is still down is still down on the next tick too.
        /// </summary>
        public ButtonState Consume()
        {
            var state = new ButtonState(_held, _pressed, _released);
            _pressed = false;
            _released = false;
            return state;
        }

        /// <summary>Drop everything, including the held level. For focus loss and scene changes,
        /// where a button may have been released while nothing was watching.</summary>
        public void Clear()
        {
            _held = false;
            _pressed = false;
            _released = false;
        }

        /// <summary>
        /// Adopt the current physical level without inventing an edge. The menu-close path:
        /// a <see cref="Clear"/> there sets the level false while the finger is still on B,
        /// so the very next <see cref="Sample"/> reads a rising edge and the close becomes a
        /// dodge on the first live tick. Rebaselining says "this is where the button already
        /// was" — a held button stays a level, and only an actual new press is ever an event.
        /// </summary>
        public void Rebaseline(bool isDown)
        {
            _held = isDown;
            _pressed = false;
            _released = false;
        }
    }
}

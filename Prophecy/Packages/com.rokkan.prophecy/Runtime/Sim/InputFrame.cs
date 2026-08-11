using UnityEngine;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// One button's state for a single sim tick. Edges are carried explicitly rather than being
    /// derived inside the sim, because presentation samples at render rate and the sim consumes
    /// at a fixed 60 Hz — a press and release inside one tick would otherwise vanish entirely.
    /// The capture layer latches edges between ticks so nothing is lost.
    /// </summary>
    public readonly struct ButtonState
    {
        public readonly bool Held;

        /// <summary>Went down at least once since the previous tick.</summary>
        public readonly bool Pressed;

        /// <summary>Came up at least once since the previous tick.</summary>
        public readonly bool Released;

        public ButtonState(bool held, bool pressed, bool released)
        {
            Held = held;
            Pressed = pressed;
            Released = released;
        }

        public static readonly ButtonState None = new ButtonState(false, false, false);

        /// <summary>A button held down with no edges this tick — the common case when writing tests.</summary>
        public static ButtonState Holding => new ButtonState(true, false, false);

        /// <summary>A fresh press: down, with the press edge set.</summary>
        public static ButtonState Press => new ButtonState(true, true, false);

        /// <summary>A release: up, with the release edge set.</summary>
        public static ButtonState Release => new ButtonState(false, false, true);
    }

    /// <summary>
    /// The complete input picture for one tick — the only channel by which presentation may
    /// influence the sim. MonoBehaviours fill this from the Input System and hand it over;
    /// they never call into gameplay directly.
    ///
    /// <para>It carries the whole final moveset from day one, including abilities that ship
    /// disabled, so that unlocking one later is a flag flip rather than an input-plumbing change.</para>
    /// </summary>
    public readonly struct InputFrame
    {
        /// <summary>Analog stick / d-pad, each axis in [-1,1]. In side-scroll, Y is used for
        /// crouch and down-thrust intent; in top-down it is the second movement axis.</summary>
        public readonly Vector2 Move;

        public readonly ButtonState Jump;
        public readonly ButtonState Attack;
        public readonly ButtonState Block;
        public readonly ButtonState Parry;
        public readonly ButtonState Dodge;
        public readonly ButtonState FlameArt;
        public readonly ButtonState Interact;
        public readonly ButtonState RunToggle;
        public readonly ButtonState DrinkFlask;

        public InputFrame(
            Vector2 move,
            ButtonState jump = default,
            ButtonState attack = default,
            ButtonState block = default,
            ButtonState parry = default,
            ButtonState dodge = default,
            ButtonState flameArt = default,
            ButtonState interact = default,
            ButtonState runToggle = default,
            ButtonState drinkFlask = default)
        {
            Move = move;
            Jump = jump;
            Attack = attack;
            Block = block;
            Parry = parry;
            Dodge = dodge;
            FlameArt = flameArt;
            Interact = interact;
            RunToggle = runToggle;
            DrinkFlask = drinkFlask;
        }

        /// <summary>No input at all — the neutral frame.</summary>
        public static readonly InputFrame Empty = new InputFrame(Vector2.zero);

        /// <summary>Horizontal-only helper for tests and for the common walk case.</summary>
        public static InputFrame MoveX(float x) => new InputFrame(new Vector2(x, 0f));
    }
}

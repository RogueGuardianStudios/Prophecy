using System;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// Which play mode a character is being simulated in. Zelda II's dual structure: a top-down
    /// overworld and side-scroll action areas. One motor serves both — modules declare which
    /// spaces they are valid in and the sim skips the rest, so there is a single character, a
    /// single state, and a single save shape rather than two forked controllers.
    /// </summary>
    [Flags]
    public enum MovementSpace
    {
        None = 0,

        /// <summary>Side-on action. Gravity, jumping, stance combat; movement on X/Y with Z railed.</summary>
        SideScroll = 1 << 0,

        /// <summary>Top-down overworld traversal. No gravity, movement on X/Z.</summary>
        TopDown = 1 << 1,

        Both = SideScroll | TopDown,
    }

    /// <summary>
    /// The posture that gates Zelda II's high/low attacks. Standing attacks high, crouching
    /// attacks low, and airborne enables the down-thrust — so stance is a first-class piece of
    /// combat state, not a cosmetic animation flag.
    /// </summary>
    public enum Stance
    {
        Stand,
        Crouch,
        Air,
    }

    /// <summary>
    /// What kind of climbable something is, and therefore what it must be attached to.
    ///
    /// <para>The sim treats them almost identically — both are volumes you hold position in and
    /// move vertically through. The distinction earns its place in how they are <i>anchored</i>:
    /// a ladder is fixed against a wall, a rope hangs from a platform you can pass through. Those
    /// are placement rules a generator can follow and a validator can check, and getting them
    /// wrong produces a ladder floating in space that still works, which is worse than one that
    /// visibly does not.</para>
    /// </summary>
    public enum ClimbableKind
    {
        /// <summary>Fixed against a wall. Climbed facing the surface.</summary>
        Ladder,

        /// <summary>Hangs from a pass-through platform, free on both sides.</summary>
        Rope,
    }

    /// <summary>What a character is holding onto. See <see cref="CharacterState.Attachment"/>.</summary>
    public enum AttachmentKind
    {
        None,

        /// <summary>Hanging from a ledge by the hands.</summary>
        Ledge,

        /// <summary>On a ladder or rope.</summary>
        Ladder,
    }

    /// <summary>
    /// How the run button decides top speed.
    ///
    /// <para>Three genuinely different feels, and the argument for each is real, so this is a
    /// setting rather than a decision baked into code. <see cref="Toggle"/> is the Zelda II
    /// answer — no analog stick existed, and committing to a speed is itself a choice.
    /// <see cref="Hold"/> is what most players' hands now expect, and it self-cancels when they
    /// let go in a panic. <see cref="AnalogBlend"/> ignores the button entirely and reads stick
    /// deflection, which is the modern default and the one that makes a keyboard feel worst.</para>
    ///
    /// <para>They were previously two overlapping booleans, which left a nonsense state (toggle
    /// and blend both on) and no way to express hold at all.</para>
    /// </summary>
    public enum RunMode
    {
        /// <summary>Press to switch between walking and running; it stays where you left it.</summary>
        Toggle,

        /// <summary>Run while the button is held, walk when released.</summary>
        Hold,

        /// <summary>Stick deflection blends walk to run. The run button is ignored.</summary>
        AnalogBlend,
    }

    /// <summary>
    /// Capabilities an <see cref="ActionLock"/> can suppress.
    ///
    /// <para>This exists because "ability modules never reference each other" is only survivable
    /// if something arbitrates between them. Attack must suppress walking; hit-react must
    /// interrupt attack; parry must be able to cancel attack recovery. Without a shared
    /// vocabulary of what is currently forbidden, each of those becomes a direct module-to-module
    /// reference and the architecture rots on the first combat feature.</para>
    /// </summary>
    [Flags]
    public enum LockFlags
    {
        None = 0,

        /// <summary>Horizontal locomotion input is ignored.</summary>
        Move = 1 << 0,

        /// <summary>Facing cannot change — matters for committed attacks.</summary>
        Turn = 1 << 1,

        /// <summary>Jump cannot be initiated.</summary>
        Jump = 1 << 2,

        /// <summary>No new attack can start.</summary>
        Attack = 1 << 3,

        /// <summary>Block/parry cannot be raised.</summary>
        Defend = 1 << 4,

        /// <summary>No item can be used — flask drinks, and whatever joins them. Its own flag
        /// because "may I swing" and "may I swig" are different questions: a guard forbids
        /// attacking but not drinking, and which locks forbid drinking is a tuning decision
        /// that needs a bit to be expressed in.</summary>
        Item = 1 << 5,

        /// <summary>Fully committed — the usual state during an attack's active frames.</summary>
        All = Move | Turn | Jump | Attack | Defend | Item,
    }
}

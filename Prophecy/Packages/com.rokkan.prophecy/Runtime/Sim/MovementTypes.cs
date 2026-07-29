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

        /// <summary>Fully committed — the usual state during an attack's active frames.</summary>
        All = Move | Turn | Jump | Attack | Defend,
    }
}

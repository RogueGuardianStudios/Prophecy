namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// Anything that can supply a character's intent for one tick.
    ///
    /// <para><b>This is the line between "the AI drives the game" and "the AI is part of it", and
    /// it is worth being blunt about which one this project wants.</b> A brain produces an
    /// <see cref="InputFrame"/> — the same struct a gamepad produces — and the simulation executes
    /// it. The brain presses buttons. It does not move anything.</para>
    ///
    /// <para>The test that proves it: you could hand a player's input to an enemy, or a planner's
    /// output to the player, and neither the simulation nor any ability module would notice. If
    /// that ever stops being true, something below this line has learned who is driving.</para>
    ///
    /// <para><b>The rule that erodes silently.</b> A brain that writes <c>sim.State.Position</c>,
    /// moves a transform, or calls an ability directly has stepped over this line, and nothing will
    /// fail to warn you — the game will look correct and the character will simply be outside its
    /// own rules: no action locks, no i-frames, no cover, and nothing a headless test can
    /// reproduce. Actions press buttons. Only ever buttons.</para>
    /// </summary>
    public interface IInputSource
    {
        /// <summary>
        /// This tick's intent.
        ///
        /// <para>"Consume" is the contract: edge flags — pressed, released — are true for exactly
        /// one read. A source that returned the same frame twice would let one button press start
        /// two attacks, which is the bug the player's latch already exists to prevent.</para>
        /// </summary>
        InputFrame ConsumeFrame();
    }
}

using System.Collections.Generic;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// Runs a chain of attacks: one link at a time, with the next input buffered so a slightly
    /// early press still chains.
    ///
    /// <para><b>Buffering is the whole difference between a combo that feels responsive and one
    /// that feels broken.</b> Players press the next attack as the current one is still landing,
    /// not after it finishes — so an unbuffered chain drops most inputs and reads as the game
    /// ignoring you. This remembers a press for a few ticks and spends it the moment the cancel
    /// window opens. It is the jump buffer's logic, applied to attacks.</para>
    ///
    /// <para><b>The cancel window is the gate, and it is the same gate the arbiter uses.</b>
    /// <c>ActionLock</c> already refuses a takeover unless the holder has opened its cancel
    /// window; a chain link opening early would let the player skip the commitment that makes an
    /// attack a decision. So the runner never chains before the window, and the same window is what
    /// lets a parry interrupt recovery.</para>
    ///
    /// <para>Plain C# over a lookup rather than a graph of object references, so a moveset can come
    /// from an asset and a save file can name where a chain got to.</para>
    /// </summary>
    public sealed class ComboRunner
    {
        private readonly AttackTimeline _timeline = new AttackTimeline();
        private readonly IReadOnlyDictionary<string, AttackDefinition> _moveset;

        private long _bufferedPressTick = long.MinValue;
        private int _bufferTicks;

        public ComboRunner(IReadOnlyDictionary<string, AttackDefinition> moveset, int bufferTicks = 10)
        {
            _moveset = moveset;
            _bufferTicks = bufferTicks < 0 ? 0 : bufferTicks;
        }

        public AttackTimeline Timeline => _timeline;

        public bool IsAttacking => _timeline.IsArmed;

        /// <summary>The attack currently running, or null.</summary>
        public AttackDefinition Current => _timeline.Definition;

        /// <summary>How many links deep the chain is. 0 when idle, 1 on the opening attack.</summary>
        public int ChainDepth { get; private set; }

        /// <summary>Ticks left on a remembered press. Debug overlay.</summary>
        public int BufferedTicksRemaining(long currentTick)
        {
            if (_bufferedPressTick == long.MinValue) return 0;
            long left = _bufferTicks - (currentTick - _bufferedPressTick);
            return left > 0 ? (int)left : 0;
        }

        /// <summary>Record an attack press. Consumed when a chain or a fresh attack can start.</summary>
        public void QueueInput(long currentTick) => _bufferedPressTick = currentTick;

        /// <summary>Begin a named attack from neutral, ignoring any chain state.</summary>
        public bool Begin(string attackId, in AttackModifiers modifiers)
        {
            if (_moveset == null || string.IsNullOrEmpty(attackId)) return false;
            if (!_moveset.TryGetValue(attackId, out var definition) || definition == null) return false;

            _timeline.Arm(definition, modifiers);
            ChainDepth = 1;
            _bufferedPressTick = long.MinValue;
            return true;
        }

        public bool Begin(string attackId) => Begin(attackId, AttackModifiers.None);

        /// <summary>
        /// Advance one tick. Chains into the next link if a buffered press is waiting and the
        /// cancel window is open; ends the chain when the action runs out with nothing queued.
        /// </summary>
        /// <returns>True if a new link started this tick.</returns>
        public bool Advance(long currentTick, in AttackModifiers modifiers)
        {
            if (!_timeline.IsArmed) return false;

            _timeline.Advance();
            ExpireBuffer(currentTick);

            if (TryChain(currentTick, modifiers)) return true;

            // Nothing queued and the move is over: back to neutral.
            if (_timeline.IsComplete) End();

            return false;
        }

        public bool Advance(long currentTick) => Advance(currentTick, AttackModifiers.None);

        /// <summary>Abandon the chain — a hit-react, a scene change, death.</summary>
        public void End()
        {
            _timeline.Disarm();
            ChainDepth = 0;
            _bufferedPressTick = long.MinValue;
        }

        private bool TryChain(long currentTick, in AttackModifiers modifiers)
        {
            if (_bufferedPressTick == long.MinValue) return false;

            var current = _timeline.Definition;
            if (current == null || string.IsNullOrEmpty(current.ChainsInto)) return false;

            // The cancel window is the gate. Before it, the player is committed — which is what
            // makes throwing the first attack a decision rather than a free action.
            if (!_timeline.IsCancellable) return false;

            if (!_moveset.TryGetValue(current.ChainsInto, out var next) || next == null) return false;

            _timeline.Arm(next, modifiers);
            ChainDepth++;
            _bufferedPressTick = long.MinValue;
            return true;
        }

        private void ExpireBuffer(long currentTick)
        {
            if (_bufferedPressTick == long.MinValue) return;
            if (currentTick - _bufferedPressTick <= _bufferTicks) return;

            _bufferedPressTick = long.MinValue;
        }
    }
}

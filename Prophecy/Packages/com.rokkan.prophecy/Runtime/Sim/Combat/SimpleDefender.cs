using System.Collections.Generic;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// The receiving end for a thing with no simulation — a training dummy today, a crate or
    /// a destructible tomorrow. Owns the vitals and answers hits through the same
    /// <see cref="DamageGateChain"/> a <see cref="CharacterSim"/> walks, so "what a hit means"
    /// has exactly one shape however simple the target.
    ///
    /// <para>Before this existed, the dummy's hit rules lived in its MonoBehaviour — the one
    /// place in the project where presentation decided a gameplay outcome, and a divergence
    /// factory: every future hittable that was not a full character would have grown its own
    /// copy of the damage rules. Gates are empty by default; a dummy that one day blocks adds
    /// a gate here rather than growing an if in a component.</para>
    /// </summary>
    public sealed class SimpleDefender : IDefendable
    {
        private readonly List<IDamageGate> _gates = new List<IDamageGate>();

        public Vitals Vitals { get; } = new Vitals();

        /// <summary>Which way the body faces. A dummy's is set by whatever poses it.</summary>
        public int Facing { get; set; } = 1;

        /// <summary>Add a gate in precedence order — the same insertion rule the character
        /// sim applies, so a gate means the same thing wherever it is mounted.</summary>
        public void AddGate(IDamageGate gate)
        {
            if (gate == null) return;

            int i = 0;
            while (i < _gates.Count && _gates[i].GateOrder <= gate.GateOrder) i++;
            _gates.Insert(i, gate);
        }

        /// <summary>Nothing here takes locks — a gate that asks is told no, which correctly
        /// reads as "that shield is not being held up".</summary>
        public bool HoldsLock(object owner) => false;

        /// <summary>A body with no motion to write ignores shoves. The day a simple target
        /// needs real pushback is the day it needs a sim, not a bigger defender.</summary>
        public void Impulse(float velocityX, long tick) { }

        /// <summary>
        /// Take a hit: the gates get their say, and what none of them claims is damage.
        /// The unanswered-hit rule here is only the damage — nothing simple staggers.
        /// </summary>
        public HitResult ReceiveHit(in HitEvent hit)
        {
            if (!Vitals.IsAlive) return HitResult.Ignored;

            var answered = DamageGateChain.Answer(_gates, this, in hit, Vitals);
            if (answered.Outcome != HitOutcome.Continue) return answered;

            int landed = Vitals.ApplyDamage(hit.Damage, hit.Tick);
            return new HitResult(HitOutcome.Landed, landed);
        }
    }
}

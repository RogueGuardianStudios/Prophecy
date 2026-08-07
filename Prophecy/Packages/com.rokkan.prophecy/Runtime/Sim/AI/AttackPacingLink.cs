using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Sim.AI
{
    /// <summary>
    /// The one touchpoint between an enemy's input path and the fight's
    /// <see cref="AttackDirector"/>, applied once per tick between the brain deciding and
    /// the sim consuming: keep the standing attack request alive, and strip an attack press
    /// the director has not licensed. Static and plain, so the whole pacing seam is
    /// exercised headlessly by the same tests that run the sim.
    ///
    /// <para><b>The gate paces every decider.</b> The GOAP planner learns about tokens
    /// through a sensor and mostly never presses without one; the built-in fallback brain
    /// knows nothing of tokens and never has to — its presses simply do not survive this
    /// gate until its turn comes. One rule, both brains.</para>
    /// </summary>
    public static class AttackPacingLink
    {
        /// <summary>Apply the gate for one tick. Returns whether the token is held — the
        /// answer the host publishes for sensors to read.</summary>
        public static bool Apply(CombatState fight, EnemyIntent intent, in Percept percept,
                                 int combatId, AttackPool pool, int beatTicks,
                                 bool incapacitated, long tick)
        {
            if (fight == null || intent == null) return false;

            // The standing request: renewed every tick this enemy is a live, targeted
            // combatant. Going quiet — death, stun, lost target — is itself the signal
            // that lapses a granted token back to the pool.
            if (percept.HasTarget && !incapacitated)
                fight.Attacks.Request(combatId, pool, beatTicks, tick);

            bool holds = fight.Attacks.HoldsToken(combatId);

            // The veto: an unlicensed press dies here, before the sim ever sees it.
            if (!holds && intent.HasPendingAttack)
                intent.CancelAttackPress();

            return holds;
        }
    }
}

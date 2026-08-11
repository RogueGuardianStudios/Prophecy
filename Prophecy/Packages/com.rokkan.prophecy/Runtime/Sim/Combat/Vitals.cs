using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// How much of a character is left. Plain C#, owned by <see cref="CharacterSim"/>.
    ///
    /// <para><b>On the sim, not on a MonoBehaviour.</b> Death changes what the character may do and
    /// a headless test has to be able to kill something and assert what follows. Health living in
    /// presentation would put the one number every combat rule reads on the wrong side of the
    /// line.</para>
    ///
    /// <para>Deliberately thin. Resistances, armour, poise and overheal are all real things this
    /// will probably grow, and all of them are gate business — a rule that inspects the incoming
    /// hit and decides. Putting them here would rebuild the branch the gate chain exists to
    /// avoid.</para>
    /// </summary>
    public sealed class Vitals
    {
        private int _maxHealth = 20;

        public Vitals(int maxHealth = 20)
        {
            _maxHealth = Mathf.Max(1, maxHealth);
            Health = _maxHealth;
        }

        public int Health { get; private set; }

        public int MaxHealth
        {
            get => _maxHealth;
            set
            {
                _maxHealth = Mathf.Max(1, value);
                if (Health > _maxHealth) Health = _maxHealth;
            }
        }

        public bool IsAlive => Health > 0;

        /// <summary>Health is authored in QUARTERS: four to a heart (Matt). These are the
        /// HUD's read — the vessel count and how full the current vessel is.</summary>
        public const int QuartersPerHeart = 4;

        public int HeartCount => (MaxHealth + QuartersPerHeart - 1) / QuartersPerHeart;

        /// <summary>Fraction remaining, 0..1. For UI and for anything gated on being hurt.</summary>
        public float Fraction => Health / (float)_maxHealth;

        /// <summary>The tick health last went down. <c>long.MinValue</c> if never.</summary>
        public long LastDamagedTick { get; private set; } = long.MinValue;

        /// <summary>Apply damage and report how much actually landed — less than asked for when
        /// the blow was more than the character had left, which is what a damage counter that adds
        /// up wants.</summary>
        public int ApplyDamage(int amount, long tick)
        {
            if (amount <= 0 || !IsAlive) return 0;

            int applied = Mathf.Min(amount, Health);
            Health -= applied;
            LastDamagedTick = tick;
            return applied;
        }

        public void Heal(int amount)
        {
            if (amount <= 0 || !IsAlive) return;
            Health = Mathf.Min(_maxHealth, Health + amount);
        }

        /// <summary>Back to full. Respawn and scene transitions.</summary>
        public void Reset()
        {
            Health = _maxHealth;
            LastDamagedTick = long.MinValue;
        }

        public void Kill(long tick)
        {
            if (!IsAlive) return;
            Health = 0;
            LastDamagedTick = tick;
        }
    }
}

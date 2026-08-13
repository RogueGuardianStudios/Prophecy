namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// The player's earning: Resolve banked toward level-ups, level-ups banked toward the
    /// three-way choice, and the finale's power gate reading the total. Design bible §6.2–6.3.
    ///
    /// <para><b>Beside the <see cref="StatBlock"/>, not inside it.</b> Every combatant carries a
    /// stat block — the modifier algebra is the fight's — but only the player EARNS, and the
    /// economy's rules will move at a different speed than the stacking rules combat correctness
    /// rests on. Splitting them keeps a Resolve retune out of the file every hit resolves
    /// through, and gives the save system one object that is durable player state rather than a
    /// mixture of progress and transient buffs.</para>
    /// </summary>
    public sealed class Progression
    {
        private readonly StatBlock _stats;

        public Progression(StatBlock stats)
        {
            _stats = stats ?? new StatBlock();
        }

        /// <summary>Resolve banked toward the next level-up. Design bible §6.2 — this is XP.</summary>
        public int Resolve { get; private set; }

        /// <summary>Level-ups earned and not yet spent. The player picks which stat.</summary>
        public int UnspentLevels { get; private set; }

        /// <summary>
        /// Sum of the earned levels. What the finale's power gate reads (§6.3).
        ///
        /// <para>Progression stats only, by MEMBERSHIP — a haste potion must not read as
        /// complicity, and the answer must not depend on where an enum member happens to sit.</para>
        /// </summary>
        public int TotalLevels
        {
            get
            {
                int total = 0;
                var kinds = StatKinds.Progression;
                for (int i = 0; i < kinds.Length; i++) total += _stats.LevelOf(kinds[i]);
                return total;
            }
        }

        /// <summary>What the next rite costs, at the current totals — the number every seam
        /// and label divides by, computed in one place.</summary>
        public int NextRiteCost => _stats.Tuning.ResolveForNextLevel(TotalLevels);

        /// <summary>
        /// Bank Resolve, and convert it into level-ups at the authored thresholds.
        /// </summary>
        /// <returns>How many level-ups this award produced.</returns>
        public int AwardResolve(int amount)
        {
            if (amount <= 0) return 0;

            Resolve += amount;
            int earned = 0;

            // A loop rather than a single check: a Protector's essence is a large, deliberate
            // spike (§6.2) and can cross several thresholds at once. Awarding one level and
            // discarding the rest would quietly rob the moment the whole progression is built on.
            while (Resolve >= _stats.Tuning.ResolveForNextLevel(TotalLevels + earned))
            {
                Resolve -= _stats.Tuning.ResolveForNextLevel(TotalLevels + earned);
                earned++;

                if (TotalLevels + earned >=
                    StatBlock.MaxLevel * StatKinds.Progression.Length) break;   // all capped
            }

            UnspentLevels += earned;
            return earned;
        }

        /// <summary>
        /// Spend a banked level-up on a stat. Refuses if there is nothing banked or the stat is
        /// capped, and says so, because silently swallowing the choice is how a player loses a
        /// level-up and cannot tell you what happened.
        /// </summary>
        public bool SpendLevel(StatKind kind)
        {
            // Speed and anything like it can be modified but never earned — spending a level-up on
            // one would silently lose it, and would also change the design bible's three-way choice
            // into a four-way one.
            if (!kind.IsProgression()) return false;

            if (UnspentLevels <= 0) return false;
            if (_stats.LevelOf(kind) >= StatBlock.MaxLevel) return false;

            _stats.SetLevel(kind, _stats.LevelOf(kind) + 1);
            UnspentLevels--;
            return true;
        }

        /// <summary>Back to a fresh hero: nothing banked, nothing owed. The stat block's own
        /// reset handles the levels; this clears only what the economy holds.</summary>
        public void Reset()
        {
            Resolve = 0;
            UnspentLevels = 0;
        }
    }
}

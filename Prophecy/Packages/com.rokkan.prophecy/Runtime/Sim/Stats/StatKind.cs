namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// The three things Resolve can be spent on. Design bible §6.2.
    ///
    /// <para><b>Three, not six.</b> HopeFell's stat sheet is Strength / Dexterity / Vitality /
    /// Intelligence / Wisdom / Willpower plus an Aether floor — a generic RPG spread for a game
    /// with gear, classes and a skill tree. Prophecy is a Zelda II homage whose entire progression
    /// is "the bar filled, pick one of three", and every extra stat is a choice the player has to
    /// understand before the first level-up rather than a knob a designer gets to turn.</para>
    ///
    /// <para>They are levels, not scores. A stat is 1..8 and the numbers it produces are derived
    /// through <c>StatTuningData</c>, so what Might is <i>worth</i> stays a tuning decision and
    /// never gets baked into a save file.</para>
    /// </summary>
    public enum StatKind
    {
        /// <summary>Attack. Scales damage dealt.</summary>
        Might = 0,

        /// <summary>Magic. Sizes the Flame meter the flame-arts draw from.</summary>
        Flame = 1,

        /// <summary>Life. Sizes maximum health.</summary>
        Heart = 2,
    }

    /// <summary>
    /// When a modifier applies. Fixed stages rather than an arbitrary order, and that is the whole
    /// point of them existing.
    ///
    /// <para><b>Order has to be a property of the stage, not of the arrival.</b> HopeFell applies
    /// modifiers through a multicast event in subscription order, with <c>// add prority</c> left
    /// in the source as a note to future self. That makes +10 then ×2 give 40 while ×2 then +10
    /// gives 30 — the same gear in a different pickup order producing different numbers. Harmless
    /// in single player and fatal under host authority, where two machines must agree.</para>
    ///
    /// <para>Every operation within a stage commutes: flats sum, percents sum before being applied
    /// once, finals sum. So the arrival order genuinely cannot matter, rather than merely being
    /// unlikely to.</para>
    /// </summary>
    public enum StatStage
    {
        /// <summary>Added to the base value. Summed.</summary>
        Flat = 0,

        /// <summary>A fraction of the flat total. Summed, then applied once — so +10% and +20%
        /// give +30%, not +32%.</summary>
        Percent = 1,

        /// <summary>Added after scaling. Summed. For effects that must not be multiplied.</summary>
        Final = 2,
    }
}

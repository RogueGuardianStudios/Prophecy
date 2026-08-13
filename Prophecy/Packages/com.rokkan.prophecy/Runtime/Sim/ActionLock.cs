namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// A claim on the character by one module, suppressing the capabilities in
    /// <see cref="Flags"/> until released.
    ///
    /// <para>Deliberately ONE lock at a time rather than a stack. A stack raises immediate
    /// questions with no good answers — what happens when a mid-stack lock releases, how do
    /// overlapping flag sets combine — and every case the design actually needs (attack
    /// suppresses movement, hit-react interrupts attack, parry cancels attack recovery) is a
    /// straight priority comparison. If a genuine case for concurrency appears, it should be a
    /// deliberate change, not something that arrived by accident.</para>
    /// </summary>
    public readonly struct ActionLock
    {
        /// <summary>The module holding the lock. Only the owner may release it.</summary>
        public readonly object Owner;

        public readonly LockFlags Flags;

        /// <summary>Higher wins. A lock may only be taken over by a strictly higher priority,
        /// and only while <see cref="CancelWindowOpen"/> is set.</summary>
        public readonly int Priority;

        /// <summary>
        /// True during the window in which this action may be cancelled out of. Attack recovery
        /// opens it so a parry or dodge can interrupt the tail of a swing — the difference
        /// between combat that feels responsive and combat that feels like committing to a
        /// vending machine.
        /// </summary>
        public readonly bool CancelWindowOpen;

        public ActionLock(object owner, LockFlags flags, int priority, bool cancelWindowOpen = false)
        {
            Owner = owner;
            Flags = flags;
            Priority = priority;
            CancelWindowOpen = cancelWindowOpen;
        }

        public bool IsHeld => Owner != null;

        public static readonly ActionLock None = default;

        public ActionLock WithCancelWindow(bool open) =>
            new ActionLock(Owner, Flags, Priority, open);
    }

    /// <summary>
    /// Conventional priorities. Named rather than magic numbers so the ordering is arguable in
    /// one place. Gaps are intentional — new actions slot between without renumbering.
    /// </summary>
    public static class LockPriority
    {
        /// <summary>Ordinary locomotion-level actions.</summary>
        public const int Movement = 10;

        /// <summary>Blocking and other held defensive stances.</summary>
        public const int Defend = 20;

        /// <summary>Attacks — outrank movement, so a swing commits you.</summary>
        public const int Attack = 30;

        /// <summary>Dodge/parry — may cancel an attack, but only in its cancel window.</summary>
        public const int Reaction = 40;

        /// <summary>Being hit. Interrupts essentially everything.</summary>
        public const int HitReact = 90;

        /// <summary>Death, cutscenes, scene transitions. Never overridden.</summary>
        public const int Absolute = 1000;
    }
}

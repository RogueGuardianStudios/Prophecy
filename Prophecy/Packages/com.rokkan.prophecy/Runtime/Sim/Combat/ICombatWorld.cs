using System.Collections.Generic;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// One connected hit, reported the tick it lands.
    ///
    /// <para>Deliberately a record of what happened rather than an instruction to apply damage.
    /// The attacker's job ends at "this box touched that hurtbox for this much"; whether the
    /// target has i-frames up, is blocking, is already dead, or takes double damage from fire is
    /// the receiving end's decision. Collapsing the two would put every defensive rule inside the
    /// attack module, which is the reference tangle the architecture exists to prevent.</para>
    /// </summary>
    public readonly struct HitEvent
    {
        public readonly int AttackerId;
        public readonly int TargetId;

        /// <summary>Authored damage before any defensive gate has had a say.</summary>
        public readonly int Damage;

        /// <summary>The attacker's facing when it landed: -1 or +1. Knockback direction.</summary>
        public readonly int Facing;

        /// <summary>Sim tick the hit landed on. Two hits on the same tick are simultaneous.</summary>
        public readonly long Tick;

        /// <summary>Which attack landed it, and which of its boxes.</summary>
        public readonly string AttackId;
        public readonly int HitBoxIndex;

        public HitEvent(int attackerId, int targetId, int damage, int facing, long tick,
                        string attackId, int hitBoxIndex)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            Damage = damage;
            Facing = facing;
            Tick = tick;
            AttackId = attackId;
            HitBoxIndex = hitBoxIndex;
        }
    }

    /// <summary>
    /// Who else is in the fight, and where hits go.
    ///
    /// <para><b>Why this exists at all.</b> A <see cref="CharacterSim"/> knows about exactly one
    /// character — that is what makes it testable — so an attack module has no way to ask "who is
    /// standing in front of me". This is the one seam through which it can, and keeping it an
    /// interface means a test can supply three hurtboxes and a list, while the game supplies
    /// whatever an encounter turns out to be. Neither needs the other to exist.</para>
    ///
    /// <para>Plain C#, no engine types, because it is consumed inside the tick.</para>
    /// </summary>
    public interface ICombatWorld
    {
        /// <summary>
        /// Every hurtbox that can be hit this tick, including the attacker's own — the resolver
        /// filters by id and team, so callers do not have to build a different list per attacker.
        ///
        /// <para>Rebuilt or refreshed by the owner once per tick, not per attack: a list that
        /// changed midway through a tick would let two simultaneous attacks see different
        /// worlds.</para>
        /// </summary>
        IReadOnlyList<Hurtbox> Hurtboxes { get; }

        /// <summary>A hit connected. Apply it, gate it, or ignore it.</summary>
        void OnHit(in HitEvent hit);
    }
}

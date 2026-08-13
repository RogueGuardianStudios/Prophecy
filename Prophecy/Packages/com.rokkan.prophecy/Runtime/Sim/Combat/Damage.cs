namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>What happened to a hit once the defender had its say.</summary>
    public enum HitOutcome
    {
        /// <summary>No gate has decided yet. Only ever returned <i>by a gate</i>, meaning "not my
        /// business, ask the next one" — never the final answer.</summary>
        Continue,

        /// <summary>Connected. Damage was applied.</summary>
        Landed,

        /// <summary>Blocked. Reduced damage, pushback, and the defender keeps their footing.</summary>
        Blocked,

        /// <summary>Parried. No damage, and the <i>attacker</i> pays for it.</summary>
        Parried,

        /// <summary>Passed through invulnerability. Nothing happened to anyone.</summary>
        Invulnerable,

        /// <summary>There was nothing there to hit — already dead, or not a valid target.</summary>
        Ignored,
    }

    /// <summary>
    /// The defender's answer, handed back to the attacker.
    ///
    /// <para><b>Returned rather than swallowed, because a parry has to cost the attacker
    /// something.</b> A defensive system that only subtracts damage makes parrying a slightly
    /// better block; one that reports back lets the attacker stagger itself, which is what turns a
    /// correct read into an opening. The attacker learns its own fate from its own hit and no
    /// module has to reach across at anyone.</para>
    /// </summary>
    public readonly struct HitResult
    {
        public readonly HitOutcome Outcome;

        /// <summary>Damage that actually landed, after gates. Zero on a parry or i-frame.</summary>
        public readonly int DamageApplied;

        /// <summary>Ticks the attacker should be stunned for. Non-zero only on a parry.</summary>
        public readonly int AttackerStunTicks;

        public HitResult(HitOutcome outcome, int damageApplied = 0, int attackerStunTicks = 0)
        {
            Outcome = outcome;
            DamageApplied = damageApplied;
            AttackerStunTicks = attackerStunTicks;
        }

        /// <summary>Pass to the next gate.</summary>
        public static readonly HitResult Continue = new HitResult(HitOutcome.Continue);

        /// <summary>Nothing was there. The default when a hit reaches no one.</summary>
        public static readonly HitResult Ignored = new HitResult(HitOutcome.Ignored);

        /// <summary>True if the defender took nothing at all.</summary>
        public bool Negated => Outcome == HitOutcome.Parried ||
                               Outcome == HitOutcome.Invulnerable ||
                               Outcome == HitOutcome.Ignored;
    }

    /// <summary>
    /// What a damage gate may see and do of the body it defends: which way it faces, whether a
    /// given module still holds it, and the parking of a shove.
    ///
    /// <para><b>Narrow on purpose.</b> The gates used to take the whole <c>CharacterSim</c>,
    /// which welded the entire defensive pipeline to one kind of combatant — a training dummy's
    /// hits had to be resolved in a MonoBehaviour because the gates were unreachable for
    /// anything that was not a full character. Everything a gate has ever needed is these three
    /// members; anything hittable can offer them.</para>
    /// </summary>
    public interface IDefendable
    {
        /// <summary>Which way the body faces: -1 or +1. A shield answers only its front.</summary>
        int Facing { get; }

        /// <summary>True if <paramref name="owner"/> currently holds the body's action lock.
        /// A thing with no locks answers false — a shield nobody is holding up is not a shield.</summary>
        bool HoldsLock(object owner);

        /// <summary>Park a shove for the body's own tick — a blocked hit's pushback.</summary>
        void Impulse(float velocityX, long tick);
    }

    /// <summary>
    /// One rule that may answer an incoming hit before it becomes damage.
    ///
    /// <para><b>First wins, in a fixed order.</b> Invulnerability outranks a parry, a parry
    /// outranks a block, and anything that gets past all three is damage. Expressing that as an
    /// ordered chain rather than a branch is what lets a new defensive answer — a counter, a
    /// guard-break, an elemental ward — be added without editing the ones already there. It is the
    /// same argument as ability modules never referencing each other, applied to the receiving
    /// end.</para>
    ///
    /// <para>A gate is asked, it does not act. Returning <see cref="HitOutcome.Continue"/> means
    /// "not mine"; anything else ends the chain and is the answer.</para>
    /// </summary>
    public interface IDamageGate
    {
        /// <summary>Lower runs first. Named constants live in <see cref="GateOrder"/>.</summary>
        int GateOrder { get; }

        /// <summary>Answer this hit, or pass.</summary>
        HitResult Evaluate(IDefendable owner, in HitEvent hit);
    }

    /// <summary>
    /// The one walk of a gate chain, shared by everything hittable — a full character and a
    /// gateless dummy resolve "what does this hit mean" through the same lines, so the two can
    /// never disagree about what a reduced block or a spent parry does.
    /// </summary>
    public static class DamageGateChain
    {
        /// <summary>
        /// Ask each gate in precedence order. <see cref="HitResult.Continue"/> back means no
        /// gate claimed it — the caller applies its own unanswered-hit rule.
        /// </summary>
        public static HitResult Answer(System.Collections.Generic.List<IDamageGate> gates,
                                       IDefendable owner, in HitEvent hit, Vitals vitals)
        {
            for (int i = 0; i < gates.Count; i++)
            {
                var answer = gates[i].Evaluate(owner, in hit);
                if (answer.Outcome == HitOutcome.Continue) continue;

                // A gate that lets damage through in reduced form — a block — still deals it.
                if (answer.DamageApplied > 0)
                {
                    int applied = vitals.ApplyDamage(answer.DamageApplied, hit.Tick);
                    return new HitResult(answer.Outcome, applied, answer.AttackerStunTicks);
                }

                return answer;
            }

            return HitResult.Continue;
        }
    }

    /// <summary>
    /// A stun waiting to be taken up by the hit-react module on its next tick.
    ///
    /// <para><b>Parked in shared state rather than applied where it is decided.</b> A hit arrives
    /// during the <i>attacker's</i> tick, which may be before or after the defender's, so applying
    /// it on the spot would make the reaction's start depend on registration order. Parking it and
    /// letting the owner pick it up on its own tick is the same pattern <c>Grounded</c> and
    /// <c>Attachment</c> already use, and it costs at most one tick.</para>
    ///
    /// <para>Being hit and being parried are the same thing from here: you are stunned, for this
    /// long, shoved this way. What caused it is carried for presentation and for anything that
    /// wants to punish a whiffed attack differently from a clean hit.</para>
    /// </summary>
    public readonly struct PendingStun
    {
        public readonly int Ticks;

        /// <summary>-1 or +1: the direction the character is shoved.</summary>
        public readonly int Direction;

        /// <summary>Tick the stun was caused on.</summary>
        public readonly long Tick;

        public readonly HitOutcome Cause;

        public PendingStun(int ticks, int direction, long tick, HitOutcome cause)
        {
            Ticks = ticks;
            Direction = direction < 0 ? -1 : 1;
            Tick = tick;
            Cause = cause;
        }

        public bool IsSet => Ticks > 0;
    }

    /// <summary>
    /// A shove waiting to be applied on the shoved character's own tick.
    ///
    /// <para>Same law as <see cref="PendingStun"/>, for the same reason: a hit arrives during the
    /// <i>attacker's</i> tick, and writing the defender's velocity on the spot would make whether
    /// it integrates this tick or next depend on registration order. Parked here, it is consumed
    /// at the top of the defender's next tick, every time, whoever registered first.</para>
    ///
    /// <para>The strongest pending shove wins rather than the latest — two blocked hits landing on
    /// the same tick should push like the harder one, not like whichever resolved last.</para>
    /// </summary>
    public readonly struct PendingImpulse
    {
        /// <summary>Horizontal velocity to write, signed. Replaces, never adds.</summary>
        public readonly float VelocityX;

        /// <summary>Tick the shove was caused on.</summary>
        public readonly long Tick;

        private readonly bool _set;

        public PendingImpulse(float velocityX, long tick)
        {
            VelocityX = velocityX;
            Tick = tick;
            _set = true;
        }

        public bool IsSet => _set;
    }

    /// <summary>
    /// Conventional gate ordering, in one place so the precedence is arguable at a glance rather
    /// than reconstructed from five files. Gaps are intentional.
    /// </summary>
    public static class GateOrder
    {
        /// <summary>Invulnerability. Beats everything — if the hit never touched you, nothing else
        /// about it matters.</summary>
        public const int IFrames = 10;

        /// <summary>Parry. Beats blocking, because a parry is a block done well and should never
        /// be downgraded into one by ordering.</summary>
        public const int Parry = 20;

        /// <summary>Blocking. The last thing between an attack and your health.</summary>
        public const int Block = 30;
    }
}

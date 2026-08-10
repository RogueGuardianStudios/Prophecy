using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// The moveset and the numbers that drive it, as plain C#.
    ///
    /// <para>Same shape and same reasons as <see cref="MovementTuningData"/>: the gate forbids a
    /// <c>UnityEngine.Object</c> under <c>Rokkan.Prophecy.Sim</c>, so the numbers live here and
    /// <see cref="Rokkan.Prophecy.Core.CombatTuning"/> is a thin asset wrapper. A class, not a
    /// struct, so the module holds the same reference the asset serialises and an inspector edit
    /// lands on the next tick.</para>
    ///
    /// <para><b>Which attack a press produces is data, not a branch.</b> Zelda II's identity is
    /// that stance chooses the attack — standing swings high, crouching thrusts low — so the
    /// module reads an id off the stance and looks it up. Adding a stance-specific move, or
    /// pointing two stances at the same one, is then an asset edit.</para>
    ///
    /// <para><b>These defaults are a first pass and have not been felt.</b> They were reasoned
    /// from the movement numbers, which are themselves provisional. Startup is long enough to read
    /// as a telegraph, recovery long enough that a whiff costs something, and the cancel window
    /// opens partway through recovery so a chain is responsive without letting the player skip the
    /// commitment. Expect all of it to move once there is art to swing.</para>
    /// </summary>
    [Serializable]
    public class CombatTuningData
    {
        // ------------------------------------------------------------------ entry points

        [Header("Stance entry points")]
        [Tooltip("Attack started by a press while standing.")]
        public string StandingAttackId = "slash_high";

        [Tooltip("Attack started by a press while crouching. Zelda II's low stab.")]
        public string CrouchingAttackId = "thrust_low";

        [Tooltip("Attack started by a press while airborne. Empty means the air press does " +
                 "nothing here — the down-thrust is its own module and owns the dive.")]
        public string AirAttackId = "";

        // ------------------------------------------------------------------ input

        [Header("Input")]
        [Tooltip("How long an attack press is remembered while it cannot yet be acted on. The " +
                 "same forgiveness the jump buffer gives, for the same reason: players press the " +
                 "next attack while the current one is still landing.")]
        public int AttackBufferTicks = 10;

        // ------------------------------------------------------------------ moveset

        [Header("Moveset")]
        [Tooltip("Every attack, keyed by Id. Chains name their successor by Id, so a combo is " +
                 "authored here rather than wired in code.")]
        public AttackDefinition[] Attacks =
        {
            new AttackDefinition
            {
                Id = "slash_high",
                RequiredStance = Stance.Stand,
                StartupTicks = 6,
                ActiveTicks = 4,
                RecoveryTicks = 12,
                HitBoxes = new[]
                {
                    new AttackHitBox
                    {
                        OpenTick = 6, CloseTick = 10,
                        Offset = new Vector2(0.70f, 1.10f),
                        HalfExtents = new Vector2(0.55f, 0.35f),
                        Damage = 10,
                        StoppedByGeometry = true,
                    },
                },
                CancelWindow = new ScalableWindow(12, 8),
                ChainsInto = "slash_high_2",
            },

            new AttackDefinition
            {
                Id = "slash_high_2",
                RequiredStance = Stance.Stand,
                StartupTicks = 5,
                ActiveTicks = 5,
                RecoveryTicks = 16,
                HitBoxes = new[]
                {
                    new AttackHitBox
                    {
                        OpenTick = 5, CloseTick = 10,
                        Offset = new Vector2(0.85f, 1.00f),
                        HalfExtents = new Vector2(0.65f, 0.45f),
                        RotationDegrees = -15f,
                        Damage = 14,
                        StoppedByGeometry = true,
                    },
                },
                // No further link: the chain ends here. The window still exists so a dodge or
                // parry can cut the tail — cancelling out of recovery is not only for combos.
                CancelWindow = new ScalableWindow(18, 6),
            },

            new AttackDefinition
            {
                Id = "thrust_low",
                RequiredStance = Stance.Crouch,
                StartupTicks = 5,
                ActiveTicks = 4,
                RecoveryTicks = 14,
                HitBoxes = new[]
                {
                    new AttackHitBox
                    {
                        OpenTick = 5, CloseTick = 9,
                        Offset = new Vector2(0.75f, 0.35f),
                        HalfExtents = new Vector2(0.60f, 0.25f),
                        Damage = 9,
                        StoppedByGeometry = true,
                    },
                },
                CancelWindow = new ScalableWindow(13, 6),
            },
        };

        // ------------------------------------------------------------------ down-thrust

        [Header("Down-thrust")]
        [Tooltip("The blade under the feet during a dive. Its window is not consulted: the dive " +
                 "lasts until it connects or lands, so the volume is live for exactly as long as " +
                 "the move is, and no tick count could say that.")]
        public AttackHitBox DownThrustBox = new AttackHitBox
        {
            OpenTick = 0,
            CloseTick = 1,
            Offset = new Vector2(0f, -0.20f),
            HalfExtents = new Vector2(0.45f, 0.35f),
            Damage = 16,
            Height = AttackHeight.Any,
        };

        [Tooltip("Id the down-thrust reports its hits under. Shows up in the hit log and, later, " +
                 "in anything that cares which move killed something.")]
        public string DownThrustAttackId = "down_thrust";

        // ------------------------------------------------------------------ up-thrust

        [Header("Up-thrust")]
        [Tooltip("The blade over the head during a rising stab. Like the dive's, its window is " +
                 "not consulted: the thrust lasts from the press to the apex, and the volume is " +
                 "live for exactly as long as the move is. Offset is from the feet, so it sits " +
                 "just past the crown of a 1.8 m body.")]
        public AttackHitBox UpThrustBox = new AttackHitBox
        {
            OpenTick = 0,
            CloseTick = 1,
            Offset = new Vector2(0f, 2.0f),
            HalfExtents = new Vector2(0.45f, 0.35f),
            Damage = 16,
            Height = AttackHeight.Any,
        };

        [Tooltip("Id the up-thrust reports its hits under.")]
        public string UpThrustAttackId = "up_thrust";

        // ------------------------------------------------------------------ health

        [Header("Health")]
        public int MaxHealth = 100;

        // ------------------------------------------------------------------ blocking

        [Header("Blocking")]
        [Tooltip("Fraction of damage that gets through a good block. Zero makes holding the " +
                 "shield strictly correct against anything blockable; chip damage is what keeps " +
                 "a turtle on a timer.")]
        [Range(0f, 1f)]
        public float BlockedDamageFraction = 0.25f;

        [Tooltip("Metres per second the defender slides backwards on a blocked hit. This is the " +
                 "pressure a blocked hit applies — there is no blockstun, because a guard already " +
                 "suppresses moving and attacking and taking the lock for it would drop the guard.")]
        public float BlockPushbackSpeed = 2.5f;

        // ------------------------------------------------------------------ parry

        [Header("Parry")]
        [Tooltip("Ticks after the guard goes up during which a hit is parried rather than blocked. " +
                 "Measured from the press, so holding the button spends the parry immediately and " +
                 "settles into a block — you cannot hold a parry, only time one.")]
        public ScalableWindow ParryWindow = new ScalableWindow(0, 8, minTicks: 2, maxTicks: 24);

        // ------------------------------------------------------------------ dodge

        [Header("Dodge")]
        [Tooltip("The step: a vulnerable wind-up, the intangible move itself, and a recovery that " +
                 "punishes dodging early. Startup is what stops it being a panic button — a dodge " +
                 "that is safe from tick zero can simply be mashed.")]
        public AttackDefinition DodgeAction = new AttackDefinition
        {
            Id = "dodge",
            RequiredStance = Stance.Stand,
            StartupTicks = 3,
            ActiveTicks = 10,
            RecoveryTicks = 14,
            HitBoxes = new AttackHitBox[0],

            // Deliberately wider than the step itself: it opens a tick before the character moves
            // and closes two ticks after they stop. A dash whose intangibility ended exactly when
            // the movement did would demand frame-perfect timing to pass through anything, and
            // "dash through that" is the one thing a dodge is for. The startup that remains is what
            // keeps it a read rather than a panic button.
            IFrames = new ScalableWindow(2, 13, minTicks: 2, maxTicks: 30),

            CancelWindow = new ScalableWindow(20, 7),
        };

        [Tooltip("How far the step carries, in metres. Speed is derived from this and the active " +
                 "phase, so the number here is one you can measure against an enemy's reach.")]
        public float DodgeDistance = 2.2f;

        [Tooltip("Ticks the ATTACKER is stunned by a successful parry. This number is the entire " +
                 "reward — too short and a correct read buys nothing, too long and one parry ends " +
                 "the fight.")]
        public int ParryStunTicks = 40;

        // ------------------------------------------------------------------ hit react

        [Header("Hit react")]
        [Tooltip("Ticks of lost control on a clean hit.")]
        public int HitStunTicks = 18;

        [Tooltip("Metres per second the victim is knocked back.")]
        public float KnockbackSpeed = 4f;

        [Tooltip("Upward metres per second added to knockback, so a hit pops you off the floor " +
                 "slightly and reads as impact rather than a slide.")]
        public float KnockbackLift = 2f;

        [Tooltip("Ticks of invulnerability after a clean hit, so a multi-hit attack cannot chain " +
                 "the player to death with no chance to answer.")]
        public int HitInvulnerabilityTicks = 20;

        // ------------------------------------------------------------------ lookup

        private Dictionary<string, AttackDefinition> _lookup;
        private AttackDefinition[] _lookupSource;

        /// <summary>
        /// The moveset keyed by id, built on demand.
        ///
        /// <para>Rebuilt when the array itself is replaced or resized, which is what an inspector
        /// edit that adds or removes an attack does. Editing the numbers <i>inside</i> an existing
        /// attack needs no rebuild at all — the dictionary holds the same references the array
        /// does, which is the whole reason this is a class.</para>
        /// </summary>
        public IReadOnlyDictionary<string, AttackDefinition> Moveset
        {
            get
            {
                if (Attacks == null) return _lookup ?? (_lookup = new Dictionary<string, AttackDefinition>());

                if (_lookup == null || !ReferenceEquals(_lookupSource, Attacks) || _lookup.Count != CountNamed())
                    Rebuild();

                return _lookup;
            }
        }

        /// <summary>Force the lookup to be rebuilt. Only needed if an attack's Id is changed in
        /// place, which renames a key without changing the array.</summary>
        public void Rebuild()
        {
            _lookup = new Dictionary<string, AttackDefinition>();
            _lookupSource = Attacks;

            if (Attacks == null) return;

            for (int i = 0; i < Attacks.Length; i++)
            {
                var attack = Attacks[i];
                if (attack == null || string.IsNullOrEmpty(attack.Id)) continue;
                _lookup[attack.Id] = attack;
            }
        }

        private int CountNamed()
        {
            int n = 0;
            for (int i = 0; i < Attacks.Length; i++)
                if (Attacks[i] != null && !string.IsNullOrEmpty(Attacks[i].Id)) n++;
            return n;
        }

        /// <summary>The attack a press produces in <paramref name="stance"/>, or null.</summary>
        public AttackDefinition ForStance(Stance stance)
        {
            string id = IdForStance(stance);
            if (string.IsNullOrEmpty(id)) return null;

            return Moveset.TryGetValue(id, out var definition) ? definition : null;
        }

        public string IdForStance(Stance stance)
        {
            switch (stance)
            {
                case Stance.Crouch: return CrouchingAttackId;
                case Stance.Air: return AirAttackId;
                default: return StandingAttackId;
            }
        }

        // ------------------------------------------------------------------ validation

        /// <summary>
        /// Describes everything malformed in the moveset, or null if it is sound. Catches the
        /// failures that are invisible in play: a duplicate id silently shadowing a move, a chain
        /// naming an attack that does not exist, a stance whose entry point was renamed.
        /// </summary>
        public string Validate()
        {
            var problems = new StringBuilder();
            var seen = new HashSet<string>();

            if (Attacks != null)
            {
                for (int i = 0; i < Attacks.Length; i++)
                {
                    var attack = Attacks[i];
                    if (attack == null)
                    {
                        problems.AppendLine($"attack {i} is null");
                        continue;
                    }

                    if (string.IsNullOrEmpty(attack.Id))
                    {
                        problems.AppendLine($"attack {i} has no id");
                        continue;
                    }

                    if (!seen.Add(attack.Id))
                        problems.AppendLine($"duplicate id '{attack.Id}' — one of them is unreachable");

                    string malformed = attack.Validate();
                    if (malformed != null) problems.AppendLine(malformed);
                }

                for (int i = 0; i < Attacks.Length; i++)
                {
                    var attack = Attacks[i];
                    if (attack == null || string.IsNullOrEmpty(attack.ChainsInto)) continue;
                    if (!seen.Contains(attack.ChainsInto))
                        problems.AppendLine($"{attack.Id}: chains into '{attack.ChainsInto}', which does not exist");
                }
            }

            CheckEntryPoint(problems, seen, StandingAttackId, Stance.Stand, "standing");
            CheckEntryPoint(problems, seen, CrouchingAttackId, Stance.Crouch, "crouching");
            CheckEntryPoint(problems, seen, AirAttackId, Stance.Air, "air");

            if (AttackBufferTicks < 0)
                problems.AppendLine("the attack buffer cannot be negative");

            // The window is not checked, because the down-thrust does not consult it — but a volume
            // with no size is a dive that can never connect and therefore never bounce, which looks
            // exactly like the bounce being broken.
            if (DownThrustBox.HalfExtents.x <= 0f || DownThrustBox.HalfExtents.y <= 0f)
                problems.AppendLine("the down-thrust box has no size, so the dive can never connect");

            if (UpThrustBox.HalfExtents.x <= 0f || UpThrustBox.HalfExtents.y <= 0f)
                problems.AppendLine("the up-thrust box has no size, so the rising stab can never connect");

            return problems.Length == 0 ? null : problems.ToString().TrimEnd();
        }

        private void CheckEntryPoint(StringBuilder problems, HashSet<string> known, string id,
                                     Stance stance, string label)
        {
            // An empty entry point is a deliberate "no attack in this stance", not a mistake.
            if (string.IsNullOrEmpty(id)) return;

            if (!known.Contains(id))
            {
                problems.AppendLine($"the {label} attack is '{id}', which is not in the moveset");
                return;
            }

            // The module refuses to start an attack whose RequiredStance disagrees with the stance
            // that selected it. That refusal is silent — the press just does nothing — so the
            // mismatch has to be caught here or it costs a playtest to find.
            if (Moveset.TryGetValue(id, out var definition) && definition != null &&
                definition.RequiredStance != stance)
            {
                problems.AppendLine(
                    $"the {label} attack is '{id}', which requires {definition.RequiredStance} — " +
                    "it can never start");
            }
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The rig the sim fixtures stand on: the floor, the player build, the tick loop, and the
    /// test doubles more than one fixture needs.
    ///
    /// <para>One copy, on purpose. A double pasted into each fixture drifts — four recording
    /// worlds had grown three different default answers and one had lost its spawn log — and a
    /// drifted double is worse than none, because every fixture believes it is testing against
    /// the same world and is not. A fixture that needs a different answer configures the shared
    /// double at the call site, where the difference is visible.</para>
    ///
    /// <para>Imported with <c>using static</c>, so a fixture reads exactly as if the helpers were
    /// its own. Anything genuinely fixture-specific — an input alias, a bespoke world shape —
    /// stays private to its fixture.</para>
    /// </summary>
    public static class SimTestHarness
    {
        /// <summary>One long floor with its top at <paramref name="top"/> — the slab almost
        /// every side-scroll test stands on.</summary>
        public static CollisionWorld Ground(float top = 0f)
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, top - 2f), new Vector2(500f, top)));
            return world;
        }

        /// <summary>
        /// The player, assembled by the same factory the game uses and rigged for a fight: a
        /// combat id and a team so hits route, and facing right so "in front" means +x.
        /// A movement-only fixture passes null combat and no combat world and gets the identical
        /// character — that identity is the factory's whole point.
        /// </summary>
        public static CharacterSim Player(CollisionWorld world, CombatTuningData combat,
                                          ICombatWorld combatWorld,
                                          MovementTuningData tuning = null,
                                          Vector2 at = default,
                                          int combatId = 1, int team = 1,
                                          AbilityLoadoutData loadout = null)
        {
            var sim = PlayerCharacterFactory.Create(
                world, tuning ?? new MovementTuningData(), MovementSpace.SideScroll, loadout,
                combat, combatWorld);

            sim.State.CombatId = combatId;
            sim.State.Team = team;
            sim.Teleport(at, facing: 1);
            return sim;
        }

        /// <summary>Advance the sim whole ticks, setting the input fresh on each one — the
        /// host's loop, by hand.</summary>
        public static void Step(CharacterSim sim, InputFrame input, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                sim.SetInput(input);
                sim.Tick(new SimTickInfo(sim.CurrentTick + 1, SimConstants.FixedDeltaSeconds));
            }
        }

        /// <summary>Advance the sim whole ticks with no input held.</summary>
        public static void Step(CharacterSim sim, int ticks = 1) => Step(sim, InputFrame.Empty, ticks);

        /// <summary>
        /// Feed <paramref name="clock"/> rendered frames of <paramref name="frameDelta"/> seconds
        /// until exactly <paramref name="ticks"/> ticks have run, returning the real seconds fed.
        ///
        /// <para>The last frame is trimmed to the ticks still owed: a 30 fps frame retires two
        /// ticks, so an odd budget would otherwise overshoot by one — and the frame-rate tests
        /// compare end-of-run state, where one extra tick reads as a determinism failure that
        /// is really the harness's. The guard at the end is the other half of the bargain: the
        /// clock's catch-up clamp must never have quietly eaten part of the budget.</para>
        /// </summary>
        public static double AdvanceTicks(SimClock clock, double frameDelta, long ticks)
        {
            double fixedDelta = 1.0 / clock.TicksPerSecond;
            double elapsed = 0.0;

            while (clock.CurrentTick < ticks)
            {
                double owed = (ticks - clock.CurrentTick) * fixedDelta;
                double frame = frameDelta < owed ? frameDelta : owed;
                clock.Advance(frame);
                elapsed += frame;
            }

            Assert.AreEqual(ticks, clock.CurrentTick,
                "the run must retire exactly its tick budget — a frame either overshot or was " +
                "eaten by the clock's catch-up clamp");

            return elapsed;
        }
    }

    /// <summary>
    /// An <see cref="ISimSystem"/> made of a delegate, for renting a slot in a
    /// <see cref="SimClock"/>'s tick. Registration order is the order within the tick, so an
    /// input feed registers ahead of the sim it drives and a probe registers behind it —
    /// exactly the shape of the game's own loop.
    /// </summary>
    public sealed class TickHook : ISimSystem
    {
        private readonly System.Action<SimTickInfo> _onTick;

        public TickHook(System.Action<SimTickInfo> onTick) => _onTick = onTick;

        public void Tick(in SimTickInfo info) => _onTick(info);
    }

    /// <summary>
    /// A stand-in for whatever an encounter turns out to be: a list of hurtboxes, a log of what
    /// connected, and a log of what was launched. Recording rather than applying, because the
    /// module under test's job ends at reporting the hit.
    /// </summary>
    public sealed class RecordingCombatWorld : ICombatWorld
    {
        public readonly List<Hurtbox> Targets = new List<Hurtbox>();
        public readonly List<HitEvent> Hits = new List<HitEvent>();

        /// <summary>Everything this world was asked to launch.</summary>
        public readonly List<ProjectileDefinition> Spawned = new List<ProjectileDefinition>();

        /// <summary>What every hit is answered with. Defaults to a clean landing; a test that
        /// cares about the attacker's fate points it at something else.</summary>
        public HitResult Answer = new HitResult(HitOutcome.Landed);

        private readonly HurtboxSet _set = new HurtboxSet();

        /// <summary>Rebuilt on every read so a test can add a target mid-run and have it
        /// counted. The real one is built once a tick; correctness is the same either way.</summary>
        public HurtboxSet Hurtboxes
        {
            get
            {
                _set.Clear();
                for (int i = 0; i < Targets.Count; i++) _set.Add(Targets[i]);
                _set.Build();
                return _set;
            }
        }

        public void Spawn(ProjectileDefinition definition, in Attacker owner) =>
            Spawned.Add(definition);

        public HitResult OnHit(in HitEvent hit)
        {
            Hits.Add(hit);
            return Answer;
        }
    }

    /// <summary>
    /// An <see cref="ICombatant"/> made of fields: a position and answers a test can set,
    /// counters it can read. The hurtbox is <see cref="Position"/> plus
    /// <see cref="HurtboxOffset"/>, so a fixture can publish either a raw centred box or a
    /// body volume whose centre sits half its height over the feet.
    /// </summary>
    public sealed class DummyCombatant : ICombatant
    {
        public int CombatId { get; set; }
        public int Team { get; set; } = 2;
        public bool IsAlive { get; set; } = true;
        public int ContactDamage { get; set; }
        public int ContactIntervalTicks { get; set; } = 45;
        public DefensiveAnswer ContactDefeats { get; set; }

        public Vector2 Position;

        /// <summary>Added to <see cref="Position"/> when the hurtbox is built.</summary>
        public Vector2 HurtboxOffset;

        public Vector2 HalfExtents = new Vector2(0.45f, 0.9f);

        public int Hits;
        public int DamageTaken;

        /// <summary>How many times the fight has asked where this body is. See the tick-cost
        /// test in CombatScaleTests.</summary>
        public int BuildCalls;

        /// <summary>Where this body claims to be from its second answer onward. Null keeps it
        /// honest; setting it makes asking twice in one tick visible as a wrong answer.</summary>
        public Vector2? PositionFromSecondBuild;

        public Hurtbox BuildHurtbox()
        {
            var at = BuildCalls > 0 && PositionFromSecondBuild.HasValue
                ? PositionFromSecondBuild.Value
                : Position;

            BuildCalls++;
            return new Hurtbox(CombatId, at + HurtboxOffset, HalfExtents, 0f, Team);
        }

        public HitResult ReceiveHit(in HitEvent hit)
        {
            Hits++;
            DamageTaken += hit.Damage;
            return new HitResult(HitOutcome.Landed, hit.Damage);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The pieces that exist so combat survives more than a dozen targets: the geometry we now own,
    /// the broadphase over it, and the fight state that used to live on a MonoBehaviour.
    ///
    /// <para>Most of these assert something that was <i>wrong</i> rather than merely slow — a
    /// colliding contact key that skipped hits, a duplicate id that silently routed damage to the
    /// wrong body. Being fast is the other half.</para>
    /// </summary>
    public class CombatScaleTests
    {
        // ---------------------------------------------------------------- geometry

        [Test]
        public void TwoUnrotatedBoxesOverlapWhenTheyShould()
        {
            Assert.IsTrue(HitResolver.Overlaps(Vector2.zero, Vector2.one, 0f,
                                               new Vector2(1.5f, 0f), Vector2.one, 0f));

            Assert.IsFalse(HitResolver.Overlaps(Vector2.zero, Vector2.one, 0f,
                                                new Vector2(2.5f, 0f), Vector2.one, 0f));
        }

        [Test]
        public void RotationChangesWhetherTwoBoxesTouch()
        {
            // The case that made an AABB unusable and sent the resolver to PhysX in the first
            // place: a 0.5 half-extent projects 0.5 along X unrotated and 0.707 at 45 degrees, so
            // two boxes 1.32 apart MISS square-on and CONNECT turned.
            var half = new Vector2(0.5f, 0.5f);
            var apart = new Vector2(1.32f, 0f);

            Assert.IsFalse(HitResolver.Overlaps(Vector2.zero, half, 0f, apart, half, 0f),
                "square-on they should miss");

            Assert.IsTrue(HitResolver.Overlaps(Vector2.zero, half, 45f, apart, half, 45f),
                "turned they should connect");
        }

        [Test]
        public void ARotatedBoxReachesFurtherThanItsHalfExtent()
        {
            var bound = HitResolver.BoundingHalfExtents(new Vector2(0.5f, 0.5f), 45f);

            Assert.AreEqual(0.7071f, bound.x, 0.001f);
            Assert.AreEqual(0.7071f, bound.y, 0.001f);
        }

        [Test]
        public void OverlapIsSymmetric()
        {
            // Separating-axis tests all four face normals precisely so the answer does not depend
            // on which box you asked about.
            var a = new Vector2(0f, 0f);
            var b = new Vector2(0.9f, 0.4f);
            var half = new Vector2(0.6f, 0.3f);

            Assert.AreEqual(HitResolver.Overlaps(a, half, 20f, b, half, -35f),
                            HitResolver.Overlaps(b, half, -35f, a, half, 20f));
        }

        [Test]
        public void CrossedBladesTouchAndSeparatedOnesDoNot()
        {
            // Long and thin is where an axis-aligned bound is least like the shape, so it is where
            // the real test has to be doing the work rather than the reject.
            var blade = new Vector2(1.2f, 0.1f);

            // Upright through the middle of a flat one: they cross.
            Assert.IsTrue(HitResolver.Overlaps(Vector2.zero, blade, 0f,
                                               new Vector2(1.0f, 0f), blade, 90f));

            // The same upright blade lifted clear. Its bound still spans the flat blade in X, so
            // only the projection along the second axis rules it out.
            Assert.IsFalse(HitResolver.Overlaps(Vector2.zero, blade, 0f,
                                                new Vector2(1.0f, 1.5f), blade, 90f));
        }

        [Test]
        public void OverlapIsRepeatable()
        {
            // Bit-identity is the property that matters, not accuracy: the transcendentals are
            // DeterministicMath's precisely so this answer cannot drift between machines.
            bool first = HitResolver.Overlaps(Vector2.zero, new Vector2(0.5f, 0.9f), 37.5f,
                                              new Vector2(1.1f, 0.3f), new Vector2(0.4f, 0.4f), -12f);

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(first,
                    HitResolver.Overlaps(Vector2.zero, new Vector2(0.5f, 0.9f), 37.5f,
                                         new Vector2(1.1f, 0.3f), new Vector2(0.4f, 0.4f), -12f));
            }
        }

        // ---------------------------------------------------------------- broadphase

        private static HurtboxSet SetOf(params float[] xs)
        {
            var set = new HurtboxSet();

            for (int i = 0; i < xs.Length; i++)
                set.Add(new Hurtbox(i + 1, new Vector2(xs[i], 1f), new Vector2(0.45f, 0.9f), 0f, 2));

            set.Build();
            return set;
        }

        [Test]
        public void TheBroadphaseReturnsWhatIsNearAndNotWhatIsFar()
        {
            var set = SetOf(0f, 5f, 40f, 200f);
            var found = new List<int>();

            set.Query(-1f, 1f, found);

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(1, set[found[0]].OwnerId);
        }

        [Test]
        public void TheBroadphaseNeverReturnsTheSameBoxTwice()
        {
            // A box wide enough to sit in several buckets is in each of their lists, and a query
            // spanning them must still see it once.
            var set = new HurtboxSet();
            set.Add(new Hurtbox(1, new Vector2(0f, 1f), new Vector2(12f, 1f), 0f, 2));
            set.Build();

            var found = new List<int>();
            set.Query(-12f, 12f, found);

            Assert.AreEqual(1, found.Count);
        }

        [Test]
        public void TheBroadphaseFindsEveryBoxItShould()
        {
            // The failure that matters is a MISSED hit, so this walks every box and insists a query
            // tight around it finds it.
            var set = new HurtboxSet();
            for (int i = 0; i < 200; i++)
                set.Add(new Hurtbox(i + 1, new Vector2(i * 1.7f, 1f), new Vector2(0.45f, 0.9f), 0f, 2));
            set.Build();

            var found = new List<int>();

            for (int i = 0; i < set.Count; i++)
            {
                var box = set[i];
                set.Query(box.Centre.x - box.HalfExtents.x, box.Centre.x + box.HalfExtents.x, found);

                Assert.Contains(i, found, $"box {i} at x={box.Centre.x} was not found by a query over itself");
            }
        }

        [Test]
        public void AHundredTargetsResolveToTheOneThatIsActuallyHit()
        {
            var set = new HurtboxSet();
            for (int i = 0; i < 100; i++)
                set.Add(new Hurtbox(i + 1, new Vector2(i * 3f, 1f), new Vector2(0.45f, 0.9f), 0f, 2));
            set.Build();

            var box = new AttackHitBox
            {
                OpenTick = 0, CloseTick = 1,
                Offset = new Vector2(0.7f, 1f),
                HalfExtents = new Vector2(0.5f, 0.5f),
                Damage = 10,
            };

            // Standing just left of the target at x=30.
            var attacker = new Attacker(999, new Vector2(29.5f, 0f), new Vector2(29.5f, 0.9f), 1, 1);

            var hits = new List<int>();
            var candidates = new List<int>();

            Assert.AreEqual(1, HitResolver.Resolve(box, attacker, set, null, hits, candidates));
            Assert.AreEqual(11, set[hits[0]].OwnerId, "the eleventh target sits at x=30");
        }

        // ---------------------------------------------------------------- fight state

        private sealed class Dummy : ICombatant
        {
            public int CombatId { get; set; }
            public int Team { get; set; } = 2;
            public bool IsAlive { get; set; } = true;
            public int ContactDamage { get; set; }
            public int ContactIntervalTicks { get; set; } = 45;
            public DefensiveAnswer ContactDefeats { get; set; }

            public Vector2 Position;
            public int Hits;
            public int DamageTaken;

            /// <summary>How many times the fight has asked where this body is. See the tick-cost test.</summary>
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
                return new Hurtbox(CombatId, at, new Vector2(0.45f, 0.9f), 0f, Team);
            }

            public HitResult ReceiveHit(in HitEvent hit)
            {
                Hits++;
                DamageTaken += hit.Damage;
                return new HitResult(HitOutcome.Landed, hit.Damage);
            }
        }

        [Test]
        public void HitsRouteByIdWithoutSearching()
        {
            var state = new CombatState();

            for (int i = 1; i <= 100; i++)
                state.Register(new Dummy { CombatId = i, Position = new Vector2(i * 3f, 1f) });

            var target = (Dummy)state.Combatants[41];

            state.OnHit(new HitEvent(999, target.CombatId, 7, -1, 10, "probe", 0));

            Assert.AreEqual(1, target.Hits);
            Assert.AreEqual(7, target.DamageTaken);
        }

        [Test]
        public void ADuplicateIdIsRefusedRatherThanAccepted()
        {
            // A duplicate does not fail loudly on its own — it silently routes someone else's hits
            // to the wrong body, which is the worst way for an id scheme to be wrong.
            var state = new CombatState();

            Assert.IsTrue(state.Register(new Dummy { CombatId = 5 }));

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("share id 5"));
            Assert.IsFalse(state.Register(new Dummy { CombatId = 5 }));

            Assert.AreEqual(1, state.Combatants.Count);
        }

        [Test]
        public void RuntimeIdsCannotCollideWithAuthoredOnes()
        {
            var state = new CombatState();

            Assert.GreaterOrEqual(state.AllocateId(), CombatState.FirstRuntimeId);
            Assert.AreNotEqual(state.AllocateId(), state.AllocateId());
        }

        [Test]
        public void ContactHurtsOnlyTheOtherTeamAndOnlyOnItsInterval()
        {
            var state = new CombatState();

            var hazard = new Dummy
            {
                CombatId = 1, Team = 2, Position = new Vector2(0f, 1f),
                ContactDamage = 5, ContactIntervalTicks = 10,
            };
            var victim = new Dummy { CombatId = 2, Team = 1, Position = new Vector2(0.3f, 1f) };
            var friend = new Dummy { CombatId = 3, Team = 2, Position = new Vector2(0.3f, 1f) };

            state.Register(hazard);
            state.Register(victim);
            state.Register(friend);

            for (long tick = 0; tick < 25; tick++) state.Tick(null, tick, 1f / 60f);

            Assert.AreEqual(0, friend.Hits, "same team");
            Assert.AreEqual(3, victim.Hits, "ticks 0, 10 and 20");
        }

        [Test]
        public void ContactCooldownsDoNotCollideAcrossPairs()
        {
            // source * 1000 + victim gave 3000 for both (1, 2000) and (2, 1000), and the collision
            // showed up as a hit that silently never happened.
            var state = new CombatState();

            var a = new Dummy { CombatId = 1, Team = 2, Position = new Vector2(0f, 1f),
                                ContactDamage = 5, ContactIntervalTicks = 1000 };
            var b = new Dummy { CombatId = 2, Team = 2, Position = new Vector2(0f, 1f),
                                ContactDamage = 5, ContactIntervalTicks = 1000 };

            var victim1000 = new Dummy { CombatId = 1000, Team = 1, Position = new Vector2(0f, 1f) };
            var victim2000 = new Dummy { CombatId = 2000, Team = 1, Position = new Vector2(0f, 1f) };

            state.Register(a);
            state.Register(b);
            state.Register(victim1000);
            state.Register(victim2000);

            state.Tick(null, 0, 1f / 60f);

            // Each hazard touches each victim exactly once. Under the old key, (1,2000) and (2,1000)
            // shared a cooldown slot and one of them was skipped.
            Assert.AreEqual(2, victim1000.Hits);
            Assert.AreEqual(2, victim2000.Hits);
        }

        [Test]
        public void AHundredCombatantsWithNoContactDamageCostNoContactWork()
        {
            // The old version walked every pair every tick regardless. Nothing here deals contact
            // damage, so nothing should be hit however many bodies are standing on each other.
            var state = new CombatState();

            for (int i = 1; i <= 100; i++)
                state.Register(new Dummy { CombatId = i, Team = 2, Position = new Vector2(0f, 1f) });

            for (long tick = 0; tick < 60; tick++) state.Tick(null, tick, 1f / 60f);

            for (int i = 0; i < state.Combatants.Count; i++)
                Assert.AreEqual(0, ((Dummy)state.Combatants[i]).Hits);
        }

        [Test]
        public void ATickAsksEachBodyWhereItIsExactlyOnce()
        {
            // The number that decides whether a fight scales. A tick has to ask every live body for
            // its volume once — that pass is irreducible. Anything asking a second time is a second
            // walk of the roster, which is what this is here to catch: contact resolution used to
            // rebuild each hazard's box after the snapshot had already built it.
            var state = new CombatState();

            for (int i = 1; i <= 40; i++)
            {
                state.Register(new Dummy
                {
                    CombatId = i,
                    Team = i <= 4 ? 1 : 2,
                    Position = new Vector2(i * 0.5f, 1f),
                    ContactDamage = i <= 4 ? 5 : 0,   // four hazards standing in a crowd of victims
                });
            }

            state.Tick(null, 0, 1f / 60f);

            for (int i = 0; i < state.Combatants.Count; i++)
            {
                var dummy = (Dummy)state.Combatants[i];
                Assert.AreEqual(1, dummy.BuildCalls,
                    $"combatant {dummy.CombatId} was asked for its hurtbox {dummy.BuildCalls} times in one tick");
            }
        }

        [Test]
        public void ContactTestsTheSnapshotEveryoneElseResolvedAgainst()
        {
            // A hazard whose second answer differs from its first. There is no way to move a body
            // mid-tick from outside, so the disagreement has to come from the body itself — and a
            // body that answers twice is precisely the thing being ruled out.
            //
            // Resolving against the snapshot, contact uses the box at x=0 and connects. Rebuilding,
            // it would get x=50 and the hazard would miss a victim it is standing inside.
            var state = new CombatState();

            var hazard = new Dummy
            {
                CombatId = 1,
                Team = 1,
                ContactDamage = 7,
                Position = new Vector2(0f, 1f),
                PositionFromSecondBuild = new Vector2(50f, 1f),
            };

            state.Register(hazard);
            state.Register(new Dummy { CombatId = 2, Team = 2, Position = new Vector2(0f, 1f) });

            state.Tick(null, 0, 1f / 60f);

            Assert.AreEqual(1, ((Dummy)state.Combatants[1]).Hits,
                "contact should resolve against the tick's snapshot, not a freshly built box");
        }

        [Test]
        public void DeadCombatantsPublishNoHurtbox()
        {
            var state = new CombatState();

            state.Register(new Dummy { CombatId = 1, Position = Vector2.zero });
            state.Register(new Dummy { CombatId = 2, Position = Vector2.zero, IsAlive = false });

            state.RebuildHurtboxes();

            Assert.AreEqual(1, state.Hurtboxes.Count);
            Assert.AreEqual(1, state.Hurtboxes[0].OwnerId);
        }
    }
}

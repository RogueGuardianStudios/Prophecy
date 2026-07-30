using System.Collections.Generic;
using NUnit.Framework;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Hit resolution: rotated volumes via our own separating-axis test, cover via the same baked
    /// geometry movement uses.
    ///
    /// <para>All headless — no scene, no colliders. Combat outcomes are the thing this project
    /// most needs to reproduce identically at any frame rate, and that is only worth claiming if
    /// the resolution itself can be stepped in a test.</para>
    /// </summary>
    public class HitResolverTests
    {
        private const int MyId = 1;
        private const int MyTeam = 1;

        private readonly List<int> _hits = new List<int>();

        /// <summary>An attacker standing at the origin, body centre at chest height.</summary>
        private static Attacker Me(int facing = 1) =>
            new Attacker(MyId, Vector2.zero, new Vector2(0f, 0.9f), facing, MyTeam);

        private static AttackHitBox Box(Vector2 offset, Vector2 halfExtents,
                                        float rotation = 0f, bool stoppedByGeometry = false)
        {
            return new AttackHitBox
            {
                OpenTick = 0, CloseTick = 3,
                Offset = offset,
                HalfExtents = halfExtents,
                RotationDegrees = rotation,
                Damage = 10,
                StoppedByGeometry = stoppedByGeometry,
            };
        }

        // ---------------------------------------------------------------- basics

        [Test]
        public void ATargetInsideTheBoxIsHit()
        {
            var box = Box(new Vector2(1f, 1f), new Vector2(0.7f, 0.7f));
            var targets = new List<Hurtbox> { new Hurtbox(2, new Vector2(1.2f, 1f), Vector2.one * 0.4f) };

            Assert.AreEqual(1, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits));
            Assert.AreEqual(0, _hits[0]);
        }

        [Test]
        public void ATargetOutOfReachIsNotHit()
        {
            var box = Box(new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
            var targets = new List<Hurtbox> { new Hurtbox(2, new Vector2(8f, 1f), Vector2.one * 0.4f) };

            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits));
        }

        [Test]
        public void FacingMirrorsTheVolume()
        {
            // The same authored attack must reach behind the character when they turn around, and
            // must stop reaching in front.
            var box = Box(new Vector2(1f, 1f), new Vector2(0.6f, 0.6f));
            var inFront = new List<Hurtbox> { new Hurtbox(2, new Vector2(1f, 1f), Vector2.one * 0.4f) };
            var behind = new List<Hurtbox> { new Hurtbox(2, new Vector2(-1f, 1f), Vector2.one * 0.4f) };

            Assert.AreEqual(1, HitResolver.ResolveWithoutBroadphase(box, Me(1), inFront, null, _hits));
            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(1), behind, null, _hits));

            Assert.AreEqual(1, HitResolver.ResolveWithoutBroadphase(box, Me(-1), behind, null, _hits));
            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(-1), inFront, null, _hits));
        }

        [Test]
        public void RotationChangesWhoIsHit()
        {
            // The capability an AABB could not have, exercised through the real resolver rather
            // than only in the probe.
            var target = new List<Hurtbox> { new Hurtbox(2, new Vector2(0f, 2.1f), Vector2.one * 0.3f) };

            var flat = Box(new Vector2(0f, 1f), new Vector2(1.2f, 0.3f));
            var upright = Box(new Vector2(0f, 1f), new Vector2(1.2f, 0.3f), rotation: 90f);

            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(flat, Me(), target, null, _hits),
                "a wide flat box does not reach up");
            Assert.AreEqual(1, HitResolver.ResolveWithoutBroadphase(upright, Me(), target, null, _hits),
                "the same box turned upright does");
        }

        // ---------------------------------------------------------------- teams

        [Test]
        public void TheAttackerNeverHitsThemselves()
        {
            var box = Box(Vector2.zero, Vector2.one * 2f);
            var targets = new List<Hurtbox> { new Hurtbox(MyId, Vector2.zero, Vector2.one * 0.5f) };

            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits));
        }

        [Test]
        public void AlliesAreSkippedAndNeutralsAreNot()
        {
            var box = Box(Vector2.zero, Vector2.one * 2f);
            var targets = new List<Hurtbox>
            {
                new Hurtbox(2, Vector2.zero, Vector2.one * 0.3f, 0f, team: MyTeam), // ally
                new Hurtbox(3, Vector2.zero, Vector2.one * 0.3f, 0f, team: 2),      // enemy
                new Hurtbox(4, Vector2.zero, Vector2.one * 0.3f, 0f, team: 0),      // neutral crate
            };

            HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits);

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, _hits, "the enemy and the crate, not the ally");
        }

        // ---------------------------------------------------------------- cover

        /// <summary>A wall standing between the attacker at the origin and anything past x = 1.2.</summary>
        private static CollisionWorld WallBetween()
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(0.8f, -2f), new Vector2(1.2f, 4f)));
            return world;
        }

        [Test]
        public void AnAttackThatRespectsCoverIsStoppedByAWall()
        {
            // The reaching case: the hit VOLUME is already past the wall, so cover can only work
            // if the ray is traced from the attacker's body rather than from the box.
            var box = Box(new Vector2(1.8f, 1f), new Vector2(0.8f, 0.6f), stoppedByGeometry: true);
            var targets = new List<Hurtbox> { new Hurtbox(2, new Vector2(2.2f, 1f), Vector2.one * 0.4f) };

            Assert.AreEqual(1, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits),
                "with no world it connects");

            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, WallBetween(), _hits),
                "and a wall between attacker and target stops it");
        }

        [Test]
        public void AnAttackThatIgnoresCoverPassesThroughTheSameWall()
        {
            // Per-attack, not per-world: the spear stops, the shockwave does not.
            var box = Box(new Vector2(1.8f, 1f), new Vector2(0.8f, 0.6f), stoppedByGeometry: false);
            var targets = new List<Hurtbox> { new Hurtbox(2, new Vector2(2.2f, 1f), Vector2.one * 0.4f) };

            Assert.AreEqual(1, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, WallBetween(), _hits));
        }

        [Test]
        public void CoverOnlyBlocksTheTargetsItActuallyShields()
        {
            var box = Box(new Vector2(1.5f, 1f), new Vector2(2.5f, 0.6f), stoppedByGeometry: true);
            var targets = new List<Hurtbox>
            {
                new Hurtbox(2, new Vector2(0.2f, 1f), Vector2.one * 0.3f),  // this side of the wall
                new Hurtbox(3, new Vector2(2.6f, 1f), Vector2.one * 0.3f),  // behind it
            };

            HitResolver.ResolveWithoutBroadphase(box, Me(), targets, WallBetween(), _hits);

            CollectionAssert.AreEqual(new[] { 0 }, _hits, "only the sheltered one is spared");
        }

        // ---------------------------------------------------------------- batching and edges

        [Test]
        public void ManyTargetsResolveInOneCall()
        {
            var box = Box(new Vector2(1f, 1f), new Vector2(2f, 1f));
            var targets = new List<Hurtbox>();

            for (int i = 0; i < 6; i++)
                targets.Add(new Hurtbox(10 + i, new Vector2(0.5f + i * 0.4f, 1f), Vector2.one * 0.2f));

            int count = HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits);

            Assert.Greater(count, 1, "a wide sweep catches several at once");
            Assert.AreEqual(count, _hits.Count);
        }

        [Test]
        public void NoTargetsIsHarmless()
        {
            var box = Box(Vector2.one, Vector2.one);

            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(), new List<Hurtbox>(), null, _hits));
            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(), null, null, _hits));
        }

        [Test]
        public void ASizelessBoxHitsNothing()
        {
            var box = Box(Vector2.one, Vector2.zero);
            var targets = new List<Hurtbox> { new Hurtbox(2, Vector2.one, Vector2.one) };

            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits));
        }

        [Test]
        public void TheSkinInflatesReach()
        {
            var box = Box(new Vector2(1f, 1f), new Vector2(0.4f, 0.4f));
            var targets = new List<Hurtbox> { new Hurtbox(2, new Vector2(1.9f, 1f), Vector2.one * 0.3f) };

            Assert.AreEqual(0, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits));
            Assert.AreEqual(1, HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits, skin: 0.5f),
                "forgiveness without resizing the authored volume");
        }

        [Test]
        public void RepeatedResolutionIsIdentical()
        {
            var box = Box(new Vector2(1f, 1f), new Vector2(0.9f, 0.5f), rotation: 27f);
            var targets = new List<Hurtbox>
            {
                new Hurtbox(2, new Vector2(1.3f, 1.2f), Vector2.one * 0.35f),
                new Hurtbox(3, new Vector2(4f, 1f), Vector2.one * 0.35f),
            };

            HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits);
            var first = _hits.ToArray();

            for (int i = 0; i < 10; i++)
            {
                HitResolver.ResolveWithoutBroadphase(box, Me(), targets, null, _hits);
                CollectionAssert.AreEqual(first, _hits, "same inputs, same hits, every time");
            }
        }

        // ---------------------------------------------------------------- helpers

        [Test]
        public void ABodyHurtboxSitsAboveTheFeet()
        {
            var hurtbox = Hurtbox.ForBody(7, new Vector2(3f, 2f), new Vector2(0.9f, 1.8f));

            Assert.AreEqual(3f, hurtbox.Centre.x, 0.001f);
            Assert.AreEqual(2.9f, hurtbox.Centre.y, 0.001f, "feet + half the body height");
            Assert.AreEqual(0.9f, hurtbox.HalfExtents.y, 0.001f);
        }

        [Test]
        public void AnAttackerTracesCoverFromTheBodyNotTheFeet()
        {
            // Tracing from the feet would let a low wall shield a target from a head-height swing.
            var attacker = Attacker.FromBody(1, new Vector2(5f, 2f), new Vector2(0.9f, 1.8f), facing: -1);

            Assert.AreEqual(5f, attacker.Feet.x, 0.001f);
            Assert.AreEqual(2.9f, attacker.Origin.y, 0.001f);
            Assert.AreEqual(-1, attacker.Facing);
        }
    }
}

using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Proves <see cref="ImmediatePhysics"/> is a viable backend for combat hit detection
    /// <b>before</b> anything is built on top of it.
    ///
    /// <para>The claim being tested is narrow and load-bearing: that Unity's low-level geometry
    /// queries run with <b>no scene, no PhysicsScene and no colliders</b> — pure maths over
    /// structs. If that is false, the whole approach collapses back to either scene-bound queries
    /// (which would cost the headless contract) or hand-rolled AABBs (which cannot rotate). These
    /// tests exist so that question is answered by the compiler and the runner rather than by
    /// reading release notes.</para>
    ///
    /// <para>Rotation is the reason to reach for this at all. An axis-aligned box cannot express a
    /// sword arc or a sweeping tail; if the rotated case below did not work there would be no
    /// argument for the extra complexity over <c>Aabb.Overlaps</c>.</para>
    ///
    /// <para>These are EditMode tests in the ordinary suite — if they ever stop running headless,
    /// the suite says so immediately.</para>
    /// </summary>
    public class ImmediatePhysicsProbeTests
    {
        /// <summary>
        /// Contact-generate a single pair of boxes and return how many contacts came back.
        /// Zero means "not touching".
        /// </summary>
        /// <summary>
        /// The smallest legal skin. <c>GenerateContacts</c> rejects a contact distance of zero
        /// outright ("must be positive and not equal to zero"), so "no inflation" has to be
        /// expressed as an epsilon rather than as nothing.
        /// </summary>
        private const float NoSkin = 0.0001f;

        private static int ContactsBetween(
            Vector3 halfExtentsA, Vector3 positionA, Quaternion rotationA,
            Vector3 halfExtentsB, Vector3 positionB, Quaternion rotationB,
            float contactDistance = NoSkin)
        {
            const int maxContactsPerPair = 16;

            var geomA = new NativeArray<GeometryHolder>(1, Allocator.Temp);
            var geomB = new NativeArray<GeometryHolder>(1, Allocator.Temp);
            var xformA = new NativeArray<ImmediateTransform>(1, Allocator.Temp);
            var xformB = new NativeArray<ImmediateTransform>(1, Allocator.Temp);
            var contacts = new NativeArray<ImmediateContact>(maxContactsPerPair, Allocator.Temp);
            var counts = new NativeArray<int>(1, Allocator.Temp);

            try
            {
                geomA[0] = GeometryHolder.Create(new BoxGeometry(halfExtentsA));
                geomB[0] = GeometryHolder.Create(new BoxGeometry(halfExtentsB));

                xformA[0] = new ImmediateTransform { Position = positionA, Rotation = rotationA };
                xformB[0] = new ImmediateTransform { Position = positionB, Rotation = rotationB };

                ImmediatePhysics.GenerateContacts(
                    geomA.AsReadOnly(), geomB.AsReadOnly(),
                    xformA.AsReadOnly(), xformB.AsReadOnly(),
                    pairCount: 1,
                    outContacts: contacts,
                    outContactCounts: counts,
                    contactDistance: contactDistance);

                return counts[0];
            }
            finally
            {
                geomA.Dispose();
                geomB.Dispose();
                xformA.Dispose();
                xformB.Dispose();
                contacts.Dispose();
                counts.Dispose();
            }
        }

        [Test]
        public void OverlappingBoxes_ProduceContacts_WithNoSceneAtAll()
        {
            // No GameObject, no Collider, no PhysicsScene. If this passes, hit detection can live
            // inside the sim tick and still be tested the way movement already is.
            int contacts = ContactsBetween(
                Vector3.one * 0.5f, Vector3.zero, Quaternion.identity,
                Vector3.one * 0.5f, new Vector3(0.5f, 0f, 0f), Quaternion.identity);

            Assert.Greater(contacts, 0, "two boxes sharing space must report contact");
        }

        [Test]
        public void SeparatedBoxes_ProduceNoContacts()
        {
            int contacts = ContactsBetween(
                Vector3.one * 0.5f, Vector3.zero, Quaternion.identity,
                Vector3.one * 0.5f, new Vector3(5f, 0f, 0f), Quaternion.identity);

            Assert.AreEqual(0, contacts, "boxes five metres apart are not touching");
        }

        [Test]
        public void RotationIsRespected_WhichIsTheWholePointOverAnAabb()
        {
            // One distance, two answers, decided purely by rotation — which is the capability an
            // AABB cannot have.
            //
            // A 0.5 half-extent box projects 0.5 along X unrotated, but 0.5*sqrt(2) = 0.707 when
            // turned 45 degrees. So two boxes 1.32 apart:
            //   unrotated  -> 0.5  + 0.5  = 1.0   < 1.32  -> clear of each other
            //   both at 45 -> 0.707 + 0.707 = 1.414 > 1.32  -> corners overlap
            var halfExtents = Vector3.one * 0.5f;
            var apart = new Vector3(1.32f, 0f, 0f);

            int unrotated = ContactsBetween(
                halfExtents, Vector3.zero, Quaternion.identity,
                halfExtents, apart, Quaternion.identity);

            int rotated = ContactsBetween(
                halfExtents, Vector3.zero, Quaternion.Euler(0f, 0f, 45f),
                halfExtents, apart, Quaternion.Euler(0f, 0f, 45f));

            Assert.AreEqual(0, unrotated, "axis-aligned at 1.32 m: a clear 0.32 m gap");
            Assert.Greater(rotated, 0, "turned 45 degrees the same boxes reach across and touch");
        }

        [Test]
        public void ContactDistance_CanInflateTheTest()
        {
            // contactDistance is a skin: useful for forgiving hitboxes without resizing geometry.
            var halfExtents = Vector3.one * 0.5f;
            var apart = new Vector3(1.2f, 0f, 0f);

            Assert.AreEqual(0, ContactsBetween(halfExtents, Vector3.zero, Quaternion.identity,
                                               halfExtents, apart, Quaternion.identity,
                                               contactDistance: NoSkin),
                "0.2 m apart with a negligible skin: no contact");

            Assert.Greater(ContactsBetween(halfExtents, Vector3.zero, Quaternion.identity,
                                           halfExtents, apart, Quaternion.identity,
                                           contactDistance: 0.5f), 0,
                "with a 0.5 m skin the same pair registers");
        }

        [Test]
        public void ContactsCarryNormalAndSeparation()
        {
            // Knockback direction and depenetration both want these, so confirm they are populated
            // rather than assuming the struct is filled in.
            const int maxContacts = 16;

            var geomA = new NativeArray<GeometryHolder>(1, Allocator.Temp);
            var geomB = new NativeArray<GeometryHolder>(1, Allocator.Temp);
            var xformA = new NativeArray<ImmediateTransform>(1, Allocator.Temp);
            var xformB = new NativeArray<ImmediateTransform>(1, Allocator.Temp);
            var contacts = new NativeArray<ImmediateContact>(maxContacts, Allocator.Temp);
            var counts = new NativeArray<int>(1, Allocator.Temp);

            try
            {
                geomA[0] = GeometryHolder.Create(new BoxGeometry(Vector3.one * 0.5f));
                geomB[0] = GeometryHolder.Create(new BoxGeometry(Vector3.one * 0.5f));
                xformA[0] = new ImmediateTransform { Position = Vector3.zero, Rotation = Quaternion.identity };
                xformB[0] = new ImmediateTransform { Position = new Vector3(0.6f, 0f, 0f), Rotation = Quaternion.identity };

                ImmediatePhysics.GenerateContacts(
                    geomA.AsReadOnly(), geomB.AsReadOnly(),
                    xformA.AsReadOnly(), xformB.AsReadOnly(),
                    1, contacts, counts, NoSkin);

                Assert.Greater(counts[0], 0);

                var contact = contacts[0];
                Assert.Greater(contact.Normal.sqrMagnitude, 0.5f, "normal should be a unit-ish direction");
                Assert.LessOrEqual(contact.Separation, 0f, "overlapping contacts report negative separation");
            }
            finally
            {
                geomA.Dispose();
                geomB.Dispose();
                xformA.Dispose();
                xformB.Dispose();
                contacts.Dispose();
                counts.Dispose();
            }
        }

        [Test]
        public void ManyPairsResolveInOneCall()
        {
            // Batching matters: one call per tick for every attacker/target pair, rather than a
            // call per pair. Confirms pairCount and the per-pair count array behave as expected.
            const int pairs = 4;
            const int maxContactsPerPair = 8;

            var geomA = new NativeArray<GeometryHolder>(pairs, Allocator.Temp);
            var geomB = new NativeArray<GeometryHolder>(pairs, Allocator.Temp);
            var xformA = new NativeArray<ImmediateTransform>(pairs, Allocator.Temp);
            var xformB = new NativeArray<ImmediateTransform>(pairs, Allocator.Temp);
            var contacts = new NativeArray<ImmediateContact>(pairs * maxContactsPerPair, Allocator.Temp);
            var counts = new NativeArray<int>(pairs, Allocator.Temp);

            try
            {
                for (int i = 0; i < pairs; i++)
                {
                    geomA[i] = GeometryHolder.Create(new BoxGeometry(Vector3.one * 0.5f));
                    geomB[i] = GeometryHolder.Create(new BoxGeometry(Vector3.one * 0.5f));
                    xformA[i] = new ImmediateTransform { Position = Vector3.zero, Rotation = Quaternion.identity };

                    // Alternate touching and far apart.
                    float x = i % 2 == 0 ? 0.5f : 20f;
                    xformB[i] = new ImmediateTransform { Position = new Vector3(x, 0f, 0f), Rotation = Quaternion.identity };
                }

                ImmediatePhysics.GenerateContacts(
                    geomA.AsReadOnly(), geomB.AsReadOnly(),
                    xformA.AsReadOnly(), xformB.AsReadOnly(),
                    pairs, contacts, counts, NoSkin);

                Assert.Greater(counts[0], 0, "pair 0 overlaps");
                Assert.AreEqual(0, counts[1], "pair 1 is far apart");
                Assert.Greater(counts[2], 0, "pair 2 overlaps");
                Assert.AreEqual(0, counts[3], "pair 3 is far apart");
            }
            finally
            {
                geomA.Dispose();
                geomB.Dispose();
                xformA.Dispose();
                xformB.Dispose();
                contacts.Dispose();
                counts.Dispose();
            }
        }

        [Test]
        public void RepeatedCallsAreIdentical()
        {
            // Determinism matters more here than anywhere: combat outcomes must reproduce. This
            // only proves same-binary repeatability, not cross-platform — worth knowing the
            // difference before relying on it for netcode or replays.
            int first = ContactsBetween(
                Vector3.one * 0.5f, Vector3.zero, Quaternion.Euler(0f, 0f, 33f),
                Vector3.one * 0.5f, new Vector3(0.7f, 0.2f, 0f), Quaternion.Euler(0f, 0f, -12f));

            for (int i = 0; i < 20; i++)
            {
                int again = ContactsBetween(
                    Vector3.one * 0.5f, Vector3.zero, Quaternion.Euler(0f, 0f, 33f),
                    Vector3.one * 0.5f, new Vector3(0.7f, 0.2f, 0f), Quaternion.Euler(0f, 0f, -12f));

                Assert.AreEqual(first, again, "same inputs must give the same contact count every time");
            }
        }
    }
}

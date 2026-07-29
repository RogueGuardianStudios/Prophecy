using NUnit.Framework;
using RGS.Core;
using RGS.Core.Sim;
using Rokkan.Core;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// M1 verification: proves the shared packages resolved and compiled into Prophecy.
    ///
    /// <para>Most of the value here is at COMPILE time — if the <c>file:</c> references in
    /// manifest.json are wrong, or an asmdef reference is missing, this assembly fails to build
    /// and the whole test run reports the failure loudly. The assertions are a second, runtime
    /// check that the types behave rather than merely link.</para>
    /// </summary>
    public class PackageWiringTests
    {
        [Test]
        public void SharedSimSpine_IsReachable_AndTicks()
        {
            var clock = new SimClock();
            var counter = new TickCounter();
            clock.Register(counter);

            clock.Step();
            clock.Step();

            Assert.AreEqual(2, clock.CurrentTick, "com.rgs.core sim spine is wired");
            Assert.AreEqual(2, counter.Ticks);
        }

        [Test]
        public void SimRunsHeadless_NoUnityObjectsInvolved()
        {
            // The acceptance test for the whole architecture: a sim advances with no
            // MonoBehaviour, no scene, no Animator, no Transform anywhere in the call graph.
            var clock = new SimClock();
            clock.Register(new TickCounter());

            for (int i = 0; i < SimConstants.TicksPerSecond; i++)
                clock.Step();

            Assert.AreEqual(1.0, clock.ElapsedSeconds, 1e-9,
                "one simulated second, produced entirely headless");
        }

        [Test]
        public void RgsCoreFoundationTypes_AreReachable()
        {
            var guid = SerializableGuid.NewGuid();
            Assert.AreEqual(guid, SerializableGuid.FromHexString(guid.ToHexString()),
                "com.rgs.core foundation types are wired");
        }

        [Test]
        public void RokkanCoreSavePrimitives_AreReachable()
        {
            // Compile-time proof that com.rokkan.core resolved: naming ISaveable at all
            // requires the package to be present and referenced.
            Assert.IsTrue(typeof(ISaveable).IsInterface, "com.rokkan.core is wired");
        }

        private sealed class TickCounter : ISimSystem
        {
            public int Ticks { get; private set; }
            public void Tick(in SimTickInfo info) => Ticks++;
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// That the simulation cannot tell who is driving it.
    ///
    /// <para>This is the property the whole AI design rests on: a brain produces an
    /// <see cref="InputFrame"/>, the same struct a gamepad produces, and the sim executes it. If a
    /// planner can drive a character exactly as a player does, the AI is outside the simulation
    /// rather than threaded through it — and every combat rule applies to enemies for free,
    /// because the enemy is subject to the same locks and gates the player is.</para>
    ///
    /// <para>These run with no scene, no <c>Animator</c>, no input device and no AI, which is the
    /// same claim from the other direction.</para>
    /// </summary>
    public class InputSourceTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>A driver that plays a written-down sequence. Stands in for a brain.</summary>
        private sealed class ScriptedInput : IInputSource
        {
            private readonly Queue<InputFrame> _frames = new Queue<InputFrame>();

            public int Consumed { get; private set; }

            public ScriptedInput Then(InputFrame frame, int ticks = 1)
            {
                for (int i = 0; i < ticks; i++) _frames.Enqueue(frame);
                return this;
            }

            public InputFrame ConsumeFrame()
            {
                Consumed++;
                return _frames.Count > 0 ? _frames.Dequeue() : InputFrame.Empty;
            }
        }

        private static CollisionWorld Ground()
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, -2f), new Vector2(500f, 0f)));
            return world;
        }

        private static CharacterSim Character(out MovementTuningData tuning)
        {
            tuning = new MovementTuningData();
            var sim = PlayerCharacterFactory.Create(Ground(), tuning, MovementSpace.SideScroll);
            sim.Teleport(Vector2.zero);
            return sim;
        }

        /// <summary>Run a character off a source, exactly as the host does each tick.</summary>
        private static void Drive(CharacterSim sim, IInputSource source, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                sim.SetInput(source.ConsumeFrame());
                sim.Tick(new SimTickInfo(sim.CurrentTick + 1, Dt));
            }
        }

        [Test]
        public void AScriptedSourceMovesTheCharacterExactlyAsAPlayerWould()
        {
            var sim = Character(out _);
            var brain = new ScriptedInput().Then(new InputFrame(new Vector2(1f, 0f)), ticks: 60);

            Drive(sim, brain, 60);

            Assert.Greater(sim.State.Position.x, 1f, "holding right should travel");
            Assert.AreEqual(60, brain.Consumed, "one frame consumed per tick, no more and no fewer");
        }

        [Test]
        public void ADriverPressingAttackStartsTheSameAttackAPlayerWould()
        {
            // An enemy is subject to the combat system rather than beside it — the whole reason the
            // brain presses buttons instead of calling abilities.
            var sim = Character(out _);
            var attack = sim.Get<AttackModule>();

            Assert.IsNotNull(attack);
            Assert.IsFalse(attack.IsAttacking);

            var brain = new ScriptedInput().Then(new InputFrame(Vector2.zero, attack: ButtonState.Press));
            Drive(sim, brain, 1);

            Assert.IsTrue(attack.IsAttacking, "a planner's button press is a player's button press");
        }

        [Test]
        public void TheSimulationCarriesNoNotionOfWhoIsDriving()
        {
            // Two characters, two different drivers, identical input: identical outcome. If the sim
            // ever learns the difference, this is where it shows.
            var a = Character(out _);
            var b = Character(out _);

            var walker = new ScriptedInput().Then(new InputFrame(new Vector2(1f, 0f)), 30);
            var alsoWalker = new ScriptedInput().Then(new InputFrame(new Vector2(1f, 0f)), 30);

            Drive(a, walker, 30);
            Drive(b, alsoWalker, 30);

            Assert.AreEqual(a.State.Position.x, b.State.Position.x, 0.0001f);
        }

        [Test]
        public void ADriverIsSubjectToTheActionLockLikeAnyoneElse()
        {
            // The property that makes enemies cheap: they inherit every rule. A brain holding
            // attack cannot start a second attack mid-swing, for the same reason a player cannot.
            var sim = Character(out _);
            var attack = sim.Get<AttackModule>();

            var brain = new ScriptedInput()
                .Then(new InputFrame(Vector2.zero, attack: ButtonState.Press))
                .Then(new InputFrame(Vector2.zero, attack: ButtonState.Press));

            Drive(sim, brain, 1);
            string first = attack.Current?.Id;

            Drive(sim, brain, 1);

            Assert.AreEqual(first, attack.Current?.Id,
                "the second press must not restart the swing the first one began");
        }

        [Test]
        public void AnExhaustedSourceReadsAsNoInputRatherThanRepeatingItself()
        {
            // Edge flags are true for exactly one read. A source that repeated its last frame would
            // let one press start two attacks — the bug the player's latch exists to prevent, and
            // one a brain could reintroduce.
            var sim = Character(out _);
            var brain = new ScriptedInput().Then(new InputFrame(new Vector2(1f, 0f)), 10);

            Drive(sim, brain, 30);

            float settled = sim.State.Velocity.x;

            Assert.AreEqual(0f, settled, 0.01f, "input ran out, so the character should coast to rest");
        }
    }
}

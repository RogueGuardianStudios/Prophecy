using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Mirefen's art: stop fighting the water (design doc 5.7 — Peace as not resisting; LOCKED
    /// as the dungeon's reward, live in the gray box like everything else). Cast at the
    /// surface: WALK ON THE WATER — the top face becomes a platform (Matt: not wading; the
    /// surface acts like any other platform). Cast submerged: LAUNCH — a surge that scales
    /// with depth, and a platform waiting where you splash back down.
    ///
    /// <para><b>The platform is state, not geometry.</b> This module maintains
    /// <see cref="CharacterState.HasFloatFloor"/> at the waterline and the sim's own grounding
    /// and vertical sweep consult it — so landing there lands (and resets the air, the normal
    /// jump rules), standing there stands, and a jump off the water is a ground jump. One-way
    /// from above: a submerged body rises through its own platform freely.</para>
    ///
    /// <para><b>The cast is a toggle, and only two things end it</b> (Matt): recasting, and a
    /// section change (which clears it through <see cref="Reset"/> like everything else). Dry
    /// land does NOT end it — out of water the art is simply inert, armed for the next pool,
    /// so a waterline authored flush with its banks is walked across in both directions
    /// without a thought.</para>
    ///
    /// <para><b>The launch is re-asserted every tick while the water lasts</b> — the dive's
    /// pattern, upside down. Swim's drag would otherwise bleed the surge on its way up and
    /// quietly delete the "deeper casts launch higher" rule the sluice rooms are built on
    /// (water level × buoyancy = two-variable rooms). The exit speed is sized so the body
    /// leaves the surface still carrying depth × bonus of climb; from there gravity owns the
    /// arc like any jump — and the air refreshes at the cast (the dive's bounce precedent),
    /// so an unused double jump is still in hand at the top (Matt).</para>
    ///
    /// <para>The gray box binds the cast to the FlameArt button — pressable ANYWHERE, water
    /// or dry land (Matt): on land it arms or disarms the toggle, in deep water it is the
    /// launch. No Flame is charged — the meter and its costs arrive with the real art
    /// system. This module is the movement half, which is the half the water rooms need to
    /// be designed against.</para>
    /// </summary>
    public sealed class Buoyancy : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private bool _launching;
        private float _launchSpeed;
        private float _lastY;

        public Buoyancy(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.Buoyancy;
        public override int Order => ModuleOrder.Buoyancy;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>The cast's state: floating, or primed to float on the next splash.</summary>
        public bool FloatOn { get; private set; }

        /// <summary>Mid-surge. Overlay, and the animator once the art has a look.</summary>
        public bool IsLaunching => _launching;

        public override void Reset()
        {
            FloatOn = false;
            _launching = false;
            _launchSpeed = 0f;
        }

        /// <summary>
        /// Release whatever the body is holding — ledge, rope, ladder (Matt's edge case).
        /// Clearing the shared attachment state is the sanctioned external detach: the hang
        /// modules WATCH it and clean their own internals when it changes under them, the same
        /// way a teleport's clear has always worked. Nothing here names a module.
        /// </summary>
        private static void LetGo(CharacterState state)
        {
            if (state.Attachment == AttachmentKind.None) return;

            state.Attachment = AttachmentKind.None;
            state.AttachmentAnchor = Vector2.zero;
        }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            bool inWater = sim.World.TryGetWater(state.Position, out var water);
            float depth = inWater ? water.Max.y - state.Position.y : 0f;

            // The cast, ANYWHERE (Matt): the art is a toggle you can arm standing on dry
            // land, and the next pool honours it. Only a press in deep water means something
            // different — the launch. Gated on the attack lock the way everything voluntary
            // is: no casting out of a hit-react, no casting mid-dive.
            if (input.FlameArt.Pressed && sim.Can(LockFlags.Attack))
            {
                if (inWater && depth >= _tuning.BuoyancyLaunchMinDepth)
                {
                    float climb = Mathf.Max(0.01f, depth * _tuning.BuoyancyLaunchBonus);
                    _launchSpeed = Mathf.Sqrt(2f * _tuning.RiseGravity * climb);
                    _launching = true;
                    _lastY = state.Position.y - 1f;   // "made progress" is true on the first tick
                    FloatOn = true;                   // the splash-back is a landing, not a sink

                    // The surge leaves like a jump leaves: the air refreshes (the dive's
                    // bounce precedent), so an unused double jump is still there at the top.
                    state.AirRefreshTick = info.Tick;

                    LetGo(state);
                }
                else
                {
                    FloatOn = !FloatOn;

                    // Activating the float while HANGING lets go (Matt's edge case — ledge,
                    // rope and ladder alike): the art takes over the vertical, and a hand
                    // still on the wall would pin the body under its own rising water.
                    if (FloatOn && inWater) LetGo(state);
                }
            }

            if (!inWater)
            {
                _launching = false;

                // The platform exists wherever the armed body is OVER its water — probed a
                // metre below the feet, comfortably more than one tick of terminal fall. The
                // old rule armed the floor only once the feet were INSIDE the box, which left
                // a one-tick hole on every landing: the body plunged under the line (uncapped
                // — Swim had not seen the water yet either) and the pull yanked it back up,
                // Matt's double bounce. With the floor in place BEFORE the crossing, the
                // sweep clamps the landing exactly at the line like any platform — and the
                // same rule covers standing jitter above the waterline, a jump's whole arc
                // over the pool, and the top of a launch. Past the pool's edge the probe
                // misses and the platform ends: a phantom waterline over dry land would be
                // walking on air.
                if (FloatOn && sim.World.TryGetWater(
                        new Vector2(state.Position.x, state.Position.y - 1f), out var pool))
                {
                    state.FloatFloorY = pool.Max.y;
                    state.HasFloatFloor = true;
                    return;
                }

                // The CAST survives dry land (Matt: a toggle — once on, only recasting turns
                // it off; section changes clear it through Reset like everything else). Out
                // of water it is simply inert, armed for the next pool.
                state.HasFloatFloor = false;
                return;
            }

            if (_launching)
            {
                // The surge must pass UP through its own platform, so the platform rests
                // while the launch runs; it re-arms the tick the surge ends.
                state.HasFloatFloor = false;

                // A ceiling under the water stops the surge for good — without this check the
                // re-assert would grind against it forever, an elevator to nowhere.
                if (state.Position.y <= _lastY)
                {
                    _launching = false;
                    return;
                }

                _lastY = state.Position.y;
                state.Velocity.y = _launchSpeed;
                return;
            }

            if (FloatOn)
            {
                // Walking on water (Matt): the top face IS a platform. The sim's grounding
                // and vertical sweep consult the float floor, so landing on the waterline
                // lands, standing there stands, jumps off it are ground jumps that reset the
                // air, and walking runs at full speed — the water shows in none of it.
                state.FloatFloorY = water.Max.y;
                state.HasFloatFloor = true;

                // A body under its platform is pulled up to it, and the pull OWNS the ascent:
                // set outright each tick (the first float's lesson — a coasting pull
                // overshoots), exact-arrival on the last tick, and the leftover pull speed is
                // killed at the line so the surfacing STOPS at the surface. Anything already
                // rising faster than the snap — a jump, a wall jump, the surge — sails
                // through untouched; the platform is one-way and so is its pull.
                float below = water.Max.y - state.Position.y;

                if (below > 0.001f)
                {
                    if (state.Velocity.y <= _tuning.FloatSnapSpeed + 0.01f)
                        state.Velocity.y = Mathf.Min(below / info.DeltaSeconds,
                                                     _tuning.FloatSnapSpeed);
                }
                else if (state.Velocity.y > 0f &&
                         state.Velocity.y <= _tuning.FloatSnapSpeed + 0.01f)
                {
                    state.Velocity.y = 0f;
                }
            }
            else
            {
                state.HasFloatFloor = false;
            }
        }
    }
}

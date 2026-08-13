using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// The character's motor: velocity into position, resolved against the world. Plain C#
    /// over <see cref="CharacterState"/> and the sim's own <see cref="CollisionWorld"/>,
    /// with no view of modules, locks, or anything else the controller owns — which is what
    /// lets the integrator be reasoned about (and one day tested) apart from the character
    /// riding it. A physics change lands here; a lock-arbitration change never does.
    /// </summary>
    public sealed class CharacterMover
    {
        private readonly CollisionWorld _world;

        public CharacterMover(CollisionWorld world)
        {
            _world = world;
        }

        /// <summary>
        /// Whether the body's leading edge may make this single-axis move.
        ///
        /// <para>Three points across the leading face — centre and both corners — because a feet
        /// point alone lets half a body hang over a cliff edge before anything objects. The
        /// corners pull in slightly so brushing a wall while walking parallel to it does not
        /// read as a collision.</para>
        /// </summary>
        private static bool GroundPermits(CharacterState state, ITopDownGround ground, Vector2 delta)
        {
            var from = state.Position;
            var to = from + delta;

            float half = state.BodySize.x * 0.5f;
            float lead = Mathf.Sign(delta.x != 0f ? delta.x : delta.y) * half;
            float across = half * 0.8f;

            Vector2 centre, cornerA, cornerB;

            if (delta.x != 0f)
            {
                centre = new Vector2(to.x + lead, to.y);
                cornerA = new Vector2(to.x + lead, to.y - across);
                cornerB = new Vector2(to.x + lead, to.y + across);
            }
            else
            {
                centre = new Vector2(to.x, to.y + lead);
                cornerA = new Vector2(to.x - across, to.y + lead);
                cornerB = new Vector2(to.x + across, to.y + lead);
            }

            // The edge probes VALIDATE; only a feet-to-feet probe RESOLVES the layer. The
            // leading edge runs up to half a body ahead of the feet, and committing its layer
            // flipped the token one cell early — walking a bridge deck toward its junction
            // dropped the body through the deck, and leaving a cave popped it onto the roof,
            // for every frame until the feet caught up. The token must track where the feet
            // ARE, not where the toes point.
            int edgeLayerA = state.GroundLayer;
            int edgeLayerB = state.GroundLayer;
            int edgeLayerC = state.GroundLayer;

            bool permitted = ground.CanStep(from, centre, ref edgeLayerA) &&
                             ground.CanStep(from, cornerA, ref edgeLayerB) &&
                             ground.CanStep(from, cornerB, ref edgeLayerC);
            if (!permitted) return false;

            int feetLayer = state.GroundLayer;
            ground.CanStep(from, to, ref feetLayer);
            state.GroundLayer = feetLayer;
            return true;
        }

        /// <summary>
        /// Move by velocity, resolving each axis separately against the world.
        ///
        /// <para>Axis separation is the standard platformer approach and it is what makes wall
        /// sliding fall out for free: blocked horizontally, vertical motion still proceeds. A
        /// single combined sweep would instead snag the character on any surface it brushed.
        /// Horizontal resolves first so that landing is evaluated at the position actually
        /// arrived at.</para>
        /// </summary>
        public void Integrate(CharacterState state, ITopDownGround ground, float dt)
        {
            var delta = state.Velocity * dt;
            if (delta == Vector2.zero) return;

            if (state.Space == MovementSpace.TopDown)
            {
                // No gravity and no one-ways overhead; both axes are plain lateral motion,
                // resolved against the ground seam when a scene supplies one. No ground means
                // free movement — every scene before the overworld, and every old test.
                if (ground == null)
                {
                    state.Position += delta;
                    return;
                }

                // Axis separation, exactly as side-scroll below: blocked one way, the other axis
                // still proceeds, which is what makes walls slide-alongable rather than sticky.
                if (delta.x != 0f)
                {
                    if (GroundPermits(state, ground, new Vector2(delta.x, 0f)))
                        state.Position += new Vector2(delta.x, 0f);
                    else
                    {
                        state.HitWallThisTick = true;
                        state.Velocity.x = 0f;
                    }
                }

                if (delta.y != 0f)
                {
                    if (GroundPermits(state, ground, new Vector2(0f, delta.y)))
                        state.Position += new Vector2(0f, delta.y);
                    else
                    {
                        state.HitWallThisTick = true;
                        state.Velocity.y = 0f;
                    }
                }

                return;
            }

            if (delta.x != 0f)
            {
                float allowedX = _world.SweepHorizontal(state.Body, delta.x, out bool hitX);
                state.Position += new Vector2(allowedX, 0f);
                if (hitX)
                {
                    state.HitWallThisTick = true;
                    state.Velocity.x = 0f;   // stop pushing into geometry
                }
            }

            if (delta.y != 0f)
            {
                float allowedY = _world.SweepVertical(state.Body, delta.y, out bool hitY, state.DropThrough);

                // The float floor stops a downward crossing from above, exactly as a one-way
                // platform would — a body already beneath it is not touched, which is what
                // lets a submerged body rise up through its own waterline.
                if (state.HasFloatFloor && delta.y < 0f && state.Position.y >= state.FloatFloorY)
                {
                    float toFloor = state.FloatFloorY - state.Position.y;
                    if (allowedY < toFloor)
                    {
                        allowedY = toFloor;
                        hitY = true;
                    }
                }

                state.Position += new Vector2(0f, allowedY);
                if (hitY)
                {
                    if (delta.y > 0f) state.HitCeilingThisTick = true;
                    state.Velocity.y = 0f;
                }
            }
        }

        /// <summary>
        /// Recompute support from geometry.
        ///
        /// <para>Deliberately passes <see cref="CharacterState.DropThrough"/> along. Grounding is
        /// the thing that stops a fall starting, so a character dropping through a platform must
        /// stop counting as standing on it the moment the drop is permitted — otherwise gravity
        /// keeps being zeroed and the input looks ignored.</para>
        /// </summary>
        public void RefreshGrounded(CharacterState state)
        {
            state.Grounded = state.Space == MovementSpace.TopDown ||
                             _world.IsGrounded(state.Body, dropThrough: state.DropThrough) ||
                             OnFloatFloor(state);
        }

        /// <summary>Standing on the temporary surface an ability maintains (Buoyancy's
        /// water-walk): feet at the floor, or within the same probe distance the solid
        /// grounding uses. Never true from beneath — the float floor is one-way.</summary>
        private static bool OnFloatFloor(CharacterState state) =>
            state.HasFloatFloor &&
            state.Position.y >= state.FloatFloorY - 0.001f &&
            state.Position.y <= state.FloatFloorY + 0.02f;

        /// <summary>
        /// Airborne always wins, because the down-thrust is gated on it. Crouch is otherwise a
        /// module decision — this only forces the character out of Air when they land, and never
        /// silently un-crouches someone under a low ceiling.
        /// </summary>
        public void UpdateStance(CharacterState state)
        {
            if (state.Space == MovementSpace.TopDown)
            {
                state.Stance = Stance.Stand;
                return;
            }

            if (!state.Grounded)
            {
                state.Stance = Stance.Air;
            }
            else if (state.Stance == Stance.Air)
            {
                state.Stance = Stance.Stand;
            }
        }
    }
}

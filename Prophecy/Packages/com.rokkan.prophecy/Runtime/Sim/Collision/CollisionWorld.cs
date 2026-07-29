using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Collision
{
    /// <summary>What a solid does when something tries to move through it.</summary>
    public enum SolidKind
    {
        /// <summary>Blocks from every direction.</summary>
        Solid,

        /// <summary>Blocks only downward motion, and only for a mover whose feet started at or
        /// above the platform's top surface — so you jump up through it and land on it.</summary>
        OneWay,
    }

    public readonly struct Solid
    {
        public readonly Aabb Box;
        public readonly SolidKind Kind;

        /// <summary>
        /// Whether a deliberate drop can pass down through this. Only meaningful for
        /// <see cref="SolidKind.OneWay"/>.
        ///
        /// <para>Both behaviours are wanted, and the difference is a level design decision rather
        /// than a physics one. A platform in the middle of a room should let the player drop back
        /// down — being stranded on top of scenery is miserable. A platform sealing the floor of
        /// an area you climbed up into should not, or the player falls out of the level by
        /// crouching.</para>
        /// </summary>
        public readonly bool AllowsDropThrough;

        public Solid(in Aabb box, SolidKind kind, bool allowsDropThrough = true)
        {
            Box = box;
            Kind = kind;
            AllowsDropThrough = allowsDropThrough;
        }
    }

    /// <summary>A ladder or rope: a volume you can hold position in, that never blocks movement.</summary>
    public readonly struct Climbable
    {
        public readonly Aabb Box;
        public readonly ClimbableKind Kind;

        public Climbable(in Aabb box, ClimbableKind kind)
        {
            Box = box;
            Kind = kind;
        }
    }

    /// <summary>
    /// The sim's own collision representation, and the reason movement can obey the headless
    /// contract at all.
    ///
    /// <para><b>Why not Unity physics.</b> <c>ISimSystem</c> forbids scene coupling, and
    /// <c>Physics.CapsuleCast</c> needs a live <c>PhysicsScene</c> — so a mover built on Unity
    /// queries could never run in a headless test. Instead the sim owns a plain list of boxes,
    /// filled once at scene load by a presentation-side baker that reads the scene's colliders.
    /// The bake touches Transforms, but it happens outside the tick, so the split holds.</para>
    ///
    /// <para>The payoff is that jump arcs, coyote windows and landing ticks become assertable in
    /// a unit test with no scene at all — which is exactly what "lock the movement numbers"
    /// requires. Gray box geometry is axis-aligned boxes anyway, so AABB-only costs nothing
    /// today; slopes and rotated geometry would need this extended.</para>
    ///
    /// <para>Deliberately a linear scan. The gray box has tens of solids, not thousands, and a
    /// broadphase would be unverifiable complexity at this stage. If profiling ever says
    /// otherwise, a uniform grid slots in behind this same API.</para>
    /// </summary>
    public sealed class CollisionWorld
    {
        /// <summary>
        /// Leaves a sliver between the mover and what it hits. Without it, a sweep lands exactly
        /// flush and the next frame's strict-overlap test sits on the knife edge of float
        /// equality — which shows up as intermittent stuck-in-wall or lost-grounding frames.
        /// </summary>
        public const float SkinWidth = 0.0001f;

        private readonly List<Solid> _solids = new List<Solid>();
        private readonly List<Climbable> _climbables = new List<Climbable>();

        public int Count => _solids.Count;
        public IReadOnlyList<Solid> Solids => _solids;

        /// <summary>Ladders and ropes. Kept apart from solids because they are the opposite of
        /// solid — you pass through them and they only matter to the ability that reads them.</summary>
        public int ClimbableCount => _climbables.Count;
        public IReadOnlyList<Climbable> Climbables => _climbables;

        public void Add(in Aabb box, SolidKind kind = SolidKind.Solid, bool allowsDropThrough = true) =>
            _solids.Add(new Solid(box, kind, allowsDropThrough));

        public void AddClimbable(in Aabb box, ClimbableKind kind = ClimbableKind.Ladder) =>
            _climbables.Add(new Climbable(box, kind));

        public void Clear()
        {
            _solids.Clear();
            _climbables.Clear();
        }

        /// <summary>True if <paramref name="box"/> strictly overlaps any Solid. One-way platforms
        /// are ignored — you are never "inside" one.</summary>
        public bool OverlapsAnySolid(in Aabb box)
        {
            for (int i = 0; i < _solids.Count; i++)
            {
                var s = _solids[i];
                if (s.Kind != SolidKind.Solid) continue;
                if (box.Overlaps(s.Box)) return true;
            }
            return false;
        }

        /// <summary>
        /// How far <paramref name="box"/> may move horizontally before something stops it.
        /// Returns the permitted delta (same sign as <paramref name="dx"/>, magnitude &lt;= |dx|).
        /// One-way platforms never block horizontal motion.
        /// </summary>
        public float SweepHorizontal(in Aabb box, float dx, out bool hit)
        {
            hit = false;
            if (Mathf.Approximately(dx, 0f)) return 0f;

            float allowed = dx;

            for (int i = 0; i < _solids.Count; i++)
            {
                var s = _solids[i];
                if (s.Kind != SolidKind.Solid) continue;

                // Only solids sharing vertical span can be in the way.
                if (!box.OverlapsY(s.Box)) continue;

                if (dx > 0f)
                {
                    // Moving right: care about solids starting at or beyond our right edge.
                    if (s.Box.Min.x < box.Max.x) continue;
                    float gap = s.Box.Min.x - box.Max.x - SkinWidth;
                    if (gap < allowed) { allowed = Mathf.Max(0f, gap); hit = true; }
                }
                else
                {
                    if (s.Box.Max.x > box.Min.x) continue;
                    float gap = s.Box.Max.x - box.Min.x + SkinWidth;
                    if (gap > allowed) { allowed = Mathf.Min(0f, gap); hit = true; }
                }
            }

            return allowed;
        }

        /// <summary>
        /// How far <paramref name="box"/> may move vertically before something stops it.
        /// Returns the permitted delta (same sign as <paramref name="dy"/>).
        ///
        /// <para>One-way platforms block only downward motion, and only when the mover's feet
        /// began at or above the platform's top — that single rule gives both "jump up through
        /// it" and "land on it". <paramref name="dropThrough"/> disables them entirely so a
        /// deliberate drop-through can pass.</para>
        /// </summary>
        public float SweepVertical(in Aabb box, float dy, out bool hit, bool dropThrough = false)
        {
            hit = false;
            if (Mathf.Approximately(dy, 0f)) return 0f;

            float allowed = dy;

            for (int i = 0; i < _solids.Count; i++)
            {
                var s = _solids[i];

                if (!box.OverlapsX(s.Box)) continue;

                if (s.Kind == SolidKind.OneWay)
                {
                    if (dy >= 0f) continue;                            // only ever blocks a fall
                    if (dropThrough && s.AllowsDropThrough) continue;   // a deliberate drop, permitted
                    if (box.Min.y < s.Box.Max.y - SkinWidth) continue;  // started below the surface
                }

                if (dy > 0f)
                {
                    if (s.Box.Min.y < box.Max.y) continue;
                    float gap = s.Box.Min.y - box.Max.y - SkinWidth;
                    if (gap < allowed) { allowed = Mathf.Max(0f, gap); hit = true; }
                }
                else
                {
                    if (s.Box.Max.y > box.Min.y) continue;
                    float gap = s.Box.Max.y - box.Min.y + SkinWidth;
                    if (gap > allowed) { allowed = Mathf.Min(0f, gap); hit = true; }
                }
            }

            return allowed;
        }

        /// <summary>
        /// True if something supports <paramref name="box"/> from directly below. Probes by
        /// <paramref name="probeDistance"/> rather than testing for contact, so a mover resting
        /// flush on ground (where strict overlap is deliberately false) still reads as grounded.
        /// </summary>
        /// <summary>
        /// True if something supports <paramref name="box"/> from directly below.
        ///
        /// <para><paramref name="dropThrough"/> must be honoured here, not only in the sweep. A
        /// character dropping through a platform stays <i>grounded</i> on it otherwise — gravity
        /// keeps zeroing their velocity and the drop never starts, which reads as the input being
        /// ignored rather than as a collision rule.</para>
        /// </summary>
        public bool IsGrounded(in Aabb box, float probeDistance = 0.02f, bool dropThrough = false)
        {
            SweepVertical(box, -probeDistance, out bool hit, dropThrough);
            return hit;
        }

        /// <summary>
        /// True if a wall is within <paramref name="probeDistance"/> to the character's
        /// <paramref name="direction"/> (-1 left, +1 right). Reuses the horizontal sweep so a wall
        /// is by definition the same thing that would stop horizontal movement — wall slide can
        /// never disagree with the mover about whether something is there.
        /// </summary>
        public bool HasWall(in Aabb box, int direction, float probeDistance = 0.08f)
        {
            if (direction == 0 || probeDistance <= 0f) return false;

            SweepHorizontal(box, direction * probeDistance, out bool hit);
            return hit;
        }

        /// <summary>The solid strictly containing <paramref name="point"/>, if any.</summary>
        public bool TryGetSolidAt(Vector2 point, out Aabb solid)
        {
            for (int i = 0; i < _solids.Count; i++)
            {
                var s = _solids[i];
                if (s.Kind != SolidKind.Solid) continue;

                if (point.x > s.Box.Min.x && point.x < s.Box.Max.x &&
                    point.y > s.Box.Min.y && point.y < s.Box.Max.y)
                {
                    solid = s.Box;
                    return true;
                }
            }

            solid = default;
            return false;
        }

        /// <summary>
        /// Look for a ledge the character could catch, ahead of them in <paramref name="facing"/>.
        ///
        /// <para>Two probes, and the pair is the whole trick: it must be <b>clear</b> where the
        /// head would end up and <b>solid</b> where the hands would grip. Testing only for solid
        /// would grab the middle of a tall wall; testing only for clear would grab thin air. The
        /// gap between them is exactly the definition of an edge.</para>
        ///
        /// <para>Returns the surface height and the wall face, which together are everything the
        /// hang pose needs to place itself.</para>
        /// </summary>
        public bool TryFindLedge(in Aabb body, int facing, float minHeight, float maxHeight,
                                 float probeDistance, out float ledgeTopY, out float ledgeFaceX)
        {
            ledgeTopY = 0f;
            ledgeFaceX = 0f;

            if (facing == 0 || maxHeight <= minHeight) return false;

            float feetY = body.Min.y;
            float edgeX = facing > 0 ? body.Max.x : body.Min.x;
            float probeX = edgeX + facing * probeDistance;

            if (TryGetSolidAt(new Vector2(probeX, feetY + maxHeight), out _)) return false;
            if (!TryGetSolidAt(new Vector2(probeX, feetY + minHeight), out var solid)) return false;

            // The surface has to sit inside the grab band, or this is a wall that happens to end
            // somewhere unhelpful rather than a ledge at hand height.
            float top = solid.Max.y;
            if (top < feetY + minHeight || top > feetY + maxHeight) return false;

            ledgeTopY = top;
            ledgeFaceX = facing > 0 ? solid.Min.x : solid.Max.x;
            return true;
        }

        /// <summary>The first climbable volume overlapping <paramref name="box"/>.</summary>
        public bool TryGetClimbable(in Aabb box, out Climbable climbable)
        {
            for (int i = 0; i < _climbables.Count; i++)
            {
                if (!box.Overlaps(_climbables[i].Box)) continue;
                climbable = _climbables[i];
                return true;
            }

            climbable = default;
            return false;
        }

        /// <summary>
        /// True if a droppable pass-through platform is directly underfoot — the question "can I
        /// drop from here?" Strict one-way platforms answer no, which is what makes them the
        /// sealed floor of an area rather than a trapdoor.
        /// </summary>
        public bool StandingOnDroppablePlatform(in Aabb box, float probeDistance = 0.05f)
        {
            for (int i = 0; i < _solids.Count; i++)
            {
                var s = _solids[i];

                if (s.Kind != SolidKind.OneWay || !s.AllowsDropThrough) continue;
                if (!box.OverlapsX(s.Box)) continue;

                // Feet resting on its top surface, within the probe.
                float gap = box.Min.y - s.Box.Max.y;
                if (gap >= -SkinWidth && gap <= probeDistance) return true;
            }

            return false;
        }
    }
}

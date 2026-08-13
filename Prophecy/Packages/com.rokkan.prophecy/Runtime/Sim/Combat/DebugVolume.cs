using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>Which law a volume lives under, so a viewer can colour it honestly.</summary>
    public enum DebugVolumeKind
    {
        /// <summary>Windowed on a timeline: armed through the swing, dangerous only while the
        /// window is open.</summary>
        Windowed,

        /// <summary>Live for its whole existence — the thrusts' blades, the way projectiles
        /// read. Showing one dim would be a lie about the one thing that matters.</summary>
        Blade,
    }

    /// <summary>
    /// One damaging volume, resolved to where it actually is this tick. What a module reports
    /// through <see cref="IDebugVolumeSource"/>, and everything a viewer needs to draw it.
    /// </summary>
    public struct DebugVolume
    {
        public Vector2 Centre;
        public Vector2 HalfExtents;

        /// <summary>Rotation about the depth axis, already mirrored by facing. Rotation is the
        /// reason hit volumes are not AABBs, so an honest picture has to carry it.</summary>
        public float RotationDegrees;

        public DebugVolumeKind Kind;

        /// <summary>Dangerous this tick. False is armed-but-waiting: the wind-up's box, drawn
        /// dim so the tick it turns dangerous is visible.</summary>
        public bool Live;

        /// <summary>Level geometry between attacker and target stops this hit, so a viewer
        /// should trace the cover ray that explains a blocked swing.</summary>
        public bool StoppedByGeometry;
    }

    /// <summary>
    /// A module that swings damaging volumes, describing them for a viewer.
    ///
    /// <para><b>Exists because the overlay kept going blind, one module at a time.</b> Every
    /// box-owning module used to need its own private draw path in <c>CombatDebugOverlay</c>,
    /// and each of those paths was written only after its attack shipped invisible — the
    /// thrusts' blades were undrawable for two milestones, the planning enemy's swing for one.
    /// The overlay cannot know the modules; the modules must say what they are asking the
    /// world, through the seam every present and future box-owner shares. Implement this and
    /// the volumes appear with zero viewer edits.</para>
    ///
    /// <para>Headless-safe by construction: maths structs only, no engine objects — the
    /// architecture gate holds over this file like any other sim code. It reports what the
    /// resolver is already resolving; it must never become an input to it.</para>
    /// </summary>
    public interface IDebugVolumeSource
    {
        /// <summary>
        /// Append every volume this module is currently swinging, resolved against
        /// <paramref name="state"/> exactly as the module's own hit resolution resolves them —
        /// the drawn box and the asked box must be the same box.
        /// </summary>
        void CollectDebugVolumes(CharacterState state, List<DebugVolume> into);
    }
}

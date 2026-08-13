using System.Collections.Generic;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>A camera rig that must be put on its mark when the player arrives somewhere —
    /// a scene entry, a respawn — rather than letting damping walk it over from wherever it
    /// was looking, which is exactly the slide snapping exists to prevent.</summary>
    public interface IArrivalSnap
    {
        void SnapToTarget();
    }

    /// <summary>
    /// The rigs currently in the world that want the arrival snap. Rigs register themselves
    /// on enable, and whoever performs an arrival calls <see cref="SnapAll"/> — so a new kind
    /// of rig is a registration, never another concrete branch in the scene director. The
    /// director's own serialized lane rig stays typed there for the lane-specific work
    /// (vertical bounds); this list exists for every rig a SCENE brings with it.
    /// </summary>
    public static class ArrivalCameras
    {
        private static readonly List<IArrivalSnap> Rigs = new List<IArrivalSnap>();

        public static void Register(IArrivalSnap rig)
        {
            if (rig != null && !Rigs.Contains(rig)) Rigs.Add(rig);
        }

        public static void Unregister(IArrivalSnap rig) => Rigs.Remove(rig);

        public static void SnapAll()
        {
            for (int i = 0; i < Rigs.Count; i++) Rigs[i].SnapToTarget();
        }
    }
}

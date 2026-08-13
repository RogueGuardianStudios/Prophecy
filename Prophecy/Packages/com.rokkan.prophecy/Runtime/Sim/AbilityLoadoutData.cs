using System;
using System.Collections.Generic;
using Rokkan.Prophecy.Sim.Arts;
using UnityEngine;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// Which abilities are switched on. The authored surface for
    /// <see cref="AbilityModule.Enabled"/>, and the shape progression will take.
    ///
    /// <para><b>Why a list of entries rather than a field per ability.</b> A bool per ability means
    /// every new ability edits this class, this asset's serialised layout, and anything that reads
    /// it — the exact coupling the module architecture exists to avoid. A keyed list grows by
    /// adding a row.</para>
    ///
    /// <para><b>An absent entry means "leave it alone", not "off".</b> A loadout that has never
    /// heard of an ability must not silently disable it — that would turn every newly added
    /// ability into a bug report from anyone with an older asset. Modules keep their own default
    /// until something explicitly says otherwise.</para>
    ///
    /// <para>Held by reference and applied every tick, so ticking a box during play mode takes
    /// effect immediately — the same live-tuning property <see cref="MovementTuningData"/> has,
    /// and the reason gray-box A/B testing is quick.</para>
    /// </summary>
    [Serializable]
    public class AbilityLoadoutData
    {
        [Serializable]
        public struct Entry
        {
            public AbilityId Id;
            public bool Enabled;

            public Entry(AbilityId id, bool enabled)
            {
                Id = id;
                Enabled = enabled;
            }
        }

        [Serializable]
        public struct ArtEntry
        {
            public ArtId Id;
            public bool Known;

            public ArtEntry(ArtId id, bool known)
            {
                Id = id;
                Known = known;
            }
        }

        [Tooltip("Abilities this loadout has an opinion about. Anything absent keeps its own default.")]
        public List<Entry> Abilities = new List<Entry>();

        [Tooltip("Arts this loadout has an opinion about. Anything absent stays as the factory " +
                 "seeded it — all known, in the gray box.")]
        public List<ArtEntry> ArtToggles = new List<ArtEntry>();

        /// <summary>Switch modules on or off to match this loadout.</summary>
        public void Apply(CharacterSim sim)
        {
            if (sim == null || Abilities == null) return;

            var modules = sim.Modules;
            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module.Id == AbilityId.Custom) continue;

                if (TryGet(module.Id, out bool enabled))
                    module.Enabled = enabled;
            }

            if (ArtToggles == null) return;

            for (int i = 0; i < ArtToggles.Count; i++)
            {
                if (ArtToggles[i].Known) sim.KnownArts.Add(ArtToggles[i].Id);
                else sim.KnownArts.Remove(ArtToggles[i].Id);
            }

            // Forgetting an art unequips it — the slot cannot hold what the Order has not
            // taught, which is also what keeps the water module from casting a revoked
            // Buoyancy off the default equip.
            if (sim.EquippedArt != ArtId.None && !sim.KnownArts.Contains(sim.EquippedArt))
                sim.EquippedArt = ArtId.None;
        }

        public bool TryGet(AbilityId id, out bool enabled)
        {
            for (int i = 0; i < Abilities.Count; i++)
            {
                if (Abilities[i].Id != id) continue;
                enabled = Abilities[i].Enabled;
                return true;
            }

            enabled = false;
            return false;
        }

        /// <summary>Set an ability's state, adding a row if this loadout had no opinion yet.
        /// This is what an unlock does.</summary>
        public void Set(AbilityId id, bool enabled)
        {
            for (int i = 0; i < Abilities.Count; i++)
            {
                if (Abilities[i].Id != id) continue;
                Abilities[i] = new Entry(id, enabled);
                return;
            }

            Abilities.Add(new Entry(id, enabled));
        }

        /// <summary>Add a row for every ability the character has, at its current state, so the
        /// asset lists everything rather than only what someone has touched.</summary>
        public void PopulateFrom(CharacterSim sim)
        {
            if (sim == null) return;

            var modules = sim.Modules;
            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module.Id == AbilityId.Custom) continue;
                if (TryGet(module.Id, out _)) continue;

                Abilities.Add(new Entry(module.Id, module.Enabled));
            }
        }
    }
}

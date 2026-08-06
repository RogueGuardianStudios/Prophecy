using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The encounter decisions, as plain functions — WHAT can spawn, WHERE it may stand, and
    /// WHERE a contact carries you. The spawner MonoBehaviour is just a clock and a list; every
    /// choice it makes routes through here so the tests can pin the choices headless.
    ///
    /// <para>The two halves are deliberately independent (Matt, 2026-08-05): spawning reads the
    /// PLAYER's cell's province — an empty table or no province at all is a safe place; contact
    /// reads the CONTACT cell — a road there gives the safe crossing whatever province it runs
    /// through, anything else the province's own side-scroll section. The wanderer decides WHO
    /// you fight; the ground underfoot decides WHERE.</para>
    /// </summary>
    public static class OverworldEncounterRules
    {
        /// <summary>
        /// Weighted pick from a province's table. Null on an empty or all-zero table — that is
        /// the safe-province case, not an error. <paramref name="roll01"/> is the caller's
        /// randomness (runtime dice, not world seed: encounter pacing is not part of what the
        /// map compiles to).
        /// </summary>
        public static GameObject PickWanderer(ProvinceWanderer[] table, float roll01)
        {
            if (table == null) return null;

            float total = 0f;
            for (int i = 0; i < table.Length; i++)
                if (table[i] != null && table[i].Prefab != null && table[i].Weight > 0f)
                    total += table[i].Weight;

            if (total <= 0f) return null;

            float pick = Mathf.Clamp01(roll01) * total;
            for (int i = 0; i < table.Length; i++)
            {
                var entry = table[i];
                if (entry == null || entry.Prefab == null || entry.Weight <= 0f) continue;

                pick -= entry.Weight;
                if (pick <= 0f) return entry.Prefab;
            }

            return null;   // roll01 == 1 exactly and float dust — treat the edge as a miss
        }

        /// <summary>
        /// Where a wanderer may appear: plain ground, not refused. Ramps, water and bridge
        /// spans are out — a wanderer materialising mid-stair or mid-channel reads as a bug,
        /// and the flat ground beside them is never far.
        /// </summary>
        public static bool CanHostAWanderer(OverworldTileGrid grid, int x, int z) =>
            grid != null
            && grid.InBounds(x, z)
            && grid.KindAt(x, z) == TileCellKind.Ground
            && !grid.UnwalkableAt(x, z);

        /// <summary>
        /// Where the touch carries the player, from the contact cell outward: a road cell gives
        /// the safe crossing; otherwise the cell's province names its section; otherwise the
        /// wilderness fallback. An empty resolved scene means no transition at all — the touch
        /// is shrugged off, which is what "safe" means mechanically.
        /// </summary>
        public static void ResolveContact(OverworldTileGrid grid, int x, int z,
                                          string roadScene, string roadSpawnId,
                                          string fallbackScene, string fallbackSpawnId,
                                          out string scene, out string spawnId)
        {
            if (grid != null && grid.RoadAt(x, z) && !string.IsNullOrEmpty(roadScene))
            {
                scene = roadScene;
                spawnId = roadSpawnId;
                return;
            }

            var province = grid != null ? grid.ProvinceAt(x, z) : null;
            if (province != null && !string.IsNullOrEmpty(province.EncounterScene))
            {
                scene = province.EncounterScene;
                spawnId = province.EncounterSpawnId;
                return;
            }

            scene = fallbackScene;
            spawnId = fallbackSpawnId;
        }
    }
}

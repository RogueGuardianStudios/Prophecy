using System;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>One wanderer kind a province spawns, weighted.</summary>
    [Serializable]
    public sealed class ProvinceWanderer
    {
        public GameObject Prefab;
        public float Weight = 1f;
    }

    /// <summary>
    /// A province's RULES — what wanders here, how often, and where a contact sends you. It
    /// has NO bounds of its own: the named shapes on the map (terrain regions, biome areas,
    /// greebles) reference it, and the compiler stamps their cells — the Westwood IS a
    /// province, not a rectangle drawn twice (Matt, 2026-08-05). A ScriptableObject so
    /// encounter tuning survives play mode like all tuning.
    ///
    /// <para>The encounter systems are SEPARATE by design: the PLAYER's cell's province
    /// decides WHAT spawns around them (an empty table is a safe province); wanderers roam
    /// free; on contact the CONTACT CELL decides WHERE the fight happens — a road cell gives
    /// the safe crossing, anything else this province's section.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Overworld Province", fileName = "OverworldProvince")]
    public sealed class OverworldProvince : ScriptableObject
    {
        [Tooltip("For the tool and the map legend.")]
        public string DisplayName = "Province";

        [Tooltip("The minimap's province-view tint.")]
        public Color MapTint = Color.white;

        [Header("Spawning — read for the PLAYER's province")]
        [Tooltip("What wanders here. EMPTY = a safe province: nothing ever spawns.")]
        public ProvinceWanderer[] Wanderers = Array.Empty<ProvinceWanderer>();

        [Tooltip("Seconds between spawn attempts while the player is here.")]
        public float SpawnEverySeconds = 11f;

        [Tooltip("Most wanderers alive at once while the player is here.")]
        public int MaxAlive = 3;

        [Header("Encounters — read for the CONTACT cell")]
        [Tooltip("The side-scroll scene a contact in this province sends the player to.")]
        public string EncounterScene = "";

        [Tooltip("Arrival spawn id in that scene.")]
        public string EncounterSpawnId = "default";
    }
}

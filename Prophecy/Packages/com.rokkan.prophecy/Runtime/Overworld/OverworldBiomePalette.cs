using System;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>The ordered biome list: the splat's indices point here. Order is identity —
    /// never reorder a shipped palette, append. Its own file, because a ScriptableObject asset
    /// only keeps its script reference when the class name matches the file name.</summary>
    [CreateAssetMenu(menuName = "Prophecy/Overworld Biome Palette", fileName = "OverworldBiomePalette")]
    public sealed class OverworldBiomePalette : ScriptableObject
    {
        public OverworldBiome[] Biomes = Array.Empty<OverworldBiome>();

        public OverworldBiome ByIndex(int index) =>
            index >= 0 && Biomes != null && index < Biomes.Length ? Biomes[index] : null;
    }
}

using System;
using RGS.Core.Random;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>One candidate prefab for a biome's slot. Weight 1 is the norm; heavier appears
    /// more often; a null prefab or non-positive weight is skipped.</summary>
    [Serializable]
    public sealed class OverworldBiomeVariant
    {
        public GameObject Prefab;
        public float Weight = 1f;
    }

    /// <summary>
    /// A biome's re-skin of the base tile set: the SAME 17 slots, each holding weighted
    /// geometry variants. An EMPTY slot falls back to the gray-box piece — a biome can ship
    /// with only its caps done and stay fully buildable. This is the worldgen picker model
    /// (Biome/Slot/Variant) lifted onto Prophecy's piece vocabulary; adherence to the base set
    /// is by construction, because the slots ARE the placement contract. Variants may change
    /// geometry so long as the mating envelope holds — cut-end planes, rim tiers, skirt
    /// reaches, flat walkable tops are cross-biome law (Plans/Overworld-Tile-Set.md).
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Overworld Biome", fileName = "OverworldBiome")]
    public sealed class OverworldBiome : ScriptableObject
    {
        [Tooltip("For the tool and the map legend.")]
        public string DisplayName = "Biome";

        [Tooltip("The minimap's tint for this biome.")]
        public Color MapTint = Color.white;

        [Tooltip("The ground shader's base colour for this biome — what the caps wear, " +
                 "cross-faded at biome borders by the baked LUT.")]
        public Color GroundColor = new Color(0.45f, 0.66f, 0.45f);

        public OverworldBiomeVariant[] Cap;
        public OverworldBiomeVariant[] Ramp;
        public OverworldBiomeVariant[] Stairs;
        public OverworldBiomeVariant[] Water;
        public OverworldBiomeVariant[] Face1;
        public OverworldBiomeVariant[] Face2;
        public OverworldBiomeVariant[] Face3;
        public OverworldBiomeVariant[] OuterPost1;
        public OverworldBiomeVariant[] OuterPost2;
        public OverworldBiomeVariant[] OuterPost3;
        public OverworldBiomeVariant[] InnerPost1;
        public OverworldBiomeVariant[] InnerPost2;
        public OverworldBiomeVariant[] InnerPost3;
        public OverworldBiomeVariant[] Cheek;
        public OverworldBiomeVariant[] CheekMirrored;
        public OverworldBiomeVariant[] ShoreEdge;
        public OverworldBiomeVariant[] ShoreOuterPost;

        public OverworldBiomeVariant[] For(OverworldTilePiece piece)
        {
            switch (piece)
            {
                case OverworldTilePiece.Cap: return Cap;
                case OverworldTilePiece.Ramp: return Ramp;
                case OverworldTilePiece.Stairs: return Stairs;
                case OverworldTilePiece.Water: return Water;
                case OverworldTilePiece.Face1: return Face1;
                case OverworldTilePiece.Face2: return Face2;
                case OverworldTilePiece.Face3: return Face3;
                case OverworldTilePiece.OuterPost1: return OuterPost1;
                case OverworldTilePiece.OuterPost2: return OuterPost2;
                case OverworldTilePiece.OuterPost3: return OuterPost3;
                case OverworldTilePiece.InnerPost1: return InnerPost1;
                case OverworldTilePiece.InnerPost2: return InnerPost2;
                case OverworldTilePiece.InnerPost3: return InnerPost3;
                case OverworldTilePiece.Cheek: return Cheek;
                case OverworldTilePiece.CheekMirrored: return CheekMirrored;
                case OverworldTilePiece.ShoreEdge: return ShoreEdge;
                case OverworldTilePiece.ShoreOuterPost: return ShoreOuterPost;
                default: return null;
            }
        }
    }

    /// <summary>
    /// The geometry LUT: dominant biome + the cell's derived seed → one variant, forever. Same
    /// map, same seed, same forest every load — regeneration never reshuffles a tile. Pure and
    /// headless, tested like everything that decides what the world is.
    /// </summary>
    public static class OverworldBiomePicker
    {
        /// <summary>The prefab a placement should instantiate: a biome variant when the owner
        /// cell's dominant biome has any for this piece, else <paramref name="fallback"/> (the
        /// gray-box base set).</summary>
        public static GameObject Pick(OverworldBiomePalette palette, OverworldTileGrid grid,
                                      uint mapSeed, OverworldTilePiece piece,
                                      Vector2Int ownerCell, GameObject fallback)
        {
            if (palette == null || grid == null || ownerCell.x < 0 || ownerCell.y < 0)
                return fallback;

            int biomeIndex = grid.DominantBiomeAt(ownerCell.x, ownerCell.y);
            if (biomeIndex < 0) return fallback;

            var biome = palette.ByIndex(biomeIndex);
            var variants = biome != null ? biome.For(piece) : null;
            if (variants == null || variants.Length == 0) return fallback;

            float total = 0f;
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null && variants[i].Prefab != null && variants[i].Weight > 0f)
                    total += variants[i].Weight;
            if (total <= 0f) return fallback;

            // Salted per cell AND per piece, so a cell's cap and its wall roll independently.
            uint salt = (uint)((ownerCell.y * grid.Width + ownerCell.x) * 31 + (int)piece);
            var rng = new RGS_Random(RGS_Random.DeriveSeed(mapSeed, salt));
            float roll = rng.GetNextNormalized() * total;

            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] == null || variants[i].Prefab == null ||
                    variants[i].Weight <= 0f) continue;
                roll -= variants[i].Weight;
                if (roll <= 0f) return variants[i].Prefab;
            }

            return fallback;
        }
    }
}

using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The 17 tile prefabs, one slot per <see cref="OverworldTilePiece"/>. Filled automatically
    /// by <c>Prophecy > Build > Generate Overworld Tiles</c> — hand-wiring seventeen slots is
    /// exactly the prefab-reference-that-silently-fails-to-persist trap this project has already
    /// paid for once.
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Overworld Tile Set", fileName = "OverworldTileSet")]
    public sealed class OverworldTileSet : ScriptableObject
    {
        public GameObject Cap;
        public GameObject Ramp;
        public GameObject Stairs;
        public GameObject Water;
        public GameObject Face1;
        public GameObject Face2;
        public GameObject Face3;
        public GameObject OuterPost1;
        public GameObject OuterPost2;
        public GameObject OuterPost3;
        public GameObject InnerPost1;
        public GameObject InnerPost2;
        public GameObject InnerPost3;
        public GameObject Cheek;
        public GameObject CheekMirrored;
        public GameObject ShoreEdge;
        public GameObject ShoreOuterPost;

        public GameObject For(OverworldTilePiece piece)
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

        /// <summary>Every slot filled? The first empty one is named, because "some slot is null"
        /// is a hunt and a named slot is a fix. The loop walks the ENUM, not a remembered last
        /// member — a piece added to the vocabulary is validated the day it exists, instead of
        /// slipping past a stale bound into Instantiate(null) at build time.</summary>
        public bool IsComplete(out string firstMissing)
        {
            foreach (OverworldTilePiece piece in System.Enum.GetValues(typeof(OverworldTilePiece)))
            {
                if (For(piece) == null)
                {
                    firstMissing = piece.ToString();
                    return false;
                }
            }

            firstMissing = null;
            return true;
        }
    }
}

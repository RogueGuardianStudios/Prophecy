using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rokkan.Prophecy.Overworld;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Adding a tile piece touches several places — the enum, the set's slot, the biome's slot,
    /// the two For switches — and the failure mode for a missed one is Instantiate(null) at
    /// build time, or IsComplete waving through a set whose newest slot it never checked.
    /// These tests walk the ENUM, so the day a piece is appended they demand its slot, its
    /// switch case, and its IsComplete coverage by name: each For must return exactly the slot
    /// that bears the member's name (proving the switch neither falls through to null nor maps
    /// two members to one field), and IsComplete must refuse each hole individually.
    /// </summary>
    public sealed class OverworldPieceCoverageTests
    {
        private readonly List<UnityEngine.Object> _cleanup = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _cleanup.Clear();
        }

        private static Array Pieces => Enum.GetValues(typeof(OverworldTilePiece));

        private static FieldInfo SlotField(Type owner, OverworldTilePiece piece, Type slotType)
        {
            var field = owner.GetField(piece.ToString(),
                                       BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field,
                $"{owner.Name} has no public slot named '{piece}' — the enum grew and the " +
                "asset did not.");
            Assert.AreEqual(slotType, field.FieldType,
                $"{owner.Name}.{piece} is not a {slotType.Name} slot.");
            return field;
        }

        [Test]
        public void EveryPieceHasItsOwnTileSetSlot()
        {
            var tiles = ScriptableObject.CreateInstance<OverworldTileSet>();
            _cleanup.Add(tiles);

            foreach (OverworldTilePiece piece in Pieces)
            {
                var field = SlotField(typeof(OverworldTileSet), piece, typeof(GameObject));
                var marker = new GameObject($"Slot_{piece}");
                _cleanup.Add(marker);
                field.SetValue(tiles, marker);

                Assert.AreSame(marker, tiles.For(piece),
                    $"OverworldTileSet.For({piece}) does not answer the '{piece}' slot — " +
                    "the switch fell through or mapped the wrong field.");
            }
        }

        [Test]
        public void EveryPieceHasItsOwnBiomeSlot()
        {
            var biome = ScriptableObject.CreateInstance<OverworldBiome>();
            _cleanup.Add(biome);

            foreach (OverworldTilePiece piece in Pieces)
            {
                var field = SlotField(typeof(OverworldBiome), piece,
                                      typeof(OverworldBiomeVariant[]));
                var marker = new[] { new OverworldBiomeVariant() };
                field.SetValue(biome, marker);

                Assert.AreSame(marker, biome.For(piece),
                    $"OverworldBiome.For({piece}) does not answer the '{piece}' slot — " +
                    "the switch fell through or mapped the wrong field.");
            }
        }

        [Test]
        public void IsCompleteRefusesEachEmptySlotByName()
        {
            var tiles = ScriptableObject.CreateInstance<OverworldTileSet>();
            _cleanup.Add(tiles);
            var filler = new GameObject("Filler");
            _cleanup.Add(filler);

            foreach (OverworldTilePiece piece in Pieces)
                SlotField(typeof(OverworldTileSet), piece, typeof(GameObject))
                    .SetValue(tiles, filler);

            Assert.IsTrue(tiles.IsComplete(out string missing),
                $"A fully filled set reports slot '{missing}' missing.");

            foreach (OverworldTilePiece piece in Pieces)
            {
                var field = SlotField(typeof(OverworldTileSet), piece, typeof(GameObject));
                field.SetValue(tiles, null);

                Assert.IsFalse(tiles.IsComplete(out missing),
                    $"IsComplete never looked at the '{piece}' slot.");
                Assert.AreEqual(piece.ToString(), missing,
                    "The hole must be named — a hunt is not a fix.");

                field.SetValue(tiles, filler);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Rokkan.Prophecy.Sim.Stats;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Arts
{
    /// <summary>The eight arts (UI/Input spec §3.4). One equipped at a time, ever.</summary>
    public enum ArtId
    {
        None = 0,
        Ward = 1,
        Sharpen = 2,
        Ascent = 3,
        DivineFlame = 4,
        Censer = 5,
        Buoyancy = 6,
        Chalice = 7,
        GluttonForPunishment = 8,
    }

    /// <summary>
    /// One art's authored face: name, cost, and the facts everything dispatches on.
    ///
    /// <para><b>The dispatch facts are data on purpose.</b> An art's bespoke rules used to be
    /// an if-ladder of id checks scattered across the cast path, the HUD and the volume — and
    /// with five arts still stubs, every one of them was queued to add its own cases to all
    /// three. An art declares its rules here instead, and the code that honours them never
    /// names an id: the next behaviour-art is a table row, not surgery.</para>
    /// </summary>
    [Serializable]
    public sealed class ArtEntry
    {
        public ArtId Id;
        public string DisplayName;

        [TextArea, Tooltip("The volume's one-line telling of what the art does — intent, not " +
                           "mechanics, since most effects are still arriving art by art.")]
        public string Description;

        [Tooltip("ACTIVATION cost. Dropping a running art is free, the rule (Matt) — the " +
                 "NoRecall vow excepted.")]
        public float Cost;

        [Tooltip("This art's whole verb lives in an ability module (Buoyancy's water toggle): " +
                 "the cast button routes to that module, and the volume's page cannot cast it.")]
        public bool ModuleOwned;

        [Tooltip("The vow: once running it cannot be recalled. Only the room's end clears it.")]
        public bool NoRecall;

        [Tooltip("The cast converts reserve into a filled flask and ends there — nothing " +
                 "marks as running.")]
        public bool ConvertsReserveToFlask;

        [Tooltip("Apply the effect below for as long as the art runs.")]
        public bool HasEffect;

        [Tooltip("The art's stat effect. Room-scoping is the arts RULE — the room is the " +
                 "timer — so the cast applies it room-scoped whatever the duration here says.")]
        public StatModifierSpec Effect;
    }

    /// <summary>
    /// The authored default table — the serialized starting point of every <c>ArtTuning</c>
    /// asset, and the table an unwired sim runs on, so a headless test always plays the
    /// shipped arts. Costs are ACTIVATION costs: dropping a running art is free, the rule
    /// (Matt) — GfP excepted, the vow that cannot be recalled. Buoyancy charges one pip's
    /// worth to light the float (its module spends this number; launches with the float
    /// already lit are free).
    /// </summary>
    public static class ArtCatalog
    {
        public static ArtEntry[] DefaultTable() => new[]
        {
            new ArtEntry
            {
                Id = ArtId.Ward, DisplayName = "Ward", Cost = 3f,
                Description = "Turns aside the next blow that would have landed.",
            },
            new ArtEntry
            {
                Id = ArtId.Sharpen, DisplayName = "Sharpen", Cost = 2f,
                Description = "The blade bites a quarter deeper while this room holds.",
                HasEffect = true,
                Effect = new StatModifierSpec(StatKind.Might, StatStage.Percent, 0.25f),
            },
            new ArtEntry
            {
                Id = ArtId.Ascent, DisplayName = "Ascent", Cost = 2f,
                Description = "Gildhollow's gift — a second jump, taken from the air.",
            },
            new ArtEntry
            {
                Id = ArtId.DivineFlame, DisplayName = "Divine Flame of Justice", Cost = 3f,
                Description = "A burst of judgement upon all who stand too near.",
            },
            new ArtEntry
            {
                Id = ArtId.Censer, DisplayName = "Censer", Cost = 2f,
                Description = "A swung brazier whose smoke slows all it touches.",
            },
            new ArtEntry
            {
                Id = ArtId.Buoyancy, DisplayName = "Buoyancy", Cost = 1.5f,
                Description = "Mirefen's peace with water — stand upon it, or launch from beneath it.",
                ModuleOwned = true,
            },
            new ArtEntry
            {
                Id = ArtId.Chalice, DisplayName = "Chalice", Cost = 4f,
                Description = "Pours reserve into an empty flask, corner to corner.",
                ConvertsReserveToFlask = true,
            },
            new ArtEntry
            {
                Id = ArtId.GluttonForPunishment, DisplayName = "Glutton for Punishment", Cost = 5f,
                Description = "A vow that cannot be recalled until the room is done.",
                NoRecall = true,
            },
        };
    }

    /// <summary>
    /// The art table and its feel numbers as plain data — the payload of the <c>ArtTuning</c>
    /// asset, and the reason a cost retune is an inspector edit that survives play mode
    /// rather than a recompile. Held on <see cref="CharacterSim"/> by reference, the same
    /// live-tuning mechanism every other tuning surface uses.
    /// </summary>
    [Serializable]
    public sealed class ArtTuningData
    {
        [SerializeField, Tooltip("Seconds A must be held on the volume's page before a cast " +
                                 "commits. UI feel, authored beside the arts it commits.")]
        private float _holdToCastSeconds = 0.45f;

        [SerializeField] private ArtEntry[] _arts = ArtCatalog.DefaultTable();

        public float HoldToCastSeconds => _holdToCastSeconds;

        /// <summary>Every authored art, in page order.</summary>
        public IReadOnlyList<ArtEntry> Arts => _arts;

        /// <summary>The entry for <paramref name="id"/>, or null for an art the table does
        /// not know — which every caller must treat as "this art does not exist".</summary>
        public ArtEntry Find(ArtId id)
        {
            for (int i = 0; i < _arts.Length; i++)
                if (_arts[i] != null && _arts[i].Id == id) return _arts[i];

            return null;
        }

        /// <summary>The cost of <paramref name="id"/>, or zero for an unknown art — the
        /// null-safe read the HUD's bar segment wants.</summary>
        public float CostOf(ArtId id)
        {
            var entry = Find(id);
            return entry != null ? entry.Cost : 0f;
        }
    }
}

using System;
using Rokkan.Prophecy.Sim.Stats;

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
    /// One art's authored face: name, cost, and its gray-box effect. Real effects arrive art
    /// by art; until then a cast is a spend plus a running mark, which is everything the HUD
    /// and the arts volume need to be built against.
    /// </summary>
    public readonly struct ArtEntry
    {
        public readonly ArtId Id;
        public readonly string DisplayName;

        /// <summary>The volume's one-line telling of what the art does — intent, not
        /// mechanics, since most effects are still arriving art by art.</summary>
        public readonly string Description;

        public readonly float Cost;

        /// <summary>Room-scoped stat effect, or default for arts whose behavior lives
        /// elsewhere (Buoyancy's module, Chalice's conversion) or is not built yet.</summary>
        public readonly StatModifier Effect;
        public readonly bool HasEffect;

        public ArtEntry(ArtId id, string displayName, string description, float cost)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Cost = cost;
            Effect = default;
            HasEffect = false;
        }

        public ArtEntry(ArtId id, string displayName, string description, float cost,
                        StatModifier effect)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Cost = cost;
            Effect = effect;
            HasEffect = true;
        }
    }

    /// <summary>
    /// The art table, static for the gray box (spec costs, placeholder effects). Costs are
    /// ACTIVATION costs: dropping a running art is free, the rule (Matt) — GfP excepted, the
    /// vow that cannot be recalled. Buoyancy charges one pip's worth to light the float
    /// (its module spends this number; launches with the float already lit are free).
    /// </summary>
    public static class ArtCatalog
    {
        public static readonly ArtEntry[] All =
        {
            new ArtEntry(ArtId.Ward, "Ward",
                "Turns aside the next blow that would have landed.", 3f),
            new ArtEntry(ArtId.Sharpen, "Sharpen",
                "The blade bites a quarter deeper while this room holds.", 2f,
                new StatModifier
                {
                    Kind = StatKind.Might,
                    Stage = StatStage.Percent,
                    Value = 0.25f,
                    ExpiresOnTick = StatModifier.Permanent,
                    RoomScoped = true,
                }),
            new ArtEntry(ArtId.Ascent, "Ascent",
                "Gildhollow's gift — a second jump, taken from the air.", 2f),
            new ArtEntry(ArtId.DivineFlame, "Divine Flame of Justice",
                "A burst of judgement upon all who stand too near.", 3f),
            new ArtEntry(ArtId.Censer, "Censer",
                "A swung brazier whose smoke slows all it touches.", 2f),
            new ArtEntry(ArtId.Buoyancy, "Buoyancy",
                "Mirefen's peace with water — stand upon it, or launch from beneath it.", 1.5f),
            new ArtEntry(ArtId.Chalice, "Chalice",
                "Pours reserve into an empty flask, corner to corner.", 4f),
            new ArtEntry(ArtId.GluttonForPunishment, "Glutton for Punishment",
                "A vow that cannot be recalled until the room is done.", 5f),
        };

        public static ArtEntry Find(ArtId id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];

            return default;
        }
    }
}

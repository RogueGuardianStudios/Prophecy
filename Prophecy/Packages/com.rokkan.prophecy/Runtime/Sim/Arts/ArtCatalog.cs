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
        public readonly float Cost;

        /// <summary>Room-scoped stat effect, or default for arts whose behavior lives
        /// elsewhere (Buoyancy's module, Chalice's conversion) or is not built yet.</summary>
        public readonly StatModifier Effect;
        public readonly bool HasEffect;

        public ArtEntry(ArtId id, string displayName, float cost)
        {
            Id = id;
            DisplayName = displayName;
            Cost = cost;
            Effect = default;
            HasEffect = false;
        }

        public ArtEntry(ArtId id, string displayName, float cost, StatModifier effect)
        {
            Id = id;
            DisplayName = displayName;
            Cost = cost;
            Effect = effect;
            HasEffect = true;
        }
    }

    /// <summary>
    /// The art table, static for the gray box (spec costs, placeholder effects). Buoyancy
    /// costs nothing HERE deliberately: its cast is the water toggle Matt tuned by hand, and
    /// charging it now would break that loop — the cost joins when the Flame economy is real.
    /// </summary>
    public static class ArtCatalog
    {
        public static readonly ArtEntry[] All =
        {
            new ArtEntry(ArtId.Ward, "Ward", 3f),
            new ArtEntry(ArtId.Sharpen, "Sharpen", 2f, new StatModifier
            {
                Kind = StatKind.Might,
                Stage = StatStage.Percent,
                Value = 0.25f,
                ExpiresOnTick = StatModifier.Permanent,
                RoomScoped = true,
            }),
            new ArtEntry(ArtId.Ascent, "Ascent", 2f),
            new ArtEntry(ArtId.DivineFlame, "Divine Flame of Justice", 3f),
            new ArtEntry(ArtId.Censer, "Censer", 2f),
            new ArtEntry(ArtId.Buoyancy, "Buoyancy", 0f),
            new ArtEntry(ArtId.Chalice, "Chalice", 4f),
            new ArtEntry(ArtId.GluttonForPunishment, "Glutton for Punishment", 5f),
        };

        public static ArtEntry Find(ArtId id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];

            return default;
        }
    }
}

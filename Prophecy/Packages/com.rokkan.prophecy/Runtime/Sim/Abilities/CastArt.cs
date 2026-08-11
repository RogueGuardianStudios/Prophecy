using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Arts;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// The cast button (RB, spec §1.2), promoted from the FlameArt planned stub the way every
    /// stub graduates: into its own file. Casts THE equipped art — there is exactly one, and
    /// selection lives only in the arts volume (§3.1).
    ///
    /// <para>A cast is: afford it or fail silently (no error state beyond the HUD emblem
    /// already reading "not enough"), spend, mark it running, apply its room-scoped effect.
    /// Stacking is free — a second cast never cancels the first (§3.3); the economy is the
    /// only brake. The room is the timer: the marks and the modifiers both die at a door.</para>
    ///
    /// <para>Buoyancy is the exception that proves the module rule: its cast IS the water
    /// toggle Matt tuned, handled by its own module — this one stands aside when Buoyancy is
    /// equipped, and Buoyancy stands aside when it is not. Chalice is the other special: it
    /// converts reserve into a flask, bottom-right to bottom-left.</para>
    /// </summary>
    public sealed class CastArt : AbilityModule
    {
        private readonly CombatTuningData _combat;

        public CastArt(CombatTuningData combat)
        {
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.FlameArt;
        public override int Order => ModuleOrder.FlameArt;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (!input.FlameArt.Pressed) return;
            if (sim.EquippedArt == ArtId.None) return;
            if (sim.EquippedArt == ArtId.Buoyancy) return;   // the water module owns that cast
            if (!sim.Can(LockFlags.Attack)) return;          // no casting out of a hit-react

            Cast(sim, sim.EquippedArt, info.Tick);
        }

        /// <summary>The one cast path — the arts volume calls this too, so playing a cast
        /// from the page and from the button are the same act.</summary>
        public static bool Cast(CharacterSim sim, ArtId id, long tick)
        {
            var entry = ArtCatalog.Find(id);
            if (entry.Id == ArtId.None) return false;

            if (!sim.Reserve.TrySpend(entry.Cost)) return false;

            if (id == ArtId.Chalice)
            {
                // Reserve becomes a flask: the conversion the HUD shows moving corner to
                // corner. A full row wastes the cast — the silence is the lesson, same as
                // drinking at full health.
                sim.Flasks.FillOne();
                return true;
            }

            if (!sim.ActiveArts.Contains(id)) sim.ActiveArts.Add(id);

            if (entry.HasEffect)
            {
                var effect = entry.Effect;
                effect.ExpiresOnTick = Stats.StatModifier.Permanent;
                sim.Stats.Add(effect);   // RoomScoped on the modifier is the whole duration
            }

            return true;
        }
    }
}

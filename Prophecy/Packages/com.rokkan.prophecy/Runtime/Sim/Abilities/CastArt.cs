using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Arts;

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
    /// <para><b>Nothing here names an art.</b> The rules that used to be id checks — the
    /// module that owns its own cast, the vow that cannot be recalled, the conversion that
    /// ends at the flask — are authored facts on <see cref="ArtEntry"/>, so a new art with
    /// bespoke rules is a table row and this file does not change.</para>
    ///
    /// <para>Page casts arrive as requests parked on the sim by the arts volume and are
    /// consumed here, at the top of the tick — so a cast from the page and a cast from the
    /// button are the same act, on a tick, either way.</para>
    /// </summary>
    public sealed class CastArt : AbilityModule
    {
        public override AbilityId Id => AbilityId.FlameArt;
        public override int Order => ModuleOrder.FlameArt;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            // The page's parked casts land first, gated exactly as a button cast is. A tick
            // that cannot cast (hit-stun) leaves them parked for the tick that can.
            while (sim.Can(LockFlags.Attack) && sim.TryDequeueCastRequest(out var requested))
                Cast(sim, requested, info.Tick);

            if (!input.FlameArt.Pressed) return;

            var equipped = sim.ArtTuning.Find(sim.EquippedArt);
            if (equipped == null) return;
            if (equipped.ModuleOwned) return;        // that art's module owns the whole verb
            if (!sim.Can(LockFlags.Attack)) return;  // no casting out of a hit-react

            Cast(sim, sim.EquippedArt, info.Tick);
        }

        /// <summary>The one cast path — button presses and the volume's parked requests both
        /// resolve here. An art the Order has not taught refuses, whatever asked.</summary>
        public static bool Cast(CharacterSim sim, ArtId id, long tick)
        {
            var entry = sim.ArtTuning.Find(id);
            if (entry == null || entry.Id == ArtId.None) return false;
            if (!sim.KnownArts.Contains(id)) return false;
            if (entry.ModuleOwned) return false;

            // Recasting a RUNNING art is the DROP, and dropping is FREE — the rule (Matt).
            // The one exception is the vow, which stays until the room is done.
            if (sim.ActiveArts.Contains(id))
            {
                if (entry.NoRecall) return false;

                sim.ActiveArts.Remove(id);
                sim.Stats.RemoveSource(ArtSource(id));
                return true;
            }

            if (!sim.Reserve.TrySpend(entry.Cost)) return false;

            if (entry.ConvertsReserveToFlask)
            {
                // Reserve becomes a flask: the conversion the HUD shows moving corner to
                // corner. A full row wastes the cast — the silence is the lesson, same as
                // drinking at full health.
                sim.Flasks.FillOne();
                return true;
            }

            sim.ActiveArts.Add(id);

            if (entry.HasEffect)
            {
                // Room-scoping is the arts RULE, not per-art authoring: the room is the
                // timer, so the effect is applied room-scoped whatever its spec says.
                var effect = entry.Effect.Resolve(tick, ArtSource(id));
                effect.ExpiresOnTick = Stats.StatModifier.Permanent;
                effect.RoomScoped = true;
                sim.Stats.Add(effect);
            }

            return true;
        }

        /// <summary>Stat-source id for an art's own effect — its own range so a drop
        /// removes exactly one art's modifiers and can never touch another source's.</summary>
        private static int ArtSource(ArtId id) => 1000 + (int)id;
    }
}

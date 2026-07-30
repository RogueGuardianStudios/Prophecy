using System;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// Everything one attack does, in ticks. The authored unit of the moveset.
    ///
    /// <para><b>Phases are three durations, not three ranges.</b> Startup, active and recovery are
    /// authored as lengths that sum to the total, so they tile the action exactly — they cannot
    /// overlap, cannot leave a gap, and cannot be malformed. Three start/end pairs could do all
    /// three and nothing would catch it. Fighting-game frame data works this way for the same
    /// reason.</para>
    ///
    /// <para><b>Windows are laid over the phases, not inside them.</b> An i-frame window routinely
    /// straddles startup and active; a cancel window usually opens partway through recovery. So
    /// windows are absolute tick ranges over the whole action and are validated against its
    /// length, rather than being expressed per phase.</para>
    ///
    /// <para>Hit timing is fixed; survival timing scales. Hit boxes carry plain
    /// <see cref="TickRange"/>s because when your sword is dangerous is the move's identity, while
    /// parry and i-frames are <see cref="ScalableWindow"/>s because that is what gear should be
    /// able to widen.</para>
    /// </summary>
    [Serializable]
    public class AttackDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable id. Combo links name their successor by this, and a save file can store it.")]
        public string Id = "attack";

        [Tooltip("Stance the character must be in to start this. Air enables the down-thrust.")]
        public Stance RequiredStance = Stance.Stand;

        // ------------------------------------------------------------------ phases

        [Header("Phases (ticks — they partition the action)")]
        [Tooltip("Wind-up before anything is dangerous. This is the telegraph the player reads.")]
        public int StartupTicks = 8;

        [Tooltip("The committed swing. Hit windows live inside this.")]
        public int ActiveTicks = 6;

        [Tooltip("The vulnerable tail. Cancel windows usually open partway through this.")]
        public int RecoveryTicks = 10;

        /// <summary>Total length of the action. Derived, so it can never disagree with the phases.</summary>
        public int TotalTicks => Mathf.Max(0, StartupTicks) + Mathf.Max(0, ActiveTicks) + Mathf.Max(0, RecoveryTicks);

        public TickRange StartupRange => new TickRange(0, Mathf.Max(0, StartupTicks));

        public TickRange ActiveRange =>
            new TickRange(Mathf.Max(0, StartupTicks), Mathf.Max(0, StartupTicks) + Mathf.Max(0, ActiveTicks));

        public TickRange RecoveryRange =>
            new TickRange(ActiveRange.End, ActiveRange.End + Mathf.Max(0, RecoveryTicks));

        // ------------------------------------------------------------------ hits

        [Header("Hits")]
        [Tooltip("Damaging volumes. Plural for multi-hit moves; each carries its own window and box.")]
        public AttackHitBox[] HitBoxes = new AttackHitBox[0];

        // ------------------------------------------------------------------ defence

        [Header("Defensive windows (scale with gear)")]
        [Tooltip("Invulnerability. A dodge is mostly this; most attacks have none.")]
        public ScalableWindow IFrames = ScalableWindow.None;

        [Tooltip("Deflects an incoming attack. Only defensive actions author this.")]
        public ScalableWindow ParryWindow = ScalableWindow.None;

        // ------------------------------------------------------------------ chaining

        [Header("Chaining")]
        [Tooltip("When a higher-priority action — or the next combo link — may take over. Usually " +
                 "opens partway through recovery, which is what makes a chain feel responsive " +
                 "without letting the player skip the commitment.")]
        public ScalableWindow CancelWindow = ScalableWindow.None;

        [Tooltip("Id of the attack this chains into when the attack button is pressed during the " +
                 "cancel window. Empty ends the chain.")]
        public string ChainsInto = "";

        // ------------------------------------------------------------------ finisher

        [Header("Finisher")]
        [Tooltip("Requires a staggered target in range. Doom-model: fast, in place, grants " +
                 "resources on connect. Expressible now; the executable-target state is not built yet.")]
        public bool RequiresExecutableTarget;

        // ------------------------------------------------------------------ validation

        /// <summary>
        /// Describes anything malformed, or null if the definition is sound. Called by the editor
        /// tooling and by tests — an attack whose hit window sits outside its own duration is a
        /// silent dud, and finding that by playtest is a waste of an afternoon.
        /// </summary>
        public string Validate()
        {
            if (StartupTicks < 0 || ActiveTicks < 0 || RecoveryTicks < 0)
                return $"{Id}: phase durations cannot be negative";

            if (TotalTicks <= 0)
                return $"{Id}: the action has no duration";

            if (HitBoxes != null)
            {
                for (int i = 0; i < HitBoxes.Length; i++)
                {
                    var box = HitBoxes[i];
                    if (!box.Window.IsActive)
                        return $"{Id}: hit box {i} has no window";

                    if (box.Window.End > TotalTicks)
                        return $"{Id}: hit box {i} runs past the end of the action ({box.Window.End} > {TotalTicks})";

                    if (box.HalfExtents.x <= 0f || box.HalfExtents.y <= 0f)
                        return $"{Id}: hit box {i} has no size";
                }
            }

            if (IFrames.IsAuthored && IFrames.BaseStart >= TotalTicks)
                return $"{Id}: the i-frame window starts after the action ends";

            if (ParryWindow.IsAuthored && ParryWindow.BaseStart >= TotalTicks)
                return $"{Id}: the parry window starts after the action ends";

            if (CancelWindow.IsAuthored && CancelWindow.BaseStart >= TotalTicks)
                return $"{Id}: the cancel window starts after the action ends";

            if (!string.IsNullOrEmpty(ChainsInto) && !CancelWindow.IsAuthored)
                return $"{Id}: chains into '{ChainsInto}' but has no cancel window, so the chain can never fire";

            return null;
        }
    }
}

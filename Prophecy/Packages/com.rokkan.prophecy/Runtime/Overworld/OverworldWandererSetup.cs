using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// What a freshly instantiated body needs to LIVE on the overworld, in one place. Every
    /// spawn path — the encounter spawner today, a scripted ambush or a debug drop tomorrow —
    /// applies this, so no caller can silently lack a piece of the composition: knowledge that
    /// lives in one spawner is a bug waiting in the second one.
    ///
    /// <para>The prefab is authored for side-scroll, because every other scene is; the
    /// overworld is the thing that knows which space it plays in. And it is the thing that
    /// knows it has a baked mesh: the steering oracle bends roam headings along it
    /// (side-scroll bodies never get one, and the strategies degrade to their raw headings
    /// without it).</para>
    /// </summary>
    public static class OverworldWandererSetup
    {
        /// <summary>Configure an instantiated wanderer for overworld life. Idempotent — a
        /// prefab that already carries its oracle keeps the one it has.</summary>
        public static void Apply(GameObject wanderer)
        {
            if (wanderer == null) return;

            var host = wanderer.GetComponent<PlayerCharacterHost>();
            if (host != null) host.ConfigureSpace(MovementSpace.TopDown);

            if (wanderer.GetComponent<NavSteeringOracle>() == null)
                wanderer.AddComponent<NavSteeringOracle>();
        }
    }
}

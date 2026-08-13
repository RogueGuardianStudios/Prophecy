using Rokkan.Prophecy.Sim.Arts;
using UnityEngine;

namespace Rokkan.Prophecy.Core
{
    /// <summary>
    /// The authored art table as a project asset — the live tuning surface for costs, the
    /// dispatch facts, and the page's feel numbers.
    ///
    /// <para>Deliberately a shell, exactly as <see cref="MovementTuning"/> is: it owns an
    /// <see cref="ArtTuningData"/> and hands out the reference, so inspector edits land on
    /// the next tick and survive leaving play mode, while the data itself stays plain C#
    /// a headless test can construct.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Art Tuning", fileName = "ArtTuning")]
    public sealed class ArtTuning : ScriptableObject
    {
        [SerializeField] private ArtTuningData _data = new ArtTuningData();

        /// <summary>The live table. Held by reference, so edits apply immediately.</summary>
        public ArtTuningData Data => _data;
    }
}

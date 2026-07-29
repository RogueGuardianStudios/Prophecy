using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Core
{
    /// <summary>
    /// The authored movement numbers as a project asset — the live tuning surface.
    ///
    /// <para>Deliberately a shell. All it does is own a <see cref="MovementTuningData"/> and hand
    /// out the reference; no logic lives here. That keeps the asset on the presentation side of
    /// the line while the numbers themselves stay plain C# that a headless test can construct.</para>
    ///
    /// <para>It sits in <c>Rokkan.Prophecy.Core</c> rather than <c>.Sim</c> because the gate
    /// rejects <c>UnityEngine.Object</c> types under the Sim namespace — correctly, since a
    /// ScriptableObject cannot be part of something that must run with no engine at all.</para>
    ///
    /// <para>Because <see cref="MovementTuningData"/> is a reference type and modules keep the
    /// reference rather than a copy, inspector edits take effect on the next tick without
    /// leaving play mode. Tune while playing; the values survive the exit.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Movement Tuning", fileName = "MovementTuning")]
    public sealed class MovementTuning : ScriptableObject
    {
        [SerializeField] private MovementTuningData _data = new MovementTuningData();

        /// <summary>The live numbers. Held by reference, so edits apply immediately.</summary>
        public MovementTuningData Data => _data;
    }
}

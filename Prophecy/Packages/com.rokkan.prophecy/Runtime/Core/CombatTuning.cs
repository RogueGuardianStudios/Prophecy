using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Core
{
    /// <summary>
    /// The authored moveset as a project asset — combat's live tuning surface, and the one place
    /// frame data lives.
    ///
    /// <para>Same shell as <see cref="MovementTuning"/>, for the same reasons: no logic, the gate
    /// rejects <c>UnityEngine.Object</c> under the Sim namespace, and holding the data by
    /// reference means an inspector edit lands on the next tick without leaving play mode. Combat
    /// numbers are found by feel, so being able to widen a parry window mid-fight and keep the
    /// change is not a convenience — it is how they get authored at all.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Combat Tuning", fileName = "CombatTuning")]
    public sealed class CombatTuning : ScriptableObject
    {
        [SerializeField] private CombatTuningData _data = new CombatTuningData();

        /// <summary>The live moveset. Held by reference, so edits apply immediately.</summary>
        public CombatTuningData Data => _data;

        private void OnValidate()
        {
            // The array may have been resized in the inspector, which renames nothing but does
            // invalidate the lookup built from it.
            _data?.Rebuild();
        }
    }
}

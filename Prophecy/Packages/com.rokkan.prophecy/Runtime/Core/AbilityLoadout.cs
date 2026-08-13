using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Core
{
    /// <summary>
    /// The asset that says which abilities are switched on — and, later, the thing a save file
    /// writes to when the player earns one.
    ///
    /// <para>A shell around <see cref="AbilityLoadoutData"/> for the same reason
    /// <see cref="MovementTuning"/> is a shell: the gate refuses a <c>UnityEngine.Object</c> under
    /// <c>Rokkan.Prophecy.Sim</c>, so the data is plain C# a headless test can build and this is
    /// only its wrapper.</para>
    ///
    /// <para>Ship more than one. A "gray box, everything on" loadout and a "new game, almost
    /// nothing on" loadout are two assets, not two code paths — which makes testing a late-game
    /// moveset a matter of swapping a reference on the player.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Ability Loadout", fileName = "AbilityLoadout")]
    public sealed class AbilityLoadout : ScriptableObject
    {
        [SerializeField] private AbilityLoadoutData _data = new AbilityLoadoutData();

        /// <summary>The live list. Held by reference, so ticking a box mid-play applies at once.</summary>
        public AbilityLoadoutData Data => _data;
    }
}

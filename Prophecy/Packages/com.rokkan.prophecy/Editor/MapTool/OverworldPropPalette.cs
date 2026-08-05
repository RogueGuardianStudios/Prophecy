using UnityEngine;

namespace Rokkan.Prophecy.Editor.MapTool
{
    /// <summary>
    /// The prop picker's shelf: the prefabs a designer places from the map tool. An editor-only
    /// asset — the MAP stores direct prefab references per placed prop; this list is only what
    /// the picker offers, so reorganising the shelf never touches authored content.
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Overworld Prop Palette", fileName = "OverworldPropPalette")]
    public sealed class OverworldPropPalette : ScriptableObject
    {
        [Tooltip("What the Place Prop picker offers. Towns, trees, signs — anything that " +
                 "stands on tiles.")]
        public GameObject[] Prefabs = System.Array.Empty<GameObject>();
    }
}

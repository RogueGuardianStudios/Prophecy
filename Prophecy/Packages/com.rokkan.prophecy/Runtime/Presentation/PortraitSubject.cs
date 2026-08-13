using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Marks the transform the pack portrait films — the model root the world is actually
    /// showing.
    ///
    /// <para>The portrait used to find its subject by child NAME ("HeroModel", then "Body"),
    /// which is the kind of contract a rename breaks with no compile error and no warning: the
    /// double silently fails to build and the sheet shows empty parchment. A component is a
    /// contract the editor can see and a search can find. The name lookup survives in
    /// <see cref="UI.PlayerPortrait"/> as the fallback, because bodies installed before this
    /// marker existed carry no marker.</para>
    ///
    /// <para>Put it on the visual root only — never on the character host itself. A cloned
    /// host would carry the sim and the input capture with it, which is exactly the clone the
    /// portrait refuses to make; a marker misplaced there resolves to the host, the portrait
    /// declines to film, and the pack falls back to its gray-box figure.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortraitSubject : MonoBehaviour
    {
    }
}

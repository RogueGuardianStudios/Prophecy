using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Marks a collider as a one-way platform: you jump up through it and land on top.
    ///
    /// <para>A component rather than a layer or a tag. Layers are a scarce global resource that
    /// gets fought over as a project grows, and a tag is a string nobody can find the definition
    /// of. A component is self-documenting on the object, survives being reparented into a prefab,
    /// and shows up in a search.</para>
    ///
    /// <para>Carries no data — its presence <i>is</i> the data. <see cref="CollisionBaker"/> reads
    /// it once at load and records the platform's kind in the sim's own collision world; nothing
    /// consults this component during a tick.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OneWayPlatform : MonoBehaviour
    {
    }
}

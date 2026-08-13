using System.Collections.Generic;
using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Renders the projectiles the simulation is flying.
    ///
    /// <para><b>They existed and were invisible.</b> Projectiles live entirely in the sim — spawned
    /// by <c>AttackModule</c>, swept and expired by <c>ProjectileSystem</c>, dealing damage the
    /// whole time — and the only thing that ever drew one was the F2 debug overlay. So a caster
    /// firing looked exactly like a caster failing to fire, and the bolt that hit you came from
    /// nowhere.</para>
    ///
    /// <para><b>Reads, never writes.</b> Position, size and lifetime all come from the sim; this
    /// parents a proxy to each live projectile and removes it when the sim says it is gone. Nothing
    /// here can change where a bolt is or whether it hits, which is the same split the hit boxes
    /// keep — presentation may mirror a decision, never make one.</para>
    ///
    /// <para>Proxies are pooled rather than created per shot: a caster fires on a cadence, and
    /// allocating a primitive per bolt is a garbage spike on exactly the frames that are already
    /// busy.</para>
    /// </summary>
    [DefaultExecutionOrder(100)]   // after the sim has ticked
    public sealed class ProjectileView : MonoBehaviour
    {
        [SerializeField, Tooltip("Leave empty to find the director in the scene.")]
        private CombatDirector _director;

        [SerializeField, Tooltip("Drawn at the projectile's own half-extents, so what you see is " +
                                 "the volume that hits you.")]
        private Color _colour = new Color(1f, 0.55f, 0.15f);

        [SerializeField, Tooltip("Depth along the rail the proxies sit at. Matches the characters' " +
                                 "own rail depth so a bolt flies in the plane it is resolved in.")]
        private float _railDepth;

        private readonly List<Transform> _pool = new List<Transform>();
        private Material _material;

        private void Awake()
        {
            if (_director == null) _director = FindAnyObjectByType<CombatDirector>();

            // One shared material; a property block would be overkill for untextured proxies that
            // are all the same colour.
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _material.color = _colour;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        private void LateUpdate()
        {
            if (_director == null) return;

            var live = _director.Projectiles;
            var space = _director.Space;

            for (int i = 0; i < live.Count; i++)
            {
                var proxy = Proxy(i);
                var projectile = live[i];

                proxy.gameObject.SetActive(true);
                proxy.position = SpaceMapping.ToWorld(projectile.Position, space, _railDepth);

                // Doubled because half-extents are a radius and a primitive's scale is a diameter.
                // Growth is a real mechanic — a shockwave expands — so this is read every frame
                // rather than set once at spawn.
                proxy.localScale = new Vector3(projectile.HalfExtents.x * 2f,
                                               projectile.HalfExtents.y * 2f,
                                               projectile.HalfExtents.x * 2f);
            }

            // Anything the sim has expired stops being drawn. Hidden rather than destroyed so the
            // next volley reuses it.
            for (int i = live.Count; i < _pool.Count; i++)
                if (_pool[i] != null) _pool[i].gameObject.SetActive(false);
        }

        private Transform Proxy(int index)
        {
            while (_pool.Count <= index)
            {
                var proxy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                proxy.name = $"Projectile_{_pool.Count}";
                proxy.transform.SetParent(transform, false);

                // The sim owns every collision it cares about; a Unity collider here would be a
                // second opinion nobody asked for.
                Destroy(proxy.GetComponent<Collider>());
                proxy.GetComponent<Renderer>().sharedMaterial = _material;

                proxy.SetActive(false);
                _pool.Add(proxy.transform);
            }

            return _pool[index];
        }
    }
}

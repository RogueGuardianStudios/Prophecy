using System.Collections.Generic;
using Rokkan.Prophecy.Overworld;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.MapTool
{
    /// <summary>
    /// The live edit-mode world: the same <see cref="OverworldWorldBuilder"/> assembly the play
    /// host runs, built under a hidden, never-saved root so the designer sees exactly what the
    /// game will build while never dirtying the scene with ten thousand tile instances.
    ///
    /// <para>Edits mark CELLS; cells inflate to CHUNKS; a debounce flushes the dirty set by
    /// recompiling (pure C#, cheap) and re-instantiating only the touched chunk roots. Full
    /// rebuilds are the fallback for anything whose touched cells are unknowable — undo,
    /// Inspector edits, map swaps.</para>
    /// </summary>
    internal sealed class OverworldMapPreview
    {
        private const string RootName = "OverworldMapPreview~";
        private const double QuietSeconds = 0.3;

        private OverworldBuildOutput _built;
        private GameObject _root;

        private OverworldMap _map;
        private OverworldTileSet _tiles;
        private OverworldBiomePalette _biomes;
        private Vector3 _origin;
        private bool _stairs = true;

        private readonly HashSet<Vector2Int> _dirtyChunks = new HashSet<Vector2Int>();
        private bool _fullRebuildQueued;
        private double _flushAt;   // 0 = nothing pending

        public bool Active => _root != null;

        /// <summary>The last compiled grid — the window's canvas and the prop placement ray
        /// both read from it. Null until the first build.</summary>
        public OverworldTileGrid Grid => _built?.Grid;

        /// <summary>Milliseconds the last flush spent, for the window's status line.</summary>
        public double LastBuildMs { get; private set; }

        public void Attach(OverworldMap map, OverworldTileSet tiles, Vector3 origin, bool stairs,
                           OverworldBiomePalette biomes = null)
        {
            _map = map;
            _tiles = tiles;
            _biomes = biomes;
            _origin = origin;
            _stairs = stairs;
            MarkAll();
        }

        /// <summary>An edit touched this cell rectangle (inclusive); rebuild its chunks on the
        /// next quiet moment.</summary>
        public void MarkCells(int minX, int minZ, int maxX, int maxZ)
        {
            _dirtyChunks.UnionWith(OverworldWorldBuilder.ChunksTouchedBy(minX, minZ, maxX, maxZ));
            _flushAt = EditorApplication.timeSinceStartup + QuietSeconds;
        }

        /// <summary>Something changed whose footprint is unknown — rebuild everything.</summary>
        public void MarkAll()
        {
            _fullRebuildQueued = true;
            _flushAt = EditorApplication.timeSinceStartup + QuietSeconds;
        }

        /// <summary>Call from EditorApplication.update. Flushes the dirty set once the edits
        /// have gone quiet.</summary>
        public void Tick()
        {
            if (_flushAt <= 0 || EditorApplication.timeSinceStartup < _flushAt) return;
            Flush();
        }

        public void Flush()
        {
            _flushAt = 0;
            if (_map == null || _tiles == null || Application.isPlaying) return;
            if (!_tiles.IsComplete(out _)) return;

            double started = EditorApplication.timeSinceStartup;

            if (!Active || _fullRebuildQueued || _built == null)
            {
                Teardown();

                _root = new GameObject(RootName) { hideFlags = HideFlags.HideAndDontSave };
                var walkable = new GameObject("Ground_Walkable").transform;
                walkable.SetParent(_root.transform, false);
                var scenery = new GameObject("Ground_Scenery").transform;
                scenery.SetParent(_root.transform, false);

                // The tool compiles on every edit — its audits belong in the window, not
                // repeated into the console per stroke. Scene loads keep the console channel.
                _built = OverworldWorldBuilder.Build(_map, _tiles, walkable, scenery,
                                                     _origin, _stairs, _biomes,
                                                     logAuditsToConsole: false);
            }
            else if (_dirtyChunks.Count > 0)
            {
                OverworldWorldBuilder.RebuildChunks(_built, _dirtyChunks);
            }

            _fullRebuildQueued = false;
            _dirtyChunks.Clear();
            LastBuildMs = (EditorApplication.timeSinceStartup - started) * 1000.0;
            SceneView.RepaintAll();
        }

        public void Teardown()
        {
            if (_built != null)
            {
                foreach (var transient in _built.Transient)
                    OverworldWorldBuilder.DestroySmart(transient);
                _built = null;
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        /// <summary>A domain reload or crash can strand a hidden preview in the scene — hidden
        /// objects are exactly the ones nobody notices leaking. Swept whenever a window opens.</summary>
        public static void SweepOrphans()
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].name != RootName) continue;
                if ((all[i].hideFlags & HideFlags.DontSave) == 0) continue;
                Object.DestroyImmediate(all[i]);
            }
        }
    }
}

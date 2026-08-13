using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// Every hurtbox that can be hit this tick, with a broadphase over them.
    ///
    /// <para><b>Because there was not one.</b> Every attack used to test against every hurtbox, and
    /// every live projectile ran its own full sweep — fifty bolts against a hundred targets is five
    /// thousand pairs a tick, and it grew as the product of two numbers both meant to get bigger.
    /// A uniform grid on X turns each query into the handful of boxes actually near the swing.</para>
    ///
    /// <para><b>A grid on X only, on purpose.</b> The play space is a side-scrolling lane a few
    /// metres tall and hundreds of metres long, so X is where the separation is; bucketing Y as
    /// well would add arithmetic to distinguish things that are already within a body height of
    /// each other. If the overworld ever needs it, the second axis is a small change here and
    /// nowhere else.</para>
    ///
    /// <para>Rebuilt from scratch each tick rather than maintained incrementally. Everything moves
    /// every tick anyway, and a stale spatial index is a bug that presents as a hit that did not
    /// land for no visible reason.</para>
    /// </summary>
    public sealed class HurtboxSet
    {
        /// <summary>
        /// Bucket width in metres. Comfortably wider than a body so most boxes land in one bucket,
        /// and narrow enough that a bucket is not most of the level.
        /// </summary>
        public const float CellSize = 4f;

        private readonly List<Hurtbox> _boxes = new List<Hurtbox>();
        private readonly Dictionary<int, List<int>> _cells = new Dictionary<int, List<int>>();
        private readonly Stack<List<int>> _pool = new Stack<List<int>>();

        // Stamps rather than a HashSet: a box spanning two buckets appears in both, and a query
        // covering both must not return it twice. An int compare beats a set insert per candidate.
        private readonly List<int> _stamp = new List<int>();
        private int _query;

        public IReadOnlyList<Hurtbox> All => _boxes;

        public int Count => _boxes.Count;

        public Hurtbox this[int index] => _boxes[index];

        public void Clear()
        {
            _boxes.Clear();
            _stamp.Clear();

            foreach (var pair in _cells)
            {
                pair.Value.Clear();
                _pool.Push(pair.Value);
            }

            _cells.Clear();
        }

        /// <summary>Add a box. Call <see cref="Build"/> once every box for the tick is in.</summary>
        public void Add(in Hurtbox box)
        {
            _boxes.Add(box);
            _stamp.Add(0);
        }

        /// <summary>Bucket everything added since the last <see cref="Clear"/>.</summary>
        public void Build()
        {
            for (int i = 0; i < _boxes.Count; i++)
            {
                var box = _boxes[i];

                int from = CellOf(box.Centre.x - box.HalfExtents.x);
                int to = CellOf(box.Centre.x + box.HalfExtents.x);

                for (int cell = from; cell <= to; cell++)
                {
                    if (!_cells.TryGetValue(cell, out var bucket))
                    {
                        bucket = _pool.Count > 0 ? _pool.Pop() : new List<int>();
                        _cells[cell] = bucket;
                    }

                    bucket.Add(i);
                }
            }
        }

        /// <summary>
        /// Fill <paramref name="candidates"/> with the indices of every box whose X span touches
        /// <paramref name="minX"/>..<paramref name="maxX"/>. Broad, not exact — the caller still
        /// has to test properly.
        ///
        /// <para><b>Not re-entrant.</b> The dedup stamp is one counter on the set, so starting a
        /// second query invalidates the first one's stamps. Callers each own their candidate list
        /// and must finish reading it before querying again — copy out what you need if you are
        /// going to call something that might query. Every current caller reads its results into
        /// its own list first, which is why this is a note rather than a lock.</para>
        /// </summary>
        public int Query(float minX, float maxX, List<int> candidates)
        {
            candidates.Clear();
            if (_boxes.Count == 0) return 0;

            _query++;

            int from = CellOf(minX);
            int to = CellOf(maxX);

            for (int cell = from; cell <= to; cell++)
            {
                if (!_cells.TryGetValue(cell, out var bucket)) continue;

                for (int b = 0; b < bucket.Count; b++)
                {
                    int index = bucket[b];
                    if (_stamp[index] == _query) continue;

                    _stamp[index] = _query;
                    candidates.Add(index);
                }
            }

            return candidates.Count;
        }

        private static int CellOf(float x) => Mathf.FloorToInt(x / CellSize);
    }
}

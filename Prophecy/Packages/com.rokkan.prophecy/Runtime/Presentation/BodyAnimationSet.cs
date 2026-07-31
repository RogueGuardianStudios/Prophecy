using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Which clip depicts each <see cref="BodyState"/>, and how fast it was authored to move.
    ///
    /// <para>An asset rather than a switch statement, for the same reason tuning numbers are:
    /// swapping a clip is something you do fifty times while a character is finding its feel, and
    /// a recompile between each is fifty interruptions.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Body Animation Set", fileName = "BodyAnimationSet")]
    public sealed class BodyAnimationSet : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public BodyState State;

            public AnimationClip Clip;

            [Tooltip("Loop this clip. Locomotion and held poses loop; actions do not.")]
            public bool Loop;

            [Tooltip("The travel speed this clip was authored at, in m/s — the speed at which its " +
                     "feet do not skate. Playback is scaled by simSpeed/this.\n\n" +
                     "ZERO MEANS DO NOT SCALE, which is correct for everything that is not " +
                     "locomotion: scaling a sword swing by how fast you were walking is nonsense.\n\n" +
                     "It has to be authored because the clips cannot report it — these are in-place " +
                     "variants, so their own root barely moves and there is no stride to divide by. " +
                     "Measure it once from the RootMotion counterpart: " +
                     "Prophecy > Animation > Measure Reference Speeds.")]
            public float ReferenceSpeed;
        }

        [SerializeField, Tooltip("One entry per BodyState. Anything unmapped falls back to Idle.")]
        private Entry[] _entries = Array.Empty<Entry>();

        [SerializeField, Tooltip("Default crossfade between states, in seconds. Per-state overrides " +
                                 "belong on the entry if any state ever needs one.")]
        private float _blendSeconds = 0.12f;

        [SerializeField, Tooltip("Below this playback multiplier a clip is treated as stopped rather " +
                                 "than crawling. Stops a near-idle walk becoming a slideshow.")]
        private float _minPlaybackSpeed = 0.25f;

        [SerializeField, Tooltip("Above this, playback is clamped. A sprint scaled 4x reads as a " +
                                 "twitch, and past a point it is better to look slightly fast than broken.")]
        private float _maxPlaybackSpeed = 2.5f;

        private Dictionary<BodyState, Entry> _byState;

        public float BlendSeconds => _blendSeconds;

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>
        /// The entry for a state, or the <see cref="BodyState.Idle"/> entry if it has none.
        ///
        /// <para>Falling back rather than returning nothing: an unmapped state during a gray-box
        /// pass should leave the character standing there, not freeze the last frame of whatever
        /// was playing — which looks like a hang rather than a missing asset.</para>
        /// </summary>
        public bool TryGet(BodyState state, out Entry entry)
        {
            Build();

            if (_byState.TryGetValue(state, out entry)) return true;
            if (_byState.TryGetValue(BodyState.Idle, out entry)) return true;

            entry = default;
            return false;
        }

        /// <summary>
        /// Playback multiplier for a clip at a given travel speed, clamped to the sane range.
        /// Returns 1 for anything with no reference speed.
        /// </summary>
        public float PlaybackSpeedFor(in Entry entry, float simSpeed)
        {
            if (entry.ReferenceSpeed <= 0.0001f) return 1f;

            return Mathf.Clamp(simSpeed / entry.ReferenceSpeed, _minPlaybackSpeed, _maxPlaybackSpeed);
        }

        /// <summary>Drop the lookup so edits during play take effect on the next query.</summary>
        public void Invalidate() => _byState = null;

        private void Build()
        {
            if (_byState != null) return;

            _byState = new Dictionary<BodyState, Entry>(_entries.Length);

            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Clip == null) continue;

                // Last wins rather than first, so a duplicate added while tuning overrides the
                // original instead of being silently ignored.
                _byState[_entries[i].State] = _entries[i];
            }
        }

        private void OnValidate()
        {
            _blendSeconds = Mathf.Max(0f, _blendSeconds);
            _minPlaybackSpeed = Mathf.Clamp(_minPlaybackSpeed, 0.01f, 1f);
            _maxPlaybackSpeed = Mathf.Max(1f, _maxPlaybackSpeed);

            Invalidate();
        }

        /// <summary>Every state with no clip. The gray-box progress bar.</summary>
        public List<BodyState> MissingStates()
        {
            Build();

            var missing = new List<BodyState>();

            foreach (BodyState state in Enum.GetValues(typeof(BodyState)))
                if (!_byState.ContainsKey(state)) missing.Add(state);

            return missing;
        }
    }
}

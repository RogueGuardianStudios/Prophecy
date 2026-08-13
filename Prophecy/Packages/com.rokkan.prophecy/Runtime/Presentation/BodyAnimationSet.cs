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

        /// <summary>Which pose one authored attack shows — a row in the set's attack table.</summary>
        [Serializable]
        public struct AttackPose
        {
            [Tooltip("The authored attack id, exactly as CombatTuning spells it.")]
            public string AttackId;

            [Tooltip("The pose that swing shows.")]
            public BodyState State;
        }

        [SerializeField, Tooltip("One entry per BodyState. Anything unmapped falls back to Idle.")]
        private Entry[] _entries = Array.Empty<Entry>();

        [SerializeField, Tooltip("Which pose each authored attack id shows. Authored data for the " +
                                 "same reason the clips are: a new attack should be a table row " +
                                 "beside the clip it plays, not a code edit. An id in neither this " +
                                 "table nor the resolver's built-in defaults falls back to the " +
                                 "stance's generic swing, so the character keeps swinging " +
                                 "something rather than sliding about in idle.")]
        private AttackPose[] _attackPoses = DefaultAttackPoses();

        [SerializeField, Tooltip("Default crossfade between states, in seconds. Per-state overrides " +
                                 "belong on the entry if any state ever needs one.")]
        private float _blendSeconds = 0.12f;

        // ------------------------------------------------------------ the animator's numbers
        //
        // On the set, beside the clips they describe, rather than serialized per component: a
        // per-prefab copy is how the run threshold silently disagrees with the walk/run
        // handover on exactly one character, with nothing in any inspector looking wrong.

        [SerializeField, Tooltip("Speed below which the character is treated as standing still, " +
                                 "in m/s.")]
        private float _moveThreshold = BodyStateResolver.DefaultMoveThreshold;

        [SerializeField, Tooltip("Speed at or above which a walk becomes a run, in m/s. Keep in " +
                                 "step with MovementTuning.RunSpeed — this is where the walk clip " +
                                 "hands over to the run clip.")]
        private float _runThreshold = BodyStateResolver.DefaultRunThreshold;

        [SerializeField, Tooltip("How long the landing pose is held after touchdown, in seconds. " +
                                 "The sim's LandedThisTick is true for exactly one tick — 16 ms — " +
                                 "which is less than a single frame at 60 fps. Passed straight " +
                                 "through, a half-second landing clip got one frame of screen time " +
                                 "and read as a flicker rather than as a landing. Short enough " +
                                 "that running out of a landing does not skate.")]
        private float _landHoldSeconds = 0.18f;

        // CLAMPING IS FOOT DRIFT. The multiplier is what makes the stride match the travel, so
        // any speed at which it is clamped is a speed at which the feet slide — by exactly the
        // ratio that was clamped away. These are guards against division nonsense, not tuning
        // knobs, and they are set wide enough that nothing in the plausible range touches them.
        //
        // The range that has to fit: from the move threshold (0.15 m/s, below which the character
        // is Idle anyway) up to a hasted run. At the slow end the honest playback is about 0.06,
        // which is a near-frozen cycle — and near-frozen with planted feet looks far better than
        // stepping briskly while creeping, which is what the old 0.25 floor produced.

        [SerializeField, Tooltip("Playback floor. Reaching it means the feet are sliding, so it is " +
                                 "set below anything the game can actually produce.")]
        private float _minPlaybackSpeed = 0.05f;

        [SerializeField, Tooltip("Playback ceiling. Same rule — high enough that a hasted run does " +
                                 "not touch it.")]
        private float _maxPlaybackSpeed = 4f;

        private Dictionary<BodyState, Entry> _byState;
        private Dictionary<string, BodyState> _byAttackId;

        public float BlendSeconds => _blendSeconds;

        /// <summary>Speed below which the character reads as standing still, in m/s.</summary>
        public float MoveThreshold => _moveThreshold;

        /// <summary>Speed at or above which the walk hands over to the run, in m/s.</summary>
        public float RunThreshold => _runThreshold;

        /// <summary>How long the landing pose is held after touchdown, in seconds.</summary>
        public float LandHoldSeconds => _landHoldSeconds;

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>
        /// The shipped moveset's poses — what a fresh set's table starts as, mirroring the
        /// resolver's built-in defaults so an unedited asset behaves identically to no table
        /// at all.
        /// </summary>
        private static AttackPose[] DefaultAttackPoses() => new[]
        {
            new AttackPose { AttackId = "slash_high",   State = BodyState.AttackStandA },
            new AttackPose { AttackId = "slash_high_2", State = BodyState.AttackStandB },
            new AttackPose { AttackId = "thrust_low",   State = BodyState.AttackCrouch },
            new AttackPose { AttackId = "down_thrust",  State = BodyState.DownThrust },
            new AttackPose { AttackId = "up_thrust",    State = BodyState.UpThrust },
        };

        /// <summary>The authored pose for an attack id, if the table maps it. The resolver's
        /// built-in defaults and the stance fallback catch everything this misses.</summary>
        public bool TryGetAttackPose(string attackId, out BodyState state)
        {
            if (string.IsNullOrEmpty(attackId))
            {
                state = default;
                return false;
            }

            if (_byAttackId == null)
            {
                _byAttackId = new Dictionary<string, BodyState>(_attackPoses.Length);

                for (int i = 0; i < _attackPoses.Length; i++)
                {
                    if (string.IsNullOrEmpty(_attackPoses[i].AttackId)) continue;

                    // Last wins, the clip entries' own law: a duplicate added while tuning
                    // overrides the original instead of being silently ignored.
                    _byAttackId[_attackPoses[i].AttackId] = _attackPoses[i].State;
                }
            }

            return _byAttackId.TryGetValue(attackId, out state);
        }

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
        /// Playback multiplier for a clip at a given travel speed. One for anything with no
        /// reference speed.
        ///
        /// <para><b>Speed buffs and debuffs need no special handling here</b>, and that is worth
        /// stating because it looks like they should. The multiplier is computed from the
        /// character's <i>actual</i> velocity, so a slow that halves movement halves the stride
        /// with it and the feet stay planted. The one thing that can break the match is the clamp
        /// — see the fields.</para>
        /// </summary>
        public float PlaybackSpeedFor(in Entry entry, float simSpeed)
        {
            if (entry.ReferenceSpeed <= 0.0001f) return 1f;

            return Mathf.Clamp(simSpeed / entry.ReferenceSpeed, _minPlaybackSpeed, _maxPlaybackSpeed);
        }

        /// <summary>Drop the lookups so edits during play take effect on the next query.</summary>
        public void Invalidate()
        {
            _byState = null;
            _byAttackId = null;
        }

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
            _minPlaybackSpeed = Mathf.Clamp(_minPlaybackSpeed, 0.001f, 1f);
            _maxPlaybackSpeed = Mathf.Max(1f, _maxPlaybackSpeed);
            _moveThreshold = Mathf.Max(0f, _moveThreshold);
            _runThreshold = Mathf.Max(0f, _runThreshold);
            _landHoldSeconds = Mathf.Max(0f, _landHoldSeconds);

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

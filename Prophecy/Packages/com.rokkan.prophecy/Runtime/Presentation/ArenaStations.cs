using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Number keys warp the player to a station. A testing affordance, and an unglamorous one.
    ///
    /// <para><b>It exists because walking is not the thing being tested.</b> The arena is ninety
    /// metres long and the stations that swing back are at the far end of it, so checking whether a
    /// parry window feels right meant a twenty-second walk before every attempt — which is exactly
    /// the friction that stops anyone iterating, and the same argument that put a kill plane in the
    /// traversal course.</para>
    ///
    /// <para>Listed on the object rather than found by name, so the numbers mean whatever the
    /// generator decided they mean and the labels show up in the overlay next to the key that gets
    /// you there. Editor scaffolding: see <c>Plans/Release-Checklist.md</c>.</para>
    /// </summary>
    public sealed class ArenaStations : MonoBehaviour
    {
        [Serializable]
        public struct Station
        {
            [Tooltip("Shown in the overlay next to the key that warps here.")]
            public string Label;

            [Tooltip("Where the player lands. The floor, not the eye line.")]
            public Vector3 Position;
        }

        [SerializeField, Tooltip("Warped to by keys 1-9 then 0, in order.")]
        private List<Station> _stations = new List<Station>();

        [SerializeField, Tooltip("Leave empty to find the player in the scene.")]
        private PlayerCharacterHost _player;

        [SerializeField, Tooltip("Restore health on arrival. On, because arriving at a station on " +
                                 "two health tests nothing except how fast you die.")]
        private bool _restoreOnWarp = true;

        public IReadOnlyList<Station> Stations => _stations;

        /// <summary>The station most recently warped to, or -1. Shown in the overlay.</summary>
        public int LastStation { get; private set; } = -1;

        private static readonly Key[] Keys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0,
        };

        private void Awake()
        {
            if (_player == null) _player = FindAnyObjectByType<PlayerCharacterHost>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || _stations.Count == 0) return;

            if (_player == null)
            {
                _player = FindAnyObjectByType<PlayerCharacterHost>();
                if (_player == null) return;
            }

            int count = Mathf.Min(_stations.Count, Keys.Length);

            for (int i = 0; i < count; i++)
            {
                if (!keyboard[Keys[i]].wasPressedThisFrame) continue;

                WarpTo(i);
                return;
            }
        }

        public void WarpTo(int index)
        {
            if (_player == null || index < 0 || index >= _stations.Count) return;

            var station = _stations[index];

            if (_restoreOnWarp) _player.RespawnAt(station.Position);
            else _player.TeleportTo(station.Position);

            LastStation = index;
        }
    }
}

using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// Three objects, three doors, nothing nested (spec §8): D-pad Down opens the arts
    /// volume, Select the pack, Start the book. Not one menu with tabs — three separate
    /// things, each one press from gameplay, and none of them remembers a last page: every
    /// open lands on the object's first face.
    ///
    /// <para><b>Opening a menu stops the world.</b> The same switch the scene transitions
    /// throw — <see cref="SimClockDriver.Paused"/> — so the enemy mid-swing holds its pose
    /// while the player reads. On close the input capture's buffered edges are rebaselined
    /// (<see cref="PlayerInputCapture.ClearPending"/>): the A that confirmed a cast must not
    /// also be the A that swings a sword on the first live tick.</para>
    ///
    /// <para>Menus read the same generated action map the sim does, borrowed from
    /// <see cref="PlayerInputCapture"/> — confirm is A because Jump is A, close is B because
    /// Dodge is B, the book's pages turn on LB/RB because those are the hands' buttons. No
    /// second input asset, no UI action map to drift out of sync. UI Toolkit's own focus and
    /// navigation systems are deliberately unused: selection is a drawn highlight, not a
    /// focused element, so the pad and the keyboard cannot disagree about where it is.</para>
    ///
    /// <para>While the clock is paused the sim consumes nothing, so gameplay actions cannot
    /// fire from inside a menu — the pause IS the input gate, one mechanism doing both
    /// jobs.</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MenuRoot : MonoBehaviour
    {
        private PlayerInputCapture _capture;
        private SimClockDriver _clock;

        private InputAction _openArts;
        private InputAction _openPack;
        private InputAction _openBook;
        private InputAction _move;
        private InputAction _confirm;
        private InputAction _close;
        private InputAction _pageLeft;
        private InputAction _pageRight;

        private ArtsVolumeMenu _arts;
        private PackMenu _pack;
        private BookMenu _book;
        private MenuPanel _open;

        private bool _pausedByMenu;
        private float _navArm = 1f;

        /// <summary>What a menu reads each frame: the sim, and the few digested inputs.</summary>
        internal struct Frame
        {
            public CharacterSim Sim;
            public bool Confirm;
            public int NavY;
            public bool PageLeft;
            public bool PageRight;
        }

        internal abstract class MenuPanel
        {
            /// <summary>The panel's root element, shown and hidden by the door.</summary>
            public VisualElement Root { get; protected set; }

            public bool IsOpen
            {
                get => Root.style.display == DisplayStyle.Flex;
                set => Root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public abstract void Opened(CharacterSim sim);
            public abstract void Tick(in Frame frame);
        }

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var layer = UiBuild.Layer(root, "MenuLayer");
            layer.BringToFront();   // menus draw over the HUD's layer

            _arts = new ArtsVolumeMenu(layer);
            _pack = new PackMenu(layer);
            _book = new BookMenu(layer);
            _open = null;
        }

        private void Update()
        {
            if (!Resolve()) return;

            var director = SceneDirector.Instance;
            bool transitioning = director != null && director.IsTransitioning;

            // The doors. Each toggles its own object; a different door while one is open
            // walks straight across — still nothing nested, just one object at a time.
            if (!transitioning)
            {
                if (_openArts.WasPressedThisFrame()) Toggle(_arts);
                else if (_openPack.WasPressedThisFrame()) Toggle(_pack);
                else if (_openBook.WasPressedThisFrame()) Toggle(_book);
            }

            if (_open == null) return;

            if (_close.WasPressedThisFrame() || transitioning)
            {
                Close();
                return;
            }

            _open.Tick(new Frame
            {
                Sim = PlayerSim(),
                Confirm = _confirm.WasPressedThisFrame(),
                NavY = ReadNav(),
                PageLeft = _pageLeft.WasPressedThisFrame(),
                PageRight = _pageRight.WasPressedThisFrame(),
            });
        }

        private void Toggle(MenuPanel panel)
        {
            if (_open == panel)
            {
                Close();
                return;
            }

            if (_open != null) _open.IsOpen = false;

            _open = panel;
            _open.IsOpen = true;
            _open.Opened(PlayerSim());
            _navArm = 0f;

            if (_clock != null && !_clock.Paused)
            {
                _clock.Paused = true;
                _pausedByMenu = true;
            }
        }

        private void Close()
        {
            if (_open != null) _open.IsOpen = false;
            _open = null;

            // Unpause only what this class paused: a transition's freeze is the director's
            // to lift, and a menu closed mid-curtain must not restart the world under it.
            if (_pausedByMenu && _clock != null)
            {
                _clock.Paused = false;
                _pausedByMenu = false;
            }

            _capture.ClearPending();
        }

        /// <summary>Up/down as a discrete step, re-armed through neutral so a held stick
        /// moves one row per push rather than racing down the list.</summary>
        private int ReadNav()
        {
            float y = _move.ReadValue<Vector2>().y;

            if (Mathf.Abs(y) < 0.3f)
            {
                _navArm = 1f;
                return 0;
            }

            if (_navArm < 0.5f || Mathf.Abs(y) < 0.6f) return 0;

            _navArm = 0f;
            return y > 0f ? 1 : -1;
        }

        private static CharacterSim PlayerSim()
        {
            var director = SceneDirector.Instance;
            return director != null && director.Player != null ? director.Player.Sim : null;
        }

        private bool Resolve()
        {
            if (_openArts != null) return true;

            if (_capture == null) _capture = FindAnyObjectByType<PlayerInputCapture>();
            if (_clock == null) _clock = FindAnyObjectByType<SimClockDriver>();
            if (_capture == null || _capture.Actions == null) return false;

            var map = _capture.Actions.FindActionMap("Gameplay", throwIfNotFound: false);
            if (map == null) return false;

            _openArts = map.FindAction("OpenArts");
            _openPack = map.FindAction("OpenPack");
            _openBook = map.FindAction("OpenBook");
            _move = map.FindAction("Move");
            _confirm = map.FindAction("Jump");      // A
            _close = map.FindAction("Dodge");       // B
            _pageLeft = map.FindAction("Block");    // LB
            _pageRight = map.FindAction("FlameArt"); // RB

            if (_openArts == null || _openPack == null || _openBook == null ||
                _move == null || _confirm == null || _close == null ||
                _pageLeft == null || _pageRight == null)
            {
                // An input asset from before the menu actions existed: stand down until it
                // is regenerated rather than half-working.
                _openArts = null;
                return false;
            }

            return true;
        }
    }
}

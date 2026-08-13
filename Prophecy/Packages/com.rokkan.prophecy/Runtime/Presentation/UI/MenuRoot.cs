using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// One sheet, three TABS on a carousel (Matt, 2026-08-13 — superseding the spec's
    /// three separate objects): Pack, Arts, Book, cycled with LB/RB and wrapping at the
    /// ends. The three doors survive — D-pad Down opens straight onto Arts, Select onto
    /// Pack, Start onto Book — so every page is still one press from gameplay, and no page
    /// remembers where you left it: every open lands on the page's first face.
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
        private OptionsMenu _options;
        private MenuPanel[] _tabs;
        private MenuPanel _open;

        private bool _pausedByMenu;
        private float _navArmX = 1f;
        private float _navArmY = 1f;

        /// <summary>What a menu reads each frame: the sim, and the few digested inputs.
        /// LB/RB never appear here — the shoulders turn the tab carousel and belong to
        /// this class; a page's own flipping reads the stick's horizontal step.</summary>
        internal struct Frame
        {
            public CharacterSim Sim;
            public bool Confirm;
            public bool ConfirmHeld;
            public int NavX;
            public int NavY;
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

            /// <summary>The door shut — by close, by another door, or by a transition.
            /// Panels holding live resources (the pack's portrait camera) let go here.</summary>
            public virtual void Closed() { }
        }

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var layer = UiBuild.Layer(root, "MenuLayer");
            layer.BringToFront();   // menus draw over the HUD's layer

            _arts = new ArtsVolumeMenu(layer);
            _pack = new PackMenu(layer);
            _book = new BookMenu(layer);
            _options = new OptionsMenu(layer);

            // The carousel, in the tab rail's order (UiBuild.MenuTabs). Options has no
            // door of its own — the rail is how it is reached.
            _tabs = new MenuPanel[] { _pack, _arts, _book, _options };
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

            // The shoulders turn the carousel; the open page never sees them.
            if (_pageLeft.WasPressedThisFrame())
            {
                Cycle(-1);
                return;
            }

            if (_pageRight.WasPressedThisFrame())
            {
                Cycle(+1);
                return;
            }

            var move = _move.ReadValue<Vector2>();

            _open.Tick(new Frame
            {
                Sim = PlayerSim(),
                Confirm = _confirm.WasPressedThisFrame(),
                ConfirmHeld = _confirm.IsPressed(),
                NavX = Step(move.x, ref _navArmX),
                NavY = Step(move.y, ref _navArmY),
            });
        }

        /// <summary>One step around the tab carousel, wrapping at the ends. The leaving
        /// page closes properly (the pack lets its camera go) and the arriving one opens
        /// on its first face, exactly as if its own door had been used.</summary>
        private void Cycle(int step)
        {
            int index = System.Array.IndexOf(_tabs, _open);
            var next = _tabs[(index + step + _tabs.Length) % _tabs.Length];

            _open.IsOpen = false;
            _open.Closed();

            _open = next;
            _open.IsOpen = true;
            _open.Opened(PlayerSim());
            _navArmX = 0f;
            _navArmY = 0f;
        }

        private void Toggle(MenuPanel panel)
        {
            if (_open == panel)
            {
                Close();
                return;
            }

            if (_open != null)
            {
                _open.IsOpen = false;
                _open.Closed();
            }

            _open = panel;
            _open.IsOpen = true;
            _open.Opened(PlayerSim());
            _navArmX = 0f;
            _navArmY = 0f;

            if (_clock != null && !_clock.Paused)
            {
                _clock.Paused = true;
                _pausedByMenu = true;
            }
        }

        private void Close()
        {
            if (_open != null)
            {
                _open.IsOpen = false;
                _open.Closed();
            }

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

        /// <summary>One stick axis as a discrete step, re-armed through neutral so a held
        /// stick moves one notch per push rather than racing.</summary>
        private static int Step(float value, ref float arm)
        {
            if (Mathf.Abs(value) < 0.3f)
            {
                arm = 1f;
                return 0;
            }

            if (arm < 0.5f || Mathf.Abs(value) < 0.6f) return 0;

            arm = 0f;
            return value > 0f ? 1 : -1;
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

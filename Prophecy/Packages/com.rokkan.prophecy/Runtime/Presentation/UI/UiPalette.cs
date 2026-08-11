using UnityEngine;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The HUD's colors, verbatim from the UI/Input spec (§2.4, §4–§7). One place, so a retint
    /// is an edit here and not a hunt through widget code.
    ///
    /// <para>The rules the numbers encode: hearts are Festival Red and NEVER Smolder Red (the
    /// wound color), an unaffordable art is muted and NEVER red (red is Might and wounds; an
    /// unaffordable spell is not an error), damage feedback is Shadow Mid and NEVER red, and
    /// green does not exist on this HUD at all.</para>
    /// </summary>
    public static class UiPalette
    {
        /// <summary>The page every panel sits on.</summary>
        public static readonly Color Parchment = Hex(0xE8D9B8);

        /// <summary>Empty portions of hearts and bars — parchment in shadow.</summary>
        public static readonly Color ParchmentEmpty = Hex(0xD9CBAA);

        /// <summary>Hearts. Festival Red — the celebratory red, not the wound.</summary>
        public static readonly Color FestivalRed = Hex(0xC94F3D);

        /// <summary>The Flame reserve, and the castable emblem.</summary>
        public static readonly Color HearthGold = Hex(0xF2A63C);

        /// <summary>Flame Core — the Resolve seam's fill, and the Locked emblem.</summary>
        public static readonly Color PaleGold = Hex(0xFFE8B0);

        /// <summary>Shadow Base — the seam's ground, the one shadow-grounded element.</summary>
        public static readonly Color ShadowBase = Hex(0x2E2547);

        /// <summary>Shadow Mid — damage and danger. The player and the world die this color.</summary>
        public static readonly Color ShadowMid = Hex(0x4A3B6E);

        /// <summary>Dark umber — thin borders, and the Locked panel's dark ground.</summary>
        public static readonly Color Umber = Hex(0x4A3B2E);

        /// <summary>Gilt — the emphasized border on Running and Locked emblems.</summary>
        public static readonly Color Gilt = Hex(0xC9973F);

        /// <summary>Muted emblem when the reserve cannot cover the cost.</summary>
        public static readonly Color Muted = Hex(0xA89878);

        /// <summary>Muted cost pips beside a muted emblem.</summary>
        public static readonly Color MutedPip = Hex(0xC9B99A);

        /// <summary>The Running state's inverted ground — brighter than the page, no glow.</summary>
        public static readonly Color Bright = Hex(0xFFF1CE);

        /// <summary>Might's steel — the pack sheet's attack numeral.</summary>
        public static readonly Color Steel = Hex(0x9AA0A8);

        /// <summary>Ink on parchment — body text in the menus.</summary>
        public static readonly Color Ink = Hex(0x3A2E20);

        /// <summary>The cost-preview segment: Hearth Gold at ~32% alpha (spec §4.2).</summary>
        public static readonly Color CostSegment = new Color(
            HearthGold.r, HearthGold.g, HearthGold.b, 0.32f);

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);
    }
}

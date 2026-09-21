using UnityEngine;

namespace Sokoban
{
    /// <summary>
    /// Runtime font provision for non-Latin (CJK) text. Unity's built-in IMGUI font
    /// (<c>LegacyRuntime.ttf</c>) is Latin-first and its CJK coverage is platform-dependent (some
    /// hosts resolve CJK only via undocumented OS-font fallback), so a Chinese string may render as
    /// blank boxes on some targets. Rather than shipping a font file (licensing/size) or downloading
    /// one, we ask the OS for an installed CJK font BY NAME via
    /// <see cref="Font.CreateDynamicFontFromOSFont(string, int)"/>. This distributes nothing and
    /// stays Runtime-only: no UnityEditor, no AssetDatabase, no assets.
    /// </summary>
    public static class Localization
    {
        /// <summary>Preferred OS CJK fonts, in order. First candidate covering the sample glyph wins.</summary>
        private static readonly string[] CandidateFontNames =
        {
            "Microsoft YaHei",
            "SimHei",
            "Microsoft JhengHei",
            "SimSun"
        };

        /// <summary>Representative CJK glyph used to prove a candidate font is not Latin-only.</summary>
        private const char SampleCjkGlyph = '推';

        private static Font _uiFont;
        private static bool _resolved;

        /// <summary>
        /// The shared runtime UI font, resolved once from the OS and cached for the session.
        /// Returns <c>null</c> when no candidate OS font is available; callers must fall back to the
        /// built-in font in that case. Never throws: each candidate is individually guarded.
        /// </summary>
        public static Font UiFont
        {
            get
            {
                if (!_resolved)
                {
                    _uiFont = ResolveUiFont();
                    _resolved = true;
                }

                return _uiFont;
            }
        }

        private static Font ResolveUiFont()
        {
            foreach (string name in CandidateFontNames)
            {
                Font font = TryCreateOsFont(name);
                if (font != null && font.HasCharacter(SampleCjkGlyph))
                {
                    return font;
                }
            }

            return null;
        }

        private static Font TryCreateOsFont(string name)
        {
            try
            {
                return Font.CreateDynamicFontFromOSFont(name, 16);
            }
            catch (System.Exception)
            {
                // A missing/invalid OS font name must never break the game; try the next candidate.
                return null;
            }
        }
    }
}

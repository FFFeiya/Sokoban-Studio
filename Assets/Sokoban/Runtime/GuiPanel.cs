using UnityEngine;

namespace Sokoban
{
    /// <summary>
    /// Shared presentation helpers for the OnGUI screens (title menu, level select, completion
    /// overlay and HUD). Purely view-side: it computes centered panel rects and lazily provides the
    /// one solid backing texture those panels reuse. Nothing here is read by the rules, and no visual
    /// ever gates legality.
    /// </summary>
    internal static class GuiPanel
    {
        private static Texture2D _solidTexture;
        private static GUIStyle _titleStyle;

        /// <summary>Screen rect for a panel of the given size, centered in the game view.</summary>
        public static Rect Centered(float width, float height)
        {
            return new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
        }

        /// <summary>
        /// Bold, larger, centered label style for panel titles. A <see cref="GUIStyle"/> depends on
        /// <see cref="GUI.skin"/>, which is only valid inside OnGUI, so it is created lazily on first
        /// use and then cached for the rest of the session.
        /// </summary>
        public static GUIStyle TitleStyle
        {
            get
            {
                if (_titleStyle == null)
                {
                    _titleStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 24,
                        fontStyle = FontStyle.Bold,
                        font = Localization.UiFont ?? GUI.skin.label.font
                    };
                }

                return _titleStyle;
            }
        }

        private static GUIStyle _subtitleStyle;

        /// <summary>
        /// Centered, medium label style for panel sub-lines (counters and stats). CJK-capable via
        /// <see cref="Localization.UiFont"/> like <see cref="TitleStyle"/>.
        /// </summary>
        public static GUIStyle SubtitleStyle
        {
            get
            {
                if (_subtitleStyle == null)
                {
                    _subtitleStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 14,
                        font = Localization.UiFont ?? GUI.skin.label.font
                    };
                }

                return _subtitleStyle;
            }
        }

        private static GUIStyle _chineseButtonStyle;

        /// <summary>
        /// Compact button style sized for CJK labels. An explicit 13px avoids the default 16px dynamic
        /// font crowding inside a 28px button row; centered to match the rest of the panel.
        /// </summary>
        public static GUIStyle ChineseButtonStyle
        {
            get
            {
                if (_chineseButtonStyle == null)
                {
                    _chineseButtonStyle = new GUIStyle(GUI.skin.button)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 13,
                        font = Localization.UiFont ?? GUI.skin.button.font
                    };
                }

                return _chineseButtonStyle;
            }
        }

        private static GUIStyle _chineseLabelStyle;

        /// <summary>
        /// Compact centered label style for CJK lines that sit inside a button-sized row (the
        /// completion overlay's "全部关卡完成" / "关卡完成（测试关卡）" placeholders).
        /// </summary>
        public static GUIStyle ChineseLabelStyle
        {
            get
            {
                if (_chineseLabelStyle == null)
                {
                    _chineseLabelStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 13,
                        font = Localization.UiFont ?? GUI.skin.label.font
                    };
                }

                return _chineseLabelStyle;
            }
        }

        private static GUIStyle _hudTitleStyle;

        /// <summary>
        /// Left-aligned HUD title style (level number + name). An explicit 14px with the CJK font
        /// keeps the glyphs from being bottom-clipped inside a small fixed rect.
        /// </summary>
        public static GUIStyle HudTitleStyle
        {
            get
            {
                if (_hudTitleStyle == null)
                {
                    _hudTitleStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 14,
                        font = Localization.UiFont ?? GUI.skin.label.font
                    };
                }

                return _hudTitleStyle;
            }
        }

        private static GUIStyle _hudTextStyle;

        /// <summary>
        /// Left-aligned HUD body style (counters, mute indicator, control hint). An explicit 12px with
        /// the CJK font avoids the default 16px glyph crowding inside the small overlay rects.
        /// </summary>
        public static GUIStyle HudTextStyle
        {
            get
            {
                if (_hudTextStyle == null)
                {
                    _hudTextStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 12,
                        font = Localization.UiFont ?? GUI.skin.label.font
                    };
                }

                return _hudTextStyle;
            }
        }

        /// <summary>
        /// Points the current IMGUI skin at the CJK font for the frame so plain GUI.Label/Button text
        /// (which reads GUI.skin at draw time) renders Chinese. Call at the top of each OnGUI. No-op
        /// when no OS CJK font resolved, leaving the skin default.
        /// </summary>
        public static void ApplyCjkFont()
        {
            if (Localization.UiFont != null)
            {
                GUI.skin.font = Localization.UiFont;
            }
        }

        /// <summary>
        /// Lazily created 1x1 white texture used for solid translucent panel/HUD backgrounds. Same
        /// pattern as <see cref="BoardView"/>'s unit sprite: no imported assets, no textures.
        /// </summary>
        public static Texture2D SolidTexture
        {
            get
            {
                if (_solidTexture == null)
                {
                    _solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _solidTexture.SetPixel(0, 0, Color.white);
                    _solidTexture.Apply();
                    _solidTexture.filterMode = FilterMode.Point;
                    _solidTexture.hideFlags = HideFlags.HideAndDontSave;
                }

                return _solidTexture;
            }
        }

        /// <summary>Draws a solid tinted backing rect and restores <see cref="GUI.color"/> afterwards.</summary>
        public static void DrawBacking(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, SolidTexture, ScaleMode.StretchToFill);
            GUI.color = previous;
        }
    }
}

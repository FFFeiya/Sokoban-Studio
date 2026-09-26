using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sokoban;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Proves the runtime can obtain a CJK-capable font from the OS without shipping a font file.
    /// If <see cref="Localization.UiFont"/> cannot cover the glyphs below, this test fails, so
    /// Chinese UI text is never shipped where it would render as boxes.
    ///
    /// Note: a negative "built-in font lacks CJK" test is intentionally NOT included. Verified on
    /// this host that Unity's built-in font is dynamic and resolves CJK (both HasCharacter('推') and
    /// GetCharacterInfo('推') succeed via OS-font fallback), so no Font API can observe a Latin-only
    /// built-in font here; such a check cannot be written truthfully.
    /// </summary>
    public class LocalizationFontTests
    {
        /// <summary>
        /// Every distinct character the localization will use across runtime UI, level names and the
        /// editor. A single missing glyph would surface as a blank box in-game.
        /// </summary>
        private const string LocalizedText =
            "主菜单开始游戏关卡选择步数推箱次数撤销重新开始下一关关卡完成音效开校验可解无解未确定未分析解法预览试玩内容总览内容健康度编辑保存压力板门组最短步数最少推箱次数推箱入位绕路借道墙角陷阱先后有序以箱压板双板同压分组换门层层开门与重试返回退出游戏制作分析全部导出打开编辑器预览名称状态结论搜索耗时机制首次问题操作新建另存为红重做一键加载墙地板目标玩家箱子应用尺寸重置可解性已保存修改存在错误警告上一步下一步回到开始自动播放停止在编辑器中打开向上向下左右第关测试";

        [Test]
        public void UiFont_IsAvailable_OnThisMachine()
        {
            // Blocker signal: a null font means no OS CJK font could be loaded, so Chinese must not ship.
            Assert.IsNotNull(
                Localization.UiFont,
                "No OS CJK font could be loaded via Font.CreateDynamicFontFromOSFont. " +
                "Record a blocker; do not ship Chinese text.");
        }

        [Test]
        public void UiFont_CoversAllLocalizedGlyphs()
        {
            Font font = Localization.UiFont;
            Assert.IsNotNull(font, "UiFont is null; cannot verify glyph coverage.");

            var missing = new List<char>();
            foreach (char c in LocalizedText.Distinct())
            {
                if (!font.HasCharacter(c))
                {
                    missing.Add(c);
                }
            }

            Assert.IsEmpty(
                missing,
                "The UI font is missing required glyphs: " + string.Join(",", missing));
        }
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sokoban
{
    /// <summary>
    /// Level picker. The catalog is a serialized asset reference assigned by the scene factory,
    /// so shipped runtime code never uses AssetDatabase discovery.
    /// </summary>
    public class LevelSelectController : MonoBehaviour
    {
        [Tooltip("Ordered shipped levels. Assigned by the scene factory.")]
        public LevelCatalog catalog;

        private static GUIStyle _sectionHeaderStyle;

        private LevelDefinition _pendingLevel;
        private bool _backRequested;

        /// <summary>
        /// Small bold label used for the "Core" / "Plate &amp; Door" section headers. Created lazily
        /// because a <see cref="GUIStyle"/> depends on <see cref="GUI.skin"/>, which is only valid
        /// inside OnGUI. View-only: the grouping below never changes which level a button loads.
        /// </summary>
        private static GUIStyle SectionHeaderStyle
        {
            get
            {
                if (_sectionHeaderStyle == null)
                {
                    _sectionHeaderStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 14,
                        fontStyle = FontStyle.Bold,
                        font = Localization.UiFont ?? GUI.skin.label.font
                    };
                }

                return _sectionHeaderStyle;
            }
        }

        /// <summary>
        /// Read-only, null-safe scan of a level's tile layer. True when the level contains any
        /// <see cref="TileType.Plate"/> or <see cref="TileType.Door"/> tile. Used only to pick the
        /// display section; it never mutates the asset and adds no serialized state.
        /// </summary>
        private static bool UsesPlateOrDoor(LevelDefinition level)
        {
            if (level == null || level.cells == null)
            {
                return false;
            }

            for (int i = 0; i < level.cells.Count; i++)
            {
                TileType tile = level.cells[i];
                if (tile == TileType.Plate || tile == TileType.Door)
                {
                    return true;
                }
            }

            return false;
        }

        private void Update()
        {
            if (_pendingLevel != null)
            {
                LevelDefinition target = _pendingLevel;
                _pendingLevel = null;
                GameFlowRequest.LoadLevel(target);
                return;
            }

            if (_backRequested)
            {
                _backRequested = false;
                SceneManager.LoadScene(GameFlowRequest.MainMenuScene);
            }
        }

        private void OnGUI()
        {
            GuiPanel.ApplyCjkFont();

            // Slightly taller than the button list so the two section headers do not crowd it.
            Rect panel = GuiPanel.Centered(380f, 480f);
            GUI.Box(panel, GUIContent.none);

            GUILayout.BeginArea(panel);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20f);
            GUILayout.BeginVertical();

            GUILayout.Space(16f);
            GUILayout.Label("关卡选择", GuiPanel.TitleStyle);
            GUILayout.Space(16f);

            if (catalog == null || catalog.levels == null)
            {
                GUILayout.Label("未分配关卡目录。");
            }
            else
            {
                // Grouping is derived per frame from the level tiles, so it follows the catalog's
                // own order: a header is emitted only when the section changes. The button label
                // and the exact `_pendingLevel = level` wiring are unchanged.
                int currentGroup = -1;

                for (int i = 0; i < catalog.levels.Count; i++)
                {
                    LevelDefinition level = catalog.levels[i];
                    if (level == null)
                    {
                        continue;
                    }

                    int group = UsesPlateOrDoor(level) ? 1 : 0;
                    if (group != currentGroup)
                    {
                        if (currentGroup != -1)
                        {
                            GUILayout.Space(10f);
                        }

                        GUILayout.Label(group == 0 ? "基础" : "压力板与门", SectionHeaderStyle);
                        GUILayout.Space(2f);
                        currentGroup = group;
                    }

                    if (GUILayout.Button($"{i + 1:00} {level.levelName}", GuiPanel.ChineseButtonStyle, GUILayout.Height(30f)))
                    {
                        _pendingLevel = level;
                    }
                }
            }

            GUILayout.Space(14f);
            if (GUILayout.Button("返回", GuiPanel.ChineseButtonStyle, GUILayout.Height(30f)))
            {
                _backRequested = true;
            }

            GUILayout.EndVertical();
            GUILayout.Space(20f);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}

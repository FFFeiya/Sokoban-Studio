using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sokoban
{
    /// <summary>
    /// Title screen. "Play" opens the level select scene; "Quit" exits the player
    /// (in the editor <see cref="Application.Quit"/> is a safe no-op).
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        private static GUIStyle _taglineStyle;

        private bool _playRequested;
        private bool _quitRequested;

        /// <summary>
        /// Smaller, centered, italic label used for the tagline under the title. Created lazily
        /// because a <see cref="GUIStyle"/> depends on <see cref="GUI.skin"/>, which is only valid
        /// inside OnGUI. View-only: nothing here is read by the rules.
        /// </summary>
        private static GUIStyle TaglineStyle
        {
            get
            {
                if (_taglineStyle == null)
                {
                    _taglineStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 13,
                        fontStyle = FontStyle.Italic,
                        font = Localization.UiFont ?? GUI.skin.label.font
                    };
                }

                return _taglineStyle;
            }
        }

        private void Update()
        {
            if (_playRequested)
            {
                _playRequested = false;
                SceneManager.LoadScene(GameFlowRequest.LevelSelectScene);
                return;
            }

            if (_quitRequested)
            {
                _quitRequested = false;
                Application.Quit();
            }
        }

        private void OnGUI()
        {
            GuiPanel.ApplyCjkFont();

            // A little taller than the content so the title block and buttons read as one centered
            // group. The Play/Quit wiring below is unchanged.
            Rect panel = GuiPanel.Centered(340f, 240f);
            GUI.Box(panel, GUIContent.none);

            GUILayout.BeginArea(panel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Sokoban Studio", GuiPanel.TitleStyle);
            GUILayout.Space(4f);
            GUILayout.Label("推箱子工坊", TaglineStyle);
            GUILayout.Label("制作 · 校验 · 分析 · 试玩", TaglineStyle);
            GUILayout.Space(24f);

            GUILayout.BeginHorizontal();
            GUILayout.Space(70f);
            GUILayout.BeginVertical();

            if (GUILayout.Button("开始游戏", GuiPanel.ChineseButtonStyle, GUILayout.Height(30f)))
            {
                _playRequested = true;
            }

            GUILayout.Space(10f);

            if (GUILayout.Button("退出游戏", GuiPanel.ChineseButtonStyle, GUILayout.Height(30f)))
            {
                _quitRequested = true;
            }

            GUILayout.EndVertical();
            GUILayout.Space(70f);
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();

            GUILayout.EndArea();
        }
    }
}

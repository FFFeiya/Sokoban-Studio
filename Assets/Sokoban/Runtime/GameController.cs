using UnityEngine;

namespace Sokoban
{
    /// <summary>
    /// Translates keyboard input into <see cref="GameBootstrap"/> commands and draws the completion
    /// overlay. Completion is re-evaluated when the bootstrap exposes a new board (first frame and
    /// after Restart), so a level that already has every box on a goal shows the overlay immediately;
    /// otherwise it comes from the movement result. After completion movement stays disabled and only
    /// Undo/Restart/the overlay buttons remain available. Esc returns to level select at any time.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        [Tooltip("Optional; resolved from the same GameObject when left empty.")]
        public GameBootstrap bootstrap;

        private bool _completed;
        private bool _nextRequested;
        private bool _selectRequested;
        private Board _boundBoard;

        /// <summary>Exposes the completion overlay state to tests without driving GUI input.</summary>
        public bool IsCompletedForTests => _completed;

        private void Awake()
        {
            Bind(bootstrap != null ? bootstrap : GetComponent<GameBootstrap>());
        }

        /// <summary>Called by the bootstrap once the board exists.</summary>
        public void Bind(GameBootstrap owner)
        {
            if (owner != null)
            {
                bootstrap = owner;
            }
        }

        private void Update()
        {
            if (bootstrap == null || bootstrap.Board == null)
            {
                return;
            }

            SynchronizeCompletion();

            if (_nextRequested)
            {
                _nextRequested = false;
                bootstrap.LoadNextLevel();
                return;
            }

            if (_selectRequested || Input.GetKeyDown(KeyCode.Escape))
            {
                _selectRequested = false;
                bootstrap.LoadLevelSelect();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (bootstrap.Undo())
                {
                    _completed = bootstrap.Board.IsComplete;
                }
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                Retry();
            }

            if (_completed)
            {
                return;
            }

            Direction? direction = ReadDirection();
            if (direction.HasValue && bootstrap.TryMove(direction.Value, out bool completed) && completed)
            {
                _completed = true;
                Debug.Log("Level Complete");
            }
        }

        /// <summary>
        /// Restarts the current level and drops the completion flag. The rebuilt board is picked up by
        /// <see cref="SynchronizeCompletion"/> on the next frame, so it re-evaluates from the real
        /// board state (a level that is inherently complete shows the overlay again).
        /// </summary>
        public void Retry()
        {
            if (bootstrap == null)
            {
                return;
            }

            bootstrap.Restart();
            _completed = false;
        }

        /// <summary>
        /// Re-evaluates completion from the board whenever the bootstrap exposes a different
        /// <see cref="Board"/> instance. This runs on the first frame after Bind (so a level that
        /// already has every box on a goal shows the completion overlay immediately, with no move)
        /// and again after <see cref="Retry"/> rebuilds the board.
        /// </summary>
        private void SynchronizeCompletion()
        {
            Board board = bootstrap.Board;
            if (ReferenceEquals(board, _boundBoard))
            {
                return;
            }

            _boundBoard = board;
            _completed = board.IsComplete;
        }

        private static Direction? ReadDirection()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            {
                return Direction.Up;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                return Direction.Down;
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            {
                return Direction.Left;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                return Direction.Right;
            }

            return null;
        }

        private void OnGUI()
        {
            GuiPanel.ApplyCjkFont();

            if (!_completed || bootstrap == null || bootstrap.Board == null)
            {
                return;
            }

            Rect panel = GuiPanel.Centered(340f, 240f);
            GuiPanel.DrawBacking(panel, new Color(0f, 0f, 0f, 0.72f));

            GUILayout.BeginArea(panel);
            GUILayout.Space(12f);
            GUILayout.Label("关卡完成", GuiPanel.TitleStyle);
            GUILayout.Space(8f);
            GUILayout.Label($"步数：{bootstrap.Board.MoveCount}", GuiPanel.SubtitleStyle);
            GUILayout.Label($"推箱次数：{bootstrap.Board.PushCount}", GuiPanel.SubtitleStyle);
            GUILayout.Space(12f);

            GUILayout.BeginHorizontal();
            GUILayout.Space(16f);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (bootstrap.HasNextLevel)
            {
                if (GUILayout.Button("下一关", GuiPanel.ChineseButtonStyle, GUILayout.Height(30f)))
                {
                    AudioController.Instance?.PlayUiClick();
                    _nextRequested = true;
                }
            }
            else if (bootstrap.LevelIndex < 0)
            {
                // Non-catalog level (editor playtest or built-in fallback): no "all levels" claim.
                GUILayout.Label("关卡完成（测试关卡）", GuiPanel.ChineseLabelStyle, GUILayout.Height(28f));
            }
            else
            {
                GUILayout.Label("全部关卡完成", GuiPanel.ChineseLabelStyle, GUILayout.Height(28f));
            }

            if (GUILayout.Button("重新开始", GuiPanel.ChineseButtonStyle, GUILayout.Height(30f)))
            {
                AudioController.Instance?.PlayUiClick();
                Retry();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            if (GUILayout.Button("返回关卡选择", GuiPanel.ChineseButtonStyle, GUILayout.Height(30f)))
            {
                AudioController.Instance?.PlayUiClick();
                _selectRequested = true;
            }

            GUILayout.EndVertical();
            GUILayout.Space(16f);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}

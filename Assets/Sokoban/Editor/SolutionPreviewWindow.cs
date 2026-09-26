using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    /// <summary>
    /// Solution Preview tool window (Tools &gt; Sokoban &gt; Solution Preview). It analyzes a picked
    /// <see cref="LevelDefinition"/> with the bounded <see cref="LevelAnalyzer"/> (editor-only, never
    /// in play mode) and replays the returned <see cref="AnalysisResult.Solution"/> on a separate
    /// <see cref="Board"/> built from <see cref="LevelEditorDocument.Clone"/> — never the asset, never
    /// the level editor's working copy. The window only reads the asset through the analyzer and the
    /// session clone, and never calls any AssetDatabase write API.
    /// </summary>
    public class SolutionPreviewWindow : EditorWindow
    {
        private const float CellSize = 26f;
        private const double AutoStepIntervalSeconds = 0.5;

        private static readonly GUIContent AnalyzeContent =
            new GUIContent("分析", "对所选关卡运行有界求解器（仅编辑器）。");

        private static readonly GUIContent BackContent =
            new GUIContent("上一步", "回退一步解法移动。");

        private static readonly GUIContent ForwardContent =
            new GUIContent("下一步", "前进一步解法移动。");

        private static readonly GUIContent ResetContent =
            new GUIContent("回到开始", "跳回解法起点。");

        private static readonly GUIContent AutoPlayContent =
            new GUIContent("自动播放", "以固定速度自动重放解法。");

        private static readonly GUIContent StopContent =
            new GUIContent("停止", "停止自动播放循环。");

        private static readonly GUIContent OpenInEditorContent =
            new GUIContent("在关卡编辑器中打开", "在关卡编辑器中打开本关卡。");

        private LevelDefinition _level;
        private AnalysisResult _analysis;
        private SolutionPreviewSession _session;
        private Vector2 _scroll;
        private bool _autoPlaying;
        private double _lastAutoStepTime;

        /// <summary>The level this window is currently bound to, or null when none is selected.</summary>
        public LevelDefinition BoundLevel => _level;

        /// <summary>
        /// The analysis currently bound to this window, or null while the preview refuses a stale replay
        /// (no analysis has been run for the bound level).
        /// </summary>
        public AnalysisResult BoundAnalysis => _analysis;

        [MenuItem("Tools/Sokoban/Solution Preview")]
        public static void Open()
        {
            OpenWindow();
        }

        /// <summary>
        /// Opens the Solution Preview bound to <paramref name="level"/>. When <paramref name="result"/> is a
        /// Solvable analysis of that level the current solution is loaded straight into a replay session, so
        /// the preview shows it immediately. Otherwise (a null result, or a non-Solvable verdict) the window
        /// opens bound to the level but clears any previous analysis, so a stale replay can never be mistaken
        /// for the newly bound level's solution — the OnGUI HelpBox then instructs the user to press Analyze.
        /// </summary>
        public static void OpenFor(LevelDefinition level, AnalysisResult result = null)
        {
            SolutionPreviewWindow window = OpenWindow();

            window._level = level;

            // Rebinding (or refusing a result) first drops any previous session's detached clone, so
            // repeatedly opening Preview for different levels never leaks a clone.
            window.ClearSession();

            if (result != null && result.Verdict == AnalysisVerdict.Solvable)
            {
                window._analysis = result;
                window._session = new SolutionPreviewSession(level, result);
                window._autoPlaying = false;
            }
            else
            {
                window._analysis = null;
                window._autoPlaying = false;
            }

            window.Repaint();
        }

        /// <summary>
        /// Disposes the current replay session's detached clone before the session is replaced or
        /// dropped. A <see cref="SolutionPreviewSession"/> owns its <see cref="SolutionPreviewSession.Clone"/>
        /// (a ScriptableObject), so the window must destroy it — never just null the reference.
        /// Editor-only: the session and clone only ever exist outside play mode, so DestroyImmediate is safe.
        /// </summary>
        private void ClearSession()
        {
            if (_session == null)
            {
                return;
            }

            if (_session.Clone != null)
            {
                Object.DestroyImmediate(_session.Clone);
            }

            _session = null;
        }

        /// <summary>
        /// Navigation entry point into the Level Editor, closing the Preview → Editor half of the
        /// navigation triangle. A null level does nothing; otherwise the editor is opened with the
        /// level loaded straight into its working copy (through <see cref="LevelEditorWindow.OpenFor"/>).
        /// Editor-only and safe to call from headless tests to pin the cross-link target.
        /// </summary>
        public static void OpenInEditor(LevelDefinition level)
        {
            if (level == null)
            {
                return;
            }

            LevelEditorWindow.OpenFor(level);
        }

        /// <summary>
        /// Creates or focuses the Solution Preview window; shared by <see cref="Open"/> and
        /// <see cref="OpenFor"/> (mirrors <see cref="LevelEditorWindow.OpenWindow"/>).
        /// </summary>
        private static SolutionPreviewWindow OpenWindow()
        {
            var window = GetWindow<SolutionPreviewWindow>();
            window.titleContent = new GUIContent("解法预览");
            window.minSize = new Vector2(380f, 420f);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;

            // Default to the first shipped catalog level when one is available, matching how the
            // rest of the editor reads the shipped catalog (read-only load via AssetDatabase).
            if (_level == null)
            {
                LevelCatalog catalog = CatalogAudit.LoadShippedCatalog();
                if (catalog != null && catalog.levels != null && catalog.levels.Count > 0)
                {
                    _level = catalog.levels[0];
                }
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _autoPlaying = false;
            ClearSession();
        }

        private void OnGUI()
        {
            DrawLevelSection();
            EditorGUILayout.Space();

            if (_analysis == null)
            {
                EditorGUILayout.HelpBox(
                    "选择一个关卡并点击“分析”以重放其最短解法。",
                    MessageType.Info);
                return;
            }

            if (_analysis.Verdict != AnalysisVerdict.Solvable)
            {
                // Non-solvable verdicts show only the verdict and detail; no stepping controls.
                DrawVerdict(_analysis);
                return;
            }

            if (_session == null || _session.Result != _analysis)
            {
                _session = new SolutionPreviewSession(_level, _analysis);
            }

            DrawVerdict(_analysis);
            EditorGUILayout.Space();
            DrawReplayControls();
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGrid();
            EditorGUILayout.EndScrollView();
        }

        private void DrawLevelSection()
        {
            EditorGUILayout.LabelField("关卡", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _level = (LevelDefinition)EditorGUILayout.ObjectField(
                "关卡定义", _level, typeof(LevelDefinition), false);
            if (EditorGUI.EndChangeCheck())
            {
                // A different (or cleared) selection invalidates the previous result so the old
                // replay can never be mistaken for the newly selected level's solution.
                _analysis = null;
                ClearSession();
                _autoPlaying = false;
            }

            using (new EditorGUI.DisabledScope(_level == null))
            {
                if (GUILayout.Button(AnalyzeContent))
                {
                    Analyze();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_level == null))
                {
                    if (GUILayout.Button(OpenInEditorContent))
                    {
                        OpenInEditor(_level);
                    }
                }
            }
        }

        /// <summary>
        /// Runs the bounded solver on the selected level. Editor-only: play mode is refused outright.
        /// Every analyze rebuilds the replay session from scratch so a level changed elsewhere always
        /// replays the freshly computed path — no stale path survives.
        /// </summary>
        private void Analyze()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "解法预览",
                    "分析仅在编辑器内运行。请先退出运行模式。",
                    "确定");
                return;
            }

            if (_level == null)
            {
                return;
            }

            _analysis = LevelAnalyzer.Analyze(_level);
            _autoPlaying = false;
            ClearSession();
            _session = _analysis.Verdict == AnalysisVerdict.Solvable
                ? new SolutionPreviewSession(_level, _analysis)
                : null;

            Repaint();
        }

        private static void DrawVerdict(AnalysisResult result)
        {
            switch (result.Verdict)
            {
                case AnalysisVerdict.Solvable:
                    EditorGUILayout.HelpBox(
                        result.Detail +
                        $"（已搜索状态：{result.StatesExplored}，{result.ElapsedSeconds:0.###} 秒）",
                        MessageType.Info);
                    break;

                case AnalysisVerdict.Unsolvable:
                    EditorGUILayout.HelpBox(result.Detail, MessageType.Warning);
                    break;

                default:
                    EditorGUILayout.HelpBox(result.Detail, MessageType.Warning);
                    break;
            }
        }

        private void DrawReplayControls()
        {
            EditorGUILayout.LabelField(
                $"回放：{_session.CurrentActionLabel()}   " +
                $"步数 {_session.MoveCount}/{_session.Result.SolutionMoves}   " +
                $"推箱 {_session.PushCount}/{_session.Result.SolutionPushes}");

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_autoPlaying || !_session.CanStepBack))
                {
                    if (GUILayout.Button(BackContent))
                    {
                        _session.StepBack();
                        Repaint();
                    }
                }

                using (new EditorGUI.DisabledScope(_autoPlaying || !_session.CanStepForward))
                {
                    if (GUILayout.Button(ForwardContent))
                    {
                        _session.StepForward();
                        Repaint();
                    }
                }

                using (new EditorGUI.DisabledScope(_autoPlaying || _session.Step == 0))
                {
                    if (GUILayout.Button(ResetContent))
                    {
                        _session.Reset();
                        Repaint();
                    }
                }

                if (_autoPlaying)
                {
                    if (GUILayout.Button(StopContent))
                    {
                        _autoPlaying = false;
                        Repaint();
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(!_session.CanStepForward))
                    {
                        if (GUILayout.Button(AutoPlayContent))
                        {
                            _autoPlaying = true;
                            _lastAutoStepTime = EditorApplication.timeSinceStartup;
                            Repaint();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Paced editor-time auto-play loop. Driven by <see cref="EditorApplication.update"/>, never a
        /// coroutine, so it keeps working in the editor loop and is stopped when the window closes.
        /// </summary>
        private void OnEditorUpdate()
        {
            if (!_autoPlaying || _session == null)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _lastAutoStepTime < AutoStepIntervalSeconds)
            {
                return;
            }

            _lastAutoStepTime = EditorApplication.timeSinceStartup;
            if (!_session.StepForward())
            {
                _autoPlaying = false;
            }

            Repaint();
        }

        private void DrawGrid()
        {
            Board board = _session.Board;
            if (board == null)
            {
                return;
            }

            float gridWidth = board.Width * CellSize;
            float gridHeight = board.Height * CellSize;

            Rect area = GUILayoutUtility.GetRect(
                gridWidth, gridHeight, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            var boxes = new HashSet<(int x, int y)>(board.BoxPositions);
            (int playerX, int playerY) = board.PlayerPosition;

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var cellRect = new Rect(
                        area.x + x * CellSize, area.y + y * CellSize, CellSize - 2f, CellSize - 2f);

                    EditorGUI.DrawRect(cellRect, TileColor(board.GetTile(x, y)));

                    if (x == playerX && y == playerY)
                    {
                        DrawOccupantMark(cellRect, OccupantColor(OccupantType.Player));
                    }
                    else if (boxes.Contains((x, y)))
                    {
                        DrawOccupantMark(cellRect, OccupantColor(OccupantType.Box));
                    }
                }
            }
        }

        private static void DrawOccupantMark(Rect cellRect, Color color)
        {
            var markRect = new Rect(
                cellRect.x + 5f, cellRect.y + 5f, cellRect.width - 10f, cellRect.height - 10f);
            EditorGUI.DrawRect(markRect, color);
        }

        // Same tile color vocabulary as the Level Editor grid (LevelEditorWindow), duplicated here so
        // the preview reads identically without coupling the two windows' private helpers.
        private static Color TileColor(TileType tile)
        {
            switch (tile)
            {
                case TileType.Wall:
                    return new Color(0.25f, 0.25f, 0.28f);
                case TileType.Goal:
                    return new Color(0.20f, 0.55f, 0.25f);
                case TileType.Plate:
                    return new Color(0.35f, 0.70f, 0.35f);
                case TileType.Door:
                    return new Color(0.55f, 0.35f, 0.20f);
                default:
                    return new Color(0.80f, 0.80f, 0.82f);
            }
        }

        private static Color OccupantColor(OccupantType occupant)
        {
            switch (occupant)
            {
                case OccupantType.Player:
                    return new Color(0.18f, 0.45f, 0.95f);
                case OccupantType.Box:
                    return new Color(0.90f, 0.55f, 0.15f);
                default:
                    return Color.clear;
            }
        }
    }
}

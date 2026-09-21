using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    /// <summary>
    /// Content Dashboard tool window (Tools &gt; Sokoban &gt; Content Dashboard). A read-only,
    /// production-scale view over the shipped catalog that is backed by <see cref="CatalogAudit"/>
    /// (<see cref="CatalogAudit.Audit"/>/<see cref="CatalogAudit.Summarize"/> and its validation-only
    /// projection) and never re-implements validation, analysis or classification logic.
    ///
    /// Budget discipline: opening or repainting the window never runs the bounded solver. The window
    /// first shows a validation-only projection (<see cref="BuildValidationRows"/>); the BFS only runs
    /// on an explicit "Analyze All" (one level at a time with a cancelable progress bar), an explicit
    /// per-row "Analyze", or an explicit "Export Markdown" (which runs the full
    /// <see cref="CatalogAudit.Audit"/> like the existing menu export). A level that was edited and
    /// resaved since its verdict was cached is invalidated by content hash on repaint, never by a BFS.
    /// </summary>
    public class ContentDashboardWindow : EditorWindow
    {
        private static readonly GUIContent ValidateAllContent =
            new GUIContent("全部校验", "刷新校验投影（不运行求解器）。");

        private static readonly GUIContent AnalyzeAllContent =
            new GUIContent("全部分析", "逐关运行有界求解器，一次一关（可取消）。");

        private static readonly GUIContent ExportContent =
            new GUIContent("导出 Markdown", "运行完整目录审计并写入 Logs/Agent/CatalogAudit.md。");

        private static readonly GUIContent OpenEditorContent =
            new GUIContent("打开编辑器", "打开关卡编辑器窗口。");

        private static readonly GUIContent EditContent =
            new GUIContent("编辑", "在关卡编辑器中编辑此关卡。");

        private static readonly GUIContent PlaytestContent =
            new GUIContent("试玩", "进入此关卡的运行模式。");

        private static readonly GUIContent PreviewContent =
            new GUIContent("预览", "打开此关卡的解法预览。");

        private static readonly GUIContent ReanalyzeContent =
            new GUIContent("分析", "仅为此关卡运行有界求解器。");

        private const float StatusColumnWidth = 96f;

        private static readonly Color ValidTint = new Color(0.30f, 0.80f, 0.30f);
        private static readonly Color WarningTint = new Color(0.95f, 0.80f, 0.20f);
        private static readonly Color ErrorTint = new Color(0.95f, 0.35f, 0.35f);
        private static readonly Color NeutralTint = new Color(0.65f, 0.65f, 0.65f);

        private LevelCatalog _catalog;
        private List<CatalogAuditRow> _rows = new List<CatalogAuditRow>();
        private Vector2 _scroll;
        private bool _analyzed;

        [MenuItem("Tools/Sokoban/Content Dashboard")]
        public static void Open()
        {
            var window = GetWindow<ContentDashboardWindow>();
            window.titleContent = new GUIContent("内容总览");
            window.minSize = new Vector2(640f, 420f);
            window.Show();
        }

        /// <summary>Validation-only projection for a catalog (the analyzer never runs here).</summary>
        public static List<CatalogAuditRow> BuildValidationRows(LevelCatalog catalog)
        {
            return CatalogAudit.BuildValidationRows(catalog);
        }

        /// <summary>Pure pass-through from an analyzer result onto a projected row.</summary>
        public static void ApplyAnalysis(CatalogAuditRow row, AnalysisResult result)
        {
            CatalogAudit.ApplyAnalysis(row, result);
        }

        private void OnEnable()
        {
            _catalog = CatalogAudit.LoadShippedCatalog();
            RefreshValidationOnly();
        }

        private void OnGUI()
        {
            RefreshStaleMarkers();
            DrawToolbar();
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawTable();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            DrawSummary();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(ValidateAllContent))
                {
                    RefreshValidationOnly();
                    Repaint();
                }

                if (GUILayout.Button(AnalyzeAllContent))
                {
                    AnalyzeAll();
                }

                if (GUILayout.Button(ExportContent))
                {
                    ExportMarkdown();
                }

                if (GUILayout.Button(OpenEditorContent))
                {
                    LevelEditorWindow.Open();
                }
            }

            string mode = _analyzed ? "已分析" : "仅校验（未分析）";
            EditorGUILayout.LabelField(
                "目录：" + CatalogLabel() + "   模式：" + mode + "   槽位：" + _rows.Count,
                EditorStyles.miniLabel);
        }

        private void DrawTable()
        {
            DrawHeaderRow();

            foreach (CatalogAuditRow row in _rows)
            {
                DrawRow(row);
            }
        }

        private void DrawHeaderRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("槽位", EditorStyles.boldLabel, GUILayout.Width(40f));
                GUILayout.Label("关卡", EditorStyles.boldLabel, GUILayout.Width(90f));
                GUILayout.Label("人/箱/点", EditorStyles.boldLabel, GUILayout.Width(64f));
                GUILayout.Label("错误", EditorStyles.boldLabel, GUILayout.Width(34f));
                GUILayout.Label("警告", EditorStyles.boldLabel, GUILayout.Width(40f));
                GUILayout.Label("校验", EditorStyles.boldLabel, GUILayout.Width(StatusColumnWidth));
                GUILayout.Label("分析", EditorStyles.boldLabel, GUILayout.Width(96f));
                GUILayout.Label("最短步数", EditorStyles.boldLabel, GUILayout.Width(64f));
                GUILayout.Label("最少推箱", EditorStyles.boldLabel, GUILayout.Width(72f));
                GUILayout.Label("搜索状态", EditorStyles.boldLabel, GUILayout.Width(72f));
                GUILayout.Label("耗时", EditorStyles.boldLabel, GUILayout.Width(64f));
                GUILayout.Label("机制", EditorStyles.boldLabel, GUILayout.Width(96f));
                GUILayout.Label("首个问题", EditorStyles.boldLabel, GUILayout.Width(260f));
                GUILayout.Label("操作", EditorStyles.boldLabel, GUILayout.Width(284f));
            }
        }

        private void DrawRow(CatalogAuditRow row)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(row.Slot.ToString(), GUILayout.Width(40f));
                GUILayout.Label(DisplayName(row), GUILayout.Width(90f));
                GUILayout.Label(row.PlayerCount + "/" + row.BoxCount + "/" + row.GoalCount, GUILayout.Width(64f));
                GUILayout.Label(row.ErrorCount.ToString(), GUILayout.Width(34f));
                GUILayout.Label(row.WarningCount.ToString(), GUILayout.Width(40f));
                DrawStatusCell(row);
                DrawVerdictCell(row);
                DrawAnalysisCell(row, MovesCell(row), 64f);
                DrawAnalysisCell(row, PushesCell(row), 72f);
                DrawAnalysisCell(row, StatesCell(row), 72f);
                DrawAnalysisCell(row, TimeCell(row), 64f);
                GUILayout.Label(MechanicsCell(row), GUILayout.Width(96f));
                GUILayout.Label(row.FirstIssue, GUILayout.Width(260f));

                LevelDefinition level = RowLevel(row);

                using (new EditorGUI.DisabledScope(level == null))
                {
                    if (GUILayout.Button(EditContent, GUILayout.Width(56f)))
                    {
                        OpenInLevelEditor(level);
                    }

                    if (GUILayout.Button(PlaytestContent, GUILayout.Width(70f)))
                    {
                        PlaytestRow(level);
                    }

                    if (GUILayout.Button(PreviewContent, GUILayout.Width(64f)))
                    {
                        PreviewRow(row);
                    }
                }

                using (new EditorGUI.DisabledScope(level == null || row.ErrorCount > 0))
                {
                    if (GUILayout.Button(ReanalyzeContent, GUILayout.Width(84f)))
                    {
                        ReanalyzeRow(row);
                    }
                }
            }
        }

        private void DrawSummary()
        {
            CatalogAuditSummary summary = CatalogAudit.Summarize(_rows);
            int notAnalyzed = summary.Total - summary.Solvable - summary.Unsolvable - summary.Inconclusive;
            if (notAnalyzed < 0)
            {
                notAnalyzed = 0;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("内容健康度", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "关卡=" + summary.Total +
                    "  校验通过=" + summary.Valid +
                    "  可解=" + summary.Solvable +
                    "  错误=" + summary.Errors +
                    "  警告=" + summary.WithWarnings,
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "无解=" + summary.Unsolvable +
                    "  未确定=" + summary.Inconclusive +
                    "  未分析=" + notAnalyzed +
                    "  缺失=" + summary.Missing +
                    "  重复=" + summary.Duplicates,
                    EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// Drops cached verdicts whose content hash no longer matches the current level, so an edited
        /// and resaved level shows as "stale" (via <see cref="CatalogAuditRow.IsStaleAnalysis"/>) on the
        /// next repaint. This never runs the solver and never rebuilds validator counts (those refresh on
        /// the explicit "Validate All").
        /// </summary>
        private void RefreshStaleMarkers()
        {
            if (_catalog == null || _catalog.levels == null)
            {
                return;
            }

            int count = Mathf.Min(_rows.Count, _catalog.levels.Count);
            for (int i = 0; i < count; i++)
            {
                CatalogAuditRow row = _rows[i];
                LevelDefinition level = _catalog.levels[i];

                if (CatalogAudit.IsStale(row, level))
                {
                    CatalogAudit.ResetAnalysis(row);
                    row.ContentHash = CatalogAudit.ContentHash(level);
                }
            }
        }

        private static string StatusSymbol(CatalogAuditRow row)
        {
            switch (row.Status)
            {
                case CatalogAuditStatus.Valid:
                    return "✓";
                case CatalogAuditStatus.Warnings:
                    return "⚠";
                case CatalogAuditStatus.Error:
                    return "✗";
                case CatalogAuditStatus.Missing:
                    return "—";
                case CatalogAuditStatus.Duplicate:
                    return "≡";
                default:
                    return "·";
            }
        }

        private static Color StatusColor(CatalogAuditRow row)
        {
            switch (row.Status)
            {
                case CatalogAuditStatus.Valid:
                    return ValidTint;
                case CatalogAuditStatus.Warnings:
                    return WarningTint;
                case CatalogAuditStatus.Error:
                    return ErrorTint;
                default:
                    return NeutralTint;
            }
        }

        private static string VerdictSymbol(CatalogAuditRow row)
        {
            if (row.IsStaleAnalysis)
            {
                return "⚠";
            }

            if (!row.Verdict.HasValue)
            {
                return "·";
            }

            switch (row.Verdict.Value)
            {
                case AnalysisVerdict.Solvable:
                    return "✓";
                case AnalysisVerdict.Unsolvable:
                    return "✗";
                case AnalysisVerdict.Inconclusive:
                    return "?";
                default:
                    return "·";
            }
        }

        private static Color VerdictColor(CatalogAuditRow row)
        {
            if (row.IsStaleAnalysis)
            {
                return WarningTint;
            }

            if (!row.Verdict.HasValue)
            {
                return NeutralTint;
            }

            switch (row.Verdict.Value)
            {
                case AnalysisVerdict.Solvable:
                    return ValidTint;
                case AnalysisVerdict.Unsolvable:
                    return ErrorTint;
                case AnalysisVerdict.Inconclusive:
                    return WarningTint;
                default:
                    return NeutralTint;
            }
        }

        private static string StatusLabel(CatalogAuditStatus status)
        {
            switch (status)
            {
                case CatalogAuditStatus.Valid:
                    return "通过";
                case CatalogAuditStatus.Warnings:
                    return "有警告";
                case CatalogAuditStatus.Error:
                    return "错误";
                case CatalogAuditStatus.Missing:
                    return "缺失";
                case CatalogAuditStatus.Duplicate:
                    return "重复";
                default:
                    return status.ToString();
            }
        }

        private static void DrawStatusCell(CatalogAuditRow row)
        {
            Color previous = GUI.color;
            GUI.color = StatusColor(row);
            GUILayout.Label(StatusSymbol(row) + " " + StatusLabel(row.Status), GUILayout.Width(StatusColumnWidth));
            GUI.color = previous;
        }

        private void DrawVerdictCell(CatalogAuditRow row)
        {
            Color previous = GUI.color;
            GUI.color = VerdictColor(row);
            GUILayout.Label(VerdictSymbol(row) + " " + VerdictCell(row), GUILayout.Width(96f));
            GUI.color = previous;
        }

        private void RefreshValidationOnly()
        {
            _rows = BuildValidationRows(_catalog);
            _analyzed = false;
        }

        /// <summary>
        /// Public entry point for the same explicit "Analyze All" pass the toolbar button runs: it
        /// refreshes the validation projection and then runs the bounded solver once per level (main
        /// thread, cancelable progress bar, no Thread.Sleep). Added for the automated capture bridge,
        /// which drives the dashboard headlessly and captures after this call returns (the progress bar
        /// has already cleared by then). A no-op for an empty/absent catalog.
        /// </summary>
        public void RunAnalyzeAll()
        {
            AnalyzeAll();
            Repaint();
        }

        private void AnalyzeAll()
        {
            LevelCatalog catalog = _catalog;
            if (catalog == null || catalog.levels == null || catalog.levels.Count == 0)
            {
                return;
            }

            _rows = BuildValidationRows(catalog);

            for (int i = 0; i < _rows.Count; i++)
            {
                CatalogAuditRow row = _rows[i];
                LevelDefinition level = RowLevel(row);

                string info = string.IsNullOrEmpty(row.LevelName)
                    ? "槽位 " + row.Slot
                    : row.LevelName + " (槽位 " + row.Slot + ")";

                if (EditorUtility.DisplayCancelableProgressBar(
                        "内容总览 - 全部分析",
                        "正在分析 " + info,
                        (float)i / _rows.Count))
                {
                    EditorUtility.ClearProgressBar();
                    _analyzed = false; // Cancelled rows keep "not analyzed".
                    Repaint();
                    return;
                }

                if (level != null && row.ErrorCount == 0)
                {
                    ApplyAnalysis(
                        row,
                        LevelAnalyzer.Analyze(level, CatalogAudit.AuditMaxStates, CatalogAudit.AuditMaxTime));
                }
            }

            EditorUtility.ClearProgressBar();
            _analyzed = true;
            Repaint();
        }

        private void ReanalyzeRow(CatalogAuditRow row)
        {
            LevelDefinition level = RowLevel(row);
            if (level == null || row.ErrorCount > 0)
            {
                return;
            }

            ApplyAnalysis(
                row,
                LevelAnalyzer.Analyze(level, CatalogAudit.AuditMaxStates, CatalogAudit.AuditMaxTime));
            Repaint();
        }

        private void ExportMarkdown()
        {
            LevelCatalog catalog = _catalog;
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("内容总览", "未加载目录。", "确定");
                return;
            }

            // Mirrors CatalogAudit.ExportMarkdownMenu: full audit, then System.IO write (never an
            // AssetDatabase write), then refresh the asset database so the Logs file is picked up.
            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);
            string path = CatalogAudit.ExportMarkdownPath();

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, CatalogAudit.FormatMarkdown(catalog, rows));
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("内容总览", "目录审计已导出至 " + path, "确定");
        }

        /// <summary>
        /// Opens the Level Editor and auto-loads <paramref name="level"/> straight into its working copy
        /// via <see cref="LevelEditorWindow.OpenFor"/>, so the designer jumps from the dashboard row into
        /// authoring without touching the editor's Load field. A null level is a no-op.
        /// </summary>
        private static void OpenInLevelEditor(LevelDefinition level)
        {
            if (level == null)
            {
                return;
            }

            LevelEditorWindow.OpenFor(level);
        }

        /// <summary>
        /// Enters Play Mode for <paramref name="level"/> via <see cref="LevelEditorWindow.PlaytestLevel"/>,
        /// the production one-click Playtest bridge (arms the request then switches scene/enters play mode).
        /// A null level is a no-op.
        /// </summary>
        private static void PlaytestRow(LevelDefinition level)
        {
            if (level == null)
            {
                return;
            }

            LevelEditorWindow.PlaytestLevel(level);
        }

        /// <summary>
        /// Opens the Solution Preview bound to this row's level via <see cref="SolutionPreviewWindow.OpenFor"/>.
        /// The audit row only retains counts, not the solution path, so when the row is Solvable the replay is
        /// recomputed here with the same bounded analyzer budget the audit used. A row that was never analyzed
        /// (or a non-Solvable row) passes a null result, so the preview opens bound to the level but refuses a
        /// stale replay. A null level is a no-op.
        /// </summary>
        private void PreviewRow(CatalogAuditRow row)
        {
            LevelDefinition level = RowLevel(row);
            if (level == null)
            {
                return;
            }

            AnalysisResult result = null;
            if (row.Verdict == AnalysisVerdict.Solvable)
            {
                result = LevelAnalyzer.Analyze(level, CatalogAudit.AuditMaxStates, CatalogAudit.AuditMaxTime);
            }

            SolutionPreviewWindow.OpenFor(level, result);
        }

        private LevelDefinition RowLevel(CatalogAuditRow row)
        {
            if (_catalog == null || _catalog.levels == null ||
                row == null || row.Slot < 0 || row.Slot >= _catalog.levels.Count)
            {
                return null;
            }

            return _catalog.levels[row.Slot];
        }

        private static string DisplayName(CatalogAuditRow row)
        {
            if (row.IsMissing)
            {
                return "<缺失>";
            }

            return string.IsNullOrEmpty(row.LevelName) ? "<未命名>" : row.LevelName;
        }

        private static string VerdictLabel(AnalysisVerdict verdict)
        {
            switch (verdict)
            {
                case AnalysisVerdict.Solvable:
                    return "可解";
                case AnalysisVerdict.Unsolvable:
                    return "无解";
                case AnalysisVerdict.Inconclusive:
                    return "未确定";
                default:
                    return verdict.ToString();
            }
        }

        private string VerdictCell(CatalogAuditRow row)
        {
            if (row.IsStaleAnalysis)
            {
                return StaleText();
            }

            return row.Verdict.HasValue ? VerdictLabel(row.Verdict.Value) : NoAnalysisText();
        }

        private string MovesCell(CatalogAuditRow row)
        {
            if (row.IsStaleAnalysis)
            {
                return StaleText();
            }

            if (row.Verdict == AnalysisVerdict.Solvable)
            {
                return row.SolutionMoves.ToString();
            }

            return row.Verdict.HasValue ? "n/a" : NoAnalysisText();
        }

        private string PushesCell(CatalogAuditRow row)
        {
            if (row.IsStaleAnalysis)
            {
                return StaleText();
            }

            if (row.Verdict == AnalysisVerdict.Solvable)
            {
                return row.SolutionPushes.ToString();
            }

            return row.Verdict.HasValue ? "n/a" : NoAnalysisText();
        }

        private string StatesCell(CatalogAuditRow row)
        {
            if (row.IsStaleAnalysis)
            {
                return StaleText();
            }

            return row.Verdict.HasValue ? row.StatesExplored.ToString() : NoAnalysisText();
        }

        private string TimeCell(CatalogAuditRow row)
        {
            if (row.IsStaleAnalysis)
            {
                return StaleText();
            }

            return row.Verdict.HasValue
                ? row.ElapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                : NoAnalysisText();
        }

        private string NoAnalysisText()
        {
            return _analyzed ? "n/a" : "未分析";
        }

        /// <summary>
        /// Label for an analysis cell whose cached verdict was invalidated by an edit (see
        /// <see cref="CatalogAudit.ResetAnalysis"/>): an explicit "已过期" so it is never confused with the
        /// neutral "n/a"/"未分析" states.
        /// </summary>
        private static string StaleText()
        {
            return "已过期";
        }

        /// <summary>
        /// Draws one analysis-derived cell (moves/pushes/states/time). A stale row is tinted amber so the
        /// invalidated verdict is visually distinct; every other row keeps its normal color.
        /// </summary>
        private static void DrawAnalysisCell(CatalogAuditRow row, string text, float width)
        {
            Color previous = GUI.color;
            if (row.IsStaleAnalysis)
            {
                GUI.color = WarningTint;
            }

            GUILayout.Label(text, GUILayout.Width(width));
            GUI.color = previous;
        }

        private static string MechanicsCell(CatalogAuditRow row)
        {
            return row.MechanicsLabel;
        }

        private string CatalogLabel()
        {
            return _catalog == null ? "(空目录)" : LevelAssetFactory.CatalogPath;
        }
    }
}

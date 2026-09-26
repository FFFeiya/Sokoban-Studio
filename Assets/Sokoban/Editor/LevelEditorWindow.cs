using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.Editor
{
    /// <summary>
    /// Designer authoring surface (Tools &gt; Sokoban &gt; Level Editor) for the
    /// Create → Validate → Save workflow. All editing happens on the working copy owned by
    /// <see cref="LevelEditorDocument"/>; the loaded asset is only written on an explicit Save.
    /// The UI deliberately stays on OnGUI/EditorGUILayout so it needs no UIElements assets.
    /// </summary>
    public class LevelEditorWindow : EditorWindow
    {
        private const float CellSize = 26f;
        private const string SaveFolder = "Assets/Sokoban/Levels";

        // Cached labels. GUIContent itself is not skin-dependent, so cached instances are safe to
        // build once and reuse across OnGUI calls (no per-frame allocations).
        private static readonly GUIContent NewContent =
            new GUIContent("新建", "新建空白工作副本（丢弃未保存编辑前会询问）。");

        private static readonly GUIContent SaveContent =
            new GUIContent("保存", "校验并将工作副本写入其源资产。");

        private static readonly GUIContent SaveAsContent =
            new GUIContent("另存为", "校验并将工作副本写入在文件面板中选择的新资产。");

        private static readonly GUIContent UndoContent =
            new GUIContent("撤销", "撤销上一步编辑（Ctrl+Z）。");

        private static readonly GUIContent RedoContent =
            new GUIContent("重做", "重做上一步撤销的编辑（Ctrl+Y / Ctrl+Shift+Z）。");

        private static readonly GUIContent PlaytestContent =
            new GUIContent("一键试玩", "校验、保存，然后以本关卡进入运行模式。");

        private static readonly GUIContent LoadContent =
            new GUIContent("加载", "打开所选关卡资产的新副本。");

        private static readonly GUIContent DashboardContent =
            new GUIContent("内容总览", "打开内容总览。");

        // Toolbar labels, in the same order as BrushOrder. Erase was merged into Floor (identical
        // ops); the LevelBrush enum keeps Erase as a value for compatibility. Plate/Door come in
        // explicit A/B variants so a designer can author a two-group plate/door puzzle.
        private static readonly GUIContent[] BrushLabels =
        {
            new GUIContent("墙", "绘制实心墙格（同时清除任何占据者）。"),
            new GUIContent("地板", "绘制空白地板格（清除任何占据者；亦作擦除）。"),
            new GUIContent("目标", "绘制目标格（箱子必须停在其上）。"),
            new GUIContent("玩家", "放置唯一玩家（移动任何现有玩家）。"),
            new GUIContent("箱子", "放置可推动的箱子（墙格会变为地板）。"),
            new GUIContent("压力板 A", "绘制 A 组压力板；所有 A 组压力板被占据时打开所有 A 组门。"),
            new GUIContent("门 A", "绘制 A 组门；A 组压力板未满足时它保持关闭。"),
            new GUIContent("压力板 B", "绘制 B 组压力板；所有 B 组压力板被占据时打开所有 B 组门。"),
            new GUIContent("门 B", "绘制 B 组门；B 组压力板未满足时它保持关闭。")
        };

        // Maps a toolbar index to its brush. The enum values are not index-aligned (Erase sits
        // between Floor and Plate), so the palette never casts the raw toolbar index.
        private static readonly LevelBrush[] BrushOrder =
        {
            LevelBrush.Wall,
            LevelBrush.Floor,
            LevelBrush.Goal,
            LevelBrush.Player,
            LevelBrush.Box,
            LevelBrush.PlateA,
            LevelBrush.DoorA,
            LevelBrush.PlateB,
            LevelBrush.DoorB
        };

        private LevelEditorDocument _document;
        private LevelBrush _brush = LevelBrush.Wall;

        // Cached style for the A/B group glyph drawn over plate/door cells in the grid. Built lazily
        // (a style copies skin state, so it is only valid once the editor skin exists) and reused for
        // every cell, so the grid adds no per-cell style allocation.
        private GUIStyle _groupGlyphStyle;
        private Vector2 _scroll;
        private int _resizeWidth;
        private int _resizeHeight;
        private string _nameField = "Untitled";
        private UnityEngine.Object _loadSource;
        private AnalysisResult _lastAnalysis;

        // On-demand "Compare with saved" cache. Null until the button is clicked, then holds the one
        // analysis of the saved asset for this document. It is display-only and is cleared whenever
        // _document is replaced (New / Load), so a comparison never outlives its document. It is never
        // populated by a repaint and never by the working-copy "Analyze Solvability" button.
        private AnalysisResult _savedAnalysis;

        // True when the last "Compare with saved" click could not load the source asset (deleted or
        // moved). Kept separate from a null result so "not run yet" and "unavailable" read differently.
        private bool _savedAnalysisUnavailable;

        // Cached detailed-validation result for the current OnGUI pass only (never across frames),
        // so the file/grid/status/shortcut consumers share one validation call per frame.
        private List<LevelIssue> _frameIssues;

        // Click-to-navigate state: the cell referenced by the last clicked issue row. The focus
        // frame persists until the next click (a grid paint, or New/Load/Resize replacing the copy).
        private bool _hasSelectedCell;
        private int _selectedCellX = -1;
        private int _selectedCellY = -1;
        private bool _scrollToSelectionPending;

        [MenuItem("Tools/Sokoban/Level Editor")]
        public static void Open()
        {
            OpenWindow();
        }

        /// <summary>
        /// Opens the Level Editor and loads <paramref name="level"/> straight into a fresh working copy,
        /// so the Content Dashboard can jump from a saved asset into authoring without the designer
        /// touching the Load field. A null level just opens the window, exactly like <see cref="Open"/>.
        /// </summary>
        public static void OpenFor(LevelDefinition level)
        {
            LevelEditorWindow window = OpenWindow();

            if (level != null)
            {
                window.LoadDocument(level);
            }
        }

        /// <summary>
        /// Content Dashboard playtest entry point for an already-saved <paramref name="level"/>: arms
        /// the persistent request asset through <see cref="PlaytestLauncher"/> and then switches to the
        /// gameplay scene and enters play mode through the same shared tail the working-copy Playtest
        /// uses. A null level does nothing.
        /// </summary>
        public static void PlaytestLevel(LevelDefinition level)
        {
            if (level == null)
            {
                return;
            }

            if (!PlaytestLauncher.TryPreparePlaytest(level, out string error))
            {
                EditorUtility.DisplayDialog("无法试玩", error, "确定");
                return;
            }

            EnterPlaytestScene();
        }

        /// <summary>
        /// Navigation entry point back to the Content Dashboard (the hub this editor links out from),
        /// closing the Editor → Dashboard half of the navigation triangle. Editor-only; safe to call
        /// from headless tests to pin the cross-link target.
        /// </summary>
        public static void OpenDashboard()
        {
            ContentDashboardWindow.Open();
        }

        /// <summary>
        /// The working copy the window is currently editing. Exposed so the Content Dashboard (and
        /// headless tests) can inspect what was loaded without going through the UI.
        /// </summary>
        public LevelEditorDocument LoadedDocument => _document;

        /// <summary>Creates or focuses the Level Editor window; shared by <see cref="Open"/> and <see cref="OpenFor"/>.</summary>
        private static LevelEditorWindow OpenWindow()
        {
            var window = GetWindow<LevelEditorWindow>();
            window.titleContent = new GUIContent("关卡编辑器");
            window.minSize = new Vector2(380f, 460f);
            window.Show();
            return window;
        }

        /// <summary>
        /// Shared playtest tail: switch to the gameplay scene and enter play mode. Both the
        /// working-copy <see cref="Playtest"/> and <see cref="PlaytestLevel"/> funnel through here, so
        /// the scene switch lives in exactly one place. Returns false when the user cancelled the
        /// scene-save prompt, in which case the pending request is dropped so a later plain Play does
        /// not unexpectedly use this level.
        /// </summary>
        private static bool EnterPlaytestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                PlaytestRequestJanitor.Clear();
                return false;
            }

            EditorSceneManager.OpenScene(LevelAssetFactory.GameplayScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
            return true;
        }

        private void OnEnable()
        {
            if (_document == null)
            {
                _document = LevelEditorDocument.CreateNew();
                _nameField = _document.Working.levelName;
                ResetAnalysisState();
            }

            SyncResizeFields();
        }

        /// <summary>Unity calls this when the user confirms Save in the close-with-changes prompt.</summary>
        public override void SaveChanges()
        {
            if (_document != null && SaveCurrent())
            {
                base.SaveChanges();
            }
        }

        private void OnGUI()
        {
            if (_document == null)
            {
                _document = LevelEditorDocument.CreateNew();
                _nameField = _document.Working.levelName;
                SyncResizeFields();
                ResetAnalysisState();
            }

            // Single detailed-validation pass per frame; every consumer below reads _frameIssues.
            _frameIssues = _document.ValidateDetailed();

            DrawFileSection();
            EditorGUILayout.Space();
            DrawNameAndResizeSection();
            EditorGUILayout.Space();

            DrawPalette();
            EditorGUILayout.Space();
            DrawGridLegend();
            DrawStatusStrip();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGrid();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            DrawStatus();

            EditorGUILayout.Space();
            DrawAnalysisSection();

            UpdateTitle();

            HandleUndoRedoShortcuts();
            HandleDocumentShortcuts();
        }

        private void DrawFileSection()
        {
            EditorGUILayout.LabelField("文件", EditorStyles.boldLabel);

            // Plays the same role as the disabled Playtest button: blocking errors gate the button
            // and the Ctrl+Enter shortcut. Playtest() re-validates at runtime as the authoritative
            // guard (defense in depth).
            bool hasErrors = HasBlockingErrors(_frameIssues);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(NewContent))
                {
                    if (ConfirmDiscardIfDirty())
                    {
                        _document = LevelEditorDocument.CreateNew();
                        _nameField = _document.Working.levelName;
                        SyncResizeFields();
                        ClearSelectedCell();
                        ResetAnalysisState();
                    }
                }

                if (GUILayout.Button(SaveContent))
                {
                    SaveCurrent();
                }

                if (GUILayout.Button(SaveAsContent))
                {
                    PromptSaveAs();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_document.CanUndo))
                {
                    if (GUILayout.Button(UndoContent))
                    {
                        if (_document.Undo())
                        {
                            AfterHistoryStep();
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(!_document.CanRedo))
                {
                    if (GUILayout.Button(RedoContent))
                    {
                        if (_document.Redo())
                        {
                            AfterHistoryStep();
                        }
                    }
                }
            }

            using (new EditorGUI.DisabledScope(hasErrors))
            {
                if (GUILayout.Button(PlaytestContent))
                {
                    Playtest();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _loadSource = EditorGUILayout.ObjectField(
                    "加载关卡", _loadSource, typeof(LevelDefinition), false);

                using (new EditorGUI.DisabledScope(_loadSource == null))
                {
                    if (GUILayout.Button(LoadContent, GUILayout.Width(60f)))
                    {
                        LoadDocument(_loadSource as LevelDefinition);
                    }
                }
            }

            // Navigation action, kept separate from the file operations above so returning to the
            // Content Dashboard never reads as a destructive edit.
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(DashboardContent))
                {
                    OpenDashboard();
                }
            }
        }

        private void DrawNameAndResizeSection()
        {
            EditorGUILayout.LabelField("关卡信息", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _nameField = EditorGUILayout.TextField("名称", _nameField);
            if (EditorGUI.EndChangeCheck())
            {
                _document.SetLevelName(_nameField);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _resizeWidth = Mathf.Max(LevelEditorDocument.MinSize, EditorGUILayout.IntField("宽度", _resizeWidth));
                _resizeHeight = Mathf.Max(LevelEditorDocument.MinSize, EditorGUILayout.IntField("高度", _resizeHeight));

                if (GUILayout.Button("应用尺寸", GUILayout.Width(100f)))
                {
                    _document.Resize(_resizeWidth, _resizeHeight);
                    ClearSelectedCell();
                }

                if (GUILayout.Button("重置", GUILayout.Width(60f)))
                {
                    SyncResizeFields();
                }
            }
        }

        private void DrawPalette()
        {
            EditorGUILayout.LabelField("绘制工具", EditorStyles.boldLabel);

            // The toolbar index is a position in BrushOrder, not a LevelBrush value (Erase was
            // removed from the palette while staying in the enum). An unknown brush falls back to
            // Wall so the toolbar always has a defined selected index.
            int index = System.Array.IndexOf(BrushOrder, _brush);
            if (index < 0)
            {
                index = 0;
            }

            _brush = BrushOrder[GUILayout.Toolbar(index, BrushLabels)];
        }

        private void DrawGrid()
        {
            LevelDefinition def = _document.Working;
            float gridWidth = def.width * CellSize;
            float gridHeight = def.height * CellSize;

            Rect area = GUILayoutUtility.GetRect(
                gridWidth, gridHeight, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            // One-shot scroll after clicking an issue row: bring the referenced cell's top-left
            // corner to the top-left of the scroll view. Unity clamps the value to the valid scroll
            // range, so cells near the bottom/right edge stay visible instead of scrolling past the
            // end of the grid.
            if (_scrollToSelectionPending)
            {
                _scrollToSelectionPending = false;
                if (_hasSelectedCell && def.InBounds(_selectedCellX, _selectedCellY))
                {
                    _scroll = new Vector2(
                        area.x + _selectedCellX * CellSize,
                        area.y + _selectedCellY * CellSize);
                }
            }

            // Advisory warnings (e.g. a box trapped in a static corner) get an orange frame and
            // blocking errors referenced to a cell get a red frame, so the designer can see the
            // offending cell directly in the grid. The clicked issue cell gets a cyan focus frame.
            List<LevelIssue> issues = _frameIssues;

            for (int y = 0; y < def.height; y++)
            {
                for (int x = 0; x < def.width; x++)
                {
                    int index = def.Index(x, y);
                    var cellRect = new Rect(
                        area.x + x * CellSize, area.y + y * CellSize, CellSize - 2f, CellSize - 2f);

                    TileType tile = def.cells[index];
                    EditorGUI.DrawRect(cellRect, TileColor(tile));

                    OccupantType occupant = def.occupants[index];
                    if (occupant != OccupantType.None)
                    {
                        var markRect = new Rect(
                            cellRect.x + 5f, cellRect.y + 5f, cellRect.width - 10f, cellRect.height - 10f);
                        EditorGUI.DrawRect(markRect, OccupantColor(occupant));
                    }

                    // Plate/Door group identity is shown as a text glyph, not color alone.
                    if (tile == TileType.Plate || tile == TileType.Door)
                    {
                        DrawGroupGlyph(cellRect, def.GetGroupId(index));
                    }

                    if (HasErrorAt(issues, x, y))
                    {
                        DrawErrorFrame(cellRect);
                    }
                    else if (HasWarningAt(issues, x, y))
                    {
                        DrawWarningFrame(cellRect);
                    }

                    if (_hasSelectedCell && _selectedCellX == x && _selectedCellY == y)
                    {
                        DrawFocusFrame(cellRect);
                    }
                }
            }

            Event current = Event.current;
            bool overGrid = area.Contains(current.mousePosition);

            // Right-click = erase the cell back to plain floor (the Floor brush is byte-identical to
            // the Erase brush in LevelEditorDocument.Paint: TileType.Floor + no occupant).
            if (current.type == EventType.MouseDown && current.button == 1 && overGrid)
            {
                int cellX = Mathf.FloorToInt((current.mousePosition.x - area.x) / CellSize);
                int cellY = Mathf.FloorToInt((current.mousePosition.y - area.y) / CellSize);

                if (def.InBounds(cellX, cellY))
                {
                    _document.Paint(cellX, cellY, LevelBrush.Floor);
                    ClearSelectedCell();
                    current.Use();
                    Repaint();
                }
            }
            // Alt+left-click = eyedropper: adopt the clicked cell's brush without painting.
            else if (current.type == EventType.MouseDown && current.button == 0 && current.alt && overGrid)
            {
                int cellX = Mathf.FloorToInt((current.mousePosition.x - area.x) / CellSize);
                int cellY = Mathf.FloorToInt((current.mousePosition.y - area.y) / CellSize);

                if (def.InBounds(cellX, cellY))
                {
                    _brush = BrushForCell(def, cellX, cellY);
                    current.Use();
                    Repaint();
                }
            }
            // Plain left paint: guarded against Alt so it never fights the eyedropper above.
            else if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) &&
                current.button == 0 && !current.alt && overGrid)
            {
                int cellX = Mathf.FloorToInt((current.mousePosition.x - area.x) / CellSize);
                int cellY = Mathf.FloorToInt((current.mousePosition.y - area.y) / CellSize);

                if (def.InBounds(cellX, cellY))
                {
                    _document.Paint(cellX, cellY, _brush);
                    ClearSelectedCell();
                    current.Use();
                    Repaint();
                }
            }
        }

        /// <summary>
        /// Brush matching a cell for the eyedropper. An occupant wins over the underlying tile
        /// (a box on a goal picks Box); any other non-wall tile picks its own brush, and a plain
        /// floor or unknown tile falls back to Floor (never Erase).
        /// </summary>
        private static LevelBrush BrushForCell(LevelDefinition def, int x, int y)
        {
            int index = def.Index(x, y);

            OccupantType occupant = def.occupants[index];
            if (occupant == OccupantType.Box)
            {
                return LevelBrush.Box;
            }

            if (occupant == OccupantType.Player)
            {
                return LevelBrush.Player;
            }

            switch (def.cells[index])
            {
                case TileType.Wall:
                    return LevelBrush.Wall;
                case TileType.Goal:
                    return LevelBrush.Goal;
                case TileType.Plate:
                    // Adopt the cell's actual group so a group-B plate never picks the group-A brush.
                    return def.GetGroupId(index) == 1 ? LevelBrush.PlateB : LevelBrush.PlateA;
                case TileType.Door:
                    return def.GetGroupId(index) == 1 ? LevelBrush.DoorB : LevelBrush.DoorA;
                default:
                    return LevelBrush.Floor;
            }
        }

        /// <summary>
        /// Draws the non-color group glyph ("A"/"B") centered over a plate/door cell, so group identity
        /// stays readable for designers who cannot rely on color. Group A is 0, group B is 1.
        /// </summary>
        private void DrawGroupGlyph(Rect cellRect, int groupId)
        {
            if (_groupGlyphStyle == null)
            {
                _groupGlyphStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10
                };
                _groupGlyphStyle.normal.textColor = new Color(0.10f, 0.10f, 0.10f);
            }

            GUI.Label(cellRect, groupId == 1 ? "B" : "A", _groupGlyphStyle);
        }

        private void DrawStatus()
        {
            LevelDefinition def = _document.Working;
            EditorGUILayout.LabelField(
                $"尺寸 {def.width}x{def.height}   玩家 {_document.PlayerCount}   " +
                $"箱子 {_document.BoxCount}   目标 {_document.GoalCount}");

            List<LevelIssue> issues = _frameIssues;

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("关卡校验通过。", MessageType.Info);
                return;
            }

            // One clickable row per issue instead of one concatenated HelpBox: error rows are red,
            // warning rows are orange, and a row that references a cell navigates to it on click.
            foreach (LevelIssue issue in issues)
            {
                DrawIssueRow(issue);
            }
        }

        /// <summary>
        /// Draws a single clickable issue row. The message text is shown verbatim; a row bound to a
        /// cell appends a one-line "click to focus" hint so the navigation affordance is discoverable.
        /// </summary>
        private void DrawIssueRow(LevelIssue issue)
        {
            MessageType type = issue.Severity == LevelIssueSeverity.Error
                ? MessageType.Error
                : MessageType.Warning;

            string label = issue.HasCell
                ? issue.Message + "\n（点击聚焦格子 " + issue.CellX + "，" + issue.CellY + "）"
                : issue.Message;

            EditorGUILayout.HelpBox(label, type);

            if (!issue.HasCell)
            {
                return;
            }

            Rect rowRect = GUILayoutUtility.GetLastRect();
            Event current = Event.current;
            if (current.type == EventType.MouseUp && current.button == 0 &&
                rowRect.Contains(current.mousePosition))
            {
                SelectAndFocusCell(issue.CellX, issue.CellY);
                current.Use();
                Repaint();
            }
        }

        private void DrawGridLegend()
        {
            EditorGUILayout.LabelField(
                "图例：橙色=警告格，红色=错误格，青色=选中问题格",
                EditorStyles.miniLabel);
        }

        /// <summary>
        /// One-line status strip above the grid: source asset (or untitled), dirty/saved state, and
        /// the validation verdict. Reads the cached per-frame issues only.
        /// </summary>
        private void DrawStatusStrip()
        {
            string source = string.IsNullOrEmpty(_document.SourcePath)
                ? "未命名（未保存）"
                : _document.SourcePath;
            string dirty = _document.IsDirty ? "未保存修改" : "已保存";

            int errors = 0;
            int warnings = 0;
            for (int i = 0; i < _frameIssues.Count; i++)
            {
                if (_frameIssues[i].Severity == LevelIssueSeverity.Error)
                {
                    errors++;
                }
                else
                {
                    warnings++;
                }
            }

            string validation = errors == 0 && warnings == 0
                ? "校验通过"
                : $"{errors} 个错误，{warnings} 个警告";

            EditorGUILayout.LabelField($"{source}   {dirty}   {validation}", EditorStyles.miniLabel);
        }

        /// <summary>
        /// Selects a cell and requests a one-shot scroll to it, so the warning/error frame is visible.
        /// </summary>
        private void SelectAndFocusCell(int cellX, int cellY)
        {
            _hasSelectedCell = true;
            _selectedCellX = cellX;
            _selectedCellY = cellY;
            _scrollToSelectionPending = true;
            Repaint();
        }

        private void ClearSelectedCell()
        {
            _hasSelectedCell = false;
            _selectedCellX = -1;
            _selectedCellY = -1;
            _scrollToSelectionPending = false;
        }

        /// <summary>Refreshes the fields that mirror the working copy after an undo/redo step.</summary>
        private void AfterHistoryStep()
        {
            _nameField = _document.Working.levelName;
            SyncResizeFields();
            ClearSelectedCell();
            Repaint();
        }

        /// <summary>
        /// Ctrl+Z = undo, Ctrl+Y / Ctrl+Shift+Z = redo. Skipped while any control has keyboard focus
        /// (<see cref="GUIUtility.keyboardControl"/>), so typing in a text field is never interrupted.
        /// </summary>
        private void HandleUndoRedoShortcuts()
        {
            if (_document == null)
            {
                return;
            }

            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
            {
                return;
            }

            bool commandOrControl = current.control || current.command;
            if (!commandOrControl)
            {
                return;
            }

            if (current.keyCode == KeyCode.Z)
            {
                bool applied = current.shift ? _document.Redo() : _document.Undo();
                if (applied)
                {
                    AfterHistoryStep();
                }

                current.Use();
            }
            else if (current.keyCode == KeyCode.Y)
            {
                if (_document.Redo())
                {
                    AfterHistoryStep();
                }

                current.Use();
            }
        }

        /// <summary>
        /// Ctrl+S = Save, Ctrl+Enter = Playtest, using the same guard as
        /// <see cref="HandleUndoRedoShortcuts"/> (skipped while any control has keyboard focus).
        /// Ctrl+S invokes the same TrySave call as the Save button; Ctrl+Enter invokes the same
        /// Playtest call as the button and is suppressed while the cached per-frame issues contain a
        /// blocking error (mirroring the disabled Playtest button).
        /// </summary>
        private void HandleDocumentShortcuts()
        {
            if (_document == null || GUIUtility.keyboardControl != 0)
            {
                return;
            }

            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
            {
                return;
            }

            if (!current.control && !current.command)
            {
                return;
            }

            if (current.keyCode == KeyCode.S)
            {
                SaveCurrent();
                current.Use();
            }
            else if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            {
                // Same guard as the disabled Playtest button: do nothing (and do not consume the
                // event) while blocking errors are present.
                if (!HasBlockingErrors(_frameIssues))
                {
                    Playtest();
                    current.Use();
                }
            }
        }

        /// <summary>
        /// Advisory, editor-only solvability check. The button is always enabled and never touches
        /// Save/Playtest: it runs a bounded search over the working copy and shows the last verdict.
        /// A budget hit is reported as inconclusive, never as unsolvable.
        ///
        /// When the document has a <see cref="LevelEditorDocument.SourcePath"/>, an explicit
        /// "Compare with saved" button (never auto-run) additionally analyzes the saved asset once per
        /// click and reports the solution-metrics delta between the working copy and the saved asset.
        /// </summary>
        private void DrawAnalysisSection()
        {
            EditorGUILayout.LabelField("关卡分析", EditorStyles.boldLabel);

            if (GUILayout.Button("分析可解性"))
            {
                _lastAnalysis = LevelAnalyzer.Analyze(_document.Working);
                Repaint();
            }

            DrawWorkingAnalysisRow();

            if (!string.IsNullOrEmpty(_document.SourcePath))
            {
                if (GUILayout.Button("与已保存版本对比"))
                {
                    RunSavedComparison();
                    Repaint();
                }

                DrawComparisonRow();
            }
        }

        /// <summary>
        /// Shows the working-copy verdict from the last explicit "Analyze Solvability" run. Nothing is
        /// shown until that button has been clicked.
        /// </summary>
        private void DrawWorkingAnalysisRow()
        {
            AnalysisResult result = _lastAnalysis;
            if (result == null)
            {
                return;
            }

            double seconds = result.ElapsedSeconds;

            switch (result.Verdict)
            {
                case AnalysisVerdict.Solvable:
                    EditorGUILayout.HelpBox(
                        $"可解，{result.SolutionMoves} 步 / {result.SolutionPushes} 次推箱" +
                        $"（已搜索状态：{result.StatesExplored}，{seconds:0.###} 秒）",
                        MessageType.Info);
                    break;

                case AnalysisVerdict.Unsolvable:
                    EditorGUILayout.HelpBox(
                        "无解 - 已穷举状态空间（已搜索状态：" +
                        result.StatesExplored + "）",
                        MessageType.Warning);
                    break;

                default:
                    EditorGUILayout.HelpBox(
                        "未确定 - 已达搜索预算（已搜索状态：" +
                        result.StatesExplored + "，" + $"{seconds:0.###}" +
                        " 秒）；关卡可能仍可解",
                        MessageType.Warning);
                    break;
            }
        }

        /// <summary>
        /// Analyzes the saved asset behind <see cref="LevelEditorDocument.SourcePath"/> once and caches
        /// the result for this document's session. Explicit and on-demand only: it runs from the
        /// "Compare with saved" click and never from a repaint. The result is display-only - it never
        /// mutates the document, the saved asset, or <see cref="_lastAnalysis"/>.
        /// </summary>
        private void RunSavedComparison()
        {
            _savedAnalysis = null;
            _savedAnalysisUnavailable = false;

            LevelDefinition savedAsset = AssetDatabase.LoadAssetAtPath<LevelDefinition>(_document.SourcePath);
            if (savedAsset == null)
            {
                // Deleted or moved asset: say so honestly instead of showing metrics.
                _savedAnalysisUnavailable = true;
                return;
            }

            // Same budget as the working-copy "Analyze Solvability" call above (analyzer defaults).
            _savedAnalysis = LevelAnalyzer.Analyze(savedAsset);
        }

        /// <summary>
        /// Honest working-vs-saved solution-metrics row. It never fabricates numbers: an unanalyzed
        /// working copy or a non-Solvable side is described by verdict, and a signed delta is shown only
        /// when both sides are Solvable.
        /// </summary>
        private void DrawComparisonRow()
        {
            if (_savedAnalysisUnavailable)
            {
                EditorGUILayout.HelpBox(
                    "解法数据：已保存关卡不可用（可能已被删除或移动）。",
                    MessageType.Warning);
                return;
            }

            AnalysisResult saved = _savedAnalysis;
            if (saved == null)
            {
                // The button has not been clicked yet for this document: show nothing.
                return;
            }

            AnalysisResult working = _lastAnalysis;
            if (working == null)
            {
                EditorGUILayout.HelpBox(
                    "解法数据对比：当前副本尚未分析；已保存：" +
                    DescribeMetrics(saved),
                    MessageType.Info);
                return;
            }

            if (working.Verdict == AnalysisVerdict.Solvable && saved.Verdict == AnalysisVerdict.Solvable)
            {
                EditorGUILayout.HelpBox(
                    $"解法数据：当前 {working.SolutionMoves} 步/{working.SolutionPushes} 次推箱 vs " +
                    $"已保存 {saved.SolutionMoves} 步/{saved.SolutionPushes} 次推箱 " +
                    $"(Δ {FormatDelta(working.SolutionMoves - saved.SolutionMoves)} 步/" +
                    $"{FormatDelta(working.SolutionPushes - saved.SolutionPushes)} 次推箱)",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "解法数据：当前：" + DescribeMetrics(working) +
                "；已保存：" + DescribeMetrics(saved),
                MessageType.Warning);
        }

        /// <summary>
        /// One-line verdict for one side of the comparison. Move/push counts only exist for a Solvable
        /// result, so a non-Solvable side is named by verdict and never given arithmetic.
        /// </summary>
        private static string DescribeMetrics(AnalysisResult result)
        {
            switch (result.Verdict)
            {
                case AnalysisVerdict.Solvable:
                    return $"可解 {result.SolutionMoves} 步/{result.SolutionPushes} 次推箱";
                case AnalysisVerdict.Unsolvable:
                    return "无解";
                default:
                    return "未确定（预算）";
            }
        }

        /// <summary>
        /// Signed delta (working minus saved) rendered with a real minus sign (U+2212) for negatives.
        /// </summary>
        private static string FormatDelta(int delta)
        {
            if (delta < 0)
            {
                return "−" + (-delta);
            }

            return delta > 0 ? "+" + delta : "0";
        }

        /// <summary>
        /// Drops every cached analysis result (working-copy verdict and saved-asset comparison) when
        /// <c>_document</c> is replaced, mirroring <see cref="ClearSelectedCell"/>'s reset-on-document-
        /// change pattern. Resetting the working verdict too keeps the comparison honest: a stale
        /// working result must never be paired with a fresh saved result from a different document.
        /// </summary>
        private void ResetAnalysisState()
        {
            _lastAnalysis = null;
            _savedAnalysis = null;
            _savedAnalysisUnavailable = false;
        }

        /// <summary>True when the details contain at least one blocking error.</summary>
        private static bool HasBlockingErrors(List<LevelIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == LevelIssueSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when a warning issue references the given cell.</summary>
        private static bool HasWarningAt(List<LevelIssue> issues, int x, int y)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                LevelIssue issue = issues[i];
                if (issue.Severity == LevelIssueSeverity.Warning &&
                    issue.HasCell &&
                    issue.CellX == x &&
                    issue.CellY == y)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when a blocking error issue references the given cell.</summary>
        private static bool HasErrorAt(List<LevelIssue> issues, int x, int y)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                LevelIssue issue = issues[i];
                if (issue.Severity == LevelIssueSeverity.Error &&
                    issue.HasCell &&
                    issue.CellX == x &&
                    issue.CellY == y)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Draws a simple 3px frame in the given color around a cell.</summary>
        private static void DrawFrame(Rect cellRect, Color highlight)
        {
            const float thickness = 3f;

            EditorGUI.DrawRect(new Rect(cellRect.x, cellRect.y, cellRect.width, thickness), highlight);
            EditorGUI.DrawRect(new Rect(cellRect.x, cellRect.yMax - thickness, cellRect.width, thickness), highlight);
            EditorGUI.DrawRect(new Rect(cellRect.x, cellRect.y, thickness, cellRect.height), highlight);
            EditorGUI.DrawRect(new Rect(cellRect.xMax - thickness, cellRect.y, thickness, cellRect.height), highlight);
        }

        /// <summary>Draws a simple 3px orange frame around a warned cell.</summary>
        private static void DrawWarningFrame(Rect cellRect)
        {
            DrawFrame(cellRect, new Color(1f, 0.75f, 0.10f));
        }

        /// <summary>Draws a simple 3px red frame around a cell referenced by a blocking error.</summary>
        private static void DrawErrorFrame(Rect cellRect)
        {
            DrawFrame(cellRect, new Color(0.90f, 0.20f, 0.20f));
        }

        /// <summary>Draws a distinct cyan frame around the focused issue cell, inset 1px so it stays visible next to an error or warning frame.</summary>
        private static void DrawFocusFrame(Rect cellRect)
        {
            var inner = new Rect(cellRect.x + 1f, cellRect.y + 1f, cellRect.width - 2f, cellRect.height - 2f);
            DrawFrame(inner, new Color(0.15f, 0.95f, 1f));
        }

        private void LoadDocument(LevelDefinition source)
        {
            if (source == null || !ConfirmDiscardIfDirty())
            {
                return;
            }

            ApplyLoadedDocument(source);
        }

        /// <summary>Loads a copy of <paramref name="source"/> and resets every per-document cache.</summary>
        private void ApplyLoadedDocument(LevelDefinition source)
        {
            _document = LevelEditorDocument.LoadFrom(source);
            _nameField = _document.Working.levelName;
            SyncResizeFields();
            ClearSelectedCell();
            ResetAnalysisState();
        }

        /// <summary>
        /// Saves to the document's own asset. An untitled document (New, never saved) has no path of
        /// its own, so it always goes through the Save As dialog instead of reusing a path from an
        /// earlier document, which would silently overwrite that asset.
        /// </summary>
        private bool SaveCurrent()
        {
            return string.IsNullOrEmpty(_document.SourcePath)
                ? PromptSaveAs()
                : TrySave(_document.SourcePath);
        }

        /// <summary>Asks for a target path (the panel confirms before replacing a file) and saves there.</summary>
        private bool PromptSaveAs()
        {
            string suggestedName = string.IsNullOrEmpty(_document.Working.levelName)
                ? "NewLevel"
                : _document.Working.levelName.Replace(' ', '_');

            string path = EditorUtility.SaveFilePanelInProject(
                "关卡另存为",
                suggestedName,
                "asset",
                "选择关卡资产的保存位置",
                SaveFolder);

            return !string.IsNullOrEmpty(path) && TrySave(path);
        }

        private bool TrySave(string assetPath)
        {
            assetPath = NormalizeAssetPath(assetPath);

            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog(
                    "保存关卡", "未设置关卡路径。请使用“另存为”选择路径。", "确定");
                return false;
            }

            // Error-only pre-check (cheap, shares _document with the window's status display);
            // Playtest() re-checks the same level before entering play mode.
            var errors = _document.Validate();
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "无法保存", "保存前请修复以下问题：\n\n" + string.Join("\n", errors), "确定");
                return false;
            }

            try
            {
                if (!_document.SaveAs(assetPath))
                {
                    EditorUtility.DisplayDialog("保存关卡", "保存失败：" + assetPath, "确定");
                    return false;
                }
            }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("保存关卡", "保存失败：" + exception.Message, "确定");
                return false;
            }

            _nameField = _document.Working.levelName;
            return true;
        }

        /// <summary>
        /// One-click Playtest: validate, confirm-save unsaved/untitled edits, arm the persisted asset
        /// through <see cref="PlaytestLauncher"/>, then load the gameplay scene and enter play mode.
        /// </summary>
        private void Playtest()
        {
            // Error-only pre-check; PlaytestLauncher.TryPreparePlaytest re-validates the persisted
            // level asset as the authoritative guard, and the Playtest button is disabled while the
            // window sees blocking errors.
            var errors = LevelValidator.Validate(_document.Working);
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "无法试玩",
                    "试玩前请修复以下问题：\n\n" + string.Join("\n", errors),
                    "确定");
                return;
            }

            if (_document.IsDirty || string.IsNullOrEmpty(_document.SourcePath))
            {
                bool saveFirst = EditorUtility.DisplayDialog(
                    "试玩前保存",
                    "试玩运行的是已保存的关卡。是否先保存当前关卡？",
                    "保存",
                    "取消");

                if (!saveFirst || !SaveCurrent())
                {
                    return;
                }
            }

            if (!PlaytestLauncher.TryPreparePlaytest(_document, out string error))
            {
                EditorUtility.DisplayDialog("无法试玩", error, "确定");
                return;
            }

            EnterPlaytestScene();
        }

        /// <summary>Appends the ".asset" suffix when a Save As path is missing it.</summary>
        private static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return assetPath;
            }

            if (!assetPath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            {
                assetPath += ".asset";
            }

            return assetPath;
        }

        /// <summary>
        /// Guards a destructive action (New/Load) when the working copy has unsaved edits.
        /// Returns true when it is safe to proceed.
        /// </summary>
        private bool ConfirmDiscardIfDirty()
        {
            if (_document == null || !_document.IsDirty)
            {
                return true;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "未保存的更改",
                "当前关卡有未保存的更改。",
                "保存",
                "放弃",
                "取消");

            switch (choice)
            {
                case 0:
                    return SaveCurrent();
                case 1:
                    return true;
                default:
                    return false;
            }
        }

        private void SyncResizeFields()
        {
            if (_document == null)
            {
                return;
            }

            _resizeWidth = _document.Working.width;
            _resizeHeight = _document.Working.height;
        }

        private void UpdateTitle()
        {
            if (_document == null)
            {
                return;
            }

            bool dirty = _document.IsDirty;

            string baseName = _document.SourcePath != null
                ? System.IO.Path.GetFileNameWithoutExtension(_document.SourcePath)
                : _document.Working.levelName;

            // "*" marks unsaved edits in the tab, and hasUnsavedChanges additionally drives Unity's
            // native Save / Don't Save / Cancel close prompt.
            titleContent = new GUIContent(dirty ? baseName + "*" : baseName);
            hasUnsavedChanges = dirty;
            saveChangesMessage = "关卡有未保存的更改。关闭前是否保存？";
        }

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

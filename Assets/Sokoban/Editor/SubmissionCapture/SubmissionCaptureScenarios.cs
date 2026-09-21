using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sokoban.Editor
{
    /// <summary>
    /// The runtime screenshot directive the editor bridge writes for the runtime driver (a separate,
    /// later task that lives in the runtime assembly). <see cref="replayCount"/> is -1 for "replay all
    /// moves" or a non-negative count for a mid-state; <see cref="markCompleted"/> is true only for
    /// RuntimeComplete and <see cref="settleSeconds"/> is how long the driver should let the view
    /// settle after the replay before capturing.
    /// </summary>
    [Serializable]
    public class RuntimeScenarioDirective
    {
        public string scenario;
        public string outputPath;
        public string moves;
        public int replayCount;
        public bool markCompleted;
        public float settleSeconds;
    }

    /// <summary>The editor-window capture request the bridge writes for the PowerShell/Win32 capturer.</summary>
    [Serializable]
    public class EditorCaptureRequest
    {
        public string scenario;
        public string windowTitle;
        public string outputPath;
    }

    /// <summary>Done marker the runtime driver writes for <c>runtime-scenario.json</c>.</summary>
    [Serializable]
    public class RuntimeDoneMarker
    {
        public string scenario;
        public bool ok;
        public string error;
    }

    /// <summary>Done marker the PowerShell orchestrator writes for <c>editor-capture-request.json</c>.</summary>
    [Serializable]
    public class EditorCaptureDoneMarker
    {
        public string scenario;
        public bool ok;
        public string method;
        public string outputPath;
    }

    /// <summary>
    /// Pure, editor-only helpers behind the fully automated submission capture pipeline: protocol
    /// paths and DTOs, the scenario -&gt; stable filename / window-title maps, the shipped level
    /// picker and the two in-memory capture fixtures. Everything here is deterministic and free of
    /// window or play-mode side effects, so the whole file protocol is unit-testable in EditMode; the
    /// state machine that drives it lives in <see cref="SubmissionCaptureService"/>.
    /// </summary>
    public static class SubmissionCaptureScenarios
    {
        // Scenario ids (must match the orchestrator's names and the required PNG list).
        public const string RuntimeMainMenu = "RuntimeMainMenu";
        public const string RuntimeLevelSelect = "RuntimeLevelSelect";
        public const string RuntimeGameplay = "RuntimeGameplay";
        public const string RuntimeMultiGroup = "RuntimeMultiGroup";
        public const string RuntimeComplete = "RuntimeComplete";

        public const string EditorDashboard = "EditorDashboard";
        public const string EditorLevelEditor = "EditorLevelEditor";
        public const string EditorValidation = "EditorValidation";
        public const string EditorAnalyzer = "EditorAnalyzer";
        public const string EditorSolutionPreview = "EditorSolutionPreview";

        /// <summary>Project-root-relative directory all protocol files live in (created on demand).</summary>
        public const string TempCaptureFolder = "Temp/AgentCapture";

        /// <summary>Project-root-relative directory the final PNGs are written to.</summary>
        public const string OutputFolder = "Docs/Screenshots";

        /// <summary>Default settle delay (seconds) before a prepared editor window is handed to the capturer.</summary>
        public const float DefaultSettleSeconds = 0.6f;

        /// <summary>
        /// Persistent asset used to arm the RuntimeMultiGroup fixture through
        /// <see cref="PlaytestLauncher.Arm"/> (a non-persisted instance cannot survive the Play Mode
        /// domain reload). It is created on demand and deleted as soon as the scenario is done; it is
        /// never added to the catalog.
        /// </summary>
        public const string MultiGroupTempAssetPath = "Assets/Sokoban/Levels/TempMultiGroupCapture.asset";

        // Protocol file names. request.json is written by the orchestrator (read-only for us).
        public const string RequestFileName = "request.json";
        public const string ResultFileName = "result.json";
        public const string BridgeReadyFileName = "bridge-ready.json";
        public const string RuntimeScenarioFileName = "runtime-scenario.json";
        public const string RuntimeDoneFileName = "runtime-done.json";
        public const string EditorCaptureRequestFileName = "editor-capture-request.json";
        public const string EditorCaptureDoneFileName = "editor-capture-done.json";
        public const string LogFileName = "log.txt";

        /// <summary>Runtime scenarios, in the order the shipped run uses.</summary>
        public static readonly string[] RuntimeScenarioIds =
        {
            RuntimeMainMenu,
            RuntimeLevelSelect,
            RuntimeGameplay,
            RuntimeMultiGroup,
            RuntimeComplete
        };

        /// <summary>Editor-window scenarios, in the order the shipped run uses.</summary>
        public static readonly string[] EditorScenarioIds =
        {
            EditorDashboard,
            EditorLevelEditor,
            EditorValidation,
            EditorAnalyzer,
            EditorSolutionPreview
        };

        /// <summary>Absolute project root (the parent of <c>Assets</c>).</summary>
        public static string ProjectRoot =>
            Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory();

        /// <summary>Absolute path of the temp capture directory.</summary>
        public static string TempCaptureDirectory =>
            Path.GetFullPath(Path.Combine(ProjectRoot, TempCaptureFolder));

        /// <summary>Absolute path of the output PNG directory.</summary>
        public static string OutputDirectory =>
            Path.GetFullPath(Path.Combine(ProjectRoot, OutputFolder));

        /// <summary>Absolute path of one protocol file inside the temp capture directory.</summary>
        public static string ProtocolPath(string fileName)
        {
            return Path.Combine(TempCaptureDirectory, fileName);
        }

        /// <summary>True when <paramref name="scenario"/> is driven by the runtime screenshot driver.</summary>
        public static bool IsRuntimeScenario(string scenario)
        {
            return Array.IndexOf(RuntimeScenarioIds, scenario) >= 0;
        }

        /// <summary>True when <paramref name="scenario"/> is an editor-window (Win32) capture.</summary>
        public static bool IsEditorScenario(string scenario)
        {
            return Array.IndexOf(EditorScenarioIds, scenario) >= 0;
        }

        /// <summary>True when <paramref name="scenario"/> is any known scenario id.</summary>
        public static bool IsKnownScenario(string scenario)
        {
            return IsRuntimeScenario(scenario) || IsEditorScenario(scenario);
        }

        /// <summary>
        /// Stable output filename for a scenario. The listed names are the contract in
        /// FULL_AUTO_CAPTURE_PLAN_REVISED.md §5 (note "runtime-multigroup.png" is deliberately one
        /// word). An unknown scenario falls back to its lower-cased id so a mapping is never null.
        /// </summary>
        public static string StableFileName(string scenario)
        {
            switch (scenario)
            {
                case RuntimeMainMenu: return "runtime-main-menu.png";
                case RuntimeLevelSelect: return "runtime-level-select.png";
                case RuntimeGameplay: return "runtime-gameplay.png";
                case RuntimeMultiGroup: return "runtime-multigroup.png";
                case RuntimeComplete: return "runtime-complete.png";
                case EditorDashboard: return "editor-dashboard.png";
                case EditorLevelEditor: return "editor-level-editor.png";
                case EditorValidation: return "editor-validation.png";
                case EditorAnalyzer: return "editor-analyzer.png";
                case EditorSolutionPreview: return "editor-solution-preview.png";
                default: return (scenario ?? "unknown").ToLowerInvariant() + ".png";
            }
        }

        /// <summary>Absolute output PNG path for a scenario.</summary>
        public static string OutputPath(string scenario)
        {
            return Path.Combine(OutputDirectory, StableFileName(scenario));
        }

        /// <summary>
        /// Unique capture window title for an editor scenario. The capturer finds the HWND by this
        /// exact title, so it must stay unique and table-stable.
        /// </summary>
        public static string WindowTitle(string scenario)
        {
            switch (scenario)
            {
                case EditorDashboard: return "[CAPTURE] Sokoban 内容总览";
                case EditorLevelEditor: return "[CAPTURE] Sokoban 关卡编辑器";
                case EditorValidation: return "[CAPTURE] Sokoban 关卡校验";
                case EditorAnalyzer: return "[CAPTURE] Sokoban 关卡分析";
                case EditorSolutionPreview: return "[CAPTURE] Sokoban 解法预览";
                default: return "[CAPTURE] Sokoban " + scenario;
            }
        }

        /// <summary>Comma-joined enum names of an analyzer solution (e.g. "Up,Right,Down").</summary>
        public static string SerializeMoves(IList<Direction> solution)
        {
            if (solution == null || solution.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(",", solution);
        }

        /// <summary>Builds the runtime directive JSON (see <see cref="RuntimeScenarioDirective"/>).</summary>
        public static string BuildRuntimeDirectiveJson(
            string scenario,
            string outputPath,
            string moves,
            int replayCount,
            bool markCompleted,
            float settleSeconds)
        {
            var directive = new RuntimeScenarioDirective
            {
                scenario = scenario,
                outputPath = outputPath,
                moves = moves ?? string.Empty,
                replayCount = replayCount,
                markCompleted = markCompleted,
                settleSeconds = settleSeconds
            };

            return JsonUtility.ToJson(directive);
        }

        /// <summary>Parses a runtime directive JSON written by <see cref="BuildRuntimeDirectiveJson"/>.</summary>
        public static RuntimeScenarioDirective ParseRuntimeDirective(string json)
        {
            return JsonUtility.FromJson<RuntimeScenarioDirective>(json);
        }

        /// <summary>Builds the editor capture request JSON (see <see cref="EditorCaptureRequest"/>).</summary>
        public static string BuildEditorCaptureRequestJson(string scenario, string windowTitle, string outputPath)
        {
            var request = new EditorCaptureRequest
            {
                scenario = scenario,
                windowTitle = windowTitle,
                outputPath = outputPath
            };

            return JsonUtility.ToJson(request);
        }

        /// <summary>Parses an editor capture request JSON.</summary>
        public static EditorCaptureRequest ParseEditorCaptureRequest(string json)
        {
            return JsonUtility.FromJson<EditorCaptureRequest>(json);
        }

        /// <summary>Parses a runtime done marker JSON.</summary>
        public static RuntimeDoneMarker ParseRuntimeDone(string json)
        {
            return JsonUtility.FromJson<RuntimeDoneMarker>(json);
        }

        /// <summary>Parses an editor capture done marker JSON.</summary>
        public static EditorCaptureDoneMarker ParseEditorCaptureDone(string json)
        {
            return JsonUtility.FromJson<EditorCaptureDoneMarker>(json);
        }

        /// <summary>
        /// Picks the shipped catalog slot used by RuntimeGameplay/RuntimeComplete and the editor
        /// analyzer/preview scenarios. Preferred slot is index 6 (Level07) when it exists; otherwise
        /// the latest slot that uses both pressure plates and doors (visual interest), otherwise index
        /// 6 clamped into range, otherwise -1 for an empty/absent catalog.
        /// </summary>
        public static int SelectCaptureLevelIndex(LevelCatalog catalog)
        {
            if (catalog == null || catalog.levels == null || catalog.levels.Count == 0)
            {
                return -1;
            }

            int preferred = Mathf.Clamp(6, 0, catalog.levels.Count - 1);
            if (HasPlatesAndDoors(catalog.levels[preferred]))
            {
                return preferred;
            }

            for (int i = catalog.levels.Count - 1; i >= 0; i--)
            {
                if (HasPlatesAndDoors(catalog.levels[i]))
                {
                    return i;
                }
            }

            return preferred;
        }

        /// <summary>True when the level's tile layer contains both a pressure plate and a door.</summary>
        public static bool HasPlatesAndDoors(LevelDefinition level)
        {
            bool plates = false;
            bool doors = false;

            if (level != null && level.cells != null)
            {
                for (int i = 0; i < level.cells.Count; i++)
                {
                    if (level.cells[i] == TileType.Plate)
                    {
                        plates = true;
                    }
                    else if (level.cells[i] == TileType.Door)
                    {
                        doors = true;
                    }

                    if (plates && doors)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The RuntimeMultiGroup capture fixture: a 9x7, two-group plate/door level with a box and a
        /// goal per group (2 boxes, 2 goals). Pushing each box right walks it over its group plate and
        /// through its group door onto its goal, so the level is solvable and both groups light up.
        /// Built in memory; the bridge persists a copy only for the time it is armed.
        /// </summary>
        public static LevelDefinition BuildMultiGroupCaptureLevel()
        {
            string[] rows =
            {
                "#########",
                "#.......#",
                "#.PBTDG.#",
                "#.......#",
                "#..BUEG.#",
                "#.......#",
                "#########"
            };

            return BuildCaptureLevel("CaptureMultiGroup", rows);
        }

        /// <summary>
        /// The EditorValidation capture fixture: a valid 7x6 level whose first box sits in a static
        /// corner (off its goal), so <see cref="LevelValidator.ValidateDetailed"/> reports exactly the
        /// corner-deadlock <see cref="LevelIssueSeverity.Warning"/> at (1, 1) and no blocking error.
        /// Built in memory and never saved.
        /// </summary>
        public static LevelDefinition BuildValidationCaptureLevel()
        {
            string[] rows =
            {
                "#######",
                "#B.G..#",
                "#.....#",
                "#..B.G#",
                "#..P..#",
                "#######"
            };

            return BuildCaptureLevel("CaptureValidation", rows);
        }

        /// <summary>
        /// First cell-bound warning from <see cref="LevelValidator.ValidateDetailed"/>, or false when
        /// the level has none. Used to focus the offending cell in the validation capture.
        /// </summary>
        public static bool TryGetFirstWarningCell(LevelDefinition level, out int cellX, out int cellY)
        {
            cellX = -1;
            cellY = -1;

            List<LevelIssue> issues = LevelValidator.ValidateDetailed(level);
            foreach (LevelIssue issue in issues)
            {
                if (issue.Severity == LevelIssueSeverity.Warning && issue.HasCell)
                {
                    cellX = issue.CellX;
                    cellY = issue.CellY;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds a dense <see cref="LevelDefinition"/> from ASCII rows (row 0 = top). Extends the
        /// factory character map with explicit group variants (T/U = plate group A/B, D/E = door group
        /// A/B) so the multi-group fixture can be authored without a YAML asset.
        /// </summary>
        public static LevelDefinition BuildCaptureLevel(string levelName, string[] rows)
        {
            if (rows == null || rows.Length == 0)
            {
                throw new ArgumentException("A capture level needs at least one row.", nameof(rows));
            }

            int width = rows[0].Length;
            int height = rows.Length;

            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = levelName;
            def.levelName = levelName;
            def.width = width;
            def.height = height;
            def.cells = new List<TileType>(width * height);
            def.occupants = new List<OccupantType>(width * height);
            def.groupIds = new List<int>(width * height);

            for (int y = 0; y < height; y++)
            {
                string row = rows[y];
                if (row.Length != width)
                {
                    throw new ArgumentException($"Row {y} is {row.Length} wide, expected {width}.", nameof(rows));
                }

                for (int x = 0; x < width; x++)
                {
                    TileType tile;
                    OccupantType occupant;
                    int groupId = 0;

                    switch (row[x])
                    {
                        case '#': tile = TileType.Wall; occupant = OccupantType.None; break;
                        case '.': tile = TileType.Floor; occupant = OccupantType.None; break;
                        case 'G': tile = TileType.Goal; occupant = OccupantType.None; break;
                        case 'P': tile = TileType.Floor; occupant = OccupantType.Player; break;
                        case 'B': tile = TileType.Floor; occupant = OccupantType.Box; break;
                        case 'T': tile = TileType.Plate; occupant = OccupantType.None; groupId = 0; break;
                        case 'U': tile = TileType.Plate; occupant = OccupantType.None; groupId = 1; break;
                        case 'D': tile = TileType.Door; occupant = OccupantType.None; groupId = 0; break;
                        case 'E': tile = TileType.Door; occupant = OccupantType.None; groupId = 1; break;
                        default:
                            throw new ArgumentException(
                                $"Unknown capture level character '{row[x]}' at ({x}, {y}).", nameof(rows));
                    }

                    def.cells.Add(tile);
                    def.occupants.Add(occupant);
                    def.groupIds.Add(groupId);
                }
            }

            return def;
        }
    }
}

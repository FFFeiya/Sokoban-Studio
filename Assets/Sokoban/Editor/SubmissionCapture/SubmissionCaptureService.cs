using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.Editor
{
    /// <summary>
    /// Editor-only bridge of the fully automated submission capture pipeline (see
    /// FULL_AUTO_CAPTURE_PLAN_REVISED.md, C2). It is loaded on every domain reload, reads the
    /// orchestrator's <c>Temp/AgentCapture/request.json</c>, and drives the requested scenarios one at
    /// a time from <see cref="EditorApplication.update"/> (never blocking with Thread.Sleep):
    ///
    /// <list type="bullet">
    /// <item>runtime scenarios write the runtime directive, arm the level and enter play mode, then
    /// poll for the runtime driver's done marker;</item>
    /// <item>editor scenarios open/float the target window, hand a capture request to the PowerShell
    /// Win32 capturer and poll for its done marker.</item>
    /// </list>
    ///
    /// All in-progress state lives in <c>Temp/AgentCapture/bridge-state.json</c> (read back after the
    /// Play Mode domain reload), so the bridge is re-entrant: entering or leaving play mode never
    /// restarts a scenario that is already in flight. It never references the runtime screenshot
    /// driver (a separate task) — the two sides only share JSON files.
    /// </summary>
    [InitializeOnLoad]
    public static class SubmissionCaptureService
    {
        private const double PollIntervalSeconds = 0.5;
        private const double ScenarioTimeoutSeconds = 120.0;

        private const string PhaseIdle = "Idle";
        private const string PhaseSettling = "Settling";
        private const string PhaseAwaitingRuntime = "AwaitingRuntime";
        private const string PhaseAwaitingEditor = "AwaitingEditor";
        private const string PhaseComplete = "Complete";

        private const string StateFileName = "bridge-state.json";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Persisted in-progress state; re-read from disk after every domain reload.</summary>
        [Serializable]
        private class BridgeState
        {
            public string requestId;
            public string phase = PhaseIdle;
            public string scenario;
            public long deadlineTicks;
            public string pendingDeleteAssetPath;
            public List<string> completedScenarios = new List<string>();
            public List<string> failedScenarios = new List<string>();
            public List<CaptureOutput> outputs = new List<CaptureOutput>();
        }

        private static BridgeState _state;
        private static SubmissionCaptureRequest _request;
        private static double _nextPollTime;

        static SubmissionCaptureService()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            Directory.CreateDirectory(SubmissionCaptureScenarios.TempCaptureDirectory);
            _state = LoadState();
            LoadRequest();
            WriteBridgeReady();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Resume promptly after Enter/Exit Play Mode instead of waiting out the poll interval.
            _nextPollTime = 0;
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPollTime)
            {
                return;
            }

            _nextPollTime = now + PollIntervalSeconds;

            TryRunDeferredCleanup();

            LoadRequest();
            if (_request == null || _request.scenarios == null || _request.scenarios.Length == 0)
            {
                return;
            }

            EnsureStateForRequest();
            if (_state.phase == PhaseComplete)
            {
                return;
            }

            Step();
        }

        private static void Step()
        {
            switch (_state.phase)
            {
                case PhaseIdle:
                    StartNextScenario();
                    break;

                case PhaseSettling:
                    if (DateTime.UtcNow.Ticks >= _state.deadlineTicks)
                    {
                        WriteEditorCaptureRequest();
                    }
                    break;

                case PhaseAwaitingRuntime:
                    PollRuntime();
                    break;

                case PhaseAwaitingEditor:
                    PollEditor();
                    break;
            }
        }

        private static void StartNextScenario()
        {
            // A runtime scenario enters play mode; never begin one while Unity is still leaving it.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            int index = NextPendingIndex();
            if (index < 0)
            {
                FinalizeResult();
                return;
            }

            string scenario = _request.scenarios[index];
            _state.scenario = scenario;

            if (!SubmissionCaptureScenarios.IsKnownScenario(scenario))
            {
                FailScenario(scenario, "unknown scenario id");
                return;
            }

            string outputPath = SubmissionCaptureScenarios.OutputPath(scenario);
            if (!_request.overwrite && File.Exists(outputPath))
            {
                // Adopt the existing PNG instead of re-capturing; the PNG QA owns the verdict.
                _state.outputs.Add(new CaptureOutput
                {
                    scenario = scenario,
                    path = outputPath,
                    captureMethod = "existing",
                    width = 0,
                    height = 0,
                    validation = "pending"
                });
                _state.completedScenarios.Add(scenario);
                AppendLog("kept existing output: " + scenario);
                AdvanceToNext();
                return;
            }

            try
            {
                if (SubmissionCaptureScenarios.IsRuntimeScenario(scenario))
                {
                    PrepareRuntimeScenario(scenario);
                }
                else
                {
                    PrepareEditorScenario(scenario);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SubmissionCapture] " + scenario + " preparation failed: " + exception);
                FailScenario(scenario, exception.Message);
            }
        }

        /// <summary>True when the bridge has a request loaded (raw request file present and valid).</summary>
        public static bool HasRequest => _request != null;

        // ----- runtime scenarios -----------------------------------------------------------------

        private static void PrepareRuntimeScenario(string scenario)
        {
            PlaytestRequestJanitor.Clear();

            string moves = string.Empty;
            int replayCount = 0;
            bool markCompleted = false;

            switch (scenario)
            {
                case SubmissionCaptureScenarios.RuntimeMainMenu:
                    EditorSceneManager.OpenScene(LevelAssetFactory.MainMenuScenePath, OpenSceneMode.Single);
                    break;

                case SubmissionCaptureScenarios.RuntimeLevelSelect:
                    EditorSceneManager.OpenScene(LevelAssetFactory.LevelSelectScenePath, OpenSceneMode.Single);
                    break;

                case SubmissionCaptureScenarios.RuntimeGameplay:
                {
                    LevelDefinition level = LoadCaptureLevel();
                    AnalysisResult result = LevelAnalyzer.Analyze(
                        level, CatalogAudit.AuditMaxStates, CatalogAudit.AuditMaxTime);
                    RequireSolvable(scenario, result);

                    PlaytestLauncher.Arm(level);
                    EditorSceneManager.OpenScene(LevelAssetFactory.GameplayScenePath, OpenSceneMode.Single);

                    moves = SubmissionCaptureScenarios.SerializeMoves(result.Solution);
                    replayCount = MidReplayCount(result.Solution, 4);
                    break;
                }

                case SubmissionCaptureScenarios.RuntimeMultiGroup:
                {
                    // A non-persisted instance cannot survive the Play Mode domain reload, so the
                    // fixture is written to a temp asset, armed, and deleted once the scenario is done.
                    LevelDefinition fixture = PersistMultiGroupLevel();
                    _state.pendingDeleteAssetPath = SubmissionCaptureScenarios.MultiGroupTempAssetPath;

                    AnalysisResult result = LevelAnalyzer.Analyze(
                        fixture, CatalogAudit.AuditMaxStates, CatalogAudit.AuditMaxTime);
                    if (result.Verdict == AnalysisVerdict.Solvable)
                    {
                        moves = SubmissionCaptureScenarios.SerializeMoves(result.Solution);
                        replayCount = MidReplayCount(result.Solution, 3);
                    }

                    PlaytestLauncher.Arm(fixture);
                    EditorSceneManager.OpenScene(LevelAssetFactory.GameplayScenePath, OpenSceneMode.Single);
                    break;
                }

                case SubmissionCaptureScenarios.RuntimeComplete:
                {
                    LevelDefinition level = LoadCaptureLevel();
                    AnalysisResult result = LevelAnalyzer.Analyze(
                        level, CatalogAudit.AuditMaxStates, CatalogAudit.AuditMaxTime);
                    RequireSolvable(scenario, result);

                    PlaytestLauncher.Arm(level);
                    EditorSceneManager.OpenScene(LevelAssetFactory.GameplayScenePath, OpenSceneMode.Single);

                    moves = SubmissionCaptureScenarios.SerializeMoves(result.Solution);
                    replayCount = -1;
                    markCompleted = true;
                    break;
                }
            }

            DeleteProtocolFile(SubmissionCaptureScenarios.RuntimeDoneFileName);

            WriteProtocolFile(
                SubmissionCaptureScenarios.RuntimeScenarioFileName,
                SubmissionCaptureScenarios.BuildRuntimeDirectiveJson(
                    scenario,
                    SubmissionCaptureScenarios.OutputPath(scenario),
                    moves,
                    replayCount,
                    markCompleted,
                    SubmissionCaptureScenarios.DefaultSettleSeconds));

            _state.phase = PhaseAwaitingRuntime;
            _state.deadlineTicks = DateTime.UtcNow.AddSeconds(ScenarioTimeoutSeconds).Ticks;
            SaveState();

            EditorApplication.EnterPlaymode();
        }

        private static void PollRuntime()
        {
            string scenario = _state.scenario;
            RuntimeDoneMarker done = TryReadMarker<RuntimeDoneMarker>(
                SubmissionCaptureScenarios.RuntimeDoneFileName);

            if (done != null && done.scenario == scenario)
            {
                EditorApplication.ExitPlaymode();
                if (done.ok)
                {
                    CompleteRuntimeScenario(scenario);
                }
                else
                {
                    FailScenario(
                        scenario,
                        string.IsNullOrEmpty(done.error) ? "runtime driver reported failure" : done.error);
                }

                return;
            }

            if (DateTime.UtcNow.Ticks >= _state.deadlineTicks)
            {
                EditorApplication.ExitPlaymode();
                FailScenario(scenario, "timeout waiting for runtime-done.json");
            }
        }

        private static void CompleteRuntimeScenario(string scenario)
        {
            CaptureResolution resolution = _request.runtimeResolution;
            _state.outputs.Add(new CaptureOutput
            {
                scenario = scenario,
                path = SubmissionCaptureScenarios.OutputPath(scenario),
                captureMethod = "UnityScreenCapture",
                width = resolution != null ? resolution.width : 0,
                height = resolution != null ? resolution.height : 0,
                validation = "pending"
            });
            _state.completedScenarios.Add(scenario);
            AppendLog("captured runtime scenario: " + scenario);
            AdvanceToNext();
        }

        // ----- editor scenarios ------------------------------------------------------------------

        private static void PrepareEditorScenario(string scenario)
        {
            DeleteProtocolFile(SubmissionCaptureScenarios.EditorCaptureDoneFileName);
            PrepareEditorWindow(scenario);
            _state.phase = PhaseSettling;
            _state.deadlineTicks = DateTime.UtcNow.AddSeconds(SubmissionCaptureScenarios.DefaultSettleSeconds).Ticks;
            SaveState();
        }

        private static void PrepareEditorWindow(string scenario)
        {
            string title = SubmissionCaptureScenarios.WindowTitle(scenario);

            switch (scenario)
            {
                case SubmissionCaptureScenarios.EditorDashboard:
                {
                    ContentDashboardWindow.Open();
                    ContentDashboardWindow window = EditorWindow.GetWindow<ContentDashboardWindow>();
                    window.RunAnalyzeAll();
                    PositionWindow(window, title, new Rect(80f, 80f, 1180f, 760f));
                    break;
                }

                case SubmissionCaptureScenarios.EditorLevelEditor:
                {
                    LevelEditorWindow window = LevelEditorWindow.LoadForCapture(LoadCaptureLevel());
                    PositionWindow(window, title, new Rect(80f, 80f, 980f, 780f));
                    break;
                }

                case SubmissionCaptureScenarios.EditorValidation:
                {
                    LevelDefinition fixture = SubmissionCaptureScenarios.BuildValidationCaptureLevel();
                    LevelEditorWindow window = LevelEditorWindow.LoadForCapture(fixture);
                    if (SubmissionCaptureScenarios.TryGetFirstWarningCell(fixture, out int cellX, out int cellY))
                    {
                        window.SelectAndFocusCell(cellX, cellY);
                    }

                    // The window owns a detached clone; the fixture itself is never needed again.
                    UnityEngine.Object.DestroyImmediate(fixture);
                    PositionWindow(window, title, new Rect(80f, 80f, 980f, 780f));
                    break;
                }

                case SubmissionCaptureScenarios.EditorAnalyzer:
                {
                    LevelDefinition level = LoadCaptureLevel();
                    LevelEditorWindow window = LevelEditorWindow.LoadForCapture(level);
                    window.InjectAnalysis(LevelAnalyzer.Analyze(
                        level, CatalogAudit.AuditMaxStates, CatalogAudit.AuditMaxTime));
                    PositionWindow(window, title, new Rect(80f, 80f, 980f, 780f));
                    break;
                }

                case SubmissionCaptureScenarios.EditorSolutionPreview:
                {
                    LevelDefinition level = LoadCaptureLevel();
                    AnalysisResult result = LevelAnalyzer.Analyze(
                        level, CatalogAudit.AuditMaxStates, CatalogAudit.AuditMaxTime);

                    SolutionPreviewWindow.OpenFor(level, result);
                    SolutionPreviewWindow window = EditorWindow.GetWindow<SolutionPreviewWindow>();
                    window.JumpToStep(MidReplayCount(result.Solution, 4));
                    PositionWindow(window, title, new Rect(80f, 80f, 760f, 700f));
                    break;
                }
            }
        }

        private static void WriteEditorCaptureRequest()
        {
            string scenario = _state.scenario;
            WriteProtocolFile(
                SubmissionCaptureScenarios.EditorCaptureRequestFileName,
                SubmissionCaptureScenarios.BuildEditorCaptureRequestJson(
                    scenario,
                    SubmissionCaptureScenarios.WindowTitle(scenario),
                    SubmissionCaptureScenarios.OutputPath(scenario)));

            _state.phase = PhaseAwaitingEditor;
            _state.deadlineTicks = DateTime.UtcNow.AddSeconds(ScenarioTimeoutSeconds).Ticks;
            SaveState();
        }

        private static void PollEditor()
        {
            string scenario = _state.scenario;
            EditorCaptureDoneMarker done = TryReadMarker<EditorCaptureDoneMarker>(
                SubmissionCaptureScenarios.EditorCaptureDoneFileName);

            if (done != null && done.scenario == scenario)
            {
                CloseCaptureWindow(scenario);
                if (done.ok)
                {
                    _state.outputs.Add(new CaptureOutput
                    {
                        scenario = scenario,
                        path = string.IsNullOrEmpty(done.outputPath)
                            ? SubmissionCaptureScenarios.OutputPath(scenario)
                            : done.outputPath,
                        captureMethod = string.IsNullOrEmpty(done.method) ? "PrintWindow" : done.method,
                        width = 0,
                        height = 0,
                        validation = "pending"
                    });
                    _state.completedScenarios.Add(scenario);
                    AppendLog("captured editor scenario: " + scenario + " [" + done.method + "]");
                    AdvanceToNext();
                }
                else
                {
                    FailScenario(scenario, "orchestrator reported the editor capture failed");
                }

                return;
            }

            if (DateTime.UtcNow.Ticks >= _state.deadlineTicks)
            {
                CloseCaptureWindow(scenario);
                FailScenario(scenario, "timeout waiting for editor-capture-done.json");
            }
        }

        private static void CloseCaptureWindow(string scenario)
        {
            switch (scenario)
            {
                case SubmissionCaptureScenarios.EditorDashboard:
                    CloseWindow<ContentDashboardWindow>();
                    break;

                case SubmissionCaptureScenarios.EditorLevelEditor:
                case SubmissionCaptureScenarios.EditorValidation:
                case SubmissionCaptureScenarios.EditorAnalyzer:
                    CloseWindow<LevelEditorWindow>();
                    break;

                case SubmissionCaptureScenarios.EditorSolutionPreview:
                    CloseWindow<SolutionPreviewWindow>();
                    break;
            }
        }

        private static void CloseWindow<T>() where T : EditorWindow
        {
            T[] windows = Resources.FindObjectsOfTypeAll<T>();
            if (windows == null)
            {
                return;
            }

            foreach (T window in windows)
            {
                if (window != null)
                {
                    window.Close();
                }
            }
        }

        private static void PositionWindow(EditorWindow window, string title, Rect rect)
        {
            if (window == null)
            {
                return;
            }

            window.Show();
            window.position = rect;
            ApplyCaptureTitle(window, title);
            window.Focus();
            window.Repaint();
        }

        private static void ApplyCaptureTitle(EditorWindow window, string title)
        {
            if (window is LevelEditorWindow levelWindow)
            {
                // LevelEditorWindow rewrites its own title every OnGUI pass, so it needs the capture
                // override for the [CAPTURE] title to survive until the Win32 capturer reads it.
                levelWindow.SetCaptureTitle(title);
            }
            else
            {
                window.titleContent = new GUIContent(title);
            }
        }

        // ----- shared helpers --------------------------------------------------------------------

        private static int NextPendingIndex()
        {
            for (int i = 0; i < _request.scenarios.Length; i++)
            {
                string scenario = _request.scenarios[i];
                if (string.IsNullOrEmpty(scenario))
                {
                    continue;
                }

                if (_state.completedScenarios.Contains(scenario) || _state.failedScenarios.Contains(scenario))
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        private static void AdvanceToNext()
        {
            DeleteProtocolFile(SubmissionCaptureScenarios.RuntimeDoneFileName);
            DeleteProtocolFile(SubmissionCaptureScenarios.EditorCaptureDoneFileName);

            _state.scenario = null;
            _state.deadlineTicks = 0;
            _state.phase = PhaseIdle;
            SaveState();
        }

        private static void FailScenario(string scenario, string reason)
        {
            _state.failedScenarios.Add(scenario);
            AppendLog("FAILED " + scenario + ": " + reason);
            Debug.LogWarning("[SubmissionCapture] " + scenario + " failed: " + reason);
            AdvanceToNext();
        }

        private static void FinalizeResult()
        {
            var result = new SubmissionCaptureResult
            {
                requestId = _request.requestId,
                status = _state.failedScenarios.Count == 0 ? "complete" : "partial",
                outputs = _state.outputs.ToArray(),
                failed = _state.failedScenarios.ToArray()
            };

            WriteProtocolFile(SubmissionCaptureScenarios.ResultFileName, JsonUtility.ToJson(result, true));
            AppendLog(
                "result written: status=" + result.status +
                " outputs=" + result.outputs.Length +
                " failed=" + result.failed.Length);

            _state.phase = PhaseComplete;
            SaveState();
        }

        private static void TryRunDeferredCleanup()
        {
            if (_state == null || string.IsNullOrEmpty(_state.pendingDeleteAssetPath))
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string path = _state.pendingDeleteAssetPath;
            _state.pendingDeleteAssetPath = null;

            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
                AppendLog("deleted temp capture asset: " + path);
            }

            SaveState();
        }

        private static LevelDefinition LoadCaptureLevel()
        {
            LevelCatalog catalog = CatalogAudit.LoadShippedCatalog();
            int index = SubmissionCaptureScenarios.SelectCaptureLevelIndex(catalog);
            if (index < 0 || catalog.levels[index] == null)
            {
                throw new InvalidOperationException("No shipped catalog level is available for capture.");
            }

            return catalog.levels[index];
        }

        private static LevelDefinition PersistMultiGroupLevel()
        {
            string assetPath = SubmissionCaptureScenarios.MultiGroupTempAssetPath;
            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            LevelDefinition fixture = SubmissionCaptureScenarios.BuildMultiGroupCaptureLevel();
            AssetDatabase.CreateAsset(fixture, assetPath);
            AssetDatabase.SaveAssets();

            LevelDefinition persisted = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            if (persisted == null)
            {
                throw new InvalidOperationException("Failed to persist the multi-group fixture at " + assetPath);
            }

            return persisted;
        }

        private static void RequireSolvable(string scenario, AnalysisResult result)
        {
            if (result == null || result.Verdict != AnalysisVerdict.Solvable)
            {
                throw new InvalidOperationException(
                    scenario + " needs a solvable level, got " +
                    (result == null ? "null" : result.Verdict.ToString()));
            }
        }

        private static int MidReplayCount(IList<Direction> solution, int preferred)
        {
            if (solution == null || solution.Count == 0)
            {
                return 0;
            }

            return Math.Min(preferred, solution.Count);
        }

        // ----- protocol file IO ------------------------------------------------------------------

        private static void EnsureStateForRequest()
        {
            if (_state == null)
            {
                _state = new BridgeState();
            }

            if (_state.requestId == _request.requestId)
            {
                return;
            }

            _state = new BridgeState { requestId = _request.requestId };
            SaveState();
            WriteBridgeReady();
        }

        private static BridgeState LoadState()
        {
            string path = SubmissionCaptureScenarios.ProtocolPath(StateFileName);
            if (!File.Exists(path))
            {
                return new BridgeState();
            }

            try
            {
                return JsonUtility.FromJson<BridgeState>(File.ReadAllText(path)) ?? new BridgeState();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SubmissionCapture] could not read bridge state: " + exception.Message);
                return new BridgeState();
            }
        }

        private static void SaveState()
        {
            if (_state == null)
            {
                return;
            }

            WriteProtocolFile(StateFileName, JsonUtility.ToJson(_state, true));
        }

        private static void LoadRequest()
        {
            string path = SubmissionCaptureScenarios.ProtocolPath(SubmissionCaptureScenarios.RequestFileName);
            if (!File.Exists(path))
            {
                _request = null;
                return;
            }

            try
            {
                SubmissionCaptureRequest request =
                    JsonUtility.FromJson<SubmissionCaptureRequest>(File.ReadAllText(path));
                _request = request != null && !string.IsNullOrEmpty(request.requestId) ? request : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SubmissionCapture] could not read request.json: " + exception.Message);
                _request = null;
            }
        }

        private static void WriteBridgeReady()
        {
            string requestId = null;
            if (_request != null && !string.IsNullOrEmpty(_request.requestId))
            {
                requestId = _request.requestId;
            }
            else if (_state != null && !string.IsNullOrEmpty(_state.requestId))
            {
                requestId = _state.requestId;
            }

            string idJson = requestId == null
                ? "null"
                : "\"" + requestId.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            WriteProtocolFile(
                SubmissionCaptureScenarios.BridgeReadyFileName,
                "{\"ready\":true,\"requestId\":" + idJson + "}");
        }

        private static T TryReadMarker<T>(string fileName) where T : class
        {
            string path = SubmissionCaptureScenarios.ProtocolPath(fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                AppendLog("could not parse " + fileName + ": " + exception.Message);
                return null;
            }
        }

        private static void WriteProtocolFile(string fileName, string contents)
        {
            try
            {
                Directory.CreateDirectory(SubmissionCaptureScenarios.TempCaptureDirectory);
                File.WriteAllText(SubmissionCaptureScenarios.ProtocolPath(fileName), contents, Utf8NoBom);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SubmissionCapture] could not write " + fileName + ": " + exception.Message);
            }
        }

        private static void DeleteProtocolFile(string fileName)
        {
            try
            {
                string path = SubmissionCaptureScenarios.ProtocolPath(fileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SubmissionCapture] could not delete " + fileName + ": " + exception.Message);
            }
        }

        private static void AppendLog(string message)
        {
            try
            {
                Directory.CreateDirectory(SubmissionCaptureScenarios.TempCaptureDirectory);
                File.AppendAllText(
                    SubmissionCaptureScenarios.ProtocolPath(SubmissionCaptureScenarios.LogFileName),
                    DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine,
                    Utf8NoBom);
            }
            catch (Exception)
            {
                // Logging must never break the capture run.
            }
        }
    }
}

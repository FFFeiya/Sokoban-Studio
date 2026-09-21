using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Sokoban
{
    /// <summary>
    /// Runtime screenshot directive written by the editor capture bridge to
    /// <c>Temp/AgentCapture/runtime-scenario.json</c>. The field names matched here are the JSON
    /// contract (see the editor-side <c>RuntimeScenarioDirective</c>); <see cref="replayCount"/> is -1
    /// for "replay all moves" or a non-negative count for a mid-state.
    /// </summary>
    [Serializable]
    public class CaptureRuntimeDirective
    {
        public string scenario;
        public string outputPath;
        public string moves;
        public int replayCount;
        public bool markCompleted;
        public float settleSeconds;
    }

    /// <summary>Done marker this driver writes back to <c>Temp/AgentCapture/runtime-done.json</c>.</summary>
    [Serializable]
    public class CaptureRuntimeDone
    {
        public string scenario;
        public bool ok;
        public string error;
    }

    /// <summary>
    /// Runtime half of the fully automated submission capture pipeline (see
    /// FULL_AUTO_CAPTURE_PLAN_REVISED.md, C3). It is auto-installed after every scene load and is a
    /// strict no-op unless the editor bridge has written <c>Temp/AgentCapture/runtime-scenario.json</c>:
    /// in that case it replays the directive's moves into the live <see cref="GameBootstrap"/>, lets
    /// the view settle, then screenshots the Game view through <see cref="ScreenCapture"/> (which
    /// includes the IMGUI overlay, e.g. the "关卡完成" panel) and writes the done marker for the bridge.
    ///
    /// This lives in the runtime assembly: it never touches UnityEditor/AssetDatabase. Because the
    /// Play Mode domain reload destroys editor statics, the directive is re-read from disk in
    /// <see cref="Start"/> on every play session.
    /// </summary>
    public class SubmissionCaptureDriver : MonoBehaviour
    {
        private const string TempCaptureFolder = "Temp/AgentCapture";
        private const string DirectiveFileName = "runtime-scenario.json";
        private const string DoneFileName = "runtime-done.json";
        private const float BoardReadyTimeoutSeconds = 5f;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Auto-installs the driver on every scene load so no scene asset has to be edited. Without a
        /// directive file the component disables itself in <see cref="Start"/>, so normal play and
        /// PlayMode tests are unaffected.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("SubmissionCaptureDriver");
            go.AddComponent<SubmissionCaptureDriver>();
        }

        /// <summary>
        /// Pure, unit-testable core: splits a comma-joined move list, trims each token, parses the
        /// <see cref="Direction"/> by name (case-insensitive) and skips unparsable tokens. Returns the
        /// first <paramref name="replayCount"/> entries, or all of them when it is negative.
        /// </summary>
        public static Direction[] ParseMoves(string commaJoined, int replayCount)
        {
            var parsed = new List<Direction>();

            if (!string.IsNullOrEmpty(commaJoined))
            {
                string[] tokens = commaJoined.Split(',');
                foreach (string token in tokens)
                {
                    string trimmed = token.Trim();
                    if (trimmed.Length > 0 && Enum.TryParse(trimmed, true, out Direction direction))
                    {
                        parsed.Add(direction);
                    }
                }
            }

            if (replayCount < 0 || replayCount >= parsed.Count)
            {
                return parsed.ToArray();
            }

            return parsed.GetRange(0, replayCount).ToArray();
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        private static string CaptureDirectory => Path.Combine(ProjectRoot, TempCaptureFolder);

        private static string DirectivePath => Path.Combine(CaptureDirectory, DirectiveFileName);

        private static string DonePath => Path.Combine(CaptureDirectory, DoneFileName);

        private void Start()
        {
            if (!File.Exists(DirectivePath))
            {
                // Strict no-op: never disturb a normal play session or a PlayMode test run.
                enabled = false;
                return;
            }

            CaptureRuntimeDirective directive;
            try
            {
                directive = JsonUtility.FromJson<CaptureRuntimeDirective>(File.ReadAllText(DirectivePath));
            }
            catch (Exception exception)
            {
                WriteDone(null, false, exception.Message);
                enabled = false;
                return;
            }

            if (directive == null)
            {
                WriteDone(null, false, "directive file could not be parsed");
                enabled = false;
                return;
            }

            StartCoroutine(Run(directive));
        }

        private IEnumerator Run(CaptureRuntimeDirective directive)
        {
            // Note: C# forbids `yield return` inside a try block that has a catch clause, so every
            // yield below lives outside any try/catch; only the synchronous, throw-prone operations
            // (replay, capture) are individually guarded.
            Direction[] moves = ParseMoves(directive.moves, directive.replayCount);
            bool needsBoard = moves.Length > 0;

            GameController controller = null;
            GameBootstrap bootstrap = null;

            // Menu scenarios (RuntimeMainMenu / RuntimeLevelSelect) carry no moves and have no board:
            // skip the board wait and just settle + screenshot the menu scene.
            if (needsBoard)
            {
                float waited = 0f;
                while (waited < BoardReadyTimeoutSeconds)
                {
                    controller = FindObjectOfType<GameController>();
                    bootstrap = controller != null ? controller.bootstrap : null;
                    if (bootstrap == null)
                    {
                        bootstrap = FindObjectOfType<GameBootstrap>();
                    }

                    if (bootstrap != null && bootstrap.Board != null)
                    {
                        break;
                    }

                    waited += Time.deltaTime;
                    yield return null;
                }

                if (bootstrap == null || bootstrap.Board == null)
                {
                    WriteDone(directive.scenario, false, "board not ready");
                    yield break;
                }

                if (controller == null)
                {
                    controller = bootstrap.controller;
                }

                try
                {
                    foreach (Direction direction in moves)
                    {
                        // Rejected moves are fine: the replay just reproduces the analyzer solution.
                        bootstrap.TryMove(direction);
                    }

                    if (directive.markCompleted && controller != null)
                    {
                        // The board is complete after a full replay, but the controller only
                        // re-evaluates completion on a board-instance change, so force the overlay
                        // for RuntimeComplete.
                        controller.MarkCompleted();
                    }
                }
                catch (Exception exception)
                {
                    WriteDone(directive.scenario, false, exception.Message);
                    yield break;
                }
            }

            float settled = 0f;
            while (settled < directive.settleSeconds)
            {
                settled += Time.deltaTime;
                yield return null;
            }

            try
            {
                // Captures the Game view including the OnGUI overlay (e.g. the 关卡完成 panel).
                ScreenCapture.CaptureScreenshot(directive.outputPath);
            }
            catch (Exception exception)
            {
                WriteDone(directive.scenario, false, exception.Message);
                yield break;
            }

            // Let the end-of-frame flip flush the PNG to disk before the bridge exits play mode.
            yield return new WaitForEndOfFrame();

            WriteDone(directive.scenario, true, string.Empty);
        }

        private static void WriteDone(string scenario, bool ok, string error)
        {
            try
            {
                Directory.CreateDirectory(CaptureDirectory);
                var done = new CaptureRuntimeDone
                {
                    scenario = scenario,
                    ok = ok,
                    error = error ?? string.Empty
                };

                File.WriteAllText(DonePath, JsonUtility.ToJson(done), Utf8NoBom);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SubmissionCaptureDriver] could not write done marker: " + exception.Message);
            }
        }
    }
}

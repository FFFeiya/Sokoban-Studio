using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// EditMode coverage for the editor-only half of the fully automated submission capture pipeline
    /// (C2 + C4): the request/result/directive file protocol round-trips, the scenario -&gt; stable
    /// filename map, and the two in-memory capture fixtures the bridge relies on. The bridge state
    /// machine itself (play-mode transitions, Win32 capture) needs a live visible editor and is not
    /// exercised here.
    /// </summary>
    public class SubmissionCaptureTests
    {
        /// <summary>Every scenario id, with the exact filename FULL_AUTO_CAPTURE_PLAN_REVISED.md §5 requires.</summary>
        private static readonly (string Id, string FileName)[] ExpectedFileNames =
        {
            (SubmissionCaptureScenarios.RuntimeMainMenu, "runtime-main-menu.png"),
            (SubmissionCaptureScenarios.RuntimeLevelSelect, "runtime-level-select.png"),
            (SubmissionCaptureScenarios.RuntimeGameplay, "runtime-gameplay.png"),
            (SubmissionCaptureScenarios.RuntimeMultiGroup, "runtime-multigroup.png"),
            (SubmissionCaptureScenarios.RuntimeComplete, "runtime-complete.png"),
            (SubmissionCaptureScenarios.EditorDashboard, "editor-dashboard.png"),
            (SubmissionCaptureScenarios.EditorLevelEditor, "editor-level-editor.png"),
            (SubmissionCaptureScenarios.EditorValidation, "editor-validation.png"),
            (SubmissionCaptureScenarios.EditorAnalyzer, "editor-analyzer.png"),
            (SubmissionCaptureScenarios.EditorSolutionPreview, "editor-solution-preview.png")
        };

        [Test]
        public void Request_RoundTripsThroughJsonUtility()
        {
            var request = new SubmissionCaptureRequest
            {
                requestId = "run-42",
                scenarios = new[]
                {
                    SubmissionCaptureScenarios.RuntimeGameplay,
                    SubmissionCaptureScenarios.EditorDashboard
                },
                runtimeResolution = new CaptureResolution { width = 1920, height = 1080 },
                overwrite = true
            };

            string json = JsonUtility.ToJson(request);
            SubmissionCaptureRequest back = JsonUtility.FromJson<SubmissionCaptureRequest>(json);

            Assert.IsNotNull(back, "The request must deserialize.");
            Assert.AreEqual("run-42", back.requestId, "requestId must survive.");
            Assert.IsNotNull(back.scenarios, "scenarios must survive.");
            Assert.AreEqual(2, back.scenarios.Length, "Both scenarios must survive.");
            Assert.AreEqual(SubmissionCaptureScenarios.RuntimeGameplay, back.scenarios[0], "Order must be preserved.");
            Assert.AreEqual(SubmissionCaptureScenarios.EditorDashboard, back.scenarios[1], "Order must be preserved.");
            Assert.IsNotNull(back.runtimeResolution, "runtimeResolution must survive.");
            Assert.AreEqual(1920, back.runtimeResolution.width, "Resolution width must survive.");
            Assert.AreEqual(1080, back.runtimeResolution.height, "Resolution height must survive.");
            Assert.IsTrue(back.overwrite, "overwrite must survive.");
        }

        [Test]
        public void Result_RoundTripsThroughJsonUtility()
        {
            var result = new SubmissionCaptureResult
            {
                requestId = "run-42",
                status = "partial",
                outputs = new[]
                {
                    new CaptureOutput
                    {
                        scenario = SubmissionCaptureScenarios.RuntimeGameplay,
                        path = @"C:\repo\Docs\Screenshots\runtime-gameplay.png",
                        captureMethod = "UnityScreenCapture",
                        width = 1920,
                        height = 1080,
                        validation = "pending"
                    }
                },
                failed = new[] { SubmissionCaptureScenarios.EditorValidation }
            };

            string json = JsonUtility.ToJson(result);
            SubmissionCaptureResult back = JsonUtility.FromJson<SubmissionCaptureResult>(json);

            Assert.IsNotNull(back, "The result must deserialize.");
            Assert.AreEqual("run-42", back.requestId, "requestId must survive.");
            Assert.AreEqual("partial", back.status, "status must survive.");
            Assert.IsNotNull(back.outputs, "outputs must survive.");
            Assert.AreEqual(1, back.outputs.Length, "One output must survive.");
            Assert.AreEqual(SubmissionCaptureScenarios.RuntimeGameplay, back.outputs[0].scenario, "scenario must survive.");
            Assert.AreEqual(
                @"C:\repo\Docs\Screenshots\runtime-gameplay.png",
                back.outputs[0].path,
                "The absolute path must survive.");
            Assert.AreEqual("UnityScreenCapture", back.outputs[0].captureMethod, "captureMethod must survive.");
            Assert.AreEqual(1920, back.outputs[0].width, "Output width must survive.");
            Assert.AreEqual(1080, back.outputs[0].height, "Output height must survive.");
            Assert.AreEqual("pending", back.outputs[0].validation, "validation must survive.");
            Assert.IsNotNull(back.failed, "failed must survive.");
            Assert.AreEqual(1, back.failed.Length, "One failure must survive.");
            Assert.AreEqual(SubmissionCaptureScenarios.EditorValidation, back.failed[0], "The failure id must survive.");
        }

        [Test]
        public void RuntimeDirective_RoundTripsThroughJsonUtility()
        {
            string json = SubmissionCaptureScenarios.BuildRuntimeDirectiveJson(
                SubmissionCaptureScenarios.RuntimeComplete,
                @"C:\repo\Docs\Screenshots\runtime-complete.png",
                "Up,Right,Down,Left",
                -1,
                true,
                0.6f);

            RuntimeScenarioDirective directive = SubmissionCaptureScenarios.ParseRuntimeDirective(json);

            Assert.IsNotNull(directive, "The directive must deserialize.");
            Assert.AreEqual(SubmissionCaptureScenarios.RuntimeComplete, directive.scenario, "scenario must survive.");
            Assert.AreEqual(
                @"C:\repo\Docs\Screenshots\runtime-complete.png",
                directive.outputPath,
                "outputPath must survive.");
            Assert.AreEqual("Up,Right,Down,Left", directive.moves, "moves must survive.");
            Assert.AreEqual(-1, directive.replayCount, "replayCount -1 (replay all) must survive.");
            Assert.IsTrue(directive.markCompleted, "markCompleted must survive.");
            Assert.AreEqual(0.6f, directive.settleSeconds, 0.0001f, "settleSeconds must survive.");
        }

        [Test]
        public void EditorCaptureRequest_RoundTripsThroughJsonUtility()
        {
            string json = SubmissionCaptureScenarios.BuildEditorCaptureRequestJson(
                SubmissionCaptureScenarios.EditorDashboard,
                SubmissionCaptureScenarios.WindowTitle(SubmissionCaptureScenarios.EditorDashboard),
                @"C:\repo\Docs\Screenshots\editor-dashboard.png");

            EditorCaptureRequest request = SubmissionCaptureScenarios.ParseEditorCaptureRequest(json);

            Assert.IsNotNull(request, "The editor capture request must deserialize.");
            Assert.AreEqual(SubmissionCaptureScenarios.EditorDashboard, request.scenario, "scenario must survive.");
            Assert.AreEqual(
                "[CAPTURE] Sokoban 内容总览",
                request.windowTitle,
                "The unique [CAPTURE] title must survive.");
            Assert.AreEqual(
                @"C:\repo\Docs\Screenshots\editor-dashboard.png",
                request.outputPath,
                "outputPath must survive.");
        }

        [Test]
        public void StableFileName_MapsEveryScenarioToItsRequiredName()
        {
            foreach ((string id, string fileName) in ExpectedFileNames)
            {
                Assert.AreEqual(fileName, SubmissionCaptureScenarios.StableFileName(id), "Wrong filename for " + id);
                StringAssert.EndsWith(fileName, SubmissionCaptureScenarios.OutputPath(id), "The output path must end in " + fileName);
            }

            // The multigroup name is deliberately one word ("multigroup", not "multi-group").
            Assert.AreEqual(
                "runtime-multigroup.png",
                SubmissionCaptureScenarios.StableFileName(SubmissionCaptureScenarios.RuntimeMultiGroup),
                "The multigroup filename must not be hyphen-split.");
        }

        [Test]
        public void ScenarioClassification_SplitsRuntimeAndEditorSets()
        {
            foreach ((string id, _) in ExpectedFileNames)
            {
                bool expectedRuntime = id.StartsWith("Runtime");
                Assert.AreEqual(
                    expectedRuntime,
                    SubmissionCaptureScenarios.IsRuntimeScenario(id),
                    "Runtime classification wrong for " + id);
                Assert.AreEqual(
                    !expectedRuntime,
                    SubmissionCaptureScenarios.IsEditorScenario(id),
                    "Editor classification wrong for " + id);
                Assert.IsTrue(SubmissionCaptureScenarios.IsKnownScenario(id), "Known classification wrong for " + id);
            }

            Assert.IsFalse(SubmissionCaptureScenarios.IsKnownScenario("NotAScenario"), "An unknown id must not be known.");
        }

        [Test]
        public void ValidationCaptureLevel_HasOneCornerDeadlockWarningAndNoBlockingError()
        {
            LevelDefinition level = SubmissionCaptureScenarios.BuildValidationCaptureLevel();
            try
            {
                List<string> errors = LevelValidator.Validate(level);
                CollectionAssert.IsEmpty(errors, "The fixture must have no blocking errors: " + string.Join(", ", errors));

                Assert.IsTrue(
                    SubmissionCaptureScenarios.TryGetFirstWarningCell(level, out int cellX, out int cellY),
                    "The fixture must carry a cell-bound warning.");
                Assert.AreEqual(1, cellX, "The corner-deadlock warning must reference the trapped box column.");
                Assert.AreEqual(1, cellY, "The corner-deadlock warning must reference the trapped box row.");
            }
            finally
            {
                Destroy(level);
            }
        }

        [Test]
        public void MultiGroupCaptureLevel_HasBothGroupsAndBalancedContent()
        {
            LevelDefinition level = SubmissionCaptureScenarios.BuildMultiGroupCaptureLevel();
            try
            {
                int players = 0;
                int boxes = 0;
                int goals = 0;
                bool plateA = false;
                bool plateB = false;
                bool doorA = false;
                bool doorB = false;

                for (int i = 0; i < level.cells.Count; i++)
                {
                    switch (level.cells[i])
                    {
                        case TileType.Goal:
                            goals++;
                            break;
                        case TileType.Plate:
                            if (level.GetGroupId(i) == 1) plateB = true; else plateA = true;
                            break;
                        case TileType.Door:
                            if (level.GetGroupId(i) == 1) doorB = true; else doorA = true;
                            break;
                    }

                    if (level.occupants[i] == OccupantType.Player) players++;
                    else if (level.occupants[i] == OccupantType.Box) boxes++;
                }

                Assert.AreEqual(1, players, "Exactly one player is required.");
                Assert.AreEqual(2, boxes, "The fixture must have two boxes.");
                Assert.AreEqual(2, goals, "The fixture must have two goals (one per group).");
                Assert.IsTrue(plateA && doorA, "Group A must have both a plate and a door.");
                Assert.IsTrue(plateB && doorB, "Group B must have both a plate and a door.");
            }
            finally
            {
                Destroy(level);
            }
        }

        [Test]
        public void LoadForCapture_BindsRequestedLevelIntoWorkingCopy()
        {
            LevelDefinition level = null;
            LevelDefinition working = null;
            LevelEditorWindow window = null;

            try
            {
                level = LevelAssetFactory.BuildLevel("CaptureHook", new[] { "#####", "#PBG#", "#####" });

                window = LevelEditorWindow.LoadForCapture(level);

                Assert.IsNotNull(window, "LoadForCapture must return the window.");
                LevelEditorDocument document = window.LoadedDocument;
                Assert.IsNotNull(document, "LoadForCapture must load a working copy.");
                working = document.Working;

                Assert.AreNotSame(level, working, "The editor must load a copy, never the source.");
                Assert.AreEqual(level.levelName, working.levelName, "The level name must be copied.");
                Assert.AreEqual(level.width, working.width, "The width must be copied.");
                Assert.AreEqual(level.height, working.height, "The height must be copied.");
            }
            finally
            {
                if (window != null)
                {
                    window.Close();
                }

                Destroy(working);
                Destroy(level);
            }
        }

        [Test]
        public void InjectAnalysis_StoresResultOnTheWindow()
        {
            LevelDefinition level = null;
            LevelDefinition working = null;
            LevelEditorWindow window = null;

            try
            {
                level = LevelAssetFactory.BuildLevel("CaptureInject", new[] { "#####", "#PBG#", "#####" });
                AnalysisResult result = LevelAnalyzer.Analyze(level);
                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);

                window = LevelEditorWindow.LoadForCapture(level);
                working = window.LoadedDocument.Working;

                Assert.IsNull(window.LastAnalysis, "No analysis is shown before injection.");
                window.InjectAnalysis(result);
                Assert.AreSame(result, window.LastAnalysis, "InjectAnalysis must store the result for rendering.");
            }
            finally
            {
                if (window != null)
                {
                    window.Close();
                }

                Destroy(working);
                Destroy(level);
            }
        }

        /// <summary>D015: destroys an in-memory ScriptableObject created by a test. Shared assets are never passed here.</summary>
        private static void Destroy(UnityEngine.Object target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}

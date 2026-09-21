using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the T2 Solution Preview replay logic (<see cref="SolutionPreviewSession"/>) headlessly:
    /// the session replays the analyzer solution through a separate <see cref="Board"/> built from a
    /// <see cref="LevelEditorDocument.Clone"/> (never the asset), labels push steps from the real
    /// <see cref="Board.PushCount"/> delta, and leaves shipped asset bytes and lists untouched.
    /// Window rendering and the auto-play editor loop are manual-gate verified (batch mode cannot
    /// render), so this suite pins the logic the tests can exercise.
    /// </summary>
    public class SolutionPreviewTests
    {
        /// <summary>Builds a level definition from ASCII rows (row 0 = top). See LevelAssetFactory.BuildLevel.</summary>
        private static LevelDefinition MakeLevel(params string[] rows)
        {
            return LevelAssetFactory.BuildLevel("SolutionPreviewTest", rows);
        }

        [Test]
        public void Session_FullReplay_CompletesWithMatchingCounts()
        {
            // Player (1, 1), box (2, 1), goal (3, 1): one push to the right wins, exactly 1 move.
            LevelDefinition def = MakeLevel("#####", "#PBG#", "#####");
            SolutionPreviewSession session = null;

            try
            {
                AnalysisResult result = LevelAnalyzer.Analyze(def);
                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);

                session = new SolutionPreviewSession(def, result);
                Assert.IsTrue(session.SetStep(session.TotalSteps), "The full solution must replay.");
                Assert.IsTrue(session.Board.IsComplete, "Replaying the solution must complete the level.");
                Assert.AreEqual(result.SolutionMoves, session.Board.MoveCount, "Replay move count must match.");
                Assert.AreEqual(result.SolutionPushes, session.Board.PushCount, "Replay push count must match.");
            }
            finally
            {
                Destroy(session != null ? session.Clone : null);
                Destroy(def);
            }
        }

        [Test]
        public void Session_PushStepLabeling_IdentifiesTheSinglePush()
        {
            // Player (1, 1), box (2, 1), goal (3, 1): the only solution move is a push.
            LevelDefinition def = MakeLevel("#####", "#PBG#", "#####");
            SolutionPreviewSession session = null;

            try
            {
                AnalysisResult result = LevelAnalyzer.Analyze(def);
                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);

                session = new SolutionPreviewSession(def, result);

                var pushSteps = new List<int>();
                while (session.CanStepForward)
                {
                    int pushesBefore = session.PushCount;
                    Assert.IsTrue(session.StepForward(), "Each solution move must be accepted.");
                    if (session.PushCount - pushesBefore == 1)
                    {
                        pushSteps.Add(session.Step);
                    }
                }

                CollectionAssert.AreEqual(new[] { 1 }, pushSteps, "Only the single move is a push.");
                Assert.AreEqual(
                    "第 1 / 1 步：向右（推箱）",
                    session.CurrentActionLabel(),
                    "The push step must be labelled with its direction.");
            }
            finally
            {
                Destroy(session != null ? session.Clone : null);
                Destroy(def);
            }
        }

        [Test]
        public void Session_CurrentActionLabel_DistinguishesWalkFromPush()
        {
            // Player (1, 1), box (3, 1), goal (4, 1): step right once (walk), then push right (push).
            LevelDefinition def = MakeLevel("######", "#P.BG#", "######");
            SolutionPreviewSession session = null;

            try
            {
                AnalysisResult result = LevelAnalyzer.Analyze(def);
                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
                Assert.AreEqual(2, result.SolutionMoves, "Walk then push is two moves.");
                Assert.AreEqual(1, result.SolutionPushes, "Only the second move pushes.");

                session = new SolutionPreviewSession(def, result);

                Assert.IsTrue(session.StepForward(), "The first move must be accepted.");
                Assert.AreEqual("第 1 / 2 步：向右", session.CurrentActionLabel(), "A walk must not be labelled push.");

                Assert.IsTrue(session.StepForward(), "The second move must be accepted.");
                Assert.AreEqual("第 2 / 2 步：向右（推箱）", session.CurrentActionLabel(), "A push must be labelled.");
            }
            finally
            {
                Destroy(session != null ? session.Clone : null);
                Destroy(def);
            }
        }

        [Test]
        public void Session_ShippedLevel_AssetBytesAreHashIdentical()
        {
            string assetPath = LevelAssetFactory.Level01Path;
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            Assert.IsNotNull(level, "Level01 asset is required for this test.");

            string before = Sha256(ProjectPathOf(assetPath));
            SolutionPreviewSession session = null;

            try
            {
                AnalysisResult result = LevelAnalyzer.Analyze(level);
                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);

                session = new SolutionPreviewSession(level, result);
                Assert.IsTrue(session.SetStep(session.TotalSteps), "The full solution must replay.");
                Assert.IsTrue(session.Board.IsComplete, "Replaying the solution must complete the level.");
            }
            finally
            {
                // The AssetDatabase-loaded level is a shared asset and must never be destroyed; only
                // the session's own in-memory clone is.
                Destroy(session != null ? session.Clone : null);
            }

            Assert.AreEqual(before, Sha256(ProjectPathOf(assetPath)), "Source asset bytes must be unchanged.");
        }

        [Test]
        public void Session_CloneIsolation_SourceAssetCellsAndOccupantsUnchanged()
        {
            string assetPath = LevelAssetFactory.Level01Path;
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            Assert.IsNotNull(level, "Level01 asset is required for this test.");

            var cellsBefore = new List<TileType>(level.cells);
            var occupantsBefore = new List<OccupantType>(level.occupants);

            SolutionPreviewSession session = null;

            try
            {
                AnalysisResult result = LevelAnalyzer.Analyze(level);
                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);

                session = new SolutionPreviewSession(level, result);

                Assert.AreNotSame(level, session.Clone, "The session must replay a separate clone.");
                Assert.IsTrue(session.SetStep(session.TotalSteps), "The full solution must replay.");
            }
            finally
            {
                Destroy(session != null ? session.Clone : null);
            }

            CollectionAssert.AreEqual(cellsBefore, level.cells, "Source cells must be unchanged.");
            CollectionAssert.AreEqual(occupantsBefore, level.occupants, "Source occupants must be unchanged.");
        }

        /// <summary>
        /// Covers the dashboard bind API: <see cref="SolutionPreviewWindow.OpenFor"/> must bind the window to
        /// the requested level, load a Solvable result's current solution immediately, and — when handed no
        /// result — keep the level bound while refusing a stale replay (no analysis).
        /// </summary>
        [Test]
        public void OpenFor_BindsLevelAndCurrentSolution_AndRefusesAStaleReplay()
        {
            // Player (1, 1), box (2, 1), goal (3, 1): one push to the right wins.
            LevelDefinition def = MakeLevel("#####", "#PBG#", "#####");
            SolutionPreviewWindow window = null;

            try
            {
                AnalysisResult result = LevelAnalyzer.Analyze(def);
                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);

                SolutionPreviewWindow.OpenFor(def, result);
                window = EditorWindow.GetWindow<SolutionPreviewWindow>();
                Assert.AreSame(def, window.BoundLevel, "OpenFor must bind the requested level.");
                Assert.AreSame(result, window.BoundAnalysis, "A Solvable result must load its current solution.");

                // Re-open without a result: still bound to the level, but no stale replay survives.
                SolutionPreviewWindow.OpenFor(def);
                Assert.AreSame(def, window.BoundLevel, "OpenFor must keep the bound level.");
                Assert.IsNull(window.BoundAnalysis, "A missing result must refuse a stale replay.");
            }
            finally
            {
                if (window != null)
                {
                    window.Close();
                }

                Destroy(def);
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

        /// <summary>SHA-256 hex of a file's bytes, for byte-for-byte immutability checks.</summary>
        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(File.ReadAllBytes(path));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        /// <summary>Resolves a project-relative asset path to an absolute path anchored at the project root.</summary>
        private static string ProjectPathOf(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}

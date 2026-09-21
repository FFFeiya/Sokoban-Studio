using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Sokoban.Tests.PlayMode
{
#if UNITY_EDITOR
    /// <summary>
    /// Regression coverage for the initially-complete level dead-end (examiner finding F-02): a level
    /// authored with every box already on a goal must not silently lock into an unplayable state. Once
    /// the gameplay scene builds such a board, the completion overlay must be active without any move,
    /// movement must stay rejected, and Retry must rebuild the level and show completion again.
    /// The editor asset pipeline authors the probe level, so these tests are editor-only, mirroring
    /// <see cref="PlaytestRequestPlayModeTests"/>.
    /// </summary>
    public class CompletedLevelPlayModeTests
    {
        private const string LevelsFolder = "Assets/Sokoban/Levels";
        private const string ProbeFolder = LevelsFolder + "/Temp";
        private const string ProbePath = ProbeFolder + "/CompletedLevelProbe.asset";
        private const string ProbeName = "Completed Level Probe";

        /// <summary>
        /// Corridor with one player and one box already on its goal, free floor at (3,1) beyond the
        /// box, so Board rules alone would accept a push Right. That makes the movement-lock assertion
        /// below non-vacuous: TryMove is rejected only because the completion lock in
        /// <see cref="GameBootstrap.TryMove(Direction)"/> gates on <see cref="Board.IsComplete"/>, which
        /// the EditMode <c>BoardTests.TryMove_OnInitiallyCompleteBoard_CanUndoCompleteState</c> proves
        /// is not a Board-level rule.
        /// </summary>
        private static readonly string[] ProbeRows =
        {
            "#####",
            "#P*.#",
            "#####"
        };

        [TearDown]
        public void TearDown()
        {
            ClearRequest();

            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(ProbePath) != null)
            {
                AssetDatabase.DeleteAsset(ProbePath);
            }

            if (AssetDatabase.IsValidFolder(ProbeFolder) &&
                AssetDatabase.FindAssets("t:Object", new[] { ProbeFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(ProbeFolder);
            }
        }

        [UnityTest]
        public IEnumerator InitiallyCompleteLevel_ShowsCompletionOverlay_WithoutAnyMove()
        {
            LoadProbeIntoGameplay();

            // One frame so Update runs SynchronizeCompletion on the freshly built board.
            yield return null;

            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "The gameplay scene must contain a GameBootstrap.");
            Assert.IsNotNull(bootstrap.Board, "The bootstrap must have built a board.");
            Assert.AreEqual(ProbeName, bootstrap.LevelName, "The probe level must be the level played.");
            Assert.IsTrue(bootstrap.Board.IsComplete, "Every box starts on a goal, so the board is complete on load.");
            Assert.AreEqual(0, bootstrap.Board.MoveCount, "No move may be needed to reach the complete state.");

            var controller = Object.FindObjectOfType<GameController>();
            Assert.IsNotNull(controller, "The gameplay scene must contain a GameController.");
            Assert.IsTrue(controller.IsCompletedForTests, "The completion overlay must be active without any move.");

            // Movement must stay locked on an already-complete board, even for a push that the bare
            // Board rules would accept (the cell beyond the box is free floor).
            Assert.IsFalse(
                bootstrap.TryMove(Direction.Right),
                "Movement must be rejected once the level is complete.");
            Assert.IsFalse(
                bootstrap.TryMove(Direction.Right, out bool completed),
                "The move out-parameter overload must reject the move too.");
            Assert.IsFalse(completed, "A rejected move must never report completion.");
            Assert.AreEqual(0, bootstrap.Board.MoveCount, "A rejected move must not advance the move counter.");
            Assert.IsTrue(bootstrap.Board.IsComplete, "The board must stay complete.");
            Assert.IsTrue(controller.IsCompletedForTests, "The overlay must stay active after the rejected move.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Retry_OnInitiallyCompleteLevel_ReevaluatesAndShowsCompletionAgain()
        {
            LoadProbeIntoGameplay();

            // One frame so Update runs SynchronizeCompletion on the freshly built board.
            yield return null;

            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "The gameplay scene must contain a GameBootstrap.");
            var controller = Object.FindObjectOfType<GameController>();
            Assert.IsNotNull(controller, "The gameplay scene must contain a GameController.");
            Assert.IsTrue(controller.IsCompletedForTests, "The overlay must start active for an initially complete level.");

            Board before = bootstrap.Board;

            controller.Retry();
            Assert.IsFalse(controller.IsCompletedForTests, "Retry must clear the completion flag before the board rebuilds.");
            Assert.AreNotSame(before, bootstrap.Board, "Retry must rebuild the board instance.");

            // The controller re-evaluates the rebuilt board on the following frame.
            yield return null;
            yield return null;

            Assert.IsTrue(bootstrap.Board.IsComplete, "The rebuilt board is still inherently complete.");
            Assert.AreEqual(0, bootstrap.Board.MoveCount, "Retry must reset the move counter.");
            Assert.IsTrue(
                controller.IsCompletedForTests,
                "Completion must reappear after Retry because the level is inherently complete.");
        }

        private static void LoadProbeIntoGameplay()
        {
            LevelDefinition probe = CreateProbeLevel();
            Assert.IsNotNull(probe, "The probe level asset must be created.");
            ArmRequest(probe);

            SceneManager.LoadScene(GameFlowRequest.GameplayScene);
        }

        private static LevelDefinition CreateProbeLevel()
        {
            if (!AssetDatabase.IsValidFolder(ProbeFolder))
            {
                AssetDatabase.CreateFolder(LevelsFolder, "Temp");
            }

            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(ProbePath) != null)
            {
                AssetDatabase.DeleteAsset(ProbePath);
            }

            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = ProbeName;
            def.levelName = ProbeName;
            def.width = ProbeRows[0].Length;
            def.height = ProbeRows.Length;
            def.cells = new List<TileType>(def.width * def.height);
            def.occupants = new List<OccupantType>(def.width * def.height);

            for (int y = 0; y < def.height; y++)
            {
                string row = ProbeRows[y];
                Assert.AreEqual(def.width, row.Length, $"Probe row {y} must match the probe width.");

                for (int x = 0; x < def.width; x++)
                {
                    switch (row[x])
                    {
                        case '#':
                            def.cells.Add(TileType.Wall);
                            def.occupants.Add(OccupantType.None);
                            break;
                        case 'G':
                            def.cells.Add(TileType.Goal);
                            def.occupants.Add(OccupantType.None);
                            break;
                        case 'P':
                            def.cells.Add(TileType.Floor);
                            def.occupants.Add(OccupantType.Player);
                            break;
                        case 'B':
                            def.cells.Add(TileType.Floor);
                            def.occupants.Add(OccupantType.Box);
                            break;
                        case '*':
                            def.cells.Add(TileType.Goal);
                            def.occupants.Add(OccupantType.Box);
                            break;
                        case '+':
                            def.cells.Add(TileType.Goal);
                            def.occupants.Add(OccupantType.Player);
                            break;
                        default:
                            def.cells.Add(TileType.Floor);
                            def.occupants.Add(OccupantType.None);
                            break;
                    }
                }
            }

            AssetDatabase.CreateAsset(def, ProbePath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<LevelDefinition>(ProbePath);
        }

        private static void ArmRequest(LevelDefinition level)
        {
            PlaytestRequest request = AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
            Assert.IsNotNull(request, "The persistent request asset must exist: " + PlaytestRequest.AssetPath);

            request.Level = level;
            EditorUtility.SetDirty(request);
            AssetDatabase.SaveAssets();
        }

        private static void ClearRequest()
        {
            PlaytestRequest request = AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
            if (request == null || request.Level == null)
            {
                return;
            }

            request.Level = null;
            EditorUtility.SetDirty(request);
            AssetDatabase.SaveAssets();
        }
    }
#endif
}

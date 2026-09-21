using System;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the editor-only bounded level analyzer: it finds a shortest-move solution and reports
    /// its move/push counts, only an exhaustive search may report unsolvable, a state or time budget
    /// hit is always inconclusive, and the real plate/door gating is searched through <see cref="Board"/>.
    /// </summary>
    public class LevelAnalyzerTests
    {
        /// <summary>Builds a level definition from ASCII rows (row 0 = top). See LevelAssetFactory.BuildLevel.</summary>
        private static LevelDefinition MakeLevel(params string[] rows)
        {
            return LevelAssetFactory.BuildLevel("AnalyzerTest", rows);
        }

        /// <summary>
        /// A level that cannot be solved in a single expansion (player starts away from a box that has
        /// to be walked around), used for the budget tests. Shortest solution is far longer than 1 move.
        /// </summary>
        private static LevelDefinition ModerateLevel()
        {
            return MakeLevel(
                "#######",
                "#P....#",
                "#.....#",
                "#...B.#",
                "#.....#",
                "#....G#",
                "#######");
        }

        [Test]
        public void Analyzer_KnownSolvableSmallLevel_ReturnsSolvableWithCounts()
        {
            // Player (1, 1), box (2, 1), goal (3, 1): one push to the right wins, exactly 1 move.
            LevelDefinition def = MakeLevel("#####", "#PBG#", "#####");

            AnalysisResult result = LevelAnalyzer.Analyze(def);

            Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
            Assert.AreEqual(1, result.SolutionMoves, "One push to the right is the shortest solution.");
            Assert.AreEqual(1, result.SolutionPushes, "That solution is a single push.");
            Assert.Greater(result.StatesExplored, 0, "At least the start state must have been expanded.");
        }

        [Test]
        public void Analyzer_KnownUnsolvableTinyLevel_ReturnsUnsolvable_Exhaustive()
        {
            // Box (1, 1) is wedged: pushing it right needs the player at (0, 1) (wall) and pushing it
            // down needs the player at (1, 0) (wall), so it can never reach the goal at (3, 1).
            LevelDefinition def = MakeLevel(
                "#####",
                "#B.G#",
                "#.P.#",
                "#####");

            AnalysisResult result = LevelAnalyzer.Analyze(def);

            Assert.AreEqual(AnalysisVerdict.Unsolvable, result.Verdict, result.Detail);
            Assert.AreEqual(-1, result.SolutionMoves, "An unsolvable result has no solution.");
            Assert.IsTrue(
                result.Detail.Contains("穷举"),
                "The verdict must come from an exhaustive search. Got: " + result.Detail);
        }

        [Test]
        public void Analyzer_ArtificialTinyBudget_ReturnsInconclusive()
        {
            AnalysisResult result = LevelAnalyzer.Analyze(ModerateLevel(), maxStates: 1);

            Assert.AreNotEqual(
                AnalysisVerdict.Unsolvable,
                result.Verdict,
                "A state-budget hit must never be reported as unsolvable.");
            Assert.AreEqual(AnalysisVerdict.Inconclusive, result.Verdict, result.Detail);
            Assert.AreEqual(-1, result.SolutionMoves, "No solution is known on a budget hit.");
        }

        [Test]
        public void Analyzer_TimeoutBudget_ReturnsInconclusive_NeverUnsolvable()
        {
            AnalysisResult result = LevelAnalyzer.Analyze(ModerateLevel(), maxTime: TimeSpan.Zero);

            Assert.AreNotEqual(
                AnalysisVerdict.Unsolvable,
                result.Verdict,
                "A time-budget hit must never be reported as unsolvable.");
            Assert.AreEqual(AnalysisVerdict.Inconclusive, result.Verdict, result.Detail);
            Assert.AreEqual(-1, result.SolutionMoves, "No solution is known on a budget hit.");
        }

        [Test]
        public void Analyzer_InitiallyCompleteLevel_ReturnsSolvable_ZeroMoves()
        {
            // Player (1, 1) and a box already sitting on the only goal at (2, 1): trivial 0-move win.
            LevelDefinition def = MakeLevel("#####", "#P*.#", "#####");

            AnalysisResult result = LevelAnalyzer.Analyze(def);

            Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
            Assert.AreEqual(0, result.SolutionMoves, "An already-complete level needs no moves.");
            Assert.AreEqual(0, result.SolutionPushes, "An already-complete level needs no pushes.");
        }

        [Test]
        public void Analyzer_PlateDoorLevel_SolvableThroughDoor()
        {
            // Player (2, 1); box1 (2, 2) over plate (2, 3); box2 (4, 2); door (5, 2); goal (6, 2).
            // Shortest solution: push box1 down onto the plate (1 move/1 push), step right to (3, 2)
            // (1 move), then push box2 right twice onto the goal (2 moves/2 pushes) = 4 moves / 3 pushes.
            // The door only opens while the plate is occupied, and box1 holds it open for the crossing.
            LevelDefinition def = MakeLevel(
                "########",
                "#.P....#",
                "#.B.BDG#",
                "#.T....#",
                "########");

            AnalysisResult result = LevelAnalyzer.Analyze(def);

            Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
            Assert.AreEqual(4, result.SolutionMoves, "Shortest-move solution is 4 moves.");
            Assert.AreEqual(3, result.SolutionPushes, "Box1 down once plus box2 right twice is 3 pushes.");
        }

        [Test]
        public void Analyzer_PlateDoorLevel_UnsolvableWhenDoorNeverOpens()
        {
            // Plate (1, 1) is sealed by walls on all four sides, so it can never be occupied: the door
            // (4, 2) stays closed forever. The goal (4, 1) can only be entered through that door, and
            // box (4, 3) cannot move at all, so the board is exhaustively unsolvable.
            LevelDefinition def = MakeLevel(
                "######",
                "#T##G#",
                "###.D#",
                "###.B#",
                "###.P#",
                "######");

            AnalysisResult result = LevelAnalyzer.Analyze(def);

            Assert.AreEqual(AnalysisVerdict.Unsolvable, result.Verdict, result.Detail);
            Assert.AreEqual(-1, result.SolutionMoves, "An unsolvable result has no solution.");
        }

        [Test]
        public void Analyzer_SolvableLevel_ReturnsReplayableSolution()
        {
            LevelDefinition def = MakeLevel("#####", "#PBG#", "#####");

            AnalysisResult result = LevelAnalyzer.Analyze(def);

            Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
            Assert.IsNotNull(result.Solution, "A solvable result must carry a solution list.");
            Assert.AreEqual(result.SolutionMoves, result.Solution.Count, "The replayed solution must match the reported move count.");

            var board = new Board(def);
            foreach (Direction direction in result.Solution)
            {
                Assert.IsTrue(board.TryMove(direction), $"Replay move {direction} must be accepted.");
            }

            Assert.IsTrue(board.IsComplete, "Replaying the solution must complete the level.");
            Assert.AreEqual(result.SolutionMoves, board.MoveCount, "Replay move count must equal the reported move count.");
            Assert.AreEqual(result.SolutionPushes, board.PushCount, "Replay push count must equal the reported push count.");
        }

        [Test]
        public void Analyzer_SolutionPath_Replays_OnEveryShippedLevel()
        {
            string[] paths =
            {
                "Assets/Sokoban/Levels/Level01.asset",
                "Assets/Sokoban/Levels/Level02.asset",
                "Assets/Sokoban/Levels/Level03.asset",
                "Assets/Sokoban/Levels/Level04.asset",
                "Assets/Sokoban/Levels/Level05.asset",
                "Assets/Sokoban/Levels/Level06.asset",
                "Assets/Sokoban/Levels/Level07.asset",
                "Assets/Sokoban/Levels/Level08.asset"
            };

            foreach (string path in paths)
            {
                LevelDefinition def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                Assert.IsNotNull(def, $"Level asset not found at {path}.");

                AnalysisResult result = LevelAnalyzer.Analyze(def);

                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, path + ": " + result.Detail);
                Assert.IsNotNull(result.Solution, path + ": a solvable result must carry a solution list.");

                var board = new Board(def);
                foreach (Direction direction in result.Solution)
                {
                    Assert.IsTrue(board.TryMove(direction), $"{path}: replay move {direction} must be accepted.");
                }

                Assert.IsTrue(board.IsComplete, $"{path}: replaying the solution must complete the level.");
                Assert.AreEqual(result.SolutionMoves, board.MoveCount, $"{path}: replay move count mismatch.");
                Assert.AreEqual(result.SolutionPushes, board.PushCount, $"{path}: replay push count mismatch.");
            }
        }

        [Test]
        public void Analyzer_NonSolvableResults_HaveEmptySolution()
        {
            LevelDefinition unsolvable = MakeLevel("#####", "#B.G#", "#.P.#", "#####");
            AnalysisResult unsolvableResult = LevelAnalyzer.Analyze(unsolvable);

            Assert.AreEqual(AnalysisVerdict.Unsolvable, unsolvableResult.Verdict, unsolvableResult.Detail);
            Assert.IsNotNull(unsolvableResult.Solution, "An unsolvable result still has a non-null Solution.");
            Assert.IsEmpty(unsolvableResult.Solution, "An unsolvable result has an empty Solution.");

            AnalysisResult stateBudgetResult = LevelAnalyzer.Analyze(ModerateLevel(), maxStates: 1);
            Assert.AreEqual(AnalysisVerdict.Inconclusive, stateBudgetResult.Verdict, stateBudgetResult.Detail);
            Assert.IsNotNull(stateBudgetResult.Solution, "A state-budget result still has a non-null Solution.");
            Assert.IsEmpty(stateBudgetResult.Solution, "A state-budget result has an empty Solution.");

            AnalysisResult timeBudgetResult = LevelAnalyzer.Analyze(ModerateLevel(), maxTime: TimeSpan.Zero);
            Assert.AreEqual(AnalysisVerdict.Inconclusive, timeBudgetResult.Verdict, timeBudgetResult.Detail);
            Assert.IsNotNull(timeBudgetResult.Solution, "A time-budget result still has a non-null Solution.");
            Assert.IsEmpty(timeBudgetResult.Solution, "A time-budget result has an empty Solution.");
        }

        [Test]
        public void Analyzer_InitiallyCompleteLevel_HasEmptySolution()
        {
            LevelDefinition def = MakeLevel("#####", "#P*.#", "#####");

            AnalysisResult result = LevelAnalyzer.Analyze(def);

            Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
            Assert.AreEqual(0, result.SolutionMoves, "An already-complete level needs no moves.");
            Assert.IsNotNull(result.Solution, "An already-complete result still has a non-null Solution.");
            Assert.IsEmpty(result.Solution, "The zero-move solution is an empty path.");
        }

        /// <summary>
        /// Shared geometry for the multi-group analyzer tests (8x7). The single goal (6, 2) is walled
        /// in on three sides, so the door at (5, 2) is the only way into it. The pocket cell (2, 5) is
        /// sealed by walls on all four sides and can never be occupied, and the spare door (4, 5) sits
        /// in the open lower-right area. The plate (2, 3) is reachable from the player start.
        /// Each test promotes a subset of these cells to group B via <c>def.groupIds</c>; every other
        /// cell (including the sealed pocket) stays group A.
        /// '#'=Wall '.'=Floor 'G'=Goal 'T'=Plate 'D'=Door 'P'=Player 'B'=Box
        /// </summary>
        private static readonly string[] MultiGroupRows =
        {
            "########",
            "#.P...##",
            "#.B.BDG#",
            "#.T...##",
            "###....#",
            "##T#D..#",
            "########"
        };

        [Test]
        public void Analyzer_TwoGroupLevel_SolvableThroughSpecificDoor_Replays()
        {
            // Group B: the reachable plate (2, 3) opens the door (5, 2) that is the only way into the
            // goal (6, 2). Group A: the sealed plate (2, 5) plus the irrelevant door (4, 5).
            // The partition matters: under the old global "every plate occupied -> every door open"
            // rule the sealed group-A plate could never be occupied, so the door would never open and
            // this level would be reported unsolvable. Per-group semantics open the group-B door as
            // soon as the group-B plate holds, regardless of group A.
            // Shortest solution: push box1 down onto the plate (1 move/1 push), step right to (3, 2)
            // (1 move), then push box2 right twice onto the goal (2 moves/2 pushes) = 4 moves / 3 pushes.
            LevelDefinition def = null;
            try
            {
                def = MakeLevel(MultiGroupRows);
                def.groupIds[def.Index(2, 3)] = 1; // group-B plate
                def.groupIds[def.Index(5, 2)] = 1; // group-B door on the only path to the goal

                AnalysisResult result = LevelAnalyzer.Analyze(def);

                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
                Assert.AreEqual(4, result.SolutionMoves, result.Detail);
                Assert.AreEqual(3, result.SolutionPushes, result.Detail);
                Assert.IsNotNull(result.Solution, "A solvable result must carry a solution list.");

                // Replay through a fresh real Board: it enforces the per-group door rule, so an
                // accepted replay proves the solution genuinely opened the group-B door.
                var board = new Board(def);
                foreach (Direction direction in result.Solution)
                {
                    Assert.IsTrue(board.TryMove(direction), $"Replay move {direction} must be accepted.");
                }

                Assert.IsTrue(board.IsComplete, "Replaying the per-group solution must complete the level.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void Analyzer_GroupA_Satisfied_DoesNotOpen_GroupB_Door_Unsolvable()
        {
            // Group A: the reachable plate (2, 3), which the player can satisfy by pushing box1 onto
            // it, plus the irrelevant door (4, 5). Group B: the sealed plate (2, 5) plus the door
            // (5, 2) that is the only way into the goal (6, 2).
            // Satisfying group A must NOT open the group-B door: the group-B plate can never be
            // occupied, so the goal stays unreachable, the box can never be pushed onto (5, 2) or
            // (6, 2), and the tiny state space is exhausted. A global rule (any satisfied plate opens
            // every door, or a door read from the wrong group) would open the door as soon as group A
            // is satisfied and report the level solvable, so this is the isolation proof.
            LevelDefinition def = null;
            try
            {
                def = MakeLevel(MultiGroupRows);
                def.groupIds[def.Index(2, 5)] = 1; // group-B sealed plate
                def.groupIds[def.Index(5, 2)] = 1; // group-B door on the only path to the goal
                // Plate (2, 3) and door (4, 5) stay group A.

                AnalysisResult result = LevelAnalyzer.Analyze(def);

                Assert.AreEqual(AnalysisVerdict.Unsolvable, result.Verdict, result.Detail);
                Assert.AreEqual(-1, result.SolutionMoves, "An unsolvable result has no solution.");
                Assert.IsTrue(
                    result.Detail.Contains("穷举"),
                    "The verdict must come from an exhaustive search. Got: " + result.Detail);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void Analyzer_TwoGroupSolution_MoveCountMatchesReplay()
        {
            LevelDefinition def = null;
            try
            {
                def = MakeLevel(MultiGroupRows);
                def.groupIds[def.Index(2, 3)] = 1; // group-B plate
                def.groupIds[def.Index(5, 2)] = 1; // group-B door on the only path to the goal

                AnalysisResult result = LevelAnalyzer.Analyze(def);

                Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
                Assert.AreEqual(
                    result.SolutionMoves,
                    result.Solution.Count,
                    "The solution path must contain exactly one entry per move.");

                var board = new Board(def);
                foreach (Direction direction in result.Solution)
                {
                    Assert.IsTrue(board.TryMove(direction), $"Replay move {direction} must be accepted.");
                }

                Assert.IsTrue(board.IsComplete, "Replaying the solution must complete the level.");
                Assert.AreEqual(result.SolutionMoves, board.MoveCount, "Replay move count must equal the reported move count.");
                Assert.AreEqual(result.SolutionPushes, board.PushCount, "Replay push count must equal the reported push count.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(def);
            }
        }
    }
}

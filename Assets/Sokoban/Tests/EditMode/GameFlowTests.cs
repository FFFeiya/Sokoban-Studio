using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers restart/reconstruction semantics, win reporting, counter restore and the shipped
    /// catalog/level content of the menu → select → gameplay flow.
    /// </summary>
    public class GameFlowTests
    {
        private const string Level01Path = "Assets/Sokoban/Levels/Level01.asset";
        private const string Level02Path = "Assets/Sokoban/Levels/Level02.asset";
        private const string Level03Path = "Assets/Sokoban/Levels/Level03.asset";
        private const string Level04Path = "Assets/Sokoban/Levels/Level04.asset";
        private const string Level05Path = "Assets/Sokoban/Levels/Level05.asset";
        private const string Level06Path = "Assets/Sokoban/Levels/Level06.asset";
        private const string Level07Path = "Assets/Sokoban/Levels/Level07.asset";
        private const string Level08Path = "Assets/Sokoban/Levels/Level08.asset";
        private const string CatalogPath = "Assets/Sokoban/Levels/LevelCatalog.asset";

        /// <summary>
        /// Builds a level definition from ASCII rows. Row 0 is the top row of the layout and
        /// maps to y = 0 (dense index = y * width + x).
        /// '#'=Wall  '.'=Floor  'G'=Goal  'P'=Player on floor  'B'=Box on floor
        /// '*'=Box on goal  '+'=Player on goal
        /// </summary>
        private static LevelDefinition MakeLevel(params string[] rows)
        {
            Assert.IsNotNull(rows, "rows must not be null.");
            Assert.Greater(rows.Length, 0, "A level needs at least one row.");

            int height = rows.Length;
            int width = rows[0].Length;

            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.levelName = "Test";
            def.width = width;
            def.height = height;
            def.cells = new List<TileType>(width * height);
            def.occupants = new List<OccupantType>(width * height);

            for (int y = 0; y < height; y++)
            {
                string row = rows[y];
                Assert.AreEqual(width, row.Length, $"Row {y} does not match the width of row 0.");

                for (int x = 0; x < width; x++)
                {
                    TileType tile;
                    OccupantType occupant;

                    switch (row[x])
                    {
                        case '#':
                            tile = TileType.Wall;
                            occupant = OccupantType.None;
                            break;
                        case '.':
                            tile = TileType.Floor;
                            occupant = OccupantType.None;
                            break;
                        case 'G':
                            tile = TileType.Goal;
                            occupant = OccupantType.None;
                            break;
                        case 'P':
                            tile = TileType.Floor;
                            occupant = OccupantType.Player;
                            break;
                        case 'B':
                            tile = TileType.Floor;
                            occupant = OccupantType.Box;
                            break;
                        case '*':
                            tile = TileType.Goal;
                            occupant = OccupantType.Box;
                            break;
                        case '+':
                            tile = TileType.Goal;
                            occupant = OccupantType.Player;
                            break;
                        default:
                            throw new ArgumentException($"Unknown level character '{row[x]}' at ({x}, {y}).");
                    }

                    def.cells.Add(tile);
                    def.occupants.Add(occupant);
                }
            }

            return def;
        }

        private static LevelDefinition LoadLevel(string assetPath)
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            Assert.IsNotNull(level, $"Level asset not found at {assetPath}.");
            return level;
        }

        [Test]
        public void ReconstructingBoard_FromSameDefinition_RestoresInitialStateAndZeroCounters()
        {
            LevelDefinition def = MakeLevel(
                "#####",
                "#PB.#",
                "#..G#",
                "#####");

            // GameBootstrap.Restart equivalent: build a fresh Board from the same definition.
            var pristine = new Board(def);
            (int px, int py) = pristine.PlayerPosition;
            var initialBoxes = new List<(int x, int y)>(pristine.BoxPositions);

            var played = new Board(def);
            Assert.IsTrue(played.TryMove(Direction.Right), "Pushing the box right must be accepted.");
            Assert.IsTrue(played.TryMove(Direction.Down), "Walking down must be accepted.");
            Assert.AreEqual(2, played.MoveCount);
            Assert.AreEqual(1, played.PushCount);

            var restarted = new Board(def);

            Assert.AreEqual(px, restarted.PlayerPosition.x, "Restart must restore the player x.");
            Assert.AreEqual(py, restarted.PlayerPosition.y, "Restart must restore the player y.");
            Assert.AreEqual(0, restarted.MoveCount, "Restart must reset the move counter.");
            Assert.AreEqual(0, restarted.PushCount, "Restart must reset the push counter.");
            CollectionAssert.AreEquivalent(initialBoxes, restarted.BoxPositions, "Restart must restore box positions.");
            Assert.IsFalse(restarted.IsComplete, "A restarted level is not complete.");
        }

        [Test]
        public void TryMove_ReportsCompletion_ForCompletingMove()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#PBG#",
                "#####"));

            bool moved = board.TryMove(Direction.Right, out bool completed);

            Assert.IsTrue(moved);
            Assert.IsTrue(completed, "The win check must be reported with the accepted move.");
            Assert.IsTrue(board.IsComplete);
        }

        [Test]
        public void TryMove_ReportsNoCompletion_ForPlainMoveOrRejectedMove()
        {
            var board = new Board(MakeLevel(
                "######",
                "#P.BG#",
                "######"));

            Assert.IsTrue(board.TryMove(Direction.Right, out bool completed));
            Assert.IsFalse(completed, "A move that does not cover every goal is not a completion.");
            Assert.IsFalse(board.IsComplete);

            var walled = new Board(MakeLevel(
                "#####",
                "#P#B#",
                "#.G.#",
                "#####"));

            Assert.IsFalse(walled.TryMove(Direction.Up, out bool rejectedCompleted));
            Assert.IsFalse(rejectedCompleted, "A rejected move must never report completion.");
        }

        [Test]
        public void RestoreSnapshot_ReturnsCountersToSnapshotValues_AfterPush()
        {
            var board = new Board(MakeLevel(
                "######",
                "#PB..#",
                "#..G.#",
                "######"));

            Assert.IsTrue(board.TryMove(Direction.Right), "Pushing the box must be accepted.");
            BoardSnapshot snapshot = board.CreateSnapshot();
            Assert.AreEqual(1, snapshot.MoveCount);
            Assert.AreEqual(1, snapshot.PushCount);

            Assert.IsTrue(board.TryMove(Direction.Down), "Walking down must be accepted.");
            Assert.AreEqual(2, board.MoveCount);

            board.RestoreSnapshot(snapshot);

            Assert.AreEqual(snapshot.MoveCount, board.MoveCount, "Undo must restore the move counter.");
            Assert.AreEqual(snapshot.PushCount, board.PushCount, "Undo must restore the push counter.");
            Assert.AreEqual(1, board.MoveCount);
            Assert.AreEqual(1, board.PushCount);
        }

        [Test]
        public void Catalog_ContainsLevelsInOrder_WithoutNulls()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, $"Level catalog asset not found at {CatalogPath}.");
            Assert.IsNotNull(catalog.levels, "Catalog levels list must be initialized.");
            Assert.AreEqual(8, catalog.levels.Count, "The shipped catalog must hold exactly eight levels.");

            for (int i = 0; i < catalog.levels.Count; i++)
            {
                Assert.IsNotNull(catalog.levels[i], $"Catalog entry {i} is null.");
            }

            Assert.AreEqual(Level01Path, AssetDatabase.GetAssetPath(catalog.levels[0]), "Catalog entry 0 must be Level01.");
            Assert.AreEqual(Level02Path, AssetDatabase.GetAssetPath(catalog.levels[1]), "Catalog entry 1 must be Level02.");
            Assert.AreEqual(Level03Path, AssetDatabase.GetAssetPath(catalog.levels[2]), "Catalog entry 2 must be Level03.");
            Assert.AreEqual(Level04Path, AssetDatabase.GetAssetPath(catalog.levels[3]), "Catalog entry 3 must be Level04.");
            Assert.AreEqual(Level05Path, AssetDatabase.GetAssetPath(catalog.levels[4]), "Catalog entry 4 must be Level05.");
            Assert.AreEqual(Level06Path, AssetDatabase.GetAssetPath(catalog.levels[5]), "Catalog entry 5 must be Level06.");
            Assert.AreEqual(Level07Path, AssetDatabase.GetAssetPath(catalog.levels[6]), "Catalog entry 6 must be Level07.");
            Assert.AreEqual(Level08Path, AssetDatabase.GetAssetPath(catalog.levels[7]), "Catalog entry 7 must be Level08.");
        }

        /// <summary>
        /// Designed shortest solution of every shipped level in catalog order, with its push count.
        /// Each was found by an exhaustive shortest-move search when the level was authored. The
        /// tests below replay it through the real <see cref="Board"/> and require the editor analyzer
        /// to reach the same optimum, so a content edit that changes a level's difficulty fails here.
        /// </summary>
        private static readonly object[] ShippedSolutions =
        {
            new object[] { 1, "DDLDRRUUURR", 5 },
            new object[] { 2, "LLLDDRRRRRRDLLLURRRRUULLLLLL", 7 },
            new object[] { 3, "UULLULUURDDURRDDUURURDD", 7 },
            new object[] { 4, "LUURRRRURDLLLUULDLDRRRRDRULLLDDLULURRRR", 17 },
            new object[] { 5, "LDDRRURDLLLURRDRDDLDRRULUULURRURRDDLLULDDDRDLL", 16 },
            new object[] { 6, "ULDLDRUUUULURDDRRRRDRUUDLLLUULLDDDDRRDLL", 14 },
            new object[] { 7, "UUUUDRURUULDDULLLRRDRDDRUULURRRLLDDDLD", 16 },
            new object[] { 8, "ULDRRRDRURRRDRULLLLLLLDDLULURRULULDDDURRRRRRLLUURDDDURRR", 21 }
        };

        private static string ShippedLevelPath(int slot)
        {
            return $"Assets/Sokoban/Levels/Level{slot:00}.asset";
        }

        private static Direction ParseDirection(char c)
        {
            switch (c)
            {
                case 'U': return Direction.Up;
                case 'D': return Direction.Down;
                case 'L': return Direction.Left;
                case 'R': return Direction.Right;
                default: throw new ArgumentException($"Unknown direction '{c}'.");
            }
        }

        [Test]
        public void ShippedLevel_HasOnePlayerAndMatchingBoxGoalCounts([NUnit.Framework.Range(1, 8)] int slot)
        {
            LevelDefinition level = LoadLevel(ShippedLevelPath(slot));

            int goals = 0;
            int boxes = 0;
            int players = 0;

            for (int i = 0; i < level.cells.Count; i++)
            {
                if (level.cells[i] == TileType.Goal)
                {
                    goals++;
                }

                if (level.occupants[i] == OccupantType.Box)
                {
                    boxes++;
                }
                else if (level.occupants[i] == OccupantType.Player)
                {
                    players++;
                }
            }

            Assert.AreEqual(1, players, $"Level{slot:00} needs exactly one player.");
            Assert.That(boxes, Is.InRange(2, 4), $"Level{slot:00} is designed with two to four boxes.");
            Assert.AreEqual(goals, boxes, $"Level{slot:00} needs boxes == goals.");
            Assert.IsEmpty(LevelValidator.ValidateDetailed(level), $"Level{slot:00} must have no validator errors or warnings.");

            var board = new Board(level);
            Assert.IsFalse(board.IsComplete, $"Level{slot:00} must not start already complete.");
        }

        [TestCaseSource(nameof(ShippedSolutions))]
        public void ShippedLevel_KnownSolution_Completes(int slot, string solution, int pushes)
        {
            var board = new Board(LoadLevel(ShippedLevelPath(slot)));

            var rejected = new List<int>();
            for (int i = 0; i < solution.Length; i++)
            {
                if (!board.TryMove(ParseDirection(solution[i])))
                {
                    rejected.Add(i);
                }
            }

            Assert.IsEmpty(rejected, "Every solution move must be accepted; rejected indices: " + string.Join(",", rejected));
            Assert.AreEqual(solution.Length, board.MoveCount, "Move count must match the solution length.");
            Assert.AreEqual(pushes, board.PushCount, $"Level{slot:00} solution push count.");
            Assert.IsTrue(board.IsComplete, $"Following the known solution must complete Level{slot:00}.");
        }

        [TestCaseSource(nameof(ShippedSolutions))]
        public void ShippedLevel_AnalyzerFindsDesignedOptimum(int slot, string solution, int pushes)
        {
            AnalysisResult result = LevelAnalyzer.Analyze(LoadLevel(ShippedLevelPath(slot)));

            Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, $"Level{slot:00} must be analyzer-solvable: " + result.Detail);
            Assert.AreEqual(solution.Length, result.SolutionMoves, $"Level{slot:00} shortest solution length. " + result.Detail);
            Assert.AreEqual(pushes, result.SolutionPushes, $"Level{slot:00} shortest solution pushes. " + result.Detail);
        }

        /// <summary>
        /// Every plate/door level must actually need its doors: with each door turned into a wall the
        /// level becomes exhaustively unsolvable, so no door is decorative.
        /// </summary>
        [Test]
        public void PlateDoorLevel_WithDoorsWalledOff_IsUnsolvable([NUnit.Framework.Range(5, 8)] int slot)
        {
            LevelDefinition level = UnityEngine.Object.Instantiate(LoadLevel(ShippedLevelPath(slot)));
            try
            {
                int doors = 0;
                for (int i = 0; i < level.cells.Count; i++)
                {
                    if (level.cells[i] == TileType.Door)
                    {
                        level.cells[i] = TileType.Wall;
                        doors++;
                    }
                }

                Assert.Greater(doors, 0, $"Level{slot:00} is a plate/door level and must have a door.");

                AnalysisResult result = LevelAnalyzer.Analyze(level);
                Assert.AreEqual(AnalysisVerdict.Unsolvable, result.Verdict, $"Level{slot:00} must need its doors: " + result.Detail);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// Levels 07 and 08 use both plate/door groups, and the split matters: with every plate and
        /// door merged into group A, each door needs every plate held at once and the level becomes
        /// exhaustively unsolvable.
        /// </summary>
        [Test]
        public void TwoGroupLevel_MergedIntoOneGroup_IsUnsolvable([Values(7, 8)] int slot)
        {
            LevelDefinition level = UnityEngine.Object.Instantiate(LoadLevel(ShippedLevelPath(slot)));
            try
            {
                Assert.IsNotNull(level.groupIds, $"Level{slot:00} must author group ids.");
                Assert.Contains(1, level.groupIds, $"Level{slot:00} must use group B.");

                for (int i = 0; i < level.groupIds.Count; i++)
                {
                    level.groupIds[i] = 0;
                }

                AnalysisResult result = LevelAnalyzer.Analyze(level);
                Assert.AreEqual(AnalysisVerdict.Unsolvable, result.Verdict, $"Level{slot:00} must depend on its A/B split: " + result.Detail);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(level);
            }
        }
    }
}

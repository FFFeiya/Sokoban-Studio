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

        private static bool BoxAt(Board board, int x, int y)
        {
            foreach ((int bx, int by) in board.BoxPositions)
            {
                if (bx == x && by == y)
                {
                    return true;
                }
            }

            return false;
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
                "###",
                "#P#",
                "###"));

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

        [Test]
        public void Level02_HasOnePlayerAndMatchingNonNullBoxGoalCounts()
        {
            LevelDefinition level = LoadLevel(Level02Path);

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

            Assert.AreEqual(1, players, "Level02 needs exactly one player.");
            Assert.Greater(boxes, 0, "Level02 needs at least one box.");
            Assert.AreEqual(goals, boxes, "Level02 needs boxes == goals.");

            var board = new Board(level);
            Assert.IsFalse(board.IsComplete, "Level02 must not start already complete.");
        }

        [Test]
        public void Level02_KnownSolutionSequence_CompletesLevel()
        {
            LevelDefinition level = LoadLevel(Level02Path);
            var board = new Board(level);

            // Push the left box up twice and left onto its goal, walk around the right side of the
            // board, then push the remaining box up twice onto the second goal.
            Direction[] solution =
            {
                Direction.Up,
                Direction.Up,
                Direction.Right,
                Direction.Up,
                Direction.Left,
                Direction.Right,
                Direction.Right,
                Direction.Down,
                Direction.Down,
                Direction.Down,
                Direction.Left,
                Direction.Up,
                Direction.Up
            };

            var rejected = new List<int>();
            for (int i = 0; i < solution.Length; i++)
            {
                if (!board.TryMove(solution[i]))
                {
                    rejected.Add(i);
                }
            }

            Assert.IsEmpty(rejected, "Every solution move must be accepted; rejected indices: " + string.Join(",", rejected));
            Assert.AreEqual(solution.Length, board.MoveCount, "Move count must match the solution length.");
            Assert.AreEqual(5, board.PushCount, "The solution pushes one box three times and the other twice.");
            Assert.IsTrue(board.IsComplete, "Following the known solution must complete Level02.");
        }

        [Test]
        public void Level03_HasOnePlayerAndMatchingNonNullBoxGoalCounts()
        {
            LevelDefinition level = LoadLevel(Level03Path);

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

            Assert.AreEqual(1, players, "Level03 needs exactly one player.");
            Assert.Greater(boxes, 0, "Level03 needs at least one box.");
            Assert.AreEqual(goals, boxes, "Level03 needs boxes == goals.");

            var board = new Board(level);
            Assert.IsFalse(board.IsComplete, "Level03 must not start already complete.");
        }

        [Test]
        public void Level03_KnownSolutionSequence_CompletesLevel()
        {
            LevelDefinition level = LoadLevel(Level03Path);
            var board = new Board(level);

            // Walk up over the lower goal, loop around the top, push one box down onto that goal,
            // then cross the board and push the remaining box left onto the second goal.
            Direction[] solution =
            {
                Direction.Up,
                Direction.Up,
                Direction.Left,
                Direction.Up,
                Direction.Up,
                Direction.Right,
                Direction.Down,
                Direction.Up,
                Direction.Right,
                Direction.Right,
                Direction.Down,
                Direction.Left,
                Direction.Left
            };

            var rejected = new List<int>();
            for (int i = 0; i < solution.Length; i++)
            {
                if (!board.TryMove(solution[i]))
                {
                    rejected.Add(i);
                }
            }

            Assert.IsEmpty(rejected, "Every solution move must be accepted; rejected indices: " + string.Join(",", rejected));
            Assert.AreEqual(solution.Length, board.MoveCount, "Move count must match the solution length.");
            Assert.AreEqual(3, board.PushCount, "The Level03 solution pushes two boxes, one of them twice.");
            Assert.IsTrue(board.IsComplete, "Following the known solution must complete Level03.");
        }

        [Test]
        public void Level04_HasOnePlayerAndMatchingNonNullBoxGoalCounts()
        {
            LevelDefinition level = LoadLevel(Level04Path);

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

            Assert.AreEqual(1, players, "Level04 needs exactly one player.");
            Assert.Greater(boxes, 0, "Level04 needs at least one box.");
            Assert.AreEqual(goals, boxes, "Level04 needs boxes == goals.");

            var board = new Board(level);
            Assert.IsFalse(board.IsComplete, "Level04 must not start already complete.");
        }

        [Test]
        public void Level04_KnownSolutionSequence_CompletesLevel()
        {
            LevelDefinition level = LoadLevel(Level04Path);
            var board = new Board(level);

            // Walk around the left of the internal wall band, push the left box up onto the top
            // row, swing the player through the gap on the right and push both boxes onto goals.
            Direction[] solution =
            {
                Direction.Up,
                Direction.Left,
                Direction.Left,
                Direction.Up,
                Direction.Up,
                Direction.Right,
                Direction.Right,
                Direction.Up,
                Direction.Right,
                Direction.Down,
                Direction.Right,
                Direction.Up,
                Direction.Left,
                Direction.Up,
                Direction.Left
            };

            var rejected = new List<int>();
            for (int i = 0; i < solution.Length; i++)
            {
                if (!board.TryMove(solution[i]))
                {
                    rejected.Add(i);
                }
            }

            Assert.IsEmpty(rejected, "Every solution move must be accepted; rejected indices: " + string.Join(",", rejected));
            Assert.AreEqual(solution.Length, board.MoveCount, "Move count must match the solution length.");
            Assert.AreEqual(4, board.PushCount, "The Level04 solution pushes each box twice.");
            Assert.IsTrue(board.IsComplete, "Following the known solution must complete Level04.");
        }

        [Test]
        public void Level05_KnownSolution_Completes()
        {
            LevelDefinition level = LoadLevel(Level05Path);
            var board = new Board(level);

            // Push the upper box left onto the plate to hold the door open, cross the door corridor
            // to push the lower box right onto its goal, then return and move the plate box onto the
            // upper goal.
            Direction[] solution =
            {
                Direction.Down,
                Direction.Left,
                Direction.Right,
                Direction.Down,
                Direction.Down,
                Direction.Right,
                Direction.Left,
                Direction.Up,
                Direction.Up,
                Direction.Left,
                Direction.Up,
                Direction.Left,
                Direction.Left,
                Direction.Down,
                Direction.Right,
                Direction.Right
            };

            var rejected = new List<int>();
            for (int i = 0; i < solution.Length; i++)
            {
                if (!board.TryMove(solution[i]))
                {
                    rejected.Add(i);
                }
            }

            Assert.IsEmpty(rejected, "Every solution move must be accepted; rejected indices: " + string.Join(",", rejected));
            Assert.AreEqual(solution.Length, board.MoveCount, "Move count must match the solution length.");
            Assert.AreEqual(4, board.PushCount, "The Level05 solution pushes four times.");
            Assert.IsTrue(board.IsComplete, "Following the known solution must complete Level05.");
        }

        [Test]
        public void Level06_KnownSolution_Completes()
        {
            LevelDefinition level = LoadLevel(Level06Path);
            var board = new Board(level);

            // Plate errand up top, through the mid door, then the two-box planning room below.
            Direction[] solution =
            {
                Direction.Down,
                Direction.Down,
                Direction.Right,
                Direction.Up,
                Direction.Down,
                Direction.Right,
                Direction.Right,
                Direction.Right,
                Direction.Right,
                Direction.Right,
                Direction.Left,
                Direction.Up,
                Direction.Left,
                Direction.Left,
                Direction.Left,
                Direction.Left,
                Direction.Left,
                Direction.Up,
                Direction.Right,
                Direction.Right,
                Direction.Right,
                Direction.Right,
                Direction.Right
            };

            var rejected = new List<int>();
            for (int i = 0; i < solution.Length; i++)
            {
                if (!board.TryMove(solution[i]))
                {
                    rejected.Add(i);
                }
            }

            Assert.IsEmpty(rejected, "Every solution move must be accepted; rejected indices: " + string.Join(",", rejected));
            Assert.AreEqual(solution.Length, board.MoveCount, "Move count must match the solution length.");
            Assert.AreEqual(8, board.PushCount, "The Level06 solution pushes eight times.");
            Assert.IsTrue(board.IsComplete, "Following the known solution must complete Level06.");
        }

        [Test]
        public void Level07_KnownSolution_Completes()
        {
            LevelDefinition level = LoadLevel(Level07Path);
            var board = new Board(level);

            // Park the upper box on the plate to hold the door open, push the lower box down through
            // the door onto the lower goal, then walk back around and push the plate box right to the
            // upper goal.
            Direction[] solution =
            {
                Direction.Down,
                Direction.Left,
                Direction.Right,
                Direction.Down,
                Direction.Down,
                Direction.Up,
                Direction.Left,
                Direction.Left,
                Direction.Left,
                Direction.Up,
                Direction.Right,
                Direction.Right,
                Direction.Right
            };

            var rejected = new List<int>();
            for (int i = 0; i < solution.Length; i++)
            {
                if (!board.TryMove(solution[i]))
                {
                    rejected.Add(i);
                }
            }

            Assert.IsEmpty(rejected, "Every solution move must be accepted; rejected indices: " + string.Join(",", rejected));
            Assert.AreEqual(solution.Length, board.MoveCount, "Move count must match the solution length.");
            Assert.AreEqual(6, board.PushCount, "The Level07 solution pushes six times.");
            Assert.IsTrue(board.IsComplete, "Following the known solution must complete Level07.");
        }

        [Test]
        public void Level08_KnownSolution_Completes()
        {
            LevelDefinition level = LoadLevel(Level08Path);
            var board = new Board(level);

            // Walk around to arm the plate with the first box, cross the door, route the lower box
            // right onto its goal, come back and push the plate box right onto the upper goal.
            Direction[] solution =
            {
                Direction.Left,
                Direction.Left,
                Direction.Left,
                Direction.Down,
                Direction.Right,
                Direction.Right,
                Direction.Down,
                Direction.Right,
                Direction.Down,
                Direction.Down,
                Direction.Right,
                Direction.Right,
                Direction.Right,
                Direction.Left,
                Direction.Left,
                Direction.Left,
                Direction.Up,
                Direction.Up,
                Direction.Left,
                Direction.Up,
                Direction.Right,
                Direction.Right
            };

            var rejected = new List<int>();
            for (int i = 0; i < solution.Length; i++)
            {
                if (!board.TryMove(solution[i]))
                {
                    rejected.Add(i);
                }
            }

            Assert.IsEmpty(rejected, "Every solution move must be accepted; rejected indices: " + string.Join(",", rejected));
            Assert.AreEqual(solution.Length, board.MoveCount, "Move count must match the solution length.");
            Assert.AreEqual(7, board.PushCount, "The Level08 solution pushes seven times.");
            Assert.IsTrue(board.IsComplete, "Following the known solution must complete Level08.");
        }

        [Test]
        public void Level05_And_Level06_AnalyzerSolvable()
        {
            LevelDefinition level05 = LoadLevel(Level05Path);
            AnalysisResult result05 = LevelAnalyzer.Analyze(level05);
            Assert.AreEqual(AnalysisVerdict.Solvable, result05.Verdict, "Level05 must be analyzer-solvable: " + result05.Detail);
            Assert.AreEqual(16, result05.SolutionMoves, "Level05 shortest solution must be 16 moves. " + result05.Detail);
            Assert.AreEqual(4, result05.SolutionPushes, "Level05 shortest solution must be 4 pushes. " + result05.Detail);

            LevelDefinition level06 = LoadLevel(Level06Path);
            AnalysisResult result06 = LevelAnalyzer.Analyze(level06);
            Assert.AreEqual(AnalysisVerdict.Solvable, result06.Verdict, "Level06 must be analyzer-solvable: " + result06.Detail);
            Assert.AreEqual(23, result06.SolutionMoves, "Level06 shortest solution must be 23 moves. " + result06.Detail);
            Assert.AreEqual(8, result06.SolutionPushes, "Level06 shortest solution must be 8 pushes. " + result06.Detail);
        }

        [Test]
        public void Level07_And_Level08_AnalyzerSolvable()
        {
            LevelDefinition level07 = LoadLevel(Level07Path);
            AnalysisResult result07 = LevelAnalyzer.Analyze(level07);
            Assert.AreEqual(AnalysisVerdict.Solvable, result07.Verdict, "Level07 must be analyzer-solvable: " + result07.Detail);
            Assert.AreEqual(13, result07.SolutionMoves, "Level07 shortest solution must be 13 moves. " + result07.Detail);
            Assert.AreEqual(6, result07.SolutionPushes, "Level07 shortest solution must be 6 pushes. " + result07.Detail);

            LevelDefinition level08 = LoadLevel(Level08Path);
            AnalysisResult result08 = LevelAnalyzer.Analyze(level08);
            Assert.AreEqual(AnalysisVerdict.Solvable, result08.Verdict, "Level08 must be analyzer-solvable: " + result08.Detail);
            Assert.AreEqual(22, result08.SolutionMoves, "Level08 shortest solution must be 22 moves. " + result08.Detail);
            Assert.AreEqual(7, result08.SolutionPushes, "Level08 shortest solution must be 7 pushes. " + result08.Detail);
        }

        [Test]
        public void Level05_PlateDoor_FirstDoorCrossing_IsOpenAndPlateHeld()
        {
            LevelDefinition level = LoadLevel(Level05Path);
            AnalysisResult result = LevelAnalyzer.Analyze(level);
            Assert.AreEqual(AnalysisVerdict.Solvable, result.Verdict, result.Detail);
            Assert.IsNotNull(result.Solution, "A solvable result must carry a solution.");

            int plateX = -1, plateY = -1;
            for (int i = 0; i < level.cells.Count; i++)
            {
                if (level.cells[i] == TileType.Plate) { plateX = i % level.width; plateY = i / level.width; }
            }
            Assert.GreaterOrEqual(plateX, 0, "Level05 must have a plate tile.");

            var board = new Board(level);
            bool crossedDoor = false;
            bool doorOpenAtCrossing = false;
            bool plateHeldAtCrossing = false;

            foreach (Direction direction in result.Solution)
            {
                (int dx, int dy) = direction.ToOffset();
                (int px, int py) = board.PlayerPosition;
                int targetX = px + dx;
                int targetY = py + dy;

                if (!crossedDoor && board.GetTile(targetX, targetY) == TileType.Door)
                {
                    crossedDoor = true;
                    doorOpenAtCrossing = board.IsDoorOpen(targetX, targetY);
                    plateHeldAtCrossing = BoxAt(board, plateX, plateY);
                }

                Assert.IsTrue(board.TryMove(direction), $"Replay move {direction} must be accepted.");
            }

            Assert.IsTrue(board.IsComplete, "Replaying the solution must complete Level05.");
            Assert.IsTrue(crossedDoor, "The Level05 solution must cross the door cell.");
            Assert.IsTrue(doorOpenAtCrossing, "The door must be open at the first crossing.");
            Assert.IsTrue(plateHeldAtCrossing, "The plate must be held by a box at the first door crossing.");
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Verifies the shipped sample level asset and the authored level data structure.
    /// These tests read real asset data; they do not fabricate playthrough evidence.
    /// </summary>
    public class BootstrapDataTests
    {
        private const string Level01Path = "Assets/Sokoban/Levels/Level01.asset";

        private static LevelDefinition LoadLevel01()
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Path);
            Assert.IsNotNull(level, $"Sample level asset not found at {Level01Path}.");
            return level;
        }

        private static int CountTile(LevelDefinition level, TileType tile)
        {
            int count = 0;
            foreach (TileType t in level.cells)
            {
                if (t == tile)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOccupant(LevelDefinition level, OccupantType occupant)
        {
            int count = 0;
            foreach (OccupantType o in level.occupants)
            {
                if (o == occupant)
                {
                    count++;
                }
            }

            return count;
        }

        [Test]
        public void Level01_Loads_AndBuildsBoard_WithMatchingBoxesAndGoals()
        {
            LevelDefinition level = LoadLevel01();
            var board = new Board(level);

            Assert.GreaterOrEqual(board.BoxPositions.Count, 1, "Sample level needs at least one box.");
            Assert.GreaterOrEqual(CountTile(level, TileType.Goal), 1, "Sample level needs at least one goal.");
            Assert.AreEqual(
                CountTile(level, TileType.Goal),
                board.BoxPositions.Count,
                "Box count must equal goal count for the sample level to be solvable.");
            Assert.IsFalse(board.IsComplete, "Sample level must not start already complete.");
        }

        [Test]
        public void Level01_HasExactlyOnePlayer_AndNoOccupantOnWalls()
        {
            LevelDefinition level = LoadLevel01();

            Assert.AreEqual(1, CountOccupant(level, OccupantType.Player), "Sample level needs exactly one player.");

            for (int y = 0; y < level.height; y++)
            {
                for (int x = 0; x < level.width; x++)
                {
                    int index = level.Index(x, y);
                    if (level.cells[index] == TileType.Wall)
                    {
                        Assert.AreEqual(
                            OccupantType.None,
                            level.occupants[index],
                            $"Occupant placed on wall at ({x}, {y}).");
                    }
                }
            }
        }

        [Test]
        public void Level01_KnownSolutionSequence_CompletesLevel()
        {
            LevelDefinition level = LoadLevel01();
            var board = new Board(level);

            // Route: push the lower box down to the bottom row, step round behind it and push it right
            // twice onto the bottom goal; then go back up and push the upper box right twice onto its goal.
            Direction[] solution =
            {
                Direction.Down,
                Direction.Down,
                Direction.Left,
                Direction.Down,
                Direction.Right,
                Direction.Right,
                Direction.Up,
                Direction.Up,
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
            Assert.AreEqual(5, board.PushCount, "The solution pushes five times.");
            Assert.IsTrue(board.IsComplete, "Following the known solution must complete the level.");
        }
    }
}

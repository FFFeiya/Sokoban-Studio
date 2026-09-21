using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEngine;

namespace Sokoban.Tests
{
    public class BoardTests
    {
        /// <summary>
        /// Builds a level definition from ASCII rows. Row 0 is the top row of the layout and
        /// maps to y = 0 (dense index = y * width + x).
        /// '#'=Wall  '.'=Floor  'G'=Goal  'P'=Player on floor  'B'=Box on floor
        /// '*'=Box on goal  '+'=Player on goal  'T'=Plate  'D'=Door
        /// 'b'=Box on door  'p'=Player on door (illegal states used to test constructor rejection)
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
                        case 'T':
                            tile = TileType.Plate;
                            occupant = OccupantType.None;
                            break;
                        case 'D':
                            tile = TileType.Door;
                            occupant = OccupantType.None;
                            break;
                        case 'p':
                            tile = TileType.Door;
                            occupant = OccupantType.Player;
                            break;
                        case 'b':
                            tile = TileType.Door;
                            occupant = OccupantType.Box;
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

        private static void AssertPlayerAt(Board board, int x, int y)
        {
            Assert.AreEqual(x, board.PlayerPosition.x, "Unexpected player x.");
            Assert.AreEqual(y, board.PlayerPosition.y, "Unexpected player y.");
        }

        private static void AssertBoxAt(Board board, int x, int y)
        {
            var boxes = board.BoxPositions;
            Assert.AreEqual(1, boxes.Count, "Expected exactly one box.");
            foreach ((int bx, int by) in boxes)
            {
                Assert.AreEqual(x, bx, "Unexpected box x.");
                Assert.AreEqual(y, by, "Unexpected box y.");
            }
        }

        [Test]
        public void DirectionOffsets_AreDeterministic()
        {
            (int dx, int dy) up = Direction.Up.ToOffset();
            Assert.AreEqual(0, up.dx);
            Assert.AreEqual(-1, up.dy);

            (int dx, int dy) down = Direction.Down.ToOffset();
            Assert.AreEqual(0, down.dx);
            Assert.AreEqual(1, down.dy);

            (int dx, int dy) left = Direction.Left.ToOffset();
            Assert.AreEqual(-1, left.dx);
            Assert.AreEqual(0, left.dy);

            (int dx, int dy) right = Direction.Right.ToOffset();
            Assert.AreEqual(1, right.dx);
            Assert.AreEqual(0, right.dy);
        }

        [Test]
        public void MoveIntoFloor_Succeeds()
        {
            var board = new Board(MakeLevel(
                "###",
                "#P.",
                "#G#"));

            bool moved = board.TryMove(Direction.Right);

            Assert.IsTrue(moved);
            AssertPlayerAt(board, 2, 1);
            Assert.AreEqual(1, board.MoveCount);
            Assert.AreEqual(0, board.PushCount);
            Assert.AreEqual(0, board.BoxPositions.Count);
            Assert.IsFalse(board.IsComplete);
        }

        [Test]
        public void MoveIntoWall_Rejected()
        {
            var board = new Board(MakeLevel(
                "###",
                "#P#",
                "###"));

            bool moved = board.TryMove(Direction.Up);

            Assert.IsFalse(moved);
            AssertPlayerAt(board, 1, 1);
            Assert.AreEqual(0, board.MoveCount);
            Assert.AreEqual(0, board.PushCount);
        }

        [Test]
        public void MoveOutOfBounds_Rejected()
        {
            var board = new Board(MakeLevel(
                "P."));

            Assert.IsFalse(board.TryMove(Direction.Left));
            Assert.IsFalse(board.TryMove(Direction.Up));
            AssertPlayerAt(board, 0, 0);
            Assert.AreEqual(0, board.MoveCount);
            Assert.AreEqual(0, board.PushCount);
        }

        [Test]
        public void PushBoxIntoFreeCell_Succeeds()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#PB.#",
                "#####"));

            bool moved = board.TryMove(Direction.Right);

            Assert.IsTrue(moved);
            AssertPlayerAt(board, 2, 1);
            AssertBoxAt(board, 3, 1);
            Assert.AreEqual(1, board.MoveCount);
            Assert.AreEqual(1, board.PushCount);
        }

        [Test]
        public void PushBoxDown_Succeeds()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#.P.#",
                "#.B.#",
                "#...#",
                "#####"));

            bool moved = board.TryMove(Direction.Down);

            Assert.IsTrue(moved);
            AssertPlayerAt(board, 2, 2);
            AssertBoxAt(board, 2, 3);
            Assert.AreEqual(1, board.MoveCount);
            Assert.AreEqual(1, board.PushCount);
        }

        [Test]
        public void PushBoxLeft_Succeeds()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#.BP#",
                "#####"));

            bool moved = board.TryMove(Direction.Left);

            Assert.IsTrue(moved);
            AssertPlayerAt(board, 2, 1);
            AssertBoxAt(board, 1, 1);
            Assert.AreEqual(1, board.MoveCount);
            Assert.AreEqual(1, board.PushCount);
        }

        [Test]
        public void PushBoxIntoWall_Rejected()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#PB##",
                "#####"));

            bool moved = board.TryMove(Direction.Right);

            Assert.IsFalse(moved);
            AssertPlayerAt(board, 1, 1);
            AssertBoxAt(board, 2, 1);
            Assert.AreEqual(0, board.MoveCount);
            Assert.AreEqual(0, board.PushCount);
        }

        [Test]
        public void PushBoxIntoBox_Rejected()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#PBB#",
                "#####"));

            bool moved = board.TryMove(Direction.Right);

            Assert.IsFalse(moved);
            AssertPlayerAt(board, 1, 1);
            CollectionAssert.AreEquivalent(
                new[] { (2, 1), (3, 1) },
                board.BoxPositions);
            Assert.AreEqual(0, board.MoveCount);
            Assert.AreEqual(0, board.PushCount);
        }

        [Test]
        public void IsComplete_FalseInitially()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#PBG#",
                "#####"));

            Assert.IsFalse(board.IsComplete);
        }

        [Test]
        public void IsComplete_TrueWhenEveryBoxStartsOnAGoal()
        {
            // '*' is a box already sitting on a goal, so the board is solved on construction.
            var board = new Board(MakeLevel(
                "#####",
                "#P*.#",
                "#####"));

            Assert.AreEqual(0, board.MoveCount, "An initially complete board needs no moves.");
            Assert.AreEqual(0, board.PushCount, "An initially complete board needs no pushes.");
            Assert.IsTrue(board.IsComplete, "Boxes already on every goal make the board complete immediately.");
        }

        [Test]
        public void IsComplete_TrueWhenMultipleBoxesAllStartOnGoals()
        {
            var board = new Board(MakeLevel(
                "######",
                "#P**.#",
                "######"));

            Assert.AreEqual(2, board.BoxPositions.Count, "Both boxes must be present.");
            Assert.IsTrue(board.IsComplete, "Every goal covered on construction means complete.");
        }

        [Test]
        public void TryMove_OnInitiallyCompleteBoard_CanUndoCompleteState()
        {
            // Regression support for the initially-complete dead-end (F-02): the Board must not lock
            // itself just because it starts solved — pushing a box off its goal leaves the board
            // incomplete again. The no-input lock lives in GameBootstrap/GameController, not in Board.
            var board = new Board(MakeLevel(
                "#####",
                "#P*.#",
                "#####"));

            Assert.IsTrue(board.IsComplete);
            Assert.IsTrue(board.TryMove(Direction.Right), "Pushing the box off its goal must be legal.");
            Assert.IsFalse(board.IsComplete, "The board is no longer complete once the box leaves its goal.");
        }

        [Test]
        public void PushBoxOntoGoal_CompletesLevel()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#PBG#",
                "#####"));

            bool moved = board.TryMove(Direction.Right);

            Assert.IsTrue(moved);
            AssertBoxAt(board, 3, 1);
            Assert.AreEqual(TileType.Goal, board.GetTile(3, 1));
            Assert.IsTrue(board.IsComplete);
            Assert.AreEqual(1, board.MoveCount);
            Assert.AreEqual(1, board.PushCount);
        }

        [Test]
        public void IsComplete_FalseWhenOnlySomeGoalsCovered()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#PBG#",
                "#..G#",
                "#####"));

            Assert.IsTrue(board.TryMove(Direction.Right));

            Assert.AreEqual(1, board.BoxPositions.Count);
            Assert.IsFalse(board.IsComplete);
        }

        [Test]
        public void SnapshotRestore_RoundTrip()
        {
            var board = new Board(MakeLevel(
                "#####",
                "#P..#",
                "#.B.#",
                "#..G#",
                "#####"));

            Assert.IsTrue(board.TryMove(Direction.Down));
            BoardSnapshot snapshot = board.CreateSnapshot();

            Assert.IsTrue(board.TryMove(Direction.Down));
            AssertPlayerAt(board, 1, 3);
            Assert.AreEqual(2, board.MoveCount);

            board.RestoreSnapshot(snapshot);

            AssertPlayerAt(board, 1, 2);
            AssertBoxAt(board, 2, 2);
            Assert.AreEqual(1, board.MoveCount);
            Assert.AreEqual(0, board.PushCount);
            Assert.IsFalse(board.IsComplete);
        }

        [Test]
        public void Snapshot_IsIndependentOfLaterMoves()
        {
            var board = new Board(MakeLevel(
                "######",
                "#PB..#",
                "######"));

            Assert.IsTrue(board.TryMove(Direction.Right));
            BoardSnapshot snapshot = board.CreateSnapshot();

            Assert.IsTrue(board.TryMove(Direction.Right));
            AssertBoxAt(board, 4, 1);

            board.RestoreSnapshot(snapshot);
            AssertBoxAt(board, 3, 1);
            AssertPlayerAt(board, 2, 1);
        }

        [Test]
        public void Constructor_RejectsWrongCellCount()
        {
            var def = MakeLevel(
                "###",
                "#P#",
                "###");
            def.cells.RemoveAt(0);

            Assert.Throws<ArgumentException>(() => new Board(def));
        }

        [Test]
        public void Constructor_RejectsWrongOccupantCount()
        {
            var def = MakeLevel(
                "###",
                "#P#",
                "###");
            def.occupants.RemoveAt(0);

            Assert.Throws<ArgumentException>(() => new Board(def));
        }

        [Test]
        public void Constructor_RejectsZeroPlayers()
        {
            var def = MakeLevel(
                "###",
                "#.#",
                "###");

            Assert.Throws<ArgumentException>(() => new Board(def));
        }

        [Test]
        public void Constructor_RejectsTwoPlayers()
        {
            var def = MakeLevel(
                "###",
                "#PP",
                "###");

            Assert.Throws<ArgumentException>(() => new Board(def));
        }

        [Test]
        public void Constructor_RejectsOccupantOnWall()
        {
            var def = MakeLevel(
                "###",
                "#P#",
                "###");
            def.occupants[def.Index(0, 0)] = OccupantType.Box;

            Assert.Throws<ArgumentException>(() => new Board(def));
        }

        [Test]
        public void Constructor_RejectsPlayerOnDoor()
        {
            // 'p' paints the single player directly onto a door tile at (1, 1).
            var def = MakeLevel(
                "###",
                "#p#",
                "###");

            var exception = Assert.Throws<ArgumentException>(() => new Board(def));
            StringAssert.Contains("Player", exception.Message, "The message must name the occupant.");
            StringAssert.Contains("door", exception.Message, "The message must name the tile kind.");
            StringAssert.Contains(
                $"index {def.Index(1, 1)}",
                exception.Message,
                "The message must name the offending index.");
        }

        [Test]
        public void Constructor_RejectsBoxOnDoor()
        {
            // 'b' paints a box directly onto a door tile at (2, 1); the player is legal on floor.
            var def = MakeLevel(
                "####",
                "#Pb#",
                "####");

            var exception = Assert.Throws<ArgumentException>(() => new Board(def));
            StringAssert.Contains("Box", exception.Message, "The message must name the occupant.");
            StringAssert.Contains("door", exception.Message, "The message must name the tile kind.");
            StringAssert.Contains(
                $"index {def.Index(2, 1)}",
                exception.Message,
                "The message must name the offending index.");
        }

        [Test]
        public void Constructor_RejectsZeroDimensions()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.width = 0;
            def.height = 0;
            def.cells = new List<TileType>();
            def.occupants = new List<OccupantType>();

            Assert.Throws<ArgumentException>(() => new Board(def));
        }

        [Test]
        public void Constructor_RejectsNullDefinition()
        {
            Assert.Throws<ArgumentException>(() => new Board(null));
        }

        [Test]
        public void Board_DoesNotMutateLevelDefinition()
        {
            var def = MakeLevel(
                "#####",
                "#PB.#",
                "#####");
            var board = new Board(def);

            Assert.IsTrue(board.TryMove(Direction.Right));

            Assert.AreEqual(OccupantType.Player, def.occupants[def.Index(1, 1)]);
            Assert.AreEqual(OccupantType.Box, def.occupants[def.Index(2, 1)]);
            Assert.AreEqual(OccupantType.None, def.occupants[def.Index(3, 1)]);
            Assert.AreEqual(TileType.Floor, def.cells[def.Index(3, 1)]);
        }

        [Test]
        public void Resize_PreservesOverlapAndFillsNewArea()
        {
            var def = MakeLevel(
                "###",
                "#P#",
                "###");

            LevelResizer.Resize(def, 5, 4);

            Assert.AreEqual(5, def.width);
            Assert.AreEqual(4, def.height);
            Assert.AreEqual(20, def.cells.Count);
            Assert.AreEqual(20, def.occupants.Count);
            Assert.AreEqual(TileType.Wall, def.cells[def.Index(0, 0)]);
            Assert.AreEqual(OccupantType.Player, def.occupants[def.Index(1, 1)]);
            Assert.AreEqual(TileType.Wall, def.cells[def.Index(2, 2)]);
            Assert.AreEqual(TileType.Floor, def.cells[def.Index(3, 0)]);
            Assert.AreEqual(OccupantType.None, def.occupants[def.Index(4, 3)]);
        }

        [Test]
        public void Resize_Shrink_KeepsOverlap()
        {
            var def = MakeLevel(
                "#####",
                "#PB.#",
                "#..G#",
                "#####");

            LevelResizer.Resize(def, 3, 2);

            Assert.AreEqual(3, def.width);
            Assert.AreEqual(2, def.height);
            Assert.AreEqual(6, def.cells.Count);
            Assert.AreEqual(TileType.Wall, def.cells[def.Index(0, 0)]);
            Assert.AreEqual(TileType.Floor, def.cells[def.Index(1, 1)]);
            Assert.AreEqual(OccupantType.Player, def.occupants[def.Index(1, 1)]);
            Assert.AreEqual(OccupantType.Box, def.occupants[def.Index(2, 1)]);
        }

        [Test]
        public void Resize_CreatesNewLists()
        {
            var def = MakeLevel(
                "###",
                "#P#",
                "###");
            List<TileType> oldCells = def.cells;
            List<OccupantType> oldOccupants = def.occupants;

            LevelResizer.Resize(def, 4, 4);

            Assert.AreNotSame(oldCells, def.cells);
            Assert.AreNotSame(oldOccupants, def.occupants);
        }

        [Test]
        public void Resize_RejectsInvalidSize()
        {
            var def = MakeLevel(
                "###",
                "#P#",
                "###");

            Assert.Throws<ArgumentException>(() => LevelResizer.Resize(def, 0, 3));
            Assert.Throws<ArgumentException>(() => LevelResizer.Resize(def, 3, -1));
            Assert.Throws<ArgumentException>(() => LevelResizer.Resize(null, 3, 3));
        }

        /// <summary>Asserts that a box stands at (x, y) without requiring the board to have one box.</summary>
        private static void AssertBoxPresent(Board board, int x, int y)
        {
            foreach ((int bx, int by) in board.BoxPositions)
            {
                if (bx == x && by == y)
                {
                    return;
                }
            }

            Assert.Fail($"Expected a box at ({x}, {y}).");
        }

        [Test]
        public void PlateDoor_ClosedDoorBlocksPlayer_WhenPlateUnoccupied()
        {
            // Player (1, 1), closed door (2, 1), unoccupied plate (1, 2).
            var board = new Board(MakeLevel(
                "#####",
                "#PD.#",
                "#T..#",
                "#...#",
                "#####"));

            Assert.IsFalse(board.IsDoorOpen(2, 1), "A door with an unoccupied plate must be closed.");
            Assert.IsFalse(board.TryMove(Direction.Right), "A closed door must block the player like a wall.");
            AssertPlayerAt(board, 1, 1);
        }

        [Test]
        public void PlateDoor_PlayerOnPlate_OpensDoor_PlayerCanPass()
        {
            // Player (2, 3) steps up onto plate (2, 2), which opens door (2, 1) directly above it.
            var board = new Board(MakeLevel(
                "#####",
                "#.D.#",
                "#.T.#",
                "#.P.#",
                "#####"));

            Assert.IsTrue(board.TryMove(Direction.Up), "The player must be able to step onto the plate.");
            AssertPlayerAt(board, 2, 2);
            Assert.IsTrue(board.IsDoorOpen(2, 1), "Occupying the only plate must open the door.");

            Assert.IsTrue(board.TryMove(Direction.Up), "An open door must be passable like floor.");
            AssertPlayerAt(board, 2, 1);
        }

        [Test]
        public void PlateDoor_BoxOnPlate_KeepsDoorOpen_AfterPlayerLeaves()
        {
            // Player (1, 2) pushes the box onto plate (4, 2); door (2, 1) then opens for the player.
            var board = new Board(MakeLevel(
                "######",
                "#.D..#",
                "#PB.T#",
                "######"));

            Assert.IsTrue(board.TryMove(Direction.Right));
            Assert.IsTrue(board.TryMove(Direction.Right));
            AssertBoxAt(board, 4, 2);
            AssertPlayerAt(board, 3, 2);

            // The player is not on the plate, yet the box keeps the door open.
            Assert.IsTrue(board.IsDoorOpen(2, 1), "A box on the plate must open the door.");

            Assert.IsTrue(board.TryMove(Direction.Up));
            AssertPlayerAt(board, 3, 1);
            Assert.IsTrue(board.TryMove(Direction.Left), "The box-held door must be passable.");
            AssertPlayerAt(board, 2, 1);
        }

        [Test]
        public void PlateDoor_PlateVacated_DoorCloses_AndBlocksAgain()
        {
            // Player (2, 3) opens door (2, 1) via plate (2, 2), then steps off the plate sideways.
            var board = new Board(MakeLevel(
                "#####",
                "#.D.#",
                "#.T.#",
                "#.P.#",
                "#####"));

            Assert.IsTrue(board.TryMove(Direction.Up), "Step onto the plate.");
            Assert.IsTrue(board.IsDoorOpen(2, 1));
            Assert.IsTrue(board.TryMove(Direction.Left), "Step off the plate.");
            AssertPlayerAt(board, 1, 2);

            Assert.IsFalse(board.IsDoorOpen(2, 1), "The door must close once the plate is empty.");

            Assert.IsTrue(board.TryMove(Direction.Up));
            AssertPlayerAt(board, 1, 1);
            Assert.IsFalse(board.TryMove(Direction.Right), "The closed door must block again.");
            AssertPlayerAt(board, 1, 1);
        }

        [Test]
        public void PlateDoor_OccupiedDoor_StaysOpen_UntilOccupantLeaves()
        {
            // Player opens door (2, 1) from plate (2, 2), steps ONTO the door, then off it sideways.
            var board = new Board(MakeLevel(
                "#####",
                "#.D.#",
                "#.T.#",
                "#.P.#",
                "#####"));

            Assert.IsTrue(board.TryMove(Direction.Up), "Step onto the plate.");
            Assert.IsTrue(board.TryMove(Direction.Up), "The open door must be enterable.");
            AssertPlayerAt(board, 2, 1);

            // The plate is empty now, but the player standing on the door keeps it open.
            Assert.IsTrue(board.IsDoorOpen(2, 1), "An occupied door must stay open until the occupant leaves.");

            Assert.IsTrue(board.TryMove(Direction.Left), "Step off the door (not onto the plate).");
            AssertPlayerAt(board, 1, 1);

            Assert.IsFalse(board.IsDoorOpen(2, 1), "A vacated door with an empty plate must close again.");
            Assert.IsFalse(board.TryMove(Direction.Right), "The closed door must reject re-entry.");
            AssertPlayerAt(board, 1, 1);
        }

        [Test]
        public void PlateDoor_ClosedDoorBlocksPushedBox()
        {
            // Player (1, 1) tries to push box (2, 1) right into closed door (3, 1); plate (4, 3) is empty.
            // A second box is then pushed onto the plate, after which the same push succeeds.
            var board = new Board(MakeLevel(
                "#######",
                "#PBD..#",
                "#.....#",
                "#.B.T.#",
                "#######"));

            Assert.IsFalse(board.TryMove(Direction.Right), "A box may not be pushed onto a closed door.");
            AssertPlayerAt(board, 1, 1);
            AssertBoxPresent(board, 2, 1);

            // Walk to the lower box and push it onto the plate to open the door group.
            Assert.IsTrue(board.TryMove(Direction.Down));
            Assert.IsTrue(board.TryMove(Direction.Down));
            Assert.IsTrue(board.TryMove(Direction.Right));
            Assert.IsTrue(board.TryMove(Direction.Right));
            AssertBoxPresent(board, 4, 3);
            AssertPlayerAt(board, 3, 3);
            Assert.IsTrue(board.IsDoorOpen(3, 1), "The box on the plate must open every door.");

            // Return behind the first box and repeat the push into the now-open door.
            Assert.IsTrue(board.TryMove(Direction.Left));
            Assert.IsTrue(board.TryMove(Direction.Left));
            Assert.IsTrue(board.TryMove(Direction.Up));
            Assert.IsTrue(board.TryMove(Direction.Up));
            AssertPlayerAt(board, 1, 1);

            Assert.IsTrue(board.TryMove(Direction.Right), "The same push must succeed once the door is open.");
            AssertBoxPresent(board, 3, 1);
            AssertPlayerAt(board, 2, 1);
        }

        [Test]
        public void PlateDoor_Undo_RecomputesDoorState_NoSnapshot()
        {
            // Door state is derived, never snapshotted: BoardSnapshot has no door field by design, so
            // restoring an earlier snapshot must recompute the door state from the restored positions.
            var board = new Board(MakeLevel(
                "#####",
                "#.D.#",
                "#.T.#",
                "#.P.#",
                "#####"));

            BoardSnapshot before = board.CreateSnapshot();
            Assert.IsFalse(board.IsDoorOpen(2, 1));

            Assert.IsTrue(board.TryMove(Direction.Up), "Step onto the plate.");
            Assert.IsTrue(board.IsDoorOpen(2, 1));
            Assert.IsTrue(board.TryMove(Direction.Up), "Move through the open door.");
            AssertPlayerAt(board, 2, 1);

            board.RestoreSnapshot(before);

            AssertPlayerAt(board, 2, 3);
            Assert.IsFalse(board.IsDoorOpen(2, 1), "Undo must recompute the door back to closed.");

            Assert.IsTrue(board.TryMove(Direction.Up), "Re-stepping on the plate must reopen the door.");
            Assert.IsTrue(board.IsDoorOpen(2, 1));
        }

        [Test]
        public void PlateDoor_Restart_RebuildsDerivedState()
        {
            // Restart builds a fresh board from the same definition: the door state is not carried over.
            var def = MakeLevel(
                "#####",
                "#.D.#",
                "#.T.#",
                "#.P.#",
                "#####");

            var board = new Board(def);
            Assert.IsTrue(board.TryMove(Direction.Up));
            Assert.IsTrue(board.IsDoorOpen(2, 1), "The plate must open the door before the restart.");

            var restarted = new Board(def);
            AssertPlayerAt(restarted, 2, 3);
            Assert.IsFalse(restarted.IsDoorOpen(2, 1), "A fresh board must rebuild the door closed.");
        }

        [Test]
        public void PlateDoor_NoPlates_DoorsAlwaysOpen()
        {
            // A plate-less board keeps its doors open, so shipped plate-less content is unaffected.
            var board = new Board(MakeLevel(
                "#####",
                "#.D.#",
                "#.P.#",
                "#...#",
                "#####"));

            Assert.IsFalse(board.HasPlateTiles, "This board has no plates.");
            Assert.IsTrue(board.IsDoorOpen(2, 1), "Without plates a door stays open.");
            Assert.IsTrue(board.TryMove(Direction.Up), "An open door is passable.");
            AssertPlayerAt(board, 2, 1);
        }
    }
}

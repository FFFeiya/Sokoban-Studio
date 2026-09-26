using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers per-group door semantics in the runtime <see cref="Board"/>. Plates and doors only
    /// gate each other inside the same group id, and a door never closes onto an occupant. Every
    /// created <see cref="LevelDefinition"/> is destroyed in a finally block; a plain <see cref="Board"/>
    /// owns only primitive arrays and needs no destroy. Layouts come from
    /// <see cref="LevelAssetFactory.BuildLevel"/> ('#'=Wall '.'=Floor 'G'=Goal 'T'=Plate 'D'=Door
    /// 'P'=Player 'B'=Box), which writes every cell as group A; the tests promote specific plate/door
    /// cells to group B through <c>def.groupIds</c>.
    /// </summary>
    public class BoardGroupTests
    {
        [Test]
        public void GroupA_PlateSatisfied_OpensOnlyGroupADoors()
        {
            LevelDefinition def = null;
            try
            {
                def = LevelAssetFactory.BuildLevel(
                    "TwoGroupA",
                    new[]
                    {
                        "#######",
                        "#D...D#",
                        "#T...T#",
                        "#G.P..#",
                        "#######"
                    });

                // Door/plate column near x=1 stays group A; the column near x=5 becomes group B.
                def.groupIds[def.Index(5, 1)] = 1;
                def.groupIds[def.Index(5, 2)] = 1;

                // Satisfy only the group-A plate with a box; the group-B plate stays empty.
                def.occupants[def.Index(1, 2)] = OccupantType.Box;

                var board = new Board(def);

                Assert.IsTrue(board.IsDoorOpen(1, 1), "A satisfied group-A plate must open the group-A door.");
                Assert.IsFalse(board.IsDoorOpen(5, 1), "An empty group-B plate must leave the group-B door closed.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void GroupB_PlateSatisfied_OpensOnlyGroupBDoors()
        {
            LevelDefinition def = null;
            try
            {
                def = LevelAssetFactory.BuildLevel(
                    "TwoGroupB",
                    new[]
                    {
                        "#######",
                        "#D...D#",
                        "#T...T#",
                        "#G.P..#",
                        "#######"
                    });

                def.groupIds[def.Index(5, 1)] = 1;
                def.groupIds[def.Index(5, 2)] = 1;

                // Satisfy only the group-B plate; the group-A plate stays empty.
                def.occupants[def.Index(5, 2)] = OccupantType.Box;

                var board = new Board(def);

                Assert.IsTrue(board.IsDoorOpen(5, 1), "A satisfied group-B plate must open the group-B door.");
                Assert.IsFalse(board.IsDoorOpen(1, 1), "An empty group-A plate must leave the group-A door closed.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void DoorWithNoPlateInGroup_IsOpen()
        {
            LevelDefinition def = null;
            try
            {
                // The group-B column has a door but no plate at all; the group-A column has an empty plate.
                def = LevelAssetFactory.BuildLevel(
                    "NoPlateGroup",
                    new[]
                    {
                        "#######",
                        "#D...D#",
                        "#T....#",
                        "#B.P.G#",
                        "#######"
                    });

                def.groupIds[def.Index(5, 1)] = 1;

                var board = new Board(def);

                Assert.IsTrue(
                    board.IsDoorOpen(5, 1),
                    "A door whose group has no plates must fall back to the open default.");
                Assert.IsFalse(
                    board.IsDoorOpen(1, 1),
                    "A group-A door with an empty group-A plate must stay closed.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void OccupiedDoor_StaysOpen_WhenItsGroupBecomesUnsatisfied()
        {
            LevelDefinition def = null;
            try
            {
                // 8x5 board, all group A.
                //   door (3,1), second box (4,1)
                //   player (2,2)
                //   plate (3,3), first box (5,3)
                def = LevelAssetFactory.BuildLevel(
                    "OccupiedDoor",
                    new[]
                    {
                        "########",
                        "#G.DB..#",
                        "#.P....#",
                        "#G.T.B.#",
                        "########"
                    });

                var board = new Board(def);

                // Start: the group-A plate is empty, so the door is closed.
                Assert.IsFalse(board.IsDoorOpen(3, 1), "An empty group-A plate must leave the door closed.");

                // Walk right along row 2, drop into row 3, then push the first box left twice onto the plate.
                Assert.IsTrue(board.TryMove(Direction.Right));
                Assert.IsTrue(board.TryMove(Direction.Right));
                Assert.IsTrue(board.TryMove(Direction.Right));
                Assert.IsTrue(board.TryMove(Direction.Right));
                Assert.IsTrue(board.TryMove(Direction.Down));
                Assert.IsTrue(board.TryMove(Direction.Left));
                Assert.IsTrue(board.TryMove(Direction.Left));
                Assert.AreEqual((4, 3), board.PlayerPosition);
                CollectionAssert.Contains(board.BoxPositions, (3, 3));
                Assert.IsTrue(board.IsDoorOpen(3, 1), "The box on the plate must open the group-A door.");

                // Climb to (5, 1) and push the second box left onto the open door.
                Assert.IsTrue(board.TryMove(Direction.Right));
                Assert.IsTrue(board.TryMove(Direction.Up));
                Assert.IsTrue(board.TryMove(Direction.Up));
                Assert.AreEqual((5, 1), board.PlayerPosition);
                Assert.IsTrue(board.TryMove(Direction.Left));
                Assert.AreEqual((4, 1), board.PlayerPosition);
                CollectionAssert.Contains(board.BoxPositions, (3, 1));
                Assert.IsTrue(board.IsDoorOpen(3, 1), "A box standing on the door must keep it open.");

                // Walk around to (2, 3) and push the first box right off the plate.
                Assert.IsTrue(board.TryMove(Direction.Down));
                Assert.IsTrue(board.TryMove(Direction.Left));
                Assert.IsTrue(board.TryMove(Direction.Left));
                Assert.IsTrue(board.TryMove(Direction.Down));
                Assert.AreEqual((2, 3), board.PlayerPosition);
                Assert.IsTrue(board.TryMove(Direction.Right));
                Assert.AreEqual((3, 3), board.PlayerPosition);
                CollectionAssert.Contains(board.BoxPositions, (4, 3));

                // Step off the plate so group A is genuinely unsatisfied: no box and no player on (3, 3).
                Assert.IsTrue(board.TryMove(Direction.Up));
                Assert.AreEqual((3, 2), board.PlayerPosition);
                CollectionAssert.DoesNotContain(board.BoxPositions, (3, 3));
                Assert.AreNotEqual((3, 3), board.PlayerPosition);

                // The group is unsatisfied, yet the door must stay open because a box occupies it.
                Assert.IsTrue(
                    board.IsDoorOpen(3, 1),
                    "A door never closes onto an occupant, even when its group becomes unsatisfied.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void Undo_RecomputesPerGroupDoorState()
        {
            // Per-group door state is derived, never snapshotted (BoardSnapshot has no door field), so
            // restoring an earlier snapshot must recompute each group's doors from the restored
            // positions. Layout: group-A door (2,1)/plate (2,2); group-B door (4,1)/plate (4,2).
            LevelDefinition def = null;
            try
            {
                def = LevelAssetFactory.BuildLevel(
                    "TwoGroupUndo",
                    new[]
                    {
                        "#######",
                        "#.D.D.#",
                        "#.T.T.#",
                        "#B.P.G#",
                        "#######"
                    });

                // The right-hand door/plate pair becomes group B; the left-hand pair stays group A.
                def.groupIds[def.Index(4, 1)] = 1;
                def.groupIds[def.Index(4, 2)] = 1;

                var board = new Board(def);

                BoardSnapshot before = board.CreateSnapshot();
                Assert.AreEqual((3, 3), board.PlayerPosition);
                Assert.IsFalse(board.IsDoorOpen(2, 1), "An empty group-A plate must leave the group-A door closed.");
                Assert.IsFalse(board.IsDoorOpen(4, 1), "An empty group-B plate must leave the group-B door closed.");

                // Step up to (3,2), then left onto the group-A plate at (2,2).
                Assert.IsTrue(board.TryMove(Direction.Up));
                Assert.AreEqual((3, 2), board.PlayerPosition);
                Assert.IsTrue(board.TryMove(Direction.Left));
                Assert.AreEqual((2, 2), board.PlayerPosition);

                Assert.IsTrue(board.IsDoorOpen(2, 1), "The player on the group-A plate must open the group-A door.");
                Assert.IsFalse(board.IsDoorOpen(4, 1), "Satisfying group A must not open the group-B door.");

                board.RestoreSnapshot(before);

                Assert.AreEqual((3, 3), board.PlayerPosition);
                Assert.IsFalse(board.IsDoorOpen(2, 1), "Undo must recompute the group-A door back to closed.");
                Assert.IsFalse(board.IsDoorOpen(4, 1), "Undo must recompute the group-B door back to closed.");

                // Re-stepping onto the group-A plate must reopen only that group's door.
                Assert.IsTrue(board.TryMove(Direction.Up));
                Assert.IsTrue(board.TryMove(Direction.Left));
                Assert.IsTrue(board.IsDoorOpen(2, 1), "Re-stepping on the group-A plate must reopen the group-A door.");
                Assert.IsFalse(board.IsDoorOpen(4, 1), "The group-B door must stay closed throughout.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void Restart_RebuildsPerGroupDoorState()
        {
            // Restart builds a fresh board from the same definition: per-group derived door state is
            // never carried over, it is rebuilt from the definition.
            LevelDefinition def = null;
            try
            {
                def = LevelAssetFactory.BuildLevel(
                    "TwoGroupRestart",
                    new[]
                    {
                        "#######",
                        "#.D.D.#",
                        "#.T.T.#",
                        "#B.P.G#",
                        "#######"
                    });

                def.groupIds[def.Index(4, 1)] = 1;
                def.groupIds[def.Index(4, 2)] = 1;

                var board = new Board(def);

                // Step up to (3,2), then left onto the group-A plate at (2,2).
                Assert.IsTrue(board.TryMove(Direction.Up));
                Assert.AreEqual((3, 2), board.PlayerPosition);
                Assert.IsTrue(board.TryMove(Direction.Left));
                Assert.AreEqual((2, 2), board.PlayerPosition);

                Assert.IsTrue(board.IsDoorOpen(2, 1), "The player on the group-A plate must open the group-A door.");
                Assert.IsFalse(board.IsDoorOpen(4, 1), "Satisfying group A must leave the group-B door closed.");

                var restarted = new Board(def);
                Assert.AreEqual((3, 3), restarted.PlayerPosition);
                Assert.IsFalse(restarted.IsDoorOpen(2, 1), "A fresh board must rebuild the group-A door closed.");
                Assert.IsFalse(restarted.IsDoorOpen(4, 1), "A fresh board must rebuild the group-B door closed.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace Sokoban
{
    /// <summary>Movement direction on the board grid.</summary>
    public enum Direction
    {
        Up = 0,
        Down = 1,
        Left = 2,
        Right = 3
    }

    public static class DirectionExtensions
    {
        /// <summary>
        /// Grid offset of a direction. The grid is row-major with row 0 at the top of the
        /// authored layout, so <see cref="Direction.Up"/> decreases y (dy = -1) and
        /// <see cref="Direction.Down"/> increases y.
        /// </summary>
        public static (int dx, int dy) ToOffset(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return (0, -1);
                case Direction.Down: return (0, 1);
                case Direction.Left: return (-1, 0);
                case Direction.Right: return (1, 0);
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction.");
            }
        }
    }

    /// <summary>
    /// Immutable-value snapshot of a <see cref="Board"/> state, used for undo/restart support.
    /// Box positions are stored as dense grid indices in a copied array, so the snapshot is
    /// unaffected by later board mutations.
    /// </summary>
    [Serializable]
    public struct BoardSnapshot
    {
        public int PlayerX;
        public int PlayerY;
        public int MoveCount;
        public int PushCount;
        public int[] BoxIndices;
    }

    /// <summary>
    /// Deterministic Sokoban board model. It copies all level data into its own arrays on
    /// construction and never mutates the source <see cref="LevelDefinition"/>.
    /// </summary>
    public sealed class Board
    {
        private readonly int _width;
        private readonly int _height;
        private readonly TileType[] _tiles;
        private readonly bool[] _boxAt;
        // Door group id per cell, copied from the level definition on construction (0 = group A).
        // Plates and doors only gate each other within the same group.
        private readonly int[] _groupIds;

        private int _playerX;
        private int _playerY;
        private int _moveCount;
        private int _pushCount;

        // When a door's group has no plate tiles, its doors are treated as permanently open so that
        // hypothetical plate-less door content still plays. Shipped levels have neither plates nor
        // doors, so their behavior is identical to before the plate/door mechanic existed.
        private bool _openDoorsWhenNoPlates = true;

        public Board(LevelDefinition def)
        {
            if (def == null)
            {
                throw new ArgumentException("Level definition is null.", nameof(def));
            }

            if (def.width <= 0)
            {
                throw new ArgumentException("Level width must be greater than zero.", nameof(def));
            }

            if (def.height <= 0)
            {
                throw new ArgumentException("Level height must be greater than zero.", nameof(def));
            }

            int cellCount = def.width * def.height;

            if (def.cells == null || def.cells.Count != cellCount)
            {
                throw new ArgumentException(
                    $"Cell count ({def.cells?.Count ?? 0}) must equal width * height ({cellCount}).",
                    nameof(def));
            }

            if (def.occupants == null || def.occupants.Count != cellCount)
            {
                throw new ArgumentException(
                    $"Occupant count ({def.occupants?.Count ?? 0}) must equal width * height ({cellCount}).",
                    nameof(def));
            }

            _width = def.width;
            _height = def.height;
            _tiles = new TileType[cellCount];
            _boxAt = new bool[cellCount];
            _groupIds = new int[cellCount];

            int playerCount = 0;
            int playerX = -1;
            int playerY = -1;

            for (int i = 0; i < cellCount; i++)
            {
                TileType tile = def.cells[i];
                OccupantType occupant = def.occupants[i];
                _tiles[i] = tile;
                _groupIds[i] = def.GetGroupId(i);

                if (tile == TileType.Wall && occupant != OccupantType.None)
                {
                    throw new ArgumentException(
                        $"Occupant '{occupant}' is placed on a wall tile at index {i}.",
                        nameof(def));
                }

                if (tile == TileType.Door && occupant != OccupantType.None)
                {
                    throw new ArgumentException(
                        $"Occupant '{occupant}' is placed on a door tile at index {i}.",
                        nameof(def));
                }

                switch (occupant)
                {
                    case OccupantType.Player:
                        playerCount++;
                        playerX = i % _width;
                        playerY = i / _width;
                        break;
                    case OccupantType.Box:
                        _boxAt[i] = true;
                        break;
                }
            }

            if (playerCount != 1)
            {
                throw new ArgumentException(
                    $"Level must contain exactly one player, but found {playerCount}.",
                    nameof(def));
            }

            _playerX = playerX;
            _playerY = playerY;
            _moveCount = 0;
            _pushCount = 0;
        }

        public int Width => _width;

        public int Height => _height;

        public int MoveCount => _moveCount;

        public int PushCount => _pushCount;

        public (int x, int y) PlayerPosition => (_playerX, _playerY);

        /// <summary>Box positions in dense index order. The returned collection is a fresh copy.</summary>
        public IReadOnlyCollection<(int x, int y)> BoxPositions
        {
            get
            {
                var positions = new List<(int x, int y)>();
                for (int i = 0; i < _boxAt.Length; i++)
                {
                    if (_boxAt[i])
                    {
                        positions.Add((i % _width, i / _width));
                    }
                }

                return positions;
            }
        }

        /// <summary>True when every Goal tile has a box on it.</summary>
        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < _tiles.Length; i++)
                {
                    if (_tiles[i] == TileType.Goal && !_boxAt[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public TileType GetTile(int x, int y)
        {
            if (!InBounds(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"Coordinate ({x}, {y}) is outside the board.");
            }

            return _tiles[Index(x, y)];
        }

        /// <summary>
        /// Group id of the tile at (x, y): 0 = group A, 1 = group B. Read from the per-cell group array
        /// copied from the level definition on construction; exposed read-only so the view layer
        /// (<see cref="BoardView"/>) can render per-group identity. It never affects rule evaluation.
        /// Throws for a coordinate outside the board, like <see cref="GetTile"/>.
        /// </summary>
        public int GetGroupId(int x, int y)
        {
            if (!InBounds(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"Coordinate ({x}, {y}) is outside the board.");
            }

            return _groupIds[Index(x, y)];
        }

        /// <summary>
        /// True when the board contains at least one pressure-plate tile. When this is false the
        /// derived door rule has no plate group to evaluate and falls back to the configured
        /// plate-less default (doors open), so shipped plate-less boards are unchanged.
        /// </summary>
        public bool HasPlateTiles
        {
            get
            {
                for (int i = 0; i < _tiles.Length; i++)
                {
                    if (_tiles[i] == TileType.Plate)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Derived open/closed state of the door tile at (x, y). It is recomputed from the tile layer,
        /// the box positions, the player position and the per-cell group ids on every call (never
        /// cached, never snapshotted): the door opens when every Plate in its own group is occupied by
        /// the player or a box, and when its group has no plate tiles the configured plate-less default
        /// applies. A specific door stays open while the player or a box occupies it, even if its
        /// plates stop being satisfied. Returns false for a non-door tile and throws for a coordinate
        /// outside the board, like <see cref="GetTile"/>.
        /// </summary>
        public bool IsDoorOpen(int x, int y)
        {
            if (!InBounds(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"Coordinate ({x}, {y}) is outside the board.");
            }

            int index = Index(x, y);
            if (_tiles[index] != TileType.Door)
            {
                return false;
            }

            return DoorOpenInGroup(_groupIds[index]) || Occupied(index);
        }

        /// <summary>Attempts one move. See <see cref="TryMove(Direction, out bool)"/>.</summary>
        public bool TryMove(Direction direction)
        {
            return TryMove(direction, out _);
        }

        /// <summary>
        /// Attempts one move. Movement order: out-of-bounds and walls always reject; an empty
        /// target moves the player; a box target is only pushed when the cell beyond the box is
        /// in bounds, not a wall and not another box. Rejected moves never change any state.
        /// On an accepted move <paramref name="completed"/> reports the win check performed as the
        /// last step of the movement order, so callers do not have to poll <see cref="IsComplete"/>.
        /// </summary>
        public bool TryMove(Direction direction, out bool completed)
        {
            completed = false;
            (int dx, int dy) = direction.ToOffset();
            int targetX = _playerX + dx;
            int targetY = _playerY + dy;

            if (!InBounds(targetX, targetY))
            {
                return false;
            }

            int targetIndex = Index(targetX, targetY);
            if (_tiles[targetIndex] == TileType.Wall || IsClosedDoor(targetIndex))
            {
                return false;
            }

            if (_boxAt[targetIndex])
            {
                int boxX = targetX + dx;
                int boxY = targetY + dy;

                if (!InBounds(boxX, boxY))
                {
                    return false;
                }

                int boxIndex = Index(boxX, boxY);
                if (_tiles[boxIndex] == TileType.Wall || _boxAt[boxIndex] || IsClosedDoor(boxIndex))
                {
                    return false;
                }

                _boxAt[targetIndex] = false;
                _boxAt[boxIndex] = true;
                _playerX = targetX;
                _playerY = targetY;
                _moveCount++;
                _pushCount++;
                completed = IsComplete;
                return true;
            }

            _playerX = targetX;
            _playerY = targetY;
            _moveCount++;
            completed = IsComplete;
            return true;
        }

        public BoardSnapshot CreateSnapshot()
        {
            var boxIndices = new List<int>();
            for (int i = 0; i < _boxAt.Length; i++)
            {
                if (_boxAt[i])
                {
                    boxIndices.Add(i);
                }
            }

            return new BoardSnapshot
            {
                PlayerX = _playerX,
                PlayerY = _playerY,
                MoveCount = _moveCount,
                PushCount = _pushCount,
                BoxIndices = boxIndices.ToArray()
            };
        }

        public void RestoreSnapshot(BoardSnapshot snapshot)
        {
            if (snapshot.BoxIndices == null)
            {
                throw new ArgumentException("Snapshot box indices are null.", nameof(snapshot));
            }

            if (!InBounds(snapshot.PlayerX, snapshot.PlayerY))
            {
                throw new ArgumentException(
                    $"Snapshot player position ({snapshot.PlayerX}, {snapshot.PlayerY}) is outside the board.",
                    nameof(snapshot));
            }

            Array.Clear(_boxAt, 0, _boxAt.Length);
            for (int i = 0; i < snapshot.BoxIndices.Length; i++)
            {
                int index = snapshot.BoxIndices[i];
                if (index < 0 || index >= _boxAt.Length)
                {
                    throw new ArgumentException($"Snapshot box index {index} is outside the board.", nameof(snapshot));
                }

                if (_tiles[index] == TileType.Wall)
                {
                    throw new ArgumentException($"Snapshot places a box on a wall tile at index {index}.", nameof(snapshot));
                }

                _boxAt[index] = true;
            }

            _playerX = snapshot.PlayerX;
            _playerY = snapshot.PlayerY;
            _moveCount = snapshot.MoveCount;
            _pushCount = snapshot.PushCount;

            // Door state is derived rather than snapshotted, so it is re-validated at restore time:
            // a restored box may not end up on a door that the derived rule leaves closed. A box that
            // stands on a door keeps that door open by the occupancy rule, so this guard only rejects
            // snapshots that cannot come from a legal board.
            for (int i = 0; i < snapshot.BoxIndices.Length; i++)
            {
                int index = snapshot.BoxIndices[i];
                if (IsClosedDoor(index))
                {
                    throw new ArgumentException(
                        $"Snapshot places a box on a closed door tile at index {index}.", nameof(snapshot));
                }
            }
        }

        /// <summary>
        /// Per-group door rule: the doors of <paramref name="group"/> are open when every plate tile
        /// that belongs to that group is occupied by the player or a box; when the group has no plate
        /// tiles the plate-less default applies. Always recomputed from the live state on every call,
        /// never stored. With an all-zero (or absent) group list every plate is in group A, so this
        /// reduces exactly to the old single-group rule.
        /// </summary>
        private bool DoorOpenInGroup(int group)
        {
            bool anyPlate = false;
            bool allPlatesOccupied = true;
            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] != TileType.Plate || _groupIds[i] != group)
                {
                    continue;
                }

                anyPlate = true;
                if (!Occupied(i))
                {
                    allPlatesOccupied = false;
                }
            }

            return anyPlate ? allPlatesOccupied : _openDoorsWhenNoPlates;
        }

        /// <summary>True when the player or a box stands on the cell with the given dense index.</summary>
        private bool Occupied(int index)
        {
            return _boxAt[index] || Index(_playerX, _playerY) == index;
        }

        /// <summary>
        /// True when the cell with the given dense index is a door that is currently closed, i.e.
        /// blocks movement exactly like a wall. Derived from the state at the time of the call.
        /// </summary>
        private bool IsClosedDoor(int index)
        {
            return _tiles[index] == TileType.Door && !IsDoorOpen(index % _width, index / _width);
        }

        private int Index(int x, int y) => y * _width + x;

        private bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height;
    }
}

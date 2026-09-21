using System.Collections.Generic;
using UnityEngine;

namespace Sokoban
{
    /// <summary>
    /// Static tile layer of a level. <see cref="Goal"/> is a floor tile that carries a goal marker,
    /// so any cell is always exactly one of Floor, Wall, Goal, Plate or Door.
    /// <see cref="Plate"/> (a pressure plate marker) and <see cref="Door"/> are floor-like tiles that
    /// carry the plate/door mechanic: they are part of the tile layer, never occupants. Occupants may
    /// stand on Plate and Door tiles; a closed Door is the only tile besides Wall that blocks movement
    /// (and that block is derived from the live plate occupancy, not stored on the tile).
    /// </summary>
    public enum TileType : byte
    {
        Floor = 0,
        Wall = 1,
        Goal = 2,
        Plate = 3,
        Door = 4
    }

    /// <summary>
    /// Dynamic occupant layer, stored separately from the tile layer.
    /// A player or a box may stand on a Floor or a Goal tile, but never on a Wall tile.
    /// </summary>
    public enum OccupantType : byte
    {
        None = 0,
        Player = 1,
        Box = 2
    }

    /// <summary>
    /// Authored level data. The grid is dense and stored row-major:
    /// <c>index = y * width + x</c>, with row 0 being the first (top) row of the authored layout.
    /// All three lists must contain exactly <c>width * height</c> entries when present.
    /// This asset is authored data; runtime code must never mutate it.
    /// </summary>
    [CreateAssetMenu(menuName = "Sokoban/Level Definition")]
    public class LevelDefinition : ScriptableObject
    {
        public string levelName = "Untitled";
        public int width;
        public int height;
        public List<TileType> cells = new List<TileType>();
        public List<OccupantType> occupants = new List<OccupantType>();

        /// <summary>
        /// Parallel per-cell group identity, dense row-major matching <see cref="cells"/> and
        /// <see cref="occupants"/> (<c>index = y * width + x</c>, <c>count == width * height</c>).
        /// <c>0</c> = group A, <c>1</c> = group B; values are meaningful only for Plate/Door cells,
        /// and any other cell carries <c>0</c>. No initializer on purpose: assets authored before this
        /// field deserialize with <c>null</c>, and every reader must treat that (or a short list) as
        /// implicit group A via <see cref="GetGroupId"/>.
        /// </summary>
        public List<int> groupIds;

        /// <summary>Dense grid index for a coordinate. <c>index = y * width + x</c>.</summary>
        public int Index(int x, int y) => y * width + x;

        /// <summary>Group id of the cell at dense <paramref name="index"/>, defaulting to group A (0) when the
        /// list is absent or the index is out of range (old assets deserialize without the field).</summary>
        public int GetGroupId(int index)
        {
            if (groupIds == null || index < 0 || index >= groupIds.Count)
            {
                return 0;
            }
            return groupIds[index];
        }

        /// <summary>True when the coordinate lies inside the level bounds.</summary>
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
    }
}

using System.Collections.Generic;

namespace Sokoban.Editor
{
    /// <summary>
    /// Resizes a level definition in place. Used by the editor on a working copy, so it lives in
    /// the editor assembly and is unreachable from shipped runtime code.
    /// New cell/occupant lists are created and data is copied by coordinate, so anything
    /// whose coordinate is inside both the old and the new bounds is preserved.
    /// Coordinates that only exist in the new area become Floor / None.
    /// </summary>
    public static class LevelResizer
    {
        public static void Resize(LevelDefinition def, int newWidth, int newHeight)
        {
            if (def == null)
            {
                throw new System.ArgumentException("Level definition is null.", nameof(def));
            }

            if (newWidth <= 0)
            {
                throw new System.ArgumentException("New width must be greater than zero.", nameof(newWidth));
            }

            if (newHeight <= 0)
            {
                throw new System.ArgumentException("New height must be greater than zero.", nameof(newHeight));
            }

            int oldWidth = def.width;
            int oldHeight = def.height;
            List<TileType> oldCells = def.cells;
            List<OccupantType> oldOccupants = def.occupants;
            List<int> oldGroupIds = def.groupIds;

            var newCells = new List<TileType>(newWidth * newHeight);
            var newOccupants = new List<OccupantType>(newWidth * newHeight);
            var newGroupIds = new List<int>(newWidth * newHeight);

            for (int y = 0; y < newHeight; y++)
            {
                for (int x = 0; x < newWidth; x++)
                {
                    int oldIndex = y * oldWidth + x;
                    bool overlapsOldArea =
                        oldCells != null && oldOccupants != null &&
                        x < oldWidth && y < oldHeight &&
                        oldIndex >= 0 &&
                        oldIndex < oldCells.Count && oldIndex < oldOccupants.Count;

                    if (overlapsOldArea)
                    {
                        newCells.Add(oldCells[oldIndex]);
                        newOccupants.Add(oldOccupants[oldIndex]);
                        newGroupIds.Add(oldGroupIds != null && oldIndex < oldGroupIds.Count ? oldGroupIds[oldIndex] : 0);
                    }
                    else
                    {
                        newCells.Add(TileType.Floor);
                        newOccupants.Add(OccupantType.None);
                        newGroupIds.Add(0);
                    }
                }
            }

            def.width = newWidth;
            def.height = newHeight;
            def.cells = newCells;
            def.occupants = newOccupants;
            def.groupIds = newGroupIds;
        }
    }
}

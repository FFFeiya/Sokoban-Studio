using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the T05 level editor authoring model: working-copy isolation from the shipped asset,
    /// resize overlap preservation, live paint invariants and save/load round-trip.
    /// </summary>
    public class EditorTests
    {
        private const string Level01Path = "Assets/Sokoban/Levels/Level01.asset";
        private const string TempFolder = "Assets/Sokoban/Levels/Temp";
        private const string TempAssetPath = TempFolder + "/EditorTestsLevel.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(TempAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(TempAssetPath);
            }

            if (AssetDatabase.IsValidFolder(TempFolder) &&
                AssetDatabase.FindAssets("t:Object", new[] { TempFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        [Test]
        public void LoadFrom_EditsDoNotChangeTheSourceAsset()
        {
            var source = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Path);
            Assert.IsNotNull(source, "Level01 asset is required for this test.");

            var originalCells = new List<TileType>(source.cells);
            var originalOccupants = new List<OccupantType>(source.occupants);
            string originalName = source.levelName;
            int originalWidth = source.width;

            LevelEditorDocument document = LevelEditorDocument.LoadFrom(source);
            Assert.AreNotSame(source, document.Working, "The working copy must be a separate instance.");

            document.Paint(1, 1, LevelBrush.Wall);
            document.Paint(2, 1, LevelBrush.Box);
            document.Resize(originalWidth + 2, source.height + 2);
            document.SetLevelName("Edited Copy");

            LevelDefinition reloaded = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Path);
            Assert.IsNotNull(reloaded);
            CollectionAssert.AreEqual(originalCells, reloaded.cells, "Source cells must be unchanged.");
            CollectionAssert.AreEqual(originalOccupants, reloaded.occupants, "Source occupants must be unchanged.");
            Assert.AreEqual(originalName, reloaded.levelName, "Source level name must be unchanged.");
            Assert.AreEqual(originalWidth, reloaded.width, "Source width must be unchanged.");
        }

        [Test]
        public void Resize_PreservesOverlappingCells_AndFillsNewAreaWithFloor()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            LevelDefinition def = document.Working;
            var before = new List<TileType>(def.cells);
            var beforeOccupants = new List<OccupantType>(def.occupants);
            int oldWidth = def.width;
            int oldHeight = def.height;

            document.Resize(oldWidth + 3, oldHeight + 2);
            def = document.Working;

            Assert.AreEqual(oldWidth + 3, def.width);
            Assert.AreEqual(oldHeight + 2, def.height);

            for (int y = 0; y < oldHeight; y++)
            {
                for (int x = 0; x < oldWidth; x++)
                {
                    int oldIndex = y * oldWidth + x;
                    int newIndex = y * def.width + x;
                    Assert.AreEqual(before[oldIndex], def.cells[newIndex], $"Tile at ({x},{y}) must be preserved.");
                    Assert.AreEqual(beforeOccupants[oldIndex], def.occupants[newIndex], $"Occupant at ({x},{y}) must be preserved.");
                }
            }

            Assert.AreEqual(TileType.Floor, def.cells[0 * def.width + oldWidth], "New area must be floor.");
            Assert.AreEqual(OccupantType.None, def.occupants[0 * def.width + oldWidth], "New area must be empty.");
        }

        [Test]
        public void Paint_PlayerBrush_MovesTheSinglePlayer()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            LevelDefinition def = document.Working;
            (int px, int py) = FindPlayer(def);

            document.Paint(2, 2, LevelBrush.Player);

            Assert.AreEqual(1, document.PlayerCount, "Exactly one player must remain.");
            Assert.AreEqual(OccupantType.None, def.occupants[def.Index(px, py)], "The previous player cell must be cleared.");
            Assert.AreEqual(OccupantType.Player, def.occupants[def.Index(2, 2)]);
        }

        [Test]
        public void Paint_WallBrush_ClearsOccupant()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            LevelDefinition def = document.Working;

            document.Paint(3, 2, LevelBrush.Box);
            Assert.AreEqual(OccupantType.Box, def.occupants[def.Index(3, 2)]);

            document.Paint(3, 2, LevelBrush.Wall);
            Assert.AreEqual(TileType.Wall, def.cells[def.Index(3, 2)]);
            Assert.AreEqual(OccupantType.None, def.occupants[def.Index(3, 2)], "A wall must clear its occupant.");
        }

        [Test]
        public void Paint_BoxBrushOntoWall_MakesTheCellWalkable()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            LevelDefinition def = document.Working;

            document.Paint(1, 1, LevelBrush.Wall);
            document.Paint(1, 1, LevelBrush.Box);

            Assert.AreNotEqual(TileType.Wall, def.cells[def.Index(1, 1)], "An occupant must only end up on a non-wall tile.");
            Assert.AreEqual(OccupantType.Box, def.occupants[def.Index(1, 1)]);
        }

        [Test]
        public void SaveAs_ThenLoadFrom_RoundTripsThroughTheAsset()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            document.Paint(4, 2, LevelBrush.Box);
            document.Paint(6, 4, LevelBrush.Goal);
            document.SetLevelName("RoundTrip");
            Assert.IsEmpty(document.Validate(), "The built document must be valid before saving.");

            Assert.IsTrue(document.SaveAs(TempAssetPath), "SaveAs must report success.");
            Assert.IsFalse(document.IsDirty, "Saving must clear the dirty flag.");
            Assert.AreEqual(TempAssetPath, document.SourcePath);

            var saved = AssetDatabase.LoadAssetAtPath<LevelDefinition>(TempAssetPath);
            Assert.IsNotNull(saved, "The saved asset must exist.");
            Assert.AreEqual("RoundTrip", saved.levelName);

            LevelEditorDocument reloaded = LevelEditorDocument.LoadFrom(saved);
            Assert.AreEqual(document.Working.width, reloaded.Working.width);
            Assert.AreEqual(document.Working.height, reloaded.Working.height);
            Assert.AreEqual(document.BoxCount, reloaded.BoxCount);
            Assert.AreEqual(document.GoalCount, reloaded.GoalCount);
            Assert.AreEqual(1, reloaded.PlayerCount);
            Assert.IsFalse(reloaded.IsDirty, "A freshly loaded document starts clean.");

            var board = new Board(reloaded.Working);
            Assert.AreEqual(reloaded.BoxCount, board.BoxPositions.Count, "The reloaded level must build a board.");
        }

        [Test]
        public void Save_BoundDocument_OverwritesTheSameAsset()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            Assert.IsTrue(document.SaveAs(TempAssetPath));
            Assert.AreEqual(TempAssetPath, document.SourcePath);

            document.Paint(5, 2, LevelBrush.Box);
            document.Paint(6, 3, LevelBrush.Goal);
            Assert.IsTrue(document.IsDirty);

            Assert.IsTrue(document.Save(), "Save must write to the bound asset.");
            Assert.IsFalse(document.IsDirty);

            var saved = AssetDatabase.LoadAssetAtPath<LevelDefinition>(TempAssetPath);
            Assert.IsNotNull(saved);
            Assert.AreEqual(document.BoxCount, CountOccupant(saved, OccupantType.Box));
        }

        [Test]
        public void Paint_PlateA_SetsGroupA()
        {
            LevelEditorDocument document = null;
            try
            {
                document = LevelEditorDocument.CreateNew(5, 5);
                document.Paint(1, 1, LevelBrush.PlateA);

                LevelDefinition def = document.Working;
                int index = def.Index(1, 1);
                Assert.AreEqual(TileType.Plate, def.cells[index], "Plate A must paint a plate tile.");
                Assert.AreEqual(OccupantType.None, def.occupants[index], "A plate carries no occupant.");
                Assert.AreEqual(0, def.GetGroupId(index), "Plate A must tag the cell as group A.");
            }
            finally
            {
                if (document != null)
                {
                    Object.DestroyImmediate(document.Working);
                }
            }
        }

        [Test]
        public void Paint_PlateB_SetsGroupB()
        {
            LevelEditorDocument document = null;
            try
            {
                document = LevelEditorDocument.CreateNew(5, 5);
                document.Paint(1, 1, LevelBrush.PlateB);

                LevelDefinition def = document.Working;
                int index = def.Index(1, 1);
                Assert.AreEqual(TileType.Plate, def.cells[index], "Plate B must paint a plate tile.");
                Assert.AreEqual(OccupantType.None, def.occupants[index], "A plate carries no occupant.");
                Assert.AreEqual(1, def.GetGroupId(index), "Plate B must tag the cell as group B.");
            }
            finally
            {
                if (document != null)
                {
                    Object.DestroyImmediate(document.Working);
                }
            }
        }

        [Test]
        public void Paint_DoorA_SetsGroupA()
        {
            LevelEditorDocument document = null;
            try
            {
                document = LevelEditorDocument.CreateNew(5, 5);
                document.Paint(1, 1, LevelBrush.DoorA);

                LevelDefinition def = document.Working;
                int index = def.Index(1, 1);
                Assert.AreEqual(TileType.Door, def.cells[index], "Door A must paint a door tile.");
                Assert.AreEqual(OccupantType.None, def.occupants[index], "A door carries no occupant.");
                Assert.AreEqual(0, def.GetGroupId(index), "Door A must tag the cell as group A.");
            }
            finally
            {
                if (document != null)
                {
                    Object.DestroyImmediate(document.Working);
                }
            }
        }

        [Test]
        public void Paint_DoorB_SetsGroupB()
        {
            LevelEditorDocument document = null;
            try
            {
                document = LevelEditorDocument.CreateNew(5, 5);
                document.Paint(1, 1, LevelBrush.DoorB);

                LevelDefinition def = document.Working;
                int index = def.Index(1, 1);
                Assert.AreEqual(TileType.Door, def.cells[index], "Door B must paint a door tile.");
                Assert.AreEqual(OccupantType.None, def.occupants[index], "A door carries no occupant.");
                Assert.AreEqual(1, def.GetGroupId(index), "Door B must tag the cell as group B.");
            }
            finally
            {
                if (document != null)
                {
                    Object.DestroyImmediate(document.Working);
                }
            }
        }

        [Test]
        public void Paint_FloorOverDoorB_ResetsGroupToZero()
        {
            LevelEditorDocument document = null;
            try
            {
                document = LevelEditorDocument.CreateNew(5, 5);
                document.Paint(1, 1, LevelBrush.DoorB);
                document.Paint(1, 1, LevelBrush.Floor);

                LevelDefinition def = document.Working;
                int index = def.Index(1, 1);
                Assert.AreEqual(TileType.Floor, def.cells[index], "Floor must replace the door tile.");
                Assert.AreEqual(0, def.GetGroupId(index), "Repainting over a group-B door must clear the stale group id.");
            }
            finally
            {
                if (document != null)
                {
                    Object.DestroyImmediate(document.Working);
                }
            }
        }

        [Test]
        public void Paint_LegacyDoor_StillGroupA()
        {
            LevelEditorDocument document = null;
            try
            {
                document = LevelEditorDocument.CreateNew(5, 5);
                document.Paint(1, 1, LevelBrush.Door);

                LevelDefinition def = document.Working;
                int index = def.Index(1, 1);
                Assert.AreEqual(TileType.Door, def.cells[index], "The legacy Door brush still paints a door.");
                Assert.AreEqual(0, def.GetGroupId(index), "The legacy Door brush means group A (back-compat).");
            }
            finally
            {
                if (document != null)
                {
                    Object.DestroyImmediate(document.Working);
                }
            }
        }

        [Test]
        public void Paint_BoxOntoPlateB_PreservesGroupB()
        {
            LevelEditorDocument document = null;
            try
            {
                document = LevelEditorDocument.CreateNew(5, 5);
                document.Paint(1, 1, LevelBrush.PlateB);
                document.Paint(1, 1, LevelBrush.Box);

                LevelDefinition def = document.Working;
                int index = def.Index(1, 1);
                Assert.AreEqual(TileType.Plate, def.cells[index], "A box must not erase the plate tile.");
                Assert.AreEqual(OccupantType.Box, def.occupants[index], "The box must land on the plate.");
                Assert.AreEqual(1, def.GetGroupId(index), "A box on a group-B plate must keep the plate's group B identity.");
            }
            finally
            {
                if (document != null)
                {
                    Object.DestroyImmediate(document.Working);
                }
            }
        }

        private static int CountOccupant(LevelDefinition def, OccupantType occupant)
        {
            int count = 0;
            for (int i = 0; i < def.occupants.Count; i++)
            {
                if (def.occupants[i] == occupant)
                {
                    count++;
                }
            }

            return count;
        }

        private static (int x, int y) FindPlayer(LevelDefinition def)
        {
            for (int i = 0; i < def.occupants.Count; i++)
            {
                if (def.occupants[i] == OccupantType.Player)
                {
                    return (i % def.width, i / def.width);
                }
            }

            Assert.Fail("The level has no player.");
            return (-1, -1);
        }
    }
}

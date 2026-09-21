using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers M1 group identity in the data model only: the parallel per-cell <c>groupIds</c> list is
    /// created with the <c>count == width*height</c> invariant by the factory and the editor document,
    /// carried by coordinate through Clone and Resize, and read defensively through
    /// <see cref="LevelDefinition.GetGroupId"/> so assets that predate the field stay implicit group A.
    /// Every created <see cref="LevelDefinition"/> is destroyed in a finally block; the shipped L05
    /// asset is only read, never mutated.
    /// </summary>
    public class GroupIdTests
    {
        [Test]
        public void GetGroupId_DefaultsToGroupA_WhenListAbsent()
        {
            LevelDefinition def = null;
            try
            {
                def = LevelAssetFactory.BuildLevel("G", new[] { "#####", "#.#.#", "#####" });
                def.groupIds = null;

                Assert.AreEqual(0, def.GetGroupId(0), "A null list must read as group A at the first index.");
                Assert.AreEqual(0, def.GetGroupId(def.width * def.height - 1), "A null list must read as group A at the last index.");
                Assert.AreEqual(0, def.GetGroupId(-1), "A negative index must fall back to group A.");
                Assert.AreEqual(0, def.GetGroupId(int.MaxValue), "An out-of-range index must fall back to group A.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void GroupIds_CreatedWithCorrectCount_ByFactoryAndCreateNew()
        {
            LevelDefinition built = null;
            LevelEditorDocument document = null;
            try
            {
                built = LevelAssetFactory.BuildLevel("G", new[] { "#####", "#PBG#", "#####" });
                Assert.IsNotNull(built.groupIds, "The factory must materialize the group list.");
                Assert.AreEqual(built.width * built.height, built.groupIds.Count, "The factory list must match width*height.");
                foreach (int id in built.groupIds)
                {
                    Assert.AreEqual(0, id, "Factory-built cells are all group A.");
                }

                document = LevelEditorDocument.CreateNew(5, 4);
                LevelDefinition created = document.Working;
                Assert.IsNotNull(created.groupIds, "CreateNew must materialize the group list.");
                Assert.AreEqual(created.width * created.height, created.groupIds.Count, "The CreateNew list must match width*height.");
                foreach (int id in created.groupIds)
                {
                    Assert.AreEqual(0, id, "A fresh document starts as group A.");
                }
            }
            finally
            {
                Object.DestroyImmediate(built);
                if (document != null)
                {
                    Object.DestroyImmediate(document.Working);
                }
            }
        }

        [Test]
        public void Clone_CarriesGroupIds()
        {
            LevelDefinition def = null;
            LevelDefinition copy = null;
            try
            {
                def = LevelAssetFactory.BuildLevel("G", new[] { "#####", "#PBG#", "#####" });
                int someIndex = def.Index(2, 1);
                def.groupIds[someIndex] = 1;
                def.cells[someIndex] = TileType.Door;

                copy = LevelEditorDocument.Clone(def);

                Assert.AreEqual(1, copy.groupIds[someIndex], "Clone must carry the group id at the same index.");
                Assert.AreEqual(def.groupIds.Count, copy.groupIds.Count, "Clone must preserve the group list length.");
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(copy);
            }
        }

        [Test]
        public void Resize_CarriesGroupByCoordinate()
        {
            LevelDefinition def = null;
            try
            {
                def = LevelAssetFactory.BuildLevel(
                    "TwoGroup",
                    new[] { "#####", "#####", "##D##", "#####", "#####" });
                int doorIndex5 = def.Index(2, 2);
                def.groupIds[doorIndex5] = 1;

                LevelResizer.Resize(def, 7, 7);

                int doorIndex7 = def.Index(2, 2);
                Assert.AreEqual(7 * 7, def.groupIds.Count, "Grow must rebuild a full-size group list.");
                Assert.AreEqual(TileType.Door, def.cells[doorIndex7], "The door must survive a grow at the same coordinate.");
                Assert.AreEqual(1, def.groupIds[doorIndex7], "Grow must carry the group id by coordinate.");

                LevelResizer.Resize(def, 3, 3);

                int doorIndex3 = def.Index(2, 2);
                Assert.AreEqual(3 * 3, def.groupIds.Count, "Shrink must rebuild a full-size group list.");
                Assert.AreEqual(TileType.Door, def.cells[doorIndex3], "The door must survive a shrink at the same coordinate.");
                Assert.AreEqual(1, def.groupIds[doorIndex3], "Shrink must carry the group id by coordinate.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void BackCompat_ShippedLevel05_HasNullGroupIds_ReadsAsGroupA()
        {
            LevelDefinition level05 = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Sokoban/Levels/Level05.asset");
            Assert.IsNotNull(level05, "The shipped Level05 asset must load.");

            for (int index = 0; index < level05.width * level05.height; index++)
            {
                Assert.AreEqual(0, level05.GetGroupId(index), "A pre-field asset must read as implicit group A at every index.");
            }
        }
    }
}

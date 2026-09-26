using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Editor Undo/Redo working-copy history. <see cref="LevelEditorDocument"/> records a deep
    /// in-memory Clone of the previous working state before every mutation (Paint/Resize/SetLevelName),
    /// swaps the working copy on Undo/Redo, clears history on New/Load, rebases on Save/SaveAs and is
    /// capped at 64 entries. The window's Ctrl+Z/Ctrl+Y input is IMGUI rendering and is therefore
    /// manual-gate verified; these tests pin the document history logic headlessly.
    /// </summary>
    public class LevelEditorHistoryTests
    {
        private const string Level01Path = "Assets/Sokoban/Levels/Level01.asset";
        private const string TempFolder = "Assets/Sokoban/Levels/Temp";
        private const string TempAssetPath = TempFolder + "/LevelEditorHistoryTests.asset";

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
        public void Paint_UndoRestoresPreviousCellsAndOccupants_RedoReapplies()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            var cellsBefore = new List<TileType>(document.Working.cells);
            var occupantsBefore = new List<OccupantType>(document.Working.occupants);

            try
            {
                document.Paint(2, 2, LevelBrush.Wall);
                Assert.AreEqual(TileType.Wall, document.Working.cells[document.Working.Index(2, 2)],
                    "Precondition: the paint must be applied.");

                Assert.IsTrue(document.Undo(), "Undo must report success.");
                CollectionAssert.AreEqual(cellsBefore, document.Working.cells, "Undo must restore the previous cells.");
                CollectionAssert.AreEqual(occupantsBefore, document.Working.occupants, "Undo must restore the previous occupants.");

                Assert.IsTrue(document.Redo(), "Redo must report success.");
                Assert.AreEqual(TileType.Wall, document.Working.cells[document.Working.Index(2, 2)],
                    "Redo must re-apply the paint.");
                Assert.AreEqual(OccupantType.None, document.Working.occupants[document.Working.Index(2, 2)],
                    "Redo must re-apply the occupant state of the paint.");
            }
            finally
            {
                Destroy(document);
            }
        }

        [Test]
        public void Resize_UndoRestoresPreviousSizeAndContents()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            var cellsBefore = new List<TileType>(document.Working.cells);
            var occupantsBefore = new List<OccupantType>(document.Working.occupants);
            int widthBefore = document.Working.width;
            int heightBefore = document.Working.height;

            try
            {
                document.Resize(widthBefore + 2, heightBefore + 1);
                Assert.AreEqual(widthBefore + 2, document.Working.width, "Precondition: the resize must be applied.");

                Assert.IsTrue(document.Undo(), "Undo must report success.");
                Assert.AreEqual(widthBefore, document.Working.width, "Undo must restore the previous width.");
                Assert.AreEqual(heightBefore, document.Working.height, "Undo must restore the previous height.");
                CollectionAssert.AreEqual(cellsBefore, document.Working.cells, "Undo must restore the previous cells.");
                CollectionAssert.AreEqual(occupantsBefore, document.Working.occupants, "Undo must restore the previous occupants.");
            }
            finally
            {
                Destroy(document);
            }
        }

        [Test]
        public void SetLevelName_UndoRestoresPreviousName()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();
            string nameBefore = document.Working.levelName;

            try
            {
                document.SetLevelName("Renamed");
                Assert.AreEqual("Renamed", document.Working.levelName, "Precondition: the rename must be applied.");

                Assert.IsTrue(document.Undo(), "Undo must report success.");
                Assert.AreEqual(nameBefore, document.Working.levelName, "Undo must restore the previous level name.");
                Assert.AreEqual(nameBefore, document.Working.name, "Undo must restore the previous asset name.");
            }
            finally
            {
                Destroy(document);
            }
        }

        [Test]
        public void MultipleMutations_UndoWalksBackInOrder_RedoWalksForwardInOrder()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();

            try
            {
                document.SetLevelName("First");
                document.Resize(7, 5);
                document.Paint(1, 1, LevelBrush.Wall);

                Assert.IsTrue(document.CanUndo, "Mutations must create undo history.");
                Assert.IsFalse(document.CanRedo, "Fresh mutations must not create redo history.");

                Assert.IsTrue(document.Undo(), "The first undo must revert the paint.");
                Assert.AreEqual(7, document.Working.width, "The first undo must restore the resized width.");
                Assert.AreEqual(5, document.Working.height, "The first undo must restore the resized height.");
                Assert.AreEqual(TileType.Floor, document.Working.cells[document.Working.Index(1, 1)],
                    "The first undo must remove the painted wall.");

                Assert.IsTrue(document.Undo(), "The second undo must revert the resize.");
                Assert.AreEqual(8, document.Working.width, "The second undo must restore the default width.");
                Assert.AreEqual(6, document.Working.height, "The second undo must restore the default height.");
                Assert.AreEqual("First", document.Working.levelName, "The second undo must keep the renamed state.");

                Assert.IsTrue(document.Undo(), "The third undo must revert the rename.");
                Assert.AreEqual("Untitled", document.Working.levelName, "The third undo must restore the original name.");
                Assert.IsFalse(document.CanUndo, "After walking back through every mutation, no undo entries remain.");
                Assert.IsTrue(document.CanRedo, "Walking back must populate redo history.");

                Assert.IsTrue(document.Redo(), "The first redo must re-apply the rename.");
                Assert.AreEqual("First", document.Working.levelName);

                Assert.IsTrue(document.Redo(), "The second redo must re-apply the resize.");
                Assert.AreEqual(7, document.Working.width);
                Assert.AreEqual(5, document.Working.height);

                Assert.IsTrue(document.Redo(), "The third redo must re-apply the paint.");
                Assert.AreEqual(TileType.Wall, document.Working.cells[document.Working.Index(1, 1)]);
                Assert.IsFalse(document.CanRedo, "After redoing every step, no redo entries remain.");
            }
            finally
            {
                Destroy(document);
            }
        }

        [Test]
        public void NewMutationAfterUndo_ClearsRedo()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();

            try
            {
                document.Paint(1, 1, LevelBrush.Wall);
                document.Paint(2, 1, LevelBrush.Goal);
                Assert.IsTrue(document.Undo(), "Precondition: undo back one step.");
                Assert.IsTrue(document.CanRedo, "Precondition: undo created redo history.");

                document.SetLevelName("Fresh Edit");

                Assert.IsFalse(document.CanRedo, "A new mutation must clear the redo history.");
                Assert.IsTrue(document.CanUndo, "A new mutation must add an undo entry.");

                Assert.IsTrue(document.Undo(), "Undo after the fresh edit must revert the fresh edit.");
                Assert.AreEqual(TileType.Wall, document.Working.cells[document.Working.Index(1, 1)],
                    "Undo must reach the state before the fresh edit.");
                Assert.AreEqual(TileType.Floor, document.Working.cells[document.Working.Index(2, 1)],
                    "The cleared redo target must no longer be reachable.");
            }
            finally
            {
                Destroy(document);
            }
        }

        [Test]
        public void CreateNew_StartsWithEmptyHistory()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();

            try
            {
                Assert.IsFalse(document.CanUndo, "A new document must start with no undo history.");
                Assert.IsFalse(document.CanRedo, "A new document must start with no redo history.");
            }
            finally
            {
                Destroy(document);
            }
        }

        [Test]
        public void LoadFrom_ClearsHistory_EvenWhenPreviousDocumentHadHistory()
        {
            var source = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Path);
            Assert.IsNotNull(source, "Level01 asset is required for this test.");

            LevelEditorDocument first = LevelEditorDocument.CreateNew();
            first.Paint(1, 1, LevelBrush.Wall);
            Assert.IsTrue(first.CanUndo, "Precondition: the first document has undo history.");

            LevelEditorDocument second = null;

            try
            {
                second = LevelEditorDocument.LoadFrom(source);
                Assert.IsFalse(second.CanUndo, "LoadFrom must start with empty undo history.");
                Assert.IsFalse(second.CanRedo, "LoadFrom must start with empty redo history.");
            }
            finally
            {
                Destroy(first);
                Destroy(second);
            }
        }

        [Test]
        public void SaveAs_RebasesHistory_ClearingUndoAndRedo()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();

            try
            {
                document.Paint(1, 1, LevelBrush.Wall);
                document.Paint(2, 1, LevelBrush.Goal);
                Assert.IsTrue(document.Undo(), "Precondition: undo back one step.");
                Assert.IsTrue(document.CanUndo, "Precondition: undo history exists before SaveAs.");
                Assert.IsTrue(document.CanRedo, "Precondition: redo history exists before SaveAs.");

                Assert.IsTrue(document.SaveAs(TempAssetPath), "SaveAs must succeed for the temp asset.");
                Assert.IsFalse(document.IsDirty, "Saving must clear the dirty flag.");
                Assert.IsFalse(document.CanUndo, "SaveAs must rebase (clear) the undo history.");
                Assert.IsFalse(document.CanRedo, "SaveAs must rebase (clear) the redo history.");
            }
            finally
            {
                Destroy(document);
            }
        }

        [Test]
        public void Undo_SetsIsDirtyUnconditionally_EvenWhenReturningToSavedState()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();

            try
            {
                Assert.IsTrue(document.SaveAs(TempAssetPath), "Precondition: save the baseline.");
                Assert.IsFalse(document.IsDirty, "Precondition: saving clears the dirty flag.");
                Assert.IsFalse(document.CanUndo, "Precondition: SaveAs rebases history.");

                document.Paint(1, 1, LevelBrush.Wall);
                Assert.IsTrue(document.IsDirty, "Precondition: the paint marks the document dirty.");

                Assert.IsTrue(document.Undo(), "Undo must report success.");
                Assert.IsTrue(
                    document.IsDirty,
                    "Undo must set IsDirty unconditionally, even when the working copy matches the saved state.");
            }
            finally
            {
                Destroy(document);
            }
        }

        [Test]
        public void HistoryCap_DropsOldestEntries_UndoStopsAfter64Steps()
        {
            LevelEditorDocument document = LevelEditorDocument.CreateNew();

            try
            {
                for (int i = 0; i < 80; i++)
                {
                    document.SetLevelName("Step " + i);
                }

                Assert.IsTrue(document.CanUndo, "The capped history must still offer undo entries.");

                int steps = 0;
                while (document.Undo())
                {
                    steps++;
                }

                Assert.AreEqual(64, steps, "80 mutations must leave exactly the 64 most recent undo entries.");
                Assert.IsFalse(document.CanUndo, "The undo stack must be exhausted after walking back.");
            }
            finally
            {
                Destroy(document);
            }
        }

        /// <summary>Destroys the in-memory working copy created by a test. Shared assets are never passed here.</summary>
        private static void Destroy(LevelEditorDocument document)
        {
            if (document == null)
            {
                return;
            }

            LevelDefinition working = document.Working;
            if (working != null)
            {
                Object.DestroyImmediate(working);
            }
        }
    }
}

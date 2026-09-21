using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the T07 one-click playtest decision logic in <see cref="PlaytestLauncher"/>: a valid
    /// saved document arms the persisted asset <em>in the persistent request channel and on disk</em>,
    /// an invalid document is refused without arming anything, preparing a playtest never mutates the
    /// shipped catalog or level assets, and the editor janitor clears the on-disk request when play
    /// mode exits.
    /// </summary>
    public class PlaytestTests
    {
        private const string Level01Path = "Assets/Sokoban/Levels/Level01.asset";
        private const string CatalogPath = "Assets/Sokoban/Levels/LevelCatalog.asset";
        private const string TempFolder = "Assets/Sokoban/Levels/Temp";
        private const string TempAssetPath = TempFolder + "/PlaytestTestsLevel.asset";

        [SetUp]
        public void SetUp()
        {
            PlaytestLauncher.Arm(null);
        }

        [TearDown]
        public void TearDown()
        {
            PlaytestLauncher.Arm(null);

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

        /// <summary>Creates a fresh, valid level document and persists it to the temp asset path.</summary>
        private static LevelEditorDocument SaveNewLevel()
        {
            LevelEditorDocument doc = LevelEditorDocument.CreateNew();
            Assert.IsTrue(doc.SaveAs(TempAssetPath), "The temp playtest level must save.");
            Assert.IsFalse(doc.IsDirty, "A saved document must be clean.");
            return doc;
        }

        [Test]
        public void TryPreparePlaytest_ValidSavedDocument_ArmsThePersistedAsset()
        {
            LevelEditorDocument doc = SaveNewLevel();
            LevelDefinition persisted = AssetDatabase.LoadAssetAtPath<LevelDefinition>(TempAssetPath);
            Assert.IsNotNull(persisted);

            bool ok = PlaytestLauncher.TryPreparePlaytest(doc, out string error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(error, "A successful prepare must not report an error.");

            PlaytestRequest request = PlaytestLauncher.LoadRequestAsset();
            Assert.IsNotNull(request, "The persistent request asset must exist.");
            Assert.AreSame(persisted, request.Level, "The persisted asset must be armed in the request asset.");
            Assert.AreNotSame(doc.Working, request.Level, "The working copy must never be armed.");
        }

        [Test]
        public void TryPreparePlaytest_ValidSavedDocument_WritesTheArmedRequestToDisk()
        {
            LevelEditorDocument doc = SaveNewLevel();
            string persistedGuid = AssetDatabase.AssetPathToGUID(TempAssetPath);
            Assert.IsNotEmpty(persistedGuid, "The temp level must have a project GUID.");

            Assert.IsTrue(PlaytestLauncher.TryPreparePlaytest(doc, out string error), error);

            string onDisk = File.ReadAllText(PlaytestRequest.AssetPath);
            StringAssert.Contains(
                "Level: {fileID: 11400000, guid: " + persistedGuid,
                onDisk,
                "Arming must persist the request before play mode starts, so the domain reload cannot lose it.");
        }

        [Test]
        public void TryPreparePlaytest_InvalidDocument_ReturnsFalseAndDoesNotArm()
        {
            LevelEditorDocument doc = SaveNewLevel();

            // Break the working copy in memory (remove the player); the saved asset stays untouched.
            for (int i = 0; i < doc.Working.occupants.Count; i++)
            {
                if (doc.Working.occupants[i] == OccupantType.Player)
                {
                    doc.Working.occupants[i] = OccupantType.None;
                    break;
                }
            }

            bool ok = PlaytestLauncher.TryPreparePlaytest(doc, out string error);

            Assert.IsFalse(ok, "An invalid level must not be playtested.");
            Assert.IsNotEmpty(error, "A failure must explain why.");
            Assert.IsNull(PlaytestLauncher.LoadRequestAsset().Level, "A failed prepare must not arm a playtest level.");
        }

        [Test]
        public void TryPreparePlaytest_LeavesCatalogAndShippedLevelsUnchanged()
        {
            var level01Before = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Path);
            var catalogBefore = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            Assert.IsNotNull(level01Before);
            Assert.IsNotNull(catalogBefore);

            var level01Cells = new List<TileType>(level01Before.cells);
            var level01Occupants = new List<OccupantType>(level01Before.occupants);
            string level01Name = level01Before.levelName;
            int level01Width = level01Before.width;
            int level01Height = level01Before.height;

            var catalogPaths = new List<string>();
            for (int i = 0; i < catalogBefore.levels.Count; i++)
            {
                catalogPaths.Add(AssetDatabase.GetAssetPath(catalogBefore.levels[i]));
            }

            LevelEditorDocument doc = SaveNewLevel();
            Assert.IsTrue(PlaytestLauncher.TryPreparePlaytest(doc, out string error), error);

            var level01After = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Path);
            var catalogAfter = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            Assert.IsNotNull(level01After);
            Assert.IsNotNull(catalogAfter);

            CollectionAssert.AreEqual(level01Cells, level01After.cells, "Level01 cells must be unchanged.");
            CollectionAssert.AreEqual(level01Occupants, level01After.occupants, "Level01 occupants must be unchanged.");
            Assert.AreEqual(level01Name, level01After.levelName, "Level01 name must be unchanged.");
            Assert.AreEqual(level01Width, level01After.width, "Level01 width must be unchanged.");
            Assert.AreEqual(level01Height, level01After.height, "Level01 height must be unchanged.");

            Assert.AreEqual(catalogPaths.Count, catalogAfter.levels.Count, "Catalog entry count must be unchanged.");
            for (int i = 0; i < catalogPaths.Count; i++)
            {
                Assert.AreEqual(
                    catalogPaths[i],
                    AssetDatabase.GetAssetPath(catalogAfter.levels[i]),
                    $"Catalog entry {i} must be unchanged.");
            }
        }

        [Test]
        public void Janitor_ClearsTheOnDiskRequest_WhenPlayModeExits()
        {
            LevelEditorDocument doc = SaveNewLevel();
            Assert.IsTrue(PlaytestLauncher.TryPreparePlaytest(doc, out string error), error);
            Assert.IsNotNull(PlaytestLauncher.LoadRequestAsset().Level, "Precondition: a request is armed.");

            PlaytestRequestJanitor.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            Assert.IsNull(
                PlaytestLauncher.LoadRequestAsset().Level,
                "Exiting play mode must clear the armed request in the asset.");
            StringAssert.Contains(
                "Level: {fileID: 0}",
                File.ReadAllText(PlaytestRequest.AssetPath),
                "Exiting play mode must clear the request on disk so the next normal Play cannot load it.");
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the public editor API surface the Content Dashboard will build on:
    /// <see cref="LevelEditorWindow.OpenFor"/> must load a level into the window's working copy without
    /// the designer touching the Load field, and the arbitrary-level playtest entry must arm the
    /// persistent <see cref="PlaytestRequest"/> with the target level while leaving the shipped
    /// <see cref="LevelCatalog"/> untouched.
    ///
    /// The scene-switch / play-mode tail of <see cref="LevelEditorWindow.PlaytestLevel"/> is editor-bound
    /// (it opens the gameplay scene and calls <c>EditorApplication.EnterPlaymode</c>) and is therefore
    /// exercised at the manual gate only; these tests pin the decision half,
    /// <see cref="PlaytestLauncher.TryPreparePlaytest(LevelDefinition, out string)"/>, that the entry
    /// funnels through.
    /// </summary>
    public class LevelEditorApiTests
    {
        private const string CatalogPath = "Assets/Sokoban/Levels/LevelCatalog.asset";
        private const string TempFolder = "Assets/Sokoban/Levels/Temp";
        private const string TempAssetPath = TempFolder + "/LevelEditorApiTestsLevel.asset";

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

        [Test]
        public void OpenFor_LoadsTheSourceLevelIntoTheWorkingCopy()
        {
            LevelDefinition source = null;
            LevelEditorWindow window = null;
            LevelDefinition startingWorking = null;
            LevelDefinition loadedWorking = null;
            try
            {
                // Open the window up front so its implicit starting document can be captured and
                // destroyed too (D015); OpenFor returns that same window instance.
                window = EditorWindow.GetWindow<LevelEditorWindow>();
                if (window.LoadedDocument != null)
                {
                    startingWorking = window.LoadedDocument.Working;
                }

                source = LevelAssetFactory.BuildLevel("ApiOpen", new[] { "######", "#P.BG#", "######" });

                LevelEditorWindow.OpenFor(source);

                LevelEditorDocument loaded = window.LoadedDocument;
                Assert.IsNotNull(loaded, "OpenFor must leave a working copy loaded in the window.");
                loadedWorking = loaded.Working;

                Assert.AreNotSame(source, loadedWorking, "The editor must load a copy, never the source asset.");
                Assert.AreEqual(source.levelName, loadedWorking.levelName, "The level name must be copied.");
                Assert.AreEqual(source.width, loadedWorking.width, "The level width must be copied.");
                Assert.AreEqual(source.height, loadedWorking.height, "The level height must be copied.");
                CollectionAssert.AreEqual(source.cells, loadedWorking.cells, "The cells must be copied.");
                CollectionAssert.AreEqual(source.occupants, loadedWorking.occupants, "The occupants must be copied.");
            }
            finally
            {
                if (window != null)
                {
                    window.Close();
                }

                Destroy(loadedWorking);
                Destroy(startingWorking);
                Destroy(source);
            }
        }

        [Test]
        public void PlaytestEntry_ArmsTheTargetLevel_AndLeavesTheCatalogUnchanged()
        {
            LevelCatalog catalogBefore = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            Assert.IsNotNull(catalogBefore, "The shipped catalog is required for this test.");

            var catalogPaths = new List<string>();
            for (int i = 0; i < catalogBefore.levels.Count; i++)
            {
                catalogPaths.Add(AssetDatabase.GetAssetPath(catalogBefore.levels[i]));
            }

            LevelEditorDocument doc = LevelEditorDocument.CreateNew();
            LevelDefinition source = doc.Working;
            try
            {
                Assert.IsTrue(doc.SaveAs(TempAssetPath), "The temp playtest level must save.");
                LevelDefinition target = AssetDatabase.LoadAssetAtPath<LevelDefinition>(TempAssetPath);
                Assert.IsNotNull(target, "The saved level asset must be loadable.");

                // The arming half of LevelEditorWindow.PlaytestLevel; the entry then enters play mode,
                // which cannot run inside an EditMode test.
                bool ok = PlaytestLauncher.TryPreparePlaytest(target, out string error);

                Assert.IsTrue(ok, error);
                Assert.IsNull(error, "A successful prepare must not report an error.");

                PlaytestRequest request = PlaytestLauncher.LoadRequestAsset();
                Assert.IsNotNull(request, "The persistent request asset must exist.");
                Assert.AreSame(target, request.Level, "The target level must be armed in the request asset.");

                LevelCatalog catalogAfter = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
                Assert.IsNotNull(catalogAfter, "The shipped catalog must still exist.");
                Assert.AreEqual(catalogPaths.Count, catalogAfter.levels.Count, "Catalog entry count must be unchanged.");
                for (int i = 0; i < catalogPaths.Count; i++)
                {
                    Assert.AreEqual(
                        catalogPaths[i],
                        AssetDatabase.GetAssetPath(catalogAfter.levels[i]),
                        $"Catalog entry {i} must be unchanged.");
                }
            }
            finally
            {
                Destroy(source);
            }
        }

        /// <summary>D015: destroys an in-memory ScriptableObject created by a test.</summary>
        private static void Destroy(LevelDefinition target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

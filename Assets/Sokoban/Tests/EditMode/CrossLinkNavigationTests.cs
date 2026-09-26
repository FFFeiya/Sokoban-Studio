using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the two cross-link seams that close the navigation triangle between the Content
    /// Dashboard, the Level Editor and the Solution Preview: <see cref="LevelEditorWindow.OpenDashboard"/>
    /// must resolve the Content Dashboard window, and <see cref="SolutionPreviewWindow.OpenInEditor"/> must
    /// open the Level Editor bound to the given level's working copy.
    ///
    /// The Dashboard already links out to the editor (Edit), the preview (Preview) and playtest
    /// (Playtest), so these two inward links make each surface reachable from the others. Only the
    /// window-opening half is exercised here; the actual GUI clicks are manual-gate concerns.
    /// </summary>
    public class CrossLinkNavigationTests
    {
        [Test]
        public void OpenDashboard_ResolvesContentDashboardWindow()
        {
            ContentDashboardWindow window = null;
            try
            {
                LevelEditorWindow.OpenDashboard();

                window = EditorWindow.GetWindow<ContentDashboardWindow>();
                Assert.IsNotNull(window, "OpenDashboard must resolve the Content Dashboard window.");
                Assert.IsTrue(window is ContentDashboardWindow, "The cross-link must open the Content Dashboard window.");
            }
            finally
            {
                if (window != null)
                {
                    window.Close();
                }
            }
        }

        [Test]
        public void OpenInEditor_OpensLevelEditorBoundToSameLevel()
        {
            LevelDefinition level = null;
            LevelEditorWindow window = null;
            LevelDefinition startingWorking = null;
            LevelDefinition loadedWorking = null;
            try
            {
                // Open the editor up front so its implicit starting document can be captured and
                // destroyed too; OpenInEditor focuses that same window instance.
                window = EditorWindow.GetWindow<LevelEditorWindow>();
                if (window.LoadedDocument != null)
                {
                    startingWorking = window.LoadedDocument.Working;
                }

                level = LevelAssetFactory.BuildLevel("H6Cross", new[] { "######", "#P.BG#", "######" });

                SolutionPreviewWindow.OpenInEditor(level);

                LevelEditorDocument loaded = window.LoadedDocument;
                Assert.IsNotNull(loaded, "OpenInEditor must leave a working copy loaded in the editor.");
                loadedWorking = loaded.Working;

                Assert.AreNotSame(level, loadedWorking, "The editor must load a copy, never the source asset.");
                Assert.AreEqual(level.levelName, loadedWorking.levelName, "The level name must be copied.");
                Assert.AreEqual(level.width, loadedWorking.width, "The level width must be copied.");
                Assert.AreEqual(level.height, loadedWorking.height, "The level height must be copied.");
                CollectionAssert.AreEqual(level.cells, loadedWorking.cells, "The cells must be copied.");
                CollectionAssert.AreEqual(level.occupants, loadedWorking.occupants, "The occupants must be copied.");
            }
            finally
            {
                if (window != null)
                {
                    window.Close();
                }

                Destroy(loadedWorking);
                Destroy(startingWorking);
                Destroy(level);
            }
        }

        /// <summary>Destroys an in-memory ScriptableObject created by a test.</summary>
        private static void Destroy(LevelDefinition target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

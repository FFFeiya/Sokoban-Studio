using System.Collections;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sokoban.Tests
{
    /// <summary>
    /// The F-01 regression suite: the one-click Playtest request must survive the default Play Mode
    /// domain reload. The test arms a uniquely named, non-Catalog level <em>in edit mode</em>, writes
    /// it into the persistent request asset, opens the gameplay scene and enters play mode with a real
    /// domain reload, then asserts the gameplay bootstrap loaded that exact level. The follow-up test
    /// asserts the editor janitor cleared the request on disk when play mode ended.
    /// </summary>
    public class PlaytestDomainReloadTests
    {
        private const string LevelsFolder = "Assets/Sokoban/Levels";
        private const string ProbeFolder = LevelsFolder + "/Temp";
        private const string ProbePath = ProbeFolder + "/PlaytestDomainProbe.asset";
        private const string ProbeName = "Playtest Domain Reload Probe";

        /// <summary>
        /// Deliberately different from every shipped level (no shipped level is 6x4), so a silent
        /// fallback to the serialized level cannot pass this test.
        /// </summary>
        private static readonly string[] ProbeRows =
        {
            "######",
            "#P.BG#",
            "#....#",
            "######"
        };

        [TearDown]
        public void TearDown()
        {
            PlaytestRequestJanitor.Clear();

            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(ProbePath) != null)
            {
                AssetDatabase.DeleteAsset(ProbePath);
            }

            if (AssetDatabase.IsValidFolder(ProbeFolder) &&
                AssetDatabase.FindAssets("t:Object", new[] { ProbeFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(ProbeFolder);
            }
        }

        private static LevelDefinition CreateProbeLevel()
        {
            if (!AssetDatabase.IsValidFolder(ProbeFolder))
            {
                AssetDatabase.CreateFolder(LevelsFolder, "Temp");
            }

            LevelDefinition probe = LevelAssetFactory.CreateLevelAsset(ProbePath, ProbeName, ProbeRows);
            Assert.IsNotNull(probe, "The probe level asset must be created.");
            return probe;
        }

        [UnityTest]
        [Order(1)]
        public IEnumerator ArmedRequest_SurvivesDomainReload_AndLoadsTheAuthoredLevel()
        {
            LevelDefinition probe = CreateProbeLevel();

            // The probe must not be a catalog level: this is exactly the case the shipped flow misses.
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(LevelAssetFactory.CatalogPath);
            Assert.IsNotNull(catalog, "The shipped catalog must exist.");
            for (int i = 0; i < catalog.levels.Count; i++)
            {
                Assert.AreNotEqual(
                    ProbePath,
                    AssetDatabase.GetAssetPath(catalog.levels[i]),
                    "The probe level must not be part of the shipped catalog.");
            }

            PlaytestRequestJanitor.Clear();
            PlaytestLauncher.Arm(probe);

            PlaytestRequest armed = PlaytestLauncher.LoadRequestAsset();
            Assert.IsNotNull(armed, "The persistent request asset must exist.");
            Assert.AreSame(probe, armed.Level, "Precondition: the request asset must be armed with the probe.");

            EditorSceneManager.OpenScene(LevelAssetFactory.GameplayScenePath, OpenSceneMode.Single);

            // Real domain reload: plain statics armed before this point are gone afterwards.
            yield return new EnterPlayMode();
            yield return null;

            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "The gameplay scene must contain a GameBootstrap in play mode.");
            Assert.IsNotNull(bootstrap.Board, "The bootstrap must have built a board.");

            LevelDefinition reloadedProbe = AssetDatabase.LoadAssetAtPath<LevelDefinition>(ProbePath);

            Assert.IsNotNull(reloadedProbe, "The probe asset must still exist after the domain reload.");
            Assert.AreEqual(
                ProbeName,
                bootstrap.LevelName,
                "The armed non-Catalog level must survive the domain reload (not fall back to the serialized level).");
            Assert.AreEqual(reloadedProbe.width, bootstrap.Board.Width, "Loaded board width must match the probe.");
            Assert.AreEqual(reloadedProbe.height, bootstrap.Board.Height, "Loaded board height must match the probe.");

            PlaytestRequest inPlay = PlaytestLauncher.LoadRequestAsset();
            Assert.IsNotNull(inPlay, "The request asset must still be loadable in play mode.");
            Assert.IsNull(inPlay.Level, "The runtime must consume the request once (in-memory).");
        }

        [Test]
        [Order(2)]
        public void OnDiskRequest_IsCleared_AfterPlayModeExits()
        {
            // Runs after the EnterPlayMode test above: play mode has been exited by then and the
            // editor janitor must have rewritten the on-disk request back to the empty state.
            PlaytestRequest request = PlaytestLauncher.LoadRequestAsset();
            Assert.IsNotNull(request, "The persistent request asset must exist.");
            Assert.IsNull(
                request.Level,
                "Leaving play mode must clear the request, so the next normal Play is not poisoned.");
            StringAssert.Contains(
                "Level: {fileID: 0}",
                System.IO.File.ReadAllText(PlaytestRequest.AssetPath),
                "The on-disk request asset must not keep a stale level reference after play mode exits.");
        }
    }
}

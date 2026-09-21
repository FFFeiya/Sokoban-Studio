using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Sokoban.Tests.PlayMode
{
#if UNITY_EDITOR
    /// <summary>
    /// Play-mode side of the one-click Playtest bridge. These tests run inside a real play-mode
    /// domain (the domain reload happened when the play-mode test session started), so they prove the
    /// persistent request channel is wired and is consumed by the gameplay bootstrap.
    /// The full "request survives the domain reload" proof lives in the EditMode test
    /// <c>PlaytestDomainReloadTests.ArmedRequest_SurvivesDomainReload_AndLoadsTheAuthoredLevel</c>,
    /// which drives <c>EnterPlayMode</c> and asserts after the real reload; these tests complement it
    /// by exercising the play-mode consumption path and the scene wiring.
    /// The editor asset pipeline is used to author the probe level, so the tests are editor-only.
    /// </summary>
    public class PlaytestRequestPlayModeTests
    {
        private const string LevelsFolder = "Assets/Sokoban/Levels";
        private const string ProbeFolder = LevelsFolder + "/Temp";
        private const string ProbePath = ProbeFolder + "/PlayModeProbe.asset";
        private const string ProbeName = "PlayMode Probe Level";

        /// <summary>Deliberately different from every shipped level, so a fallback cannot pass.</summary>
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
            ClearRequest();

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

        [UnityTest]
        public IEnumerator GameplayScene_WiresThePersistentRequestChannel()
        {
            SceneManager.LoadScene(GameFlowRequest.GameplayScene);
            yield return null;

            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "The gameplay scene must contain a GameBootstrap.");
            Assert.IsNotNull(bootstrap.catalog, "The catalog reference must stay wired.");
            Assert.IsNotNull(
                bootstrap.request,
                "GameBootstrap.request must be wired to the persistent " + PlaytestRequest.AssetPath + " channel.");
        }

        [UnityTest]
        public IEnumerator Bootstrap_ConsumesAnArmedRequest_AndLoadsThatNonCatalogLevel()
        {
            LevelDefinition probe = CreateProbeLevel();
            Assert.IsNotNull(probe, "The probe level asset must be created.");
            ArmRequest(probe);

            SceneManager.LoadScene(GameFlowRequest.GameplayScene);
            yield return null;

            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "The gameplay scene must contain a GameBootstrap.");
            Assert.IsNotNull(bootstrap.Board, "The bootstrap must have built a board.");
            Assert.AreEqual(ProbeName, bootstrap.LevelName, "The armed request level must be the level played.");
            Assert.AreEqual(probe.width, bootstrap.Board.Width, "Loaded board width must match the probe.");
            Assert.AreEqual(probe.height, bootstrap.Board.Height, "Loaded board height must match the probe.");

            PlaytestRequest request = AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
            Assert.IsNotNull(request, "The persistent request asset must exist.");
            Assert.IsNull(request.Level, "The runtime must consume the request once (in-memory).");
        }

        private static LevelDefinition CreateProbeLevel()
        {
            if (!AssetDatabase.IsValidFolder(ProbeFolder))
            {
                AssetDatabase.CreateFolder(LevelsFolder, "Temp");
            }

            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(ProbePath) != null)
            {
                AssetDatabase.DeleteAsset(ProbePath);
            }

            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = ProbeName;
            def.levelName = ProbeName;
            def.width = ProbeRows[0].Length;
            def.height = ProbeRows.Length;
            def.cells = new List<TileType>(def.width * def.height);
            def.occupants = new List<OccupantType>(def.width * def.height);

            for (int y = 0; y < def.height; y++)
            {
                string row = ProbeRows[y];
                Assert.AreEqual(def.width, row.Length, $"Probe row {y} must match the probe width.");

                for (int x = 0; x < def.width; x++)
                {
                    switch (row[x])
                    {
                        case '#':
                            def.cells.Add(TileType.Wall);
                            def.occupants.Add(OccupantType.None);
                            break;
                        case 'G':
                            def.cells.Add(TileType.Goal);
                            def.occupants.Add(OccupantType.None);
                            break;
                        case 'P':
                            def.cells.Add(TileType.Floor);
                            def.occupants.Add(OccupantType.Player);
                            break;
                        case 'B':
                            def.cells.Add(TileType.Floor);
                            def.occupants.Add(OccupantType.Box);
                            break;
                        default:
                            def.cells.Add(TileType.Floor);
                            def.occupants.Add(OccupantType.None);
                            break;
                    }
                }
            }

            AssetDatabase.CreateAsset(def, ProbePath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<LevelDefinition>(ProbePath);
        }

        private static void ArmRequest(LevelDefinition level)
        {
            PlaytestRequest request = AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
            Assert.IsNotNull(request, "The persistent request asset must exist: " + PlaytestRequest.AssetPath);

            request.Level = level;
            EditorUtility.SetDirty(request);
            AssetDatabase.SaveAssets();
        }

        private static void ClearRequest()
        {
            PlaytestRequest request = AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
            if (request == null || request.Level == null)
            {
                return;
            }

            request.Level = null;
            EditorUtility.SetDirty(request);
            AssetDatabase.SaveAssets();
        }
    }
#endif
}

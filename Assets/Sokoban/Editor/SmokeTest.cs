using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sokoban.Editor
{
    /// <summary>
    /// Headless boot check for the gameplay vertical slice and the game flow. It runs what can
    /// honestly run in batch mode: load the level assets and catalog, build the rule model, open the
    /// gameplay scene and confirm the wired components and the build-settings registration are
    /// present. It does not fake a play-mode session.
    /// </summary>
    public static class SmokeTest
    {
        public static void Run()
        {
            try
            {
                LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level01Path);
                if (level == null)
                {
                    Fail("Level asset missing: " + LevelAssetFactory.Level01Path);
                    return;
                }

                var board = new Board(level);
                int boxCount = board.BoxPositions.Count;
                if (board.Width <= 0 || board.Height <= 0 || boxCount <= 0)
                {
                    Fail($"Board is not playable: {board.Width}x{board.Height}, boxes={boxCount}.");
                    return;
                }

                LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(LevelAssetFactory.CatalogPath);
                if (catalog == null)
                {
                    Fail("Level catalog missing: " + LevelAssetFactory.CatalogPath);
                    return;
                }

                if (catalog.levels == null || catalog.levels.Count != 8)
                {
                    Fail($"Level catalog needs exactly 8 levels, found {catalog.levels?.Count ?? 0}.");
                    return;
                }

                LevelDefinition level02 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level02Path);
                if (level02 == null)
                {
                    Fail("Level asset missing: " + LevelAssetFactory.Level02Path);
                    return;
                }

                var board02 = new Board(level02);
                int goalCount02 = level02.cells.Count(t => t == TileType.Goal);
                int boxCount02 = board02.BoxPositions.Count;
                if (boxCount02 <= 0 || boxCount02 != goalCount02)
                {
                    Fail($"Level02 must have boxes == goals > 0, found boxes={boxCount02}, goals={goalCount02}.");
                    return;
                }

                LevelDefinition level03 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level03Path);
                if (level03 == null)
                {
                    Fail("Level asset missing: " + LevelAssetFactory.Level03Path);
                    return;
                }

                var board03 = new Board(level03);
                int goalCount03 = level03.cells.Count(t => t == TileType.Goal);
                int boxCount03 = board03.BoxPositions.Count;
                if (boxCount03 <= 0 || boxCount03 != goalCount03)
                {
                    Fail($"Level03 must have boxes == goals > 0, found boxes={boxCount03}, goals={goalCount03}.");
                    return;
                }

                LevelDefinition level04 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level04Path);
                if (level04 == null)
                {
                    Fail("Level asset missing: " + LevelAssetFactory.Level04Path);
                    return;
                }

                var board04 = new Board(level04);
                int goalCount04 = level04.cells.Count(t => t == TileType.Goal);
                int boxCount04 = board04.BoxPositions.Count;
                if (boxCount04 <= 0 || boxCount04 != goalCount04)
                {
                    Fail($"Level04 must have boxes == goals > 0, found boxes={boxCount04}, goals={goalCount04}.");
                    return;
                }

                LevelDefinition level05 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level05Path);
                if (level05 == null)
                {
                    Fail("Level asset missing: " + LevelAssetFactory.Level05Path);
                    return;
                }

                var board05 = new Board(level05);
                int goalCount05 = level05.cells.Count(t => t == TileType.Goal);
                int boxCount05 = board05.BoxPositions.Count;
                if (boxCount05 <= 0 || boxCount05 != goalCount05)
                {
                    Fail($"Level05 must have boxes == goals > 0, found boxes={boxCount05}, goals={goalCount05}.");
                    return;
                }

                LevelDefinition level06 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level06Path);
                if (level06 == null)
                {
                    Fail("Level asset missing: " + LevelAssetFactory.Level06Path);
                    return;
                }

                var board06 = new Board(level06);
                int goalCount06 = level06.cells.Count(t => t == TileType.Goal);
                int boxCount06 = board06.BoxPositions.Count;
                if (boxCount06 <= 0 || boxCount06 != goalCount06)
                {
                    Fail($"Level06 must have boxes == goals > 0, found boxes={boxCount06}, goals={goalCount06}.");
                    return;
                }

                LevelDefinition level07 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level07Path);
                if (level07 == null)
                {
                    Fail("Level asset missing: " + LevelAssetFactory.Level07Path);
                    return;
                }

                var board07 = new Board(level07);
                int goalCount07 = level07.cells.Count(t => t == TileType.Goal);
                int boxCount07 = board07.BoxPositions.Count;
                if (boxCount07 <= 0 || boxCount07 != goalCount07)
                {
                    Fail($"Level07 must have boxes == goals > 0, found boxes={boxCount07}, goals={goalCount07}.");
                    return;
                }

                LevelDefinition level08 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level08Path);
                if (level08 == null)
                {
                    Fail("Level asset missing: " + LevelAssetFactory.Level08Path);
                    return;
                }

                var board08 = new Board(level08);
                int goalCount08 = level08.cells.Count(t => t == TileType.Goal);
                int boxCount08 = board08.BoxPositions.Count;
                if (boxCount08 <= 0 || boxCount08 != goalCount08)
                {
                    Fail($"Level08 must have boxes == goals > 0, found boxes={boxCount08}, goals={goalCount08}.");
                    return;
                }

                string[] requiredScenes =
                {
                    LevelAssetFactory.MainMenuScenePath,
                    LevelAssetFactory.LevelSelectScenePath,
                    LevelAssetFactory.GameplayScenePath
                };

                foreach (string scenePath in requiredScenes)
                {
                    EditorBuildSettingsScene buildScene = EditorBuildSettings.scenes
                        .FirstOrDefault(s => s.path == scenePath);
                    if (buildScene == null || !buildScene.enabled)
                    {
                        Fail("Scene is not enabled in the build settings: " + scenePath);
                        return;
                    }
                }

                Scene scene = EditorSceneManager.OpenScene(LevelAssetFactory.GameplayScenePath, OpenSceneMode.Single);

                GameObject game = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Game");
                if (game == null)
                {
                    Fail("Scene " + LevelAssetFactory.GameplayScenePath + " has no GameObject named 'Game'.");
                    return;
                }

                var bootstrap = game.GetComponent<GameBootstrap>();
                var controller = game.GetComponent<GameController>();
                var view = game.GetComponent<BoardView>();
                if (bootstrap == null || controller == null || view == null)
                {
                    Fail($"'Game' components incomplete: bootstrap={bootstrap != null}, controller={controller != null}, view={view != null}.");
                    return;
                }

                if (bootstrap.level == null)
                {
                    Fail("GameBootstrap.level is not assigned in the scene.");
                    return;
                }

                if (bootstrap.catalog == null)
                {
                    Fail("GameBootstrap.catalog is not assigned in the scene.");
                    return;
                }

                if (bootstrap.request == null)
                {
                    Fail("GameBootstrap.request (persistent playtest channel) is not assigned in the scene.");
                    return;
                }

                string editorSummary = CheckLevelEditorRoundTrip();
                string validatorSummary = CheckLevelValidator();

                Debug.Log($"SMOKE OK board={board.Width}x{board.Height} boxes={boxCount} catalog={catalog.levels.Count} scenes={requiredScenes.Length} scene={LevelAssetFactory.GameplayScenePath} editor={editorSummary} {validatorSummary}");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Fail("exception: " + exception);
            }
        }

        /// <summary>
        /// T05 headless check: the level editor window type exists and the working-copy document
        /// round-trips through a real temp asset (create, paint, save, reload, build a board).
        /// Everything is cleaned up afterwards; no window is ever shown in batch mode.
        /// </summary>
        private static string CheckLevelEditorRoundTrip()
        {
            if (!typeof(EditorWindow).IsAssignableFrom(typeof(LevelEditorWindow)))
            {
                throw new InvalidOperationException("LevelEditorWindow must be an EditorWindow.");
            }

            const string tempFolder = "Assets/Sokoban/Levels/Temp";
            const string tempPath = tempFolder + "/SmokeEditorLevel.asset";

            try
            {
                LevelEditorDocument document = LevelEditorDocument.CreateNew();
                document.Paint(4, 2, LevelBrush.Box);
                document.Paint(6, 4, LevelBrush.Goal);
                document.Paint(2, 2, LevelBrush.Wall);

                var errors = document.Validate();
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Edited document is invalid: " + string.Join("; ", errors));
                }

                if (!document.SaveAs(tempPath))
                {
                    throw new InvalidOperationException("SaveAs returned false for " + tempPath);
                }

                LevelDefinition saved = AssetDatabase.LoadAssetAtPath<LevelDefinition>(tempPath);
                if (saved == null)
                {
                    throw new InvalidOperationException("Saved asset missing: " + tempPath);
                }

                LevelEditorDocument reloaded = LevelEditorDocument.LoadFrom(saved);
                var editorBoard = new Board(reloaded.Working);
                int reloadedBoxes = editorBoard.BoxPositions.Count;

                if (reloadedBoxes != reloaded.BoxCount || reloaded.PlayerCount != 1)
                {
                    throw new InvalidOperationException(
                        $"Round-trip mismatch: boxes={reloadedBoxes}, expected={reloaded.BoxCount}, players={reloaded.PlayerCount}.");
                }

                return $"{reloaded.Working.width}x{reloaded.Working.height} boxes={reloadedBoxes} goals={reloaded.GoalCount}";
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(tempPath) != null)
                {
                    AssetDatabase.DeleteAsset(tempPath);
                }

                if (AssetDatabase.IsValidFolder(tempFolder) &&
                    AssetDatabase.FindAssets("t:Object", new[] { tempFolder }).Length == 0)
                {
                    AssetDatabase.DeleteAsset(tempFolder);
                }
            }
        }

        /// <summary>
        /// T06 headless check: the shared validator accepts both shipped levels and rejects a
        /// deliberately broken in-memory level (player removed) without throwing.
        /// </summary>
        private static string CheckLevelValidator()
        {
            LevelDefinition level01 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level01Path);
            LevelDefinition level02 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetFactory.Level02Path);

            int level01Errors = LevelValidator.Validate(level01).Count;
            if (level01Errors != 0)
            {
                throw new InvalidOperationException($"Level01 must be valid, found {level01Errors} validator errors.");
            }

            int level02Errors = LevelValidator.Validate(level02).Count;
            if (level02Errors != 0)
            {
                throw new InvalidOperationException($"Level02 must be valid, found {level02Errors} validator errors.");
            }

            LevelDefinition broken = LevelEditorDocument.Clone(level01);
            try
            {
                for (int i = 0; i < broken.occupants.Count; i++)
                {
                    if (broken.occupants[i] == OccupantType.Player)
                    {
                        broken.occupants[i] = OccupantType.None;
                        break;
                    }
                }

                int brokenErrors = LevelValidator.Validate(broken).Count;
                if (brokenErrors == 0)
                {
                    throw new InvalidOperationException("Validator accepted a broken level (no player).");
                }

                return $"validator=ok broken={brokenErrors}";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(broken);
            }
        }

        private static void Fail(string reason)
        {
            Debug.LogError("SMOKE FAIL: " + reason);
            EditorApplication.Exit(1);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.Editor
{
    /// <summary>
    /// Development-only content factory and test fixture. It can author level assets, the level
    /// catalog and the three gameplay-flow scenes programmatically from ASCII layouts, so tests and
    /// fixtures do not have to hand-write that YAML, and merges the scenes into the build settings.
    ///
    /// The shipped <see cref="LevelDefinition"/> assets are the canonical authored content; this
    /// factory never regenerates them.
    /// </summary>
    public static class LevelAssetFactory
    {
        public const string LevelsFolder = "Assets/Sokoban/Levels";
        public const string ScenesFolder = "Assets/Sokoban/Scenes";
        public const string Level01Path = LevelsFolder + "/Level01.asset";
        public const string Level02Path = LevelsFolder + "/Level02.asset";
        public const string Level03Path = LevelsFolder + "/Level03.asset";
        public const string Level04Path = LevelsFolder + "/Level04.asset";
        public const string Level05Path = LevelsFolder + "/Level05.asset";
        public const string Level06Path = LevelsFolder + "/Level06.asset";
        public const string Level07Path = LevelsFolder + "/Level07.asset";
        public const string Level08Path = LevelsFolder + "/Level08.asset";
        public const string CatalogPath = LevelsFolder + "/LevelCatalog.asset";
        public const string MainMenuScenePath = ScenesFolder + "/MainMenu.unity";
        public const string LevelSelectScenePath = ScenesFolder + "/LevelSelect.unity";
        public const string GameplayScenePath = ScenesFolder + "/Gameplay.unity";

        /// <summary>Creates and saves a level asset from ASCII rows, returning the saved instance.</summary>
        public static LevelDefinition CreateLevelAsset(string assetPath, string levelName, string[] rows)
        {
            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            LevelDefinition def = BuildLevel(levelName, rows);
            AssetDatabase.CreateAsset(def, assetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
        }

        /// <summary>Creates and saves the ordered level catalog asset.</summary>
        public static LevelCatalog CreateCatalogAsset(LevelDefinition[] levels)
        {
            if (AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath) != null)
            {
                AssetDatabase.DeleteAsset(CatalogPath);
            }

            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.name = "LevelCatalog";
            catalog.levels = new List<LevelDefinition>(levels);
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
        }

        /// <summary>
        /// Builds the gameplay scene with the gameplay components and saves it. The level, the
        /// catalog and the persistent playtest request channel are reloaded by path after the scene
        /// reset, so the saved references are valid.
        /// </summary>
        public static void CreateGameplayScene(string levelPath, string catalogPath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
            if (level == null)
            {
                throw new InvalidOperationException("Cannot build the gameplay scene: missing level asset " + levelPath);
            }

            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(catalogPath);
            if (catalog == null)
            {
                throw new InvalidOperationException("Cannot build the gameplay scene: missing catalog asset " + catalogPath);
            }

            // The one-click Playtest bridge depends on this serialized reference; fail loudly instead
            // of silently regenerating a scene whose request channel is unwired.
            PlaytestRequest request = EnsurePlaytestRequestAsset();
            if (request == null)
            {
                throw new InvalidOperationException(
                    "Cannot build the gameplay scene: missing playtest request asset " + PlaytestRequest.AssetPath);
            }

            var game = new GameObject("Game");
            var bootstrap = game.AddComponent<GameBootstrap>();
            var controller = game.AddComponent<GameController>();
            var view = game.AddComponent<BoardView>();

            bootstrap.level = level;
            bootstrap.catalog = catalog;
            bootstrap.request = request;
            bootstrap.view = view;
            bootstrap.controller = controller;

            CreateCamera(new Vector3(3f, -2.5f, -10f), 4f);

            if (!EditorSceneManager.SaveScene(scene, GameplayScenePath))
            {
                throw new InvalidOperationException("Failed to save scene to " + GameplayScenePath);
            }
        }

        /// <summary>
        /// Loads the persistent playtest request asset, creating it once when the project does not
        /// have it yet. It is never deleted/recreated, so the gameplay scene reference stays stable.
        /// </summary>
        public static PlaytestRequest EnsurePlaytestRequestAsset()
        {
            PlaytestRequest request = AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
            if (request != null)
            {
                return request;
            }

            request = ScriptableObject.CreateInstance<PlaytestRequest>();
            request.name = "PlaytestRequest";
            AssetDatabase.CreateAsset(request, PlaytestRequest.AssetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
        }

        /// <summary>Builds the title scene and saves it.</summary>
        public static void CreateMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var menu = new GameObject("MainMenu");
            menu.AddComponent<MainMenuController>();

            CreateCamera(new Vector3(0f, 0f, -10f), 5f);

            if (!EditorSceneManager.SaveScene(scene, MainMenuScenePath))
            {
                throw new InvalidOperationException("Failed to save scene to " + MainMenuScenePath);
            }
        }

        /// <summary>
        /// Builds the level select scene and saves it, wiring the serialized catalog reference that
        /// the runtime reads (runtime never uses AssetDatabase discovery).
        /// </summary>
        public static void CreateLevelSelectScene(string catalogPath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(catalogPath);
            if (catalog == null)
            {
                throw new InvalidOperationException("Cannot build the level select scene: missing catalog asset " + catalogPath);
            }

            var select = new GameObject("LevelSelect");
            var controller = select.AddComponent<LevelSelectController>();
            controller.catalog = catalog;

            CreateCamera(new Vector3(0f, 0f, -10f), 5f);

            if (!EditorSceneManager.SaveScene(scene, LevelSelectScenePath))
            {
                throw new InvalidOperationException("Failed to save scene to " + LevelSelectScenePath);
            }
        }

        /// <summary>
        /// Merges <paramref name="orderedScenePaths"/> into the build settings: the given scenes come
        /// first in the given order, existing entries keep their enabled flag, missing entries are
        /// added enabled, and any other existing scene is preserved after them.
        /// </summary>
        public static void RegisterBuildSettings(params string[] orderedScenePaths)
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            var merged = new List<EditorBuildSettingsScene>();
            var used = new HashSet<string>();

            if (orderedScenePaths != null)
            {
                foreach (string path in orderedScenePaths)
                {
                    if (string.IsNullOrEmpty(path) || !used.Add(path))
                    {
                        continue;
                    }

                    EditorBuildSettingsScene current = existing.FirstOrDefault(s => s != null && s.path == path);
                    merged.Add(new EditorBuildSettingsScene(path, current == null || current.enabled));
                }
            }

            foreach (EditorBuildSettingsScene scene in existing)
            {
                if (scene == null || string.IsNullOrEmpty(scene.path) || !used.Add(scene.path))
                {
                    continue;
                }

                merged.Add(scene);
            }

            EditorBuildSettings.scenes = merged.ToArray();
        }

        /// <summary>Builds a runtime level definition from ASCII rows (row 0 = top).</summary>
        public static LevelDefinition BuildLevel(string levelName, string[] rows)
        {
            if (rows == null || rows.Length == 0)
            {
                throw new ArgumentException("A level needs at least one row.", nameof(rows));
            }

            int width = rows[0].Length;
            int height = rows.Length;

            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = levelName;
            def.levelName = levelName;
            def.width = width;
            def.height = height;
            def.cells = new List<TileType>(width * height);
            def.occupants = new List<OccupantType>(width * height);
            def.groupIds = new List<int>(width * height);

            for (int y = 0; y < height; y++)
            {
                string row = rows[y];
                if (row.Length != width)
                {
                    throw new ArgumentException($"Row {y} is {row.Length} wide, expected {width}.", nameof(rows));
                }

                for (int x = 0; x < width; x++)
                {
                    TileType tile;
                    OccupantType occupant;

                    switch (row[x])
                    {
                        case '#':
                            tile = TileType.Wall;
                            occupant = OccupantType.None;
                            break;
                        case '.':
                            tile = TileType.Floor;
                            occupant = OccupantType.None;
                            break;
                        case 'G':
                            tile = TileType.Goal;
                            occupant = OccupantType.None;
                            break;
                        case 'T':
                            tile = TileType.Plate;
                            occupant = OccupantType.None;
                            break;
                        case 'D':
                            tile = TileType.Door;
                            occupant = OccupantType.None;
                            break;
                        case 'p':
                            // Player painted onto a door: an illegal state used by validator tests.
                            tile = TileType.Door;
                            occupant = OccupantType.Player;
                            break;
                        case 'b':
                            // Box painted onto a door: an illegal state used by validator tests.
                            tile = TileType.Door;
                            occupant = OccupantType.Box;
                            break;
                        case 'P':
                            tile = TileType.Floor;
                            occupant = OccupantType.Player;
                            break;
                        case 'B':
                            tile = TileType.Floor;
                            occupant = OccupantType.Box;
                            break;
                        case '*':
                            tile = TileType.Goal;
                            occupant = OccupantType.Box;
                            break;
                        case '+':
                            tile = TileType.Goal;
                            occupant = OccupantType.Player;
                            break;
                        default:
                            throw new ArgumentException($"Unknown level character '{row[x]}' at ({x}, {y}).", nameof(rows));
                    }

                    def.cells.Add(tile);
                    def.occupants.Add(occupant);
                    def.groupIds.Add(0);
                }
            }

            return def;
        }

        private static void CreateCamera(Vector3 position, float orthographicSize)
        {
            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.transform.position = position;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}

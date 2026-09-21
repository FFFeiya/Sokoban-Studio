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
    /// catalog and the three gameplay-flow scenes programmatically from hard-coded ASCII layouts,
    /// so bootstrap, tests and fixtures do not have to hand-write that YAML, and merges the scenes
    /// into the build settings.
    ///
    /// The shipped <see cref="LevelDefinition"/> assets are the canonical authored content: they are
    /// produced through the Level Editor and must not be overwritten casually. This factory exists
    /// for bootstrapping, tests and fixtures only, and its content-regeneration entry point is
    /// guarded and marked destructive.
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

        /// <summary>
        /// Layout for the first shipped level, 7x6, one player, two boxes, two goals.
        /// '#'=Wall  '.'=Floor  'G'=Goal  'P'=Player  'B'=Box
        /// Row 0 is the top authored row.
        /// </summary>
        private static readonly string[] Level01Rows =
        {
            "#######",
            "#G.B..#",
            "#.....#",
            "#G.B..#",
            "#..P..#",
            "#######"
        };

        /// <summary>
        /// Layout for the second shipped level, 8x7, one player, two boxes, two goals. Harder than
        /// Level01: the first box must be pushed up twice and then left, and the player has to walk
        /// around the remaining box before pushing it up onto its goal.
        /// '#'=Wall  '.'=Floor  'G'=Goal  'P'=Player  'B'=Box
        /// </summary>
        private static readonly string[] Level02Rows =
        {
            "########",
            "#......#",
            "#.G.G..#",
            "#......#",
            "#..BB..#",
            "#..P...#",
            "########"
        };

        /// <summary>
        /// Layout for the third shipped level, 7x7, one player, two boxes, two goals. A step up from
        /// Level02: the player has to walk around the box cluster and push one box down onto its goal
        /// before nudging the other across the board. Shortest solution is 13 moves / 3 pushes.
        /// '#'=Wall  '.'=Floor  'G'=Goal  'P'=Player  'B'=Box
        /// </summary>
        private static readonly string[] Level03Rows =
        {
            "#######",
            "#.....#",
            "#.GBB.#",
            "#..G..#",
            "#.....#",
            "#..P..#",
            "#######"
        };

        /// <summary>
        /// Layout for the fourth shipped level, 8x8, one player, two boxes, two goals. The hardest of
        /// the shipped set: an internal wall band splits the board and both boxes must be pushed up
        /// to the top row. Shortest solution is 15 moves / 4 pushes.
        /// '#'=Wall  '.'=Floor  'G'=Goal  'P'=Player  'B'=Box
        /// </summary>
        private static readonly string[] Level04Rows =
        {
            "########",
            "#.G..G.#",
            "#..BB..#",
            "#......#",
            "#.####.#",
            "#......#",
            "#..P...#",
            "########"
        };

        /// <summary>
        /// Destructive development menu entry. Regenerates the shipped Level01-04 assets, the
        /// LevelCatalog and all three scenes from the hard-coded factory ASCII layouts, so it
        /// overwrites canonical authored content and requires explicit confirmation.
        /// </summary>
        [MenuItem("Sokoban/Development/Build All Content And Scenes (Destructive)")]
        public static void CreateAllMenu()
        {
            if (!EditorUtility.DisplayDialog("Destructive content regeneration", "This DELETES and regenerates the shipped Level01-04 assets, LevelCatalog, and all three scenes from the hard-coded factory ASCII layouts. Shipped LevelDefinition assets are canonical content; this tool exists for bootstrap/tests/fixtures only. Continue?", "Regenerate", "Cancel"))
            {
                return;
            }

            CreateAll();
        }

        /// <summary>
        /// Batch-mode entry point: builds the level assets and catalog, builds the gameplay,
        /// main menu and level select scenes, and merges them into the build settings.
        /// Destructive: existing generated assets are deleted and replaced from the hard-coded
        /// ASCII layouts. Development/bootstrap/test fixture only; do not use it to overwrite
        /// final authored content.
        /// </summary>
        public static void CreateAll()
        {
            EnsureFolder("Assets/Sokoban", "Levels");
            EnsureFolder("Assets/Sokoban", "Scenes");

            CreateLevelAsset(Level01Path, "Level 01", Level01Rows);
            CreateLevelAsset(Level02Path, "Level 02", Level02Rows);
            CreateLevelAsset(Level03Path, "Level 03", Level03Rows);
            CreateLevelAsset(Level04Path, "Level 04", Level04Rows);

            // Creating a scene resets the asset pipeline, so every asset reference is reloaded from
            // disk right before it is used instead of being kept across a scene switch.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LevelDefinition level01 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Path);
            LevelDefinition level02 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level02Path);
            LevelDefinition level03 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level03Path);
            LevelDefinition level04 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level04Path);
            CreateCatalogAsset(new[] { level01, level02, level03, level04 });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Creating a scene resets the asset pipeline; the persistent playtest request channel is
            // re-wired into the regenerated gameplay scene so the one-click Playtest bridge keeps
            // working after a content rebuild (guard against silently losing that reference).
            EnsurePlaytestRequestAsset();

            CreateGameplayScene(Level01Path, CatalogPath);
            CreateMainMenuScene();
            CreateLevelSelectScene(CatalogPath);

            RegisterBuildSettings(MainMenuScenePath, LevelSelectScenePath, GameplayScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("LevelAssetFactory: created levels + catalog and scenes in " + ScenesFolder);
        }

        /// <summary>Creates the first shipped level asset (kept for existing callers).</summary>
        public static LevelDefinition CreateSampleLevelAsset(string assetPath)
        {
            return CreateLevelAsset(assetPath, "Level 01", Level01Rows);
        }

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

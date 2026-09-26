using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sokoban
{
    /// <summary>
    /// Entry point for the gameplay scene. It resolves which <see cref="LevelDefinition"/> to play,
    /// builds the model, wires the view and the input controller, and owns the undo/restart
    /// commands and the next-level navigation. The <see cref="Board"/> remains the only rule authority.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Level played when no editor PlaytestRequest or GameFlowRequest is pending.")]
        public LevelDefinition level;

        [Tooltip("Ordered shipped levels; resolves the next level after completion. Assigned by the scene factory.")]
        public LevelCatalog catalog;

        [Tooltip("Persistent editor Playtest channel. It is an asset so the request survives the Play Mode " +
                 "domain reload. Assigned by the scene factory; a normal Play leaves it empty.")]
        public PlaytestRequest request;

        [Tooltip("Optional; resolved with GetComponent when left empty.")]
        public BoardView view;

        [Tooltip("Optional; resolved with GetComponent when left empty.")]
        public GameController controller;

        [Tooltip("Optional audio feedback layer; resolved with GetComponent and provisioned when left empty.")]
        public AudioController audioController;

        private readonly Stack<BoardSnapshot> _undoStack = new Stack<BoardSnapshot>();
        private LevelDefinition _definition;
        private int _levelIndex = -1;

        /// <summary>Current board model, or null before Awake completes.</summary>
        public Board Board { get; private set; }

        /// <summary>True when the current board has every goal covered.</summary>
        public bool IsComplete => Board != null && Board.IsComplete;

        /// <summary>Number of snapshots currently available for undo.</summary>
        public int UndoCount => _undoStack.Count;

        /// <summary>Display name of the level being played.</summary>
        public string LevelName => _definition != null ? _definition.levelName : string.Empty;

        /// <summary>Index of the current level in <see cref="catalog"/>, or -1 when it is not catalogued.</summary>
        public int LevelIndex => _levelIndex;

        /// <summary>True when <see cref="catalog"/> lists a level after the current one.</summary>
        public bool HasNextLevel => GetNextLevel() != null;

        private void Awake()
        {
            _definition = ResolveLevel();
            ApplyLevelIndex();

            if (view == null)
            {
                view = GetComponent<BoardView>();
            }

            if (controller == null)
            {
                controller = GetComponent<GameController>();
            }

            // The feedback layer is optional and provisioned on demand, so a scene that predates it
            // (or one whose scene factory does not wire it) still gets audio without any asset edit.
            if (audioController == null)
            {
                audioController = GetComponent<AudioController>();
            }

            if (audioController == null)
            {
                audioController = gameObject.AddComponent<AudioController>();
            }

            Board = new Board(_definition);
            _undoStack.Clear();

            if (view != null)
            {
                view.Build(Board);
            }

            if (audioController != null)
            {
                audioController.Build(Board);
            }

            if (controller != null)
            {
                controller.Bind(this);
            }
        }

        /// <summary>Attempts one move. See <see cref="TryMove(Direction, out bool)"/>.</summary>
        public bool TryMove(Direction direction)
        {
            return TryMove(direction, out _);
        }

        /// <summary>
        /// Attempts one move. On success the pre-move snapshot is pushed for undo and the view is
        /// refreshed. Rejected moves (wall/out of bounds/blocked push) change nothing.
        /// <paramref name="completed"/> reports the win check of the accepted move, so the input
        /// layer does not have to poll the board every frame.
        /// </summary>
        public bool TryMove(Direction direction, out bool completed)
        {
            completed = false;

            if (Board == null || Board.IsComplete)
            {
                return false;
            }

            BoardSnapshot before = Board.CreateSnapshot();
            if (!Board.TryMove(direction, out completed))
            {
                return false;
            }

            _undoStack.Push(before);

            if (view != null)
            {
                view.Refresh(Board);
            }

            if (audioController != null)
            {
                audioController.Refresh(Board);
            }

            return true;
        }

        /// <summary>Restores the previous board state. Returns false when there is nothing to undo.</summary>
        public bool Undo()
        {
            if (Board == null || _undoStack.Count == 0)
            {
                return false;
            }

            Board.RestoreSnapshot(_undoStack.Pop());

            if (view != null)
            {
                view.Refresh(Board);
            }

            if (audioController != null)
            {
                audioController.Refresh(Board);
            }

            return true;
        }

        /// <summary>
        /// Rebuilds the board from the original definition and clears the undo history, which also
        /// resets the move/push counters to zero.
        /// </summary>
        public void Restart()
        {
            if (_definition == null)
            {
                return;
            }

            Board = new Board(_definition);
            _undoStack.Clear();

            if (view != null)
            {
                view.Build(Board);
            }

            if (audioController != null)
            {
                audioController.Build(Board);
            }
        }

        /// <summary>Next level in the catalog, or null when the current level is the last one.</summary>
        public LevelDefinition GetNextLevel()
        {
            if (catalog == null || catalog.levels == null || _levelIndex < 0)
            {
                return null;
            }

            int next = _levelIndex + 1;
            if (next >= catalog.levels.Count)
            {
                return null;
            }

            return catalog.levels[next];
        }

        /// <summary>Loads the next catalog level, if any.</summary>
        public void LoadNextLevel()
        {
            LevelDefinition next = GetNextLevel();
            if (next != null)
            {
                GameFlowRequest.LoadLevel(next);
            }
        }

        /// <summary>Returns to the level select scene.</summary>
        public void LoadLevelSelect()
        {
            SceneManager.LoadScene(GameFlowRequest.LevelSelectScene);
        }

        private void ApplyLevelIndex()
        {
            _levelIndex = -1;

            if (catalog == null || catalog.levels == null || _definition == null)
            {
                return;
            }

            for (int i = 0; i < catalog.levels.Count; i++)
            {
                if (catalog.levels[i] == _definition)
                {
                    _levelIndex = i;
                    return;
                }
            }
        }

        private LevelDefinition ResolveLevel()
        {
            // Editor one-click Playtest. The request lives in a persistent asset, so it still reads
            // back after the Play Mode domain reload (a plain static would already be gone here).
            // Only the in-memory reference is consumed: runtime code never saves assets, and the
            // editor-side janitor clears the on-disk value when play mode ends.
            if (request != null && request.Level != null)
            {
                LevelDefinition requested = request.Level;
                request.Level = null;
                return requested;
            }

            LevelDefinition next = GameFlowRequest.NextLevel;
            if (next != null)
            {
                GameFlowRequest.NextLevel = null;
                return next;
            }

            if (level != null)
            {
                return level;
            }

            // No scene assignment and no pending request: never leave the scene unplayable.
            return CreateFallbackLevel();
        }

        /// <summary>Minimal built-in level so pressing Play always yields a playable board.</summary>
        private static LevelDefinition CreateFallbackLevel()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = "FallbackLevel";
            def.levelName = "Fallback";

            // "#PBG#" one push to the right wins.
            const string rows = "#####|#PBG#|#####";
            string[] lines = rows.Split('|');

            def.width = lines[0].Length;
            def.height = lines.Length;
            def.cells = new List<TileType>(def.width * def.height);
            def.occupants = new List<OccupantType>(def.width * def.height);

            foreach (string line in lines)
            {
                foreach (char c in line)
                {
                    switch (c)
                    {
                        case '#':
                            def.cells.Add(TileType.Wall);
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
                        case 'G':
                            def.cells.Add(TileType.Goal);
                            def.occupants.Add(OccupantType.None);
                            break;
                        default:
                            def.cells.Add(TileType.Floor);
                            def.occupants.Add(OccupantType.None);
                            break;
                    }
                }
            }

            return def;
        }

        /// <summary>Read-only on-screen HUD: top panel (level title + counters), bottom control hint.</summary>
        private void OnGUI()
        {
            if (Board == null)
            {
                return;
            }

            GuiPanel.ApplyCjkFont();

            // Top-left unified HUD panel: level title + counters only. The key hint
            // is weakened to a plain bottom-left line; the sound toggle stays top-right (AudioController).
            GuiPanel.DrawBacking(new Rect(4f, 4f, 472f, 58f), new Color(0f, 0f, 0f, 0.45f));

            string levelTitle = _levelIndex >= 0 ? $"{_levelIndex + 1:00} · {LevelName}" : LevelName;
            GUI.Label(new Rect(10f, 8f, 460f, 24f), levelTitle, GuiPanel.HudTitleStyle);
            GUI.Label(new Rect(10f, 32f, 460f, 22f), $"步数 {Board.MoveCount}   推箱 {Board.PushCount}", GuiPanel.HudTextStyle);

            // Bottom-left low-weight control hint (no backing).
            GUI.Label(new Rect(10f, Screen.height - 28f, 460f, 22f), "Z 撤销   R 重新开始   Esc 返回", GuiPanel.HudTextStyle);
        }
    }
}

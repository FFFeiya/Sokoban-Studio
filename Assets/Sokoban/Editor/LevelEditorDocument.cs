using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    /// <summary>Palette brush of the level editor.</summary>
    public enum LevelBrush
    {
        Wall,
        Floor,
        Goal,
        Player,
        Box,
        Erase,
        // Legacy group-A aliases, kept at their original numeric values for back-compat.
        Plate,
        Door,
        // Group-explicit variants appended so the existing values never shift.
        PlateA,
        PlateB,
        DoorA,
        DoorB
    }

    /// <summary>
    /// Plain, headless-testable authoring model behind <see cref="LevelEditorWindow"/>.
    /// It owns a <em>working copy</em> of a <see cref="LevelDefinition"/>; New, Resize and Paint only
    /// ever mutate that copy, so the source asset is untouched until an explicit <see cref="Save"/>
    /// or <see cref="SaveAs"/>. Persistence goes through the asset database and therefore stays in
    /// the editor assembly, unreachable from shipped runtime code.
    /// </summary>
    public sealed class LevelEditorDocument
    {
        public const int DefaultWidth = 8;
        public const int DefaultHeight = 6;

        /// <summary>Smallest allowed width/height; a level needs a wall border on every side.</summary>
        public const int MinSize = 3;

        private LevelDefinition _working;
        private string _sourcePath;

        /// <summary>Maximum number of undo steps kept in memory (RAM-only history).</summary>
        private const int HistoryLimit = 64;

        // Undo/redo history. Index 0 is the oldest entry, the last index is the newest. The stacks
        // own only deep in-memory Clones (never the loaded/saved asset) and are empty by
        // construction for every CreateNew/LoadFrom, so a new document always starts clean.
        private readonly List<LevelDefinition> _undo = new List<LevelDefinition>();
        private readonly List<LevelDefinition> _redo = new List<LevelDefinition>();

        private LevelEditorDocument(LevelDefinition working, string sourcePath)
        {
            _working = working;
            _sourcePath = sourcePath;
        }

        /// <summary>The in-memory working copy currently being edited (never the loaded asset).</summary>
        public LevelDefinition Working => _working;

        /// <summary>Asset path this document was loaded from / saved to, or null while untitled.</summary>
        public string SourcePath => _sourcePath;

        /// <summary>True when the working copy has edits that were not written to an asset yet.</summary>
        public bool IsDirty { get; private set; }

        /// <summary>True when at least one edit can be undone.</summary>
        public bool CanUndo => _undo.Count > 0;

        /// <summary>True when at least one undone edit can be redone.</summary>
        public bool CanRedo => _redo.Count > 0;

        public int PlayerCount => CountOccupants(OccupantType.Player);

        public int BoxCount => CountOccupants(OccupantType.Box);

        public int GoalCount => CountTiles(TileType.Goal);

        /// <summary>Creates a fresh 8x6 working copy: wall border, floor interior, player/box/goal in a row.</summary>
        public static LevelEditorDocument CreateNew()
        {
            return CreateNew(DefaultWidth, DefaultHeight);
        }

        /// <summary>Creates a fresh working copy of the given size with sensible default content.</summary>
        public static LevelEditorDocument CreateNew(int width, int height)
        {
            if (width < MinSize) width = MinSize;
            if (height < MinSize) height = MinSize;

            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = "Untitled";
            def.levelName = "Untitled";
            def.width = width;
            def.height = height;
            def.cells = new List<TileType>(width * height);
            def.occupants = new List<OccupantType>(width * height);
            def.groupIds = new List<int>(width * height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    def.cells.Add(border ? TileType.Wall : TileType.Floor);
                    def.occupants.Add(OccupantType.None);
                    def.groupIds.Add(0);
                }
            }

            // A tidy starter: player, then box, then goal, so a single push to the right wins.
            int cy = height / 2;
            int cx = width / 2;
            SetCell(def, cx - 1, cy, TileType.Floor, OccupantType.Player);
            SetCell(def, cx, cy, TileType.Floor, OccupantType.Box);
            SetCell(def, cx + 1, cy, TileType.Goal, OccupantType.None);

            return new LevelEditorDocument(def, null);
        }

        /// <summary>
        /// Loads a <em>copy</em> of <paramref name="source"/> into a new working copy. The source
        /// asset is only referenced for its path, so edits never reach it until an explicit Save.
        /// </summary>
        public static LevelEditorDocument LoadFrom(LevelDefinition source)
        {
            if (source == null)
            {
                throw new System.ArgumentException("Source level definition is null.", nameof(source));
            }

            string path = AssetDatabase.GetAssetPath(source);
            return new LevelEditorDocument(Clone(source), string.IsNullOrEmpty(path) ? null : path);
        }

        /// <summary>Deep-copies a level definition into a detached in-memory instance.</summary>
        public static LevelDefinition Clone(LevelDefinition source)
        {
            if (source == null)
            {
                throw new System.ArgumentException("Level definition is null.", nameof(source));
            }

            var clone = ScriptableObject.CreateInstance<LevelDefinition>();
            clone.name = source.name;
            clone.levelName = source.levelName;
            clone.width = source.width;
            clone.height = source.height;
            clone.cells = source.cells != null ? new List<TileType>(source.cells) : new List<TileType>();
            clone.occupants = source.occupants != null ? new List<OccupantType>(source.occupants) : new List<OccupantType>();
            // Keep the count == width*height invariant: a source without the field (old assets) clones
            // to a correctly sized all-zero list (implicit group A), never a null or empty list.
            clone.groupIds = source.groupIds != null
                ? new List<int>(source.groupIds)
                : new List<int>(new int[source.width * source.height]);
            return clone;
        }

        /// <summary>Reverts the working copy to the state before the most recent mutation.</summary>
        public bool Undo()
        {
            if (_undo.Count == 0)
            {
                return false;
            }

            _redo.Add(Clone(_working));
            int last = _undo.Count - 1;
            _working = _undo[last];
            _undo.RemoveAt(last);
            IsDirty = true;
            return true;
        }

        /// <summary>Re-applies the most recently undone mutation.</summary>
        public bool Redo()
        {
            if (_redo.Count == 0)
            {
                return false;
            }

            _undo.Add(Clone(_working));
            int last = _redo.Count - 1;
            _working = _redo[last];
            _redo.RemoveAt(last);
            IsDirty = true;
            return true;
        }

        /// <summary>
        /// Records the current working state as an undo snapshot before a mutation is applied.
        /// History is strictly RAM-only (deep in-memory Clones), bounded to <see cref="HistoryLimit"/>
        /// (oldest dropped), and any new mutation clears the redo history.
        /// </summary>
        private void RecordMutation()
        {
            _undo.Add(Clone(_working));
            if (_undo.Count > HistoryLimit)
            {
                _undo.RemoveAt(0);
            }

            _redo.Clear();
        }

        /// <summary>Changes the level display name on the working copy and marks it dirty.</summary>
        public void SetLevelName(string levelName)
        {
            string value = string.IsNullOrEmpty(levelName) ? "Untitled" : levelName;
            RecordMutation();
            _working.levelName = value;
            _working.name = value;
            IsDirty = true;
        }

        /// <summary>Resizes the working copy in place, preserving every overlapping cell.</summary>
        public void Resize(int width, int height)
        {
            RecordMutation();
            LevelResizer.Resize(_working, width, height);
            IsDirty = true;
        }

        /// <summary>
        /// Applies a brush at a coordinate. Live invariants: placing the player moves the single
        /// player, a wall clears any occupant, and an occupant only ever lands on a non-wall tile.
        /// </summary>
        public void Paint(int x, int y, LevelBrush brush)
        {
            if (_working == null || !_working.InBounds(x, y))
            {
                return;
            }

            RecordMutation();

            // Paint writes the group id at the painted index, so guarantee the parallel list is dense
            // (count == width*height) before the switch. A loaded asset that predates the field
            // deserializes as a null or short list, and Clone carries that straight through, so this
            // rebuilds a full-size list and copies any ids that were present (everything else = group A).
            int requiredCount = _working.width * _working.height;
            if (_working.groupIds == null || _working.groupIds.Count != requiredCount)
            {
                var groupIds = new List<int>(new int[requiredCount]);
                if (_working.groupIds != null)
                {
                    int copy = System.Math.Min(_working.groupIds.Count, requiredCount);
                    for (int i = 0; i < copy; i++)
                    {
                        groupIds[i] = _working.groupIds[i];
                    }
                }

                _working.groupIds = groupIds;
            }

            int index = _working.Index(x, y);

            switch (brush)
            {
                case LevelBrush.Wall:
                    _working.cells[index] = TileType.Wall;
                    _working.occupants[index] = OccupantType.None;
                    _working.groupIds[index] = 0;
                    break;
                case LevelBrush.Floor:
                    _working.cells[index] = TileType.Floor;
                    _working.occupants[index] = OccupantType.None;
                    _working.groupIds[index] = 0;
                    break;
                case LevelBrush.Goal:
                    _working.cells[index] = TileType.Goal;
                    _working.occupants[index] = OccupantType.None;
                    _working.groupIds[index] = 0;
                    break;
                case LevelBrush.Plate:
                case LevelBrush.PlateA:
                    _working.cells[index] = TileType.Plate;
                    _working.occupants[index] = OccupantType.None;
                    _working.groupIds[index] = 0;
                    break;
                case LevelBrush.PlateB:
                    _working.cells[index] = TileType.Plate;
                    _working.occupants[index] = OccupantType.None;
                    _working.groupIds[index] = 1;
                    break;
                case LevelBrush.Door:
                case LevelBrush.DoorA:
                    _working.cells[index] = TileType.Door;
                    _working.occupants[index] = OccupantType.None;
                    _working.groupIds[index] = 0;
                    break;
                case LevelBrush.DoorB:
                    _working.cells[index] = TileType.Door;
                    _working.occupants[index] = OccupantType.None;
                    _working.groupIds[index] = 1;
                    break;
                case LevelBrush.Erase:
                    _working.cells[index] = TileType.Floor;
                    _working.occupants[index] = OccupantType.None;
                    _working.groupIds[index] = 0;
                    break;
                case LevelBrush.Player:
                    MovePlayerTo(index);
                    break;
                case LevelBrush.Box:
                    // A box is an occupant, not a tile: placing one must not disturb a Plate/Door tile's
                    // group id (a starting box on a group-B plate is a legitimate level). Only a Wall is
                    // converted to Floor here, and that is already a group-A tile, so the group id is
                    // preserved in every non-Wall case.
                    if (_working.cells[index] == TileType.Wall)
                    {
                        _working.cells[index] = TileType.Floor;
                        _working.groupIds[index] = 0;
                    }
                    _working.occupants[index] = OccupantType.Box;
                    break;
            }

            IsDirty = true;
        }

        /// <summary>
        /// Validates the working copy through <see cref="LevelValidator"/>, so the editor, the playtest
        /// and the window share a single rule source. The returned list is empty exactly when valid.
        /// </summary>
        public List<string> Validate()
        {
            return LevelValidator.Validate(_working);
        }

        /// <summary>
        /// Validates the working copy with severity information, so the window can show blocking
        /// errors and advisory warnings separately. Save/Playtest keep using the error-only
        /// <see cref="Validate"/>.
        /// </summary>
        public List<LevelIssue> ValidateDetailed()
        {
            return LevelValidator.ValidateDetailed(_working);
        }

        /// <summary>Saves to the bound source asset. Returns false when the document is untitled.</summary>
        public bool Save()
        {
            if (string.IsNullOrEmpty(_sourcePath))
            {
                return false;
            }

            return SaveAs(_sourcePath);
        }

        /// <summary>
        /// Writes the working copy to <paramref name="assetPath"/> (creating the asset when missing,
        /// otherwise updating the existing asset in place) and clears the dirty flag.
        /// </summary>
        public bool SaveAs(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            EnsureAssetFolder(assetPath);

            LevelDefinition existing = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(_working, existing);
                existing.name = Path.GetFileNameWithoutExtension(assetPath);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                LevelDefinition asset = Clone(_working);
                asset.name = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            AssetDatabase.SaveAssets();
            _sourcePath = assetPath;
            IsDirty = false;
            _undo.Clear();
            _redo.Clear();
            return true;
        }

        private static void SetCell(LevelDefinition def, int x, int y, TileType tile, OccupantType occupant)
        {
            if (!def.InBounds(x, y))
            {
                return;
            }

            int index = def.Index(x, y);
            def.cells[index] = tile;
            def.occupants[index] = occupant;
        }

        private void MovePlayerTo(int index)
        {
            // Exactly one player: clear the previous player cell first.
            for (int i = 0; i < _working.occupants.Count; i++)
            {
                if (_working.occupants[i] == OccupantType.Player)
                {
                    _working.occupants[i] = OccupantType.None;
                    break;
                }
            }

            EnsureNonWall(index);
            _working.occupants[index] = OccupantType.Player;
        }

        private void EnsureNonWall(int index)
        {
            if (_working.cells[index] == TileType.Wall)
            {
                _working.cells[index] = TileType.Floor;
            }
        }

        private int CountOccupants(OccupantType occupant)
        {
            if (_working == null || _working.occupants == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _working.occupants.Count; i++)
            {
                if (_working.occupants[i] == occupant)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountTiles(TileType tile)
        {
            if (_working == null || _working.cells == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _working.cells.Count; i++)
            {
                if (_working.cells[i] == tile)
                {
                    count++;
                }
            }

            return count;
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            dir = dir.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(dir))
            {
                return;
            }

            string[] parts = dir.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}

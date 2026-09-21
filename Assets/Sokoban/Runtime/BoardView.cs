using System.Collections.Generic;
using UnityEngine;

namespace Sokoban
{
    /// <summary>
    /// Presentation layer for a <see cref="Board"/>. It renders one sprite quad per cell plus
    /// one sprite per box/player, and never feeds anything back into the model: presentation
    /// reads board state, the board is the only rule authority.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        [Header("Appearance")]
        public Color wallColor = new Color(0.25f, 0.25f, 0.28f);
        public Color floorColor = new Color(0.87f, 0.87f, 0.85f);
        public Color goalColor = new Color(0.82f, 0.72f, 0.40f);
        public Color boxColor = new Color(0.85f, 0.45f, 0.15f);
        public Color playerColor = new Color(0.20f, 0.75f, 0.85f);
        // View-only readability palette. Plate is a bright magenta pad, deliberately far from the
        // yellow goal and the green box-on-goal; closed door is a near-black brown (solid, blocked,
        // distinct from the neutral grey wall and the bright orange box); open door is a bright cyan
        // that cannot be mistaken for the off-white floor.
        public Color plateColor = new Color(0.70f, 0.40f, 0.68f);
        public Color doorClosedColor = new Color(0.18f, 0.09f, 0.07f);
        public Color doorOpenColor = new Color(0.55f, 0.90f, 1.00f);
        // Group-B variants: a distinct hue per group so A vs B is visible by color; the letter glyph
        // below is the non-color discriminator. Open stays bright, closed stays dark, matching A.
        public Color plateColorB = new Color(0.38f, 0.56f, 0.82f);
        public Color doorClosedColorB = new Color(0.08f, 0.10f, 0.28f);
        public Color doorOpenColorB = new Color(0.35f, 0.90f, 0.35f);
        // Badge accent colors for the A/B corner glyph: tinted per group so the letter is never a
        // bare white marker. Kept deliberately light so it stays readable on the dark closed door.
        private static readonly Color GroupGlyphA = new Color(1.00f, 0.62f, 0.86f);
        private static readonly Color GroupGlyphB = new Color(0.62f, 0.86f, 1.00f);
        public Color backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        public Color boxOnGoalColor = new Color(0.45f, 0.80f, 0.30f);
        public float cellSize = 1f;

        [Tooltip("View-only easing time for one cell step. The board itself is always instant.")]
        public float moveLerpDuration = 0.12f;

        [Tooltip("View-only duration of a door's open/close color fade. The board state stays instant.")]
        public float doorTransitionDuration = 0.15f;

        [Tooltip("View-only scale pulse applied to a box that changed cell between refreshes (a push).")]
        public float boxPulseDuration = 0.15f;
        public float boxPulseScale = 1.12f;

        [Tooltip("View-only scale pulse applied to a goal when a box first lands on it.")]
        public float goalPulseDuration = 0.25f;
        public float goalPulseScale = 1.15f;

        [Tooltip("View-only one-shot brighten of the board background when the level is completed.")]
        public float completionFlashDuration = 0.5f;
        public Color completionFlashColor = new Color(0.95f, 0.92f, 0.70f);

        private static Sprite _unitSprite;
        private static Font _glyphFont;

        /// <summary>
        /// Procedurally painted sprite per tile/occupant kind, all packed into one runtime-built texture
        /// atlas so the tiles still batch together. Populated lazily by <see cref="Tile"/> and rebuilt if
        /// a domain reload without scene reload leaves stale (destroyed) sprite handles behind.
        /// </summary>
        private static Sprite[] _tileSprites;

        private readonly List<GameObject> _tileObjects = new List<GameObject>();
        private readonly List<GameObject> _goalObjects = new List<GameObject>();
        private readonly List<GameObject> _plateObjects = new List<GameObject>();
        private readonly List<GameObject> _doorObjects = new List<GameObject>();
        private readonly List<(int x, int y)> _doorPositions = new List<(int x, int y)>();
        private readonly List<GameObject> _groupGlyphObjects = new List<GameObject>();
        private readonly List<GameObject> _doorGlyphObjects = new List<GameObject>();
        private readonly List<GameObject> _boxObjects = new List<GameObject>();

        // View-only interpolation targets. They are written by Build/Refresh and only read by Update,
        // which eases the sprites toward them; gameplay never reads a transform position.
        private readonly List<Vector3> _boxTargetPositions = new List<Vector3>();
        private Vector3 _playerTargetPosition;

        // View-only facing bookkeeping. The board stores no facing, so the view derives the player's
        // heading from the cell delta between two refreshes; it is only ever written to the sprite
        // rotation and nothing in gameplay or rule evaluation reads it.
        private (int x, int y) _lastPlayerCell;
        private bool _hasLastPlayerCell;

        // View-only door color transition state. The board's open/closed truth is re-read on every
        // Refresh (never cached here); these lists only ease the *presentation* of the color between
        // two re-read states. _doorDisplayColors is what the renderer currently shows and doubles as
        // the "from" color when a new open/close change begins.
        private readonly List<Color> _doorDisplayColors = new List<Color>();
        private readonly List<Color> _doorFromColors = new List<Color>();
        private readonly List<Color> _doorToColors = new List<Color>();
        private readonly List<float> _doorTransitionProgress = new List<float>();

        // View-only pulse timers, parallel to _boxObjects / _goalObjects respectively.
        private readonly List<float> _boxPulseTimers = new List<float>();
        private readonly List<float> _goalPulseTimers = new List<float>();

        // Goal cells parallel to _goalObjects, so a box landing on a goal can find its ring.
        private readonly List<(int x, int y)> _goalPositions = new List<(int x, int y)>();

        // Box / box-on-goal / completion snapshots taken from the board on Build and every Refresh.
        // They exist purely for view-side edge detection (push cue, goal cue, completion cue) and are
        // never read back by gameplay or rule evaluation.
        private readonly HashSet<(int x, int y)> _lastBoxCells = new HashSet<(int x, int y)>();
        private readonly HashSet<(int x, int y)> _lastBoxOnGoalCells = new HashSet<(int x, int y)>();
        private bool _hasLastBoxCells;
        private bool _hasLastBoxOnGoalCells;
        private bool _lastComplete;
        private bool _hasLastComplete;

        // View-only completion flash timer (seconds remaining).
        private float _completionFlashTimer;

        private GameObject _backgroundObject;
        private GameObject _playerObject;
        private Camera _camera;

        private int _width;
        private int _height;

        /// <summary>Creates the static tile layer and the dynamic box/player objects once.</summary>
        public void Build(Board board)
        {
            if (board == null)
            {
                return;
            }

            ClearObjects();

            _width = board.Width;
            _height = board.Height;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    TileType tile = board.GetTile(x, y);
                    if (tile == TileType.Wall)
                    {
                        // Full-cell so the procedural bevel meets the neighbouring tile and the wall mass
                        // reads as one solid raised block rather than a floating square.
                        _tileObjects.Add(
                            CreateCell("Wall", x, y, wallColor, -1f, 0, Tile(TileKind.Wall), 1f));
                    }
                    else
                    {
                        _tileObjects.Add(
                            CreateCell("Floor", x, y, floorColor, -1f, 0, Tile(TileKind.Floor), 1f));
                        if (tile == TileType.Goal)
                        {
                            // The target symbol is drawn at near-cell size and is transparent between its
                            // rings, so a box parked on this tile still shows the goal around it.
                            GameObject goal = CreateCell(
                                "Goal", x, y, goalColor, -0.7f, 1, Tile(TileKind.Goal), 0.9f);
                            _goalObjects.Add(goal);
                            _goalPositions.Add((x, y));
                            _goalPulseTimers.Add(0f);
                        }
                        else if (tile == TileType.Plate)
                        {
                            bool groupB = board.GetGroupId(x, y) == 1;
                            Color color = groupB ? plateColorB : plateColor;
                            GameObject plate = CreateCell(
                                "Plate", x, y, color, -0.7f, 1, Tile(TileKind.Plate), 0.46f);
                            _plateObjects.Add(plate);
                            GameObject glyph = CreateGroupGlyph(x, y, groupB ? 1 : 0, groupB ? GroupGlyphB : GroupGlyphA);
                            if (glyph != null)
                            {
                                _groupGlyphObjects.Add(glyph);
                            }
                        }
                        else if (tile == TileType.Door)
                        {
                            // Door state is read from the board on build and on every Refresh; the view
                            // never caches it. A door sits below the boxes but above the floor tile.
                            bool groupB = board.GetGroupId(x, y) == 1;
                            bool open = board.IsDoorOpen(x, y);
                            Color doorColor = open
                                ? (groupB ? doorOpenColorB : doorOpenColor)
                                : (groupB ? doorClosedColorB : doorClosedColor);
                            _doorObjects.Add(
                                CreateCell(
                                    "Door", x, y, doorColor, -0.85f, 1,
                                    Tile(open ? TileKind.DoorOpen : TileKind.Door), 1f));
                            _doorPositions.Add((x, y));
                            // The first state snaps: there is no earlier color to ease away from.
                            _doorDisplayColors.Add(doorColor);
                            _doorFromColors.Add(doorColor);
                            _doorToColors.Add(doorColor);
                            _doorTransitionProgress.Add(1f);
                            GameObject glyph = CreateGroupGlyph(x, y, groupB ? 1 : 0, open ? Color.black : Color.white);
                            if (glyph != null)
                            {
                                _doorGlyphObjects.Add(glyph);
                            }
                        }
                    }
                }
            }

            // One dark backing quad behind the whole board (tiles sit at z = -1, this at z = +1, and
            // sortingOrder -1 keeps it behind the tile sprites).
            CreateBackground();

            foreach ((int x, int y) in board.BoxPositions)
            {
                // Box look is derived from the tile under it, exactly like the door color below.
                bool onGoal = board.GetTile(x, y) == TileType.Goal;
                GameObject box = CreateCell(
                    "Box", x, y, onGoal ? boxOnGoalColor : boxColor, -0.8f, 2,
                    Tile(onGoal ? TileKind.BoxOnGoal : TileKind.Box));
                _boxObjects.Add(box);
                _boxTargetPositions.Add(box.transform.position);
                _boxPulseTimers.Add(0f);
            }

            (int px, int py) = board.PlayerPosition;
            _playerObject = CreateCell("Player", px, py, playerColor, -0.9f, 3, Tile(TileKind.Player), 0.7f);

            // First build snaps: there is no earlier visual state to ease away from, and there is no
            // earlier cell to derive a facing from, so the player starts pointing "up".
            _playerTargetPosition = _playerObject.transform.position;
            _lastPlayerCell = (px, py);
            _hasLastPlayerCell = true;

            // Snapshot the derived state the cues are measured against, so the first Refresh after a
            // build compares against the built board (a move made straight after load still cues).
            _lastBoxCells.Clear();
            _lastBoxOnGoalCells.Clear();
            foreach ((int bx, int by) in board.BoxPositions)
            {
                _lastBoxCells.Add((bx, by));
                if (board.GetTile(bx, by) == TileType.Goal)
                {
                    _lastBoxOnGoalCells.Add((bx, by));
                }
            }

            _hasLastBoxCells = true;
            _hasLastBoxOnGoalCells = true;
            _lastComplete = board.IsComplete;
            _hasLastComplete = true;
            _completionFlashTimer = 0f;

            FrameCamera();
        }

        /// <summary>Repositions the dynamic objects so the view matches the current board state.</summary>
        public void Refresh(Board board)
        {
            if (board == null)
            {
                return;
            }

            if (_playerObject == null || board.Width != _width || board.Height != _height)
            {
                Build(board);
                return;
            }

            var boxes = new List<(int x, int y)>(board.BoxPositions);
            while (_boxObjects.Count < boxes.Count)
            {
                // A new box is created straight at its cell, so it never eases in from elsewhere.
                (int bx, int by) = boxes[_boxObjects.Count];
                bool onGoal = board.GetTile(bx, by) == TileType.Goal;
                GameObject box = CreateCell(
                    "Box", bx, by, onGoal ? boxOnGoalColor : boxColor, -0.8f, 2,
                    Tile(onGoal ? TileKind.BoxOnGoal : TileKind.Box));
                _boxObjects.Add(box);
                _boxTargetPositions.Add(box.transform.position);
                _boxPulseTimers.Add(0f);
            }

            for (int i = 0; i < _boxObjects.Count; i++)
            {
                _boxObjects[i].SetActive(i < boxes.Count);
                if (i < boxes.Count)
                {
                    // Derived box look, re-read from the board on every refresh (never cached): the tint
                    // flips to the covered-goal palette and the crate shrinks so the goal rings underneath
                    // stay visible around it.
                    bool onGoal = board.GetTile(boxes[i].x, boxes[i].y) == TileType.Goal;
                    SetColor(_boxObjects[i], onGoal ? boxOnGoalColor : boxColor);
                    SetSprite(_boxObjects[i], Tile(onGoal ? TileKind.BoxOnGoal : TileKind.Box));

                    // View-only edge detection, read from the snapshot taken on the previous refresh.
                    // A box whose cell was empty of boxes before is a pushed box: pulse it. A box that
                    // newly sits on a goal pulses that goal's ring. Neither touches board state.
                    bool pushed = _hasLastBoxCells && !_lastBoxCells.Contains(boxes[i]);
                    if (pushed && i < _boxPulseTimers.Count)
                    {
                        _boxPulseTimers[i] = boxPulseDuration;
                    }

                    if (onGoal && _hasLastBoxOnGoalCells && !_lastBoxOnGoalCells.Contains(boxes[i]))
                    {
                        int goalIndex = _goalPositions.IndexOf(boxes[i]);
                        if (goalIndex >= 0 && goalIndex < _goalPulseTimers.Count)
                        {
                            _goalPulseTimers[goalIndex] = goalPulseDuration;
                        }
                    }

                    // Only the interpolation target moves here; Update() eases the sprite toward it.
                    _boxTargetPositions[i] = CellToWorld(boxes[i].x, boxes[i].y, -0.8f);
                }
            }

            // Rebuild the view-only box/goal snapshot from the board for the next refresh's edge
            // detection. Derived state only: the board remains the sole authority.
            _lastBoxCells.Clear();
            _lastBoxOnGoalCells.Clear();
            foreach ((int bx, int by) in boxes)
            {
                _lastBoxCells.Add((bx, by));
                if (board.GetTile(bx, by) == TileType.Goal)
                {
                    _lastBoxOnGoalCells.Add((bx, by));
                }
            }

            _hasLastBoxCells = true;
            _hasLastBoxOnGoalCells = true;

            (int px, int py) = board.PlayerPosition;
            _playerTargetPosition = CellToWorld(px, py, -0.9f);

            // Facing is view-only and inferred from the cell delta since the previous refresh, because
            // the board stores no facing. A push moves the player and the box together, so this sees the
            // player's own step; a refresh with no player movement leaves the last heading untouched.
            if (_hasLastPlayerCell && (px != _lastPlayerCell.x || py != _lastPlayerCell.y))
            {
                float worldDx = (px - _lastPlayerCell.x) * cellSize;
                float worldDy = -(py - _lastPlayerCell.y) * cellSize;
                float degrees = Mathf.Atan2(worldDy, worldDx) * Mathf.Rad2Deg;

                // The pawn sprite points "up" at a zero rotation, hence the quarter-turn offset.
                _playerObject.transform.rotation = Quaternion.Euler(0f, 0f, degrees - 90f);
            }

            _lastPlayerCell = (px, py);
            _hasLastPlayerCell = true;

            // Door color is derived, so it is re-read from the board here and never cached by the view.
            // When the derived open/closed state changes, the *presentation* starts easing from the
            // currently shown color toward the new one; Update() drives that fade. The door glyph's ink
            // color flips with open/closed (black on the bright open hue, white on the dark closed hue);
            // its letter never changes because a tile's group id is static.
            for (int i = 0; i < _doorObjects.Count && i < _doorPositions.Count; i++)
            {
                (int dx, int dy) = _doorPositions[i];
                bool groupB = board.GetGroupId(dx, dy) == 1;
                bool open = board.IsDoorOpen(dx, dy);
                Color target = open
                    ? (groupB ? doorOpenColorB : doorOpenColor)
                    : (groupB ? doorClosedColorB : doorClosedColor);

                if (i >= _doorToColors.Count)
                {
                    // Defensive: a door without transition state (should not happen) still renders.
                    SetColor(_doorObjects[i], target);
                }
                else if (!Application.isPlaying || doorTransitionDuration <= 0f)
                {
                    // No frame loop to ease in (edit mode) or easing disabled: snap cleanly.
                    _doorDisplayColors[i] = target;
                    _doorFromColors[i] = target;
                    _doorToColors[i] = target;
                    _doorTransitionProgress[i] = 1f;
                    SetColor(_doorObjects[i], target);
                }
                else if (target != _doorToColors[i])
                {
                    _doorFromColors[i] = _doorDisplayColors[i];
                    _doorToColors[i] = target;
                    _doorTransitionProgress[i] = 0f;
                }

                if (i < _doorGlyphObjects.Count)
                {
                    SetGlyphColor(_doorGlyphObjects[i], open ? Color.black : Color.white);
                }

                // Open and closed doors differ in shape as well as tint: the sprite swap snaps while the
                // color above keeps easing. SetSprite no-ops when the kind is unchanged.
                SetSprite(_doorObjects[i], Tile(open ? TileKind.DoorOpen : TileKind.Door));
            }

            // One-shot completion cue, fired only on a false -> true edge across refreshes.
            bool complete = board.IsComplete;
            if (_hasLastComplete && complete && !_lastComplete)
            {
                _completionFlashTimer = completionFlashDuration;
            }

            _lastComplete = complete;
            _hasLastComplete = true;
        }

        /// <summary>
        /// View-only easing toward the targets the last Build/Refresh asked for. The board state is
        /// already final when Refresh runs; this only smooths the sprites, and nothing in gameplay or
        /// rule evaluation ever reads a transform position (no logic is gated on lerp completion).
        /// </summary>
        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (moveLerpDuration <= 0f)
            {
                SnapToTargets();
            }
            else
            {
                float step = (cellSize / moveLerpDuration) * deltaTime;

                for (int i = 0; i < _boxObjects.Count && i < _boxTargetPositions.Count; i++)
                {
                    GameObject box = _boxObjects[i];
                    if (box != null && box.activeSelf)
                    {
                        box.transform.position = Vector3.MoveTowards(
                            box.transform.position, _boxTargetPositions[i], step);
                    }
                }

                if (_playerObject != null)
                {
                    _playerObject.transform.position = Vector3.MoveTowards(
                        _playerObject.transform.position, _playerTargetPosition, step);
                }
            }

            // View-only feedback runs regardless of whether the position easing is enabled, so the
            // cues still read when moveLerpDuration is zero.
            UpdateDoorTransitions(deltaTime);
            UpdateBoxPulses(deltaTime);
            UpdateGoalPulses(deltaTime);
            UpdateCompletionFlash(deltaTime);
        }

        /// <summary>Eases each door's rendered color toward the state the last Refresh re-read from the board.</summary>
        private void UpdateDoorTransitions(float deltaTime)
        {
            for (int i = 0; i < _doorObjects.Count && i < _doorToColors.Count; i++)
            {
                GameObject door = _doorObjects[i];
                if (door == null || _doorTransitionProgress[i] >= 1f)
                {
                    continue;
                }

                _doorTransitionProgress[i] = doorTransitionDuration > 0f
                    ? Mathf.Min(1f, _doorTransitionProgress[i] + (deltaTime / doorTransitionDuration))
                    : 1f;

                float eased = Mathf.SmoothStep(0f, 1f, _doorTransitionProgress[i]);
                _doorDisplayColors[i] = Color.Lerp(_doorFromColors[i], _doorToColors[i], eased);
                SetColor(door, _doorDisplayColors[i]);
            }
        }

        /// <summary>Applies the transient scale overshoot to recently pushed boxes.</summary>
        private void UpdateBoxPulses(float deltaTime)
        {
            float baseScale = cellSize * CellVisualScale;

            for (int i = 0; i < _boxObjects.Count && i < _boxPulseTimers.Count; i++)
            {
                GameObject box = _boxObjects[i];
                if (box == null || _boxPulseTimers[i] <= 0f)
                {
                    continue;
                }

                _boxPulseTimers[i] = Mathf.Max(0f, _boxPulseTimers[i] - deltaTime);
                float t = boxPulseDuration > 0f ? 1f - (_boxPulseTimers[i] / boxPulseDuration) : 1f;
                float factor = Mathf.Lerp(1f, boxPulseScale, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));
                box.transform.localScale = Vector3.one * (baseScale * factor);
            }
        }

        /// <summary>Applies the transient scale pulse to goals a box has just landed on.</summary>
        private void UpdateGoalPulses(float deltaTime)
        {
            float baseScale = cellSize * CellVisualScale;

            for (int i = 0; i < _goalObjects.Count && i < _goalPulseTimers.Count; i++)
            {
                GameObject goal = _goalObjects[i];
                if (goal == null || _goalPulseTimers[i] <= 0f)
                {
                    continue;
                }

                _goalPulseTimers[i] = Mathf.Max(0f, _goalPulseTimers[i] - deltaTime);
                float t = goalPulseDuration > 0f ? 1f - (_goalPulseTimers[i] / goalPulseDuration) : 1f;
                float factor = Mathf.Lerp(1f, goalPulseScale, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));
                goal.transform.localScale = Vector3.one * (baseScale * factor);
            }
        }

        /// <summary>Runs the one-shot background brighten fired when the board reports completion.</summary>
        private void UpdateCompletionFlash(float deltaTime)
        {
            if (_completionFlashTimer <= 0f || _backgroundObject == null)
            {
                return;
            }

            _completionFlashTimer = Mathf.Max(0f, _completionFlashTimer - deltaTime);
            float t = completionFlashDuration > 0f
                ? 1f - (_completionFlashTimer / completionFlashDuration)
                : 1f;
            float amount = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);

            var renderer = _backgroundObject.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.color = Color.Lerp(backgroundColor, completionFlashColor, amount);
            }
        }

        /// <summary>Places every dynamic object exactly on its target (used when easing is disabled).</summary>
        private void SnapToTargets()
        {
            for (int i = 0; i < _boxObjects.Count && i < _boxTargetPositions.Count; i++)
            {
                if (_boxObjects[i] != null)
                {
                    _boxObjects[i].transform.position = _boxTargetPositions[i];
                }
            }

            if (_playerObject != null)
            {
                _playerObject.transform.position = _playerTargetPosition;
            }
        }

        /// <summary>Creates the single dark quad that sits behind the whole board.</summary>
        private void CreateBackground()
        {
            const float margin = 0.5f;
            float width = _width * cellSize + margin;
            float height = _height * cellSize + margin;
            float centerX = (_width - 1) * 0.5f * cellSize;
            float centerY = -(_height - 1) * 0.5f * cellSize;

            _backgroundObject = new GameObject("BoardBackground");
            _backgroundObject.transform.SetParent(transform, false);
            _backgroundObject.transform.position = new Vector3(centerX, centerY, 1f);
            _backgroundObject.transform.localScale = new Vector3(width, height, 1f);

            var renderer = _backgroundObject.AddComponent<SpriteRenderer>();
            renderer.sprite = UnitSprite;
            renderer.color = backgroundColor;
            renderer.sortingOrder = -1;
        }

        private GameObject CreateCell(
            string label, int x, int y, Color color, float z, int sortingOrder, Sprite sprite = null, float scale = CellVisualScale)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.transform.position = CellToWorld(x, y, z);
            go.transform.localScale = Vector3.one * (cellSize * scale);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : UnitSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;

            return go;
        }

        /// <summary>Recolors an existing cell quad without recreating it.</summary>
        private static void SetColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.color = color;
            }
        }

        /// <summary>Swaps the procedural shape of an existing cell quad without recreating it.</summary>
        private static void SetSprite(GameObject go, Sprite sprite)
        {
            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != sprite)
            {
                renderer.sprite = sprite;
            }
        }

        /// <summary>Authored cell (x, y) maps to world (x, -y, z) so the top authored row renders at the top.</summary>
        private Vector3 CellToWorld(int x, int y, float z)
        {
            return new Vector3(x * cellSize, -y * cellSize, z);
        }

        private void ClearObjects()
        {
            foreach (GameObject go in _tileObjects)
            {
                DestroyObject(go);
            }

            foreach (GameObject go in _goalObjects)
            {
                DestroyObject(go);
            }

            foreach (GameObject go in _plateObjects)
            {
                DestroyObject(go);
            }

            foreach (GameObject go in _doorObjects)
            {
                DestroyObject(go);
            }

            foreach (GameObject go in _groupGlyphObjects)
            {
                DestroyObject(go);
            }

            foreach (GameObject go in _doorGlyphObjects)
            {
                DestroyObject(go);
            }

            foreach (GameObject go in _boxObjects)
            {
                DestroyObject(go);
            }

            if (_backgroundObject != null)
            {
                DestroyObject(_backgroundObject);
            }

            if (_playerObject != null)
            {
                DestroyObject(_playerObject);
            }

            _tileObjects.Clear();
            _goalObjects.Clear();
            _plateObjects.Clear();
            _doorObjects.Clear();
            _doorPositions.Clear();
            _groupGlyphObjects.Clear();
            _doorGlyphObjects.Clear();
            _boxObjects.Clear();
            _boxTargetPositions.Clear();
            _doorDisplayColors.Clear();
            _doorFromColors.Clear();
            _doorToColors.Clear();
            _doorTransitionProgress.Clear();
            _boxPulseTimers.Clear();
            _goalPulseTimers.Clear();
            _goalPositions.Clear();
            _lastBoxCells.Clear();
            _lastBoxOnGoalCells.Clear();
            _playerTargetPosition = Vector3.zero;
            _hasLastPlayerCell = false;
            _hasLastBoxCells = false;
            _hasLastBoxOnGoalCells = false;
            _hasLastComplete = false;
            _completionFlashTimer = 0f;
            _backgroundObject = null;
            _playerObject = null;
        }

        private static void DestroyObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(go);
            }
            else
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Lazily loads the built-in runtime font used for the A/B group glyphs. Unity 2022.3 names it
        /// <c>LegacyRuntime.ttf</c>; the pre-2022.2 <c>Arial.ttf</c> name is kept as a fallback. A null
        /// result means glyphs are skipped and per-group color alone still distinguishes A from B.
        /// </summary>
        private static Font GlyphFont
        {
            get
            {
                if (_glyphFont == null)
                {
                    _glyphFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_glyphFont == null)
                    {
                        _glyphFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    }
                }

                return _glyphFont;
            }
        }

        /// <summary>
        /// Creates a small "A"/"B" letter overlay for a plate or door cell so a group is identifiable
        /// without relying on color alone. The glyph is a <see cref="TextMesh"/> parented to this view,
        /// placed just above the pad (same sorting order 1, so boxes/players still draw over it) and is
        /// static for the cell's lifetime (a tile's group id never changes). Returns null when no
        /// built-in font is available so callers simply skip the overlay and keep the color distinction.
        /// </summary>
        private GameObject CreateGroupGlyph(int x, int y, int groupId, Color color)
        {
            Font font = GlyphFont;
            if (font == null)
            {
                return null;
            }

            var glyph = new GameObject("GroupGlyph");
            glyph.transform.SetParent(transform, false);
            glyph.transform.position = CellToWorld(x, y, -0.6f) + new Vector3(-cellSize * 0.28f, cellSize * 0.28f, 0f);

            var text = glyph.AddComponent<TextMesh>();
            text.font = font;
            text.text = groupId == 1 ? "B" : "A";
            text.characterSize = cellSize * 0.2f;
            text.fontSize = 10;
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
            text.color = color;

            var renderer = glyph.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 1;
            }

            return glyph;
        }

        private static void SetGlyphColor(GameObject glyph, Color color)
        {
            if (glyph == null)
            {
                return;
            }

            var text = glyph.GetComponent<TextMesh>();
            if (text != null)
            {
                text.color = color;
            }
        }

        /// <summary>Every procedurally painted shape the view can put on a cell.</summary>
        private enum TileKind
        {
            Flat = 0,
            Wall = 1,
            Floor = 2,
            Goal = 3,
            Box = 4,
            BoxOnGoal = 5,
            Plate = 6,
            Door = 7,
            Player = 8,
            DoorOpen = 9
        }

        private const int TileKindCount = 10;

        /// <summary>Default fraction of a cell a sprite covers, shared by Build and the pulse animators.</summary>
        private const float CellVisualScale = 0.9f;

        /// <summary>Edge length, in pixels, of one shape inside the generated atlas.</summary>
        private const int TileResolution = 32;

        /// <summary>Transparent gutter around each shape so point sampling can never bleed into a neighbour.</summary>
        private const int TilePadding = 1;

        private static readonly Color NoInk = new Color(0f, 0f, 0f, 0f);

        /// <summary>
        /// Returns the procedural sprite for a kind, building (or rebuilding) the shared atlas on first
        /// use. The whole atlas is generated at runtime: no imported asset, no <c>AssetDatabase</c> and no
        /// <c>Resources.Load</c> is involved. The sprite is exactly one world unit square, so the callers'
        /// existing <c>cellSize</c> scaling is unchanged, and the renderer color still tints it as before.
        /// </summary>
        private static Sprite Tile(TileKind kind)
        {
            int index = (int)kind;
            if (_tileSprites == null || _tileSprites.Length != TileKindCount || _tileSprites[index] == null)
            {
                BuildTileSprites();
            }

            return _tileSprites[index];
        }

        private static void BuildTileSprites()
        {
            int pitch = TileResolution + (TilePadding * 2);
            int atlasWidth = pitch * TileKindCount;

            // Color[] elements default to (0, 0, 0, 0), i.e. the transparent padding is already correct.
            var pixels = new Color[atlasWidth * pitch];

            for (int kind = 0; kind < TileKindCount; kind++)
            {
                System.Func<float, float, Color> shader = ShaderFor((TileKind)kind);
                int originX = (kind * pitch) + TilePadding;

                for (int y = 0; y < TileResolution; y++)
                {
                    float v = (y + 0.5f) / TileResolution;
                    int row = ((TilePadding + y) * atlasWidth) + originX;

                    for (int x = 0; x < TileResolution; x++)
                    {
                        float u = (x + 0.5f) / TileResolution;
                        pixels[row + x] = shader(u, v);
                    }
                }
            }

            var atlas = new Texture2D(atlasWidth, pitch, TextureFormat.RGBA32, false);
            atlas.SetPixels(pixels);
            atlas.Apply();
            atlas.filterMode = FilterMode.Point;
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.name = "SokobanTileAtlas";

            var sprites = new Sprite[TileKindCount];
            for (int kind = 0; kind < TileKindCount; kind++)
            {
                var rect = new Rect((kind * pitch) + TilePadding, TilePadding, TileResolution, TileResolution);
                sprites[kind] = Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), TileResolution);
                sprites[kind].name = "SokobanTile_" + ((TileKind)kind);
            }

            _tileSprites = sprites;
        }

        private static System.Func<float, float, Color> ShaderFor(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Wall: return WallShader;
                case TileKind.Floor: return FloorShader;
                case TileKind.Goal: return GoalShader;
                case TileKind.Box: return BoxShader;
                case TileKind.BoxOnGoal: return BoxOnGoalShader;
                case TileKind.Plate: return PlateShader;
                case TileKind.Door: return DoorShader;
                case TileKind.DoorOpen: return DoorOpenShader;
                case TileKind.Player: return PlayerShader;
                default: return FlatShader;
            }
        }

        /// <summary>Plain white quad; the renderer color alone is the whole appearance.</summary>
        private static Color FlatShader(float u, float v)
        {
            return Color.white;
        }

        /// <summary>
        /// Impassable block: dark outline, a bevel that is lit on the top/left and shaded on the
        /// bottom/right, and a slightly recessed fill, so it reads as a raised solid, not flat floor.
        /// </summary>
        private static Color WallShader(float u, float v)
        {
            float dx = Mathf.Min(u, 1f - u);
            float dy = Mathf.Min(v, 1f - v);
            float edge = Mathf.Min(dx, dy);

            if (edge < 0.06f)
            {
                return Ink(0.48f);
            }

            if (edge < 0.17f)
            {
                bool litTop = dy <= dx && v >= 0.5f;
                bool litLeft = dx < dy && u <= 0.5f;
                return litTop || litLeft ? Ink(0.90f) : Ink(0.63f);
            }

            return Ink(0.74f);
        }

        /// <summary>Recessed floor tile: a faint seam at the tile edge plus a barely-there inner square.</summary>
        private static Color FloorShader(float u, float v)
        {
            float dx = Mathf.Min(u, 1f - u);
            float dy = Mathf.Min(v, 1f - v);

            if (Mathf.Min(dx, dy) < 0.045f)
            {
                return Ink(0.87f);
            }

            float inset = Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f));
            return Mathf.Abs(inset - 0.26f) < 0.03f ? Ink(0.97f) : Color.white;
        }

        /// <summary>
        /// Target symbol: three concentric rings with transparent gaps. Transparent is the important
        /// part: a box parked on the goal keeps showing the rings around itself.
        /// </summary>
        private static Color GoalShader(float u, float v)
        {
            float r = Radius(u, v);

            if ((r > 0.42f && r <= 0.46f) ||
                (r > 0.28f && r <= 0.32f) ||
                (r > 0.13f && r <= 0.16f))
            {
                return Color.white;
            }

            return NoInk;
        }

        /// <summary>Crate: dark outer frame, bright rim, inner rectangle, X brace and a raised centre boss.</summary>
        private static Color BoxShader(float u, float v)
        {
            float dx = Mathf.Min(u, 1f - u);
            float dy = Mathf.Min(v, 1f - v);
            float edge = Mathf.Min(dx, dy);
            float adx = Mathf.Abs(u - 0.5f);
            float ady = Mathf.Abs(v - 0.5f);
            float inset = Mathf.Max(adx, ady);

            if (edge < 0.06f)
            {
                return Ink(0.52f);
            }

            if (edge < 0.13f)
            {
                return Color.white;
            }

            if (Mathf.Abs(inset - 0.30f) < 0.04f)
            {
                return Ink(0.72f);
            }

            if (inset < 0.30f)
            {
                if (inset < 0.09f)
                {
                    return Color.white;
                }

                if (Mathf.Abs(adx - ady) < 0.04f)
                {
                    return Ink(0.74f);
                }
            }

            return Ink(0.92f);
        }

        /// <summary>
        /// Box seated on a goal: the same crate detailing drawn half-size, so the goal rings painted on
        /// the tile underneath stay visible around it. That shape difference (smaller, ringed) plus the
        /// existing covered-goal tint keeps it distinct from both a plain box and a bare goal.
        /// </summary>
        private static Color BoxOnGoalShader(float u, float v)
        {
            float adx = Mathf.Abs(u - 0.5f);
            float ady = Mathf.Abs(v - 0.5f);
            float inset = Mathf.Max(adx, ady);

            if (inset > 0.24f)
            {
                return NoInk;
            }

            if (inset > 0.19f)
            {
                return Ink(0.50f);
            }

            if (inset > 0.15f)
            {
                return Color.white;
            }

            if (inset < 0.06f)
            {
                return Color.white;
            }

            return Mathf.Abs(adx - ady) < 0.04f ? Ink(0.74f) : Ink(0.92f);
        }

        /// <summary>Pressure pad: bright ring around a recessed field with a raised centre boss.</summary>
        private static Color PlateShader(float u, float v)
        {
            float dx = Mathf.Min(u, 1f - u);
            float dy = Mathf.Min(v, 1f - v);
            float edge = Mathf.Min(dx, dy);
            float inset = Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f));

            if (edge < 0.05f)
            {
                return NoInk;
            }

            if (edge < 0.13f || inset < 0.11f)
            {
                return Color.white;
            }

            return Ink(0.60f);
        }

        /// <summary>
        /// Mechanical door: a framed panel with horizontal ribs. The ribs only vary brightness, so the
        /// existing open/closed and group-A/group-B tints keep working through them unchanged.
        /// </summary>
        private static Color DoorShader(float u, float v)
        {
            float dx = Mathf.Min(u, 1f - u);
            float dy = Mathf.Min(v, 1f - v);
            float edge = Mathf.Min(dx, dy);

            if (edge < 0.05f)
            {
                return Ink(0.38f);
            }

            if (edge < 0.11f)
            {
                return Color.white;
            }

            return Mathf.Repeat(v, 0.25f) < 0.055f ? Color.white : Ink(0.55f);
        }

        /// <summary>
        /// Opened mechanical door: a raised bright frame around a dark recessed opening, so the open state
        /// reads as a clear doorway instead of the closed state's solid ribs. The frame stays bright under
        /// the open tint; the recessed centre is the gap a box or player passes through.
        /// </summary>
        private static Color DoorOpenShader(float u, float v)
        {
            float dx = Mathf.Min(u, 1f - u);
            float dy = Mathf.Min(v, 1f - v);
            float edge = Mathf.Min(dx, dy);

            if (edge < 0.05f)
            {
                return Ink(0.34f);
            }

            if (edge < 0.10f)
            {
                return Color.white;
            }

            return Ink(0.20f);
        }

        /// <summary>
        /// Player pawn: a disc with a bright wedge sticking out of its top. The wedge is the facing
        /// marker; <see cref="Refresh"/> rotates the whole sprite so the wedge points where the player
        /// last moved. The sprite is symmetric otherwise, so the silhouette alone reads direction.
        /// </summary>
        private static Color PlayerShader(float u, float v)
        {
            float r = Radius(u, v);

            if (v >= 0.60f && v <= 0.96f)
            {
                float halfWidth = 0.24f * ((0.96f - v) / 0.36f);
                if (Mathf.Abs(u - 0.5f) <= halfWidth)
                {
                    return Color.white;
                }
            }

            if (r <= 0.30f)
            {
                return Ink(0.90f);
            }

            return r <= 0.35f ? Ink(0.55f) : NoInk;
        }

        /// <summary>Distance from the sprite centre, in normalised sprite units (0 .. ~0.707).</summary>
        private static float Radius(float u, float v)
        {
            float du = u - 0.5f;
            float dv = v - 0.5f;
            return Mathf.Sqrt((du * du) + (dv * dv));
        }

        /// <summary>An opaque grey ink, used as a brightness multiplier under the renderer tint.</summary>
        private static Color Ink(float value)
        {
            return new Color(value, value, value, 1f);
        }

        private static Sprite UnitSprite
        {
            get
            {
                if (_unitSprite == null)
                {
                    var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    texture.SetPixel(0, 0, Color.white);
                    texture.Apply();
                    texture.filterMode = FilterMode.Point;
                    _unitSprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, 1f, 1f),
                        new Vector2(0.5f, 0.5f),
                        1f);
                    _unitSprite.name = "SokobanUnitSprite";
                }

                return _unitSprite;
            }
        }

        /// <summary>Finds an orthographic camera (creating one when the scene has none) and frames the board.</summary>
        private void FrameCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                _camera = FindObjectOfType<Camera>();
            }

            if (_camera == null)
            {
                var cameraGo = new GameObject("Main Camera");
                cameraGo.tag = "MainCamera";
                _camera = cameraGo.AddComponent<Camera>();
            }

            _camera.orthographic = true;

            float centerX = (_width - 1) * 0.5f * cellSize;
            float centerY = -(_height - 1) * 0.5f * cellSize;
            _camera.transform.position = new Vector3(centerX, centerY, -10f);

            float aspect = _camera.aspect > 0.0001f ? _camera.aspect : (16f / 9f);
            float verticalExtent = _height * 0.5f * cellSize;
            float horizontalExtent = (_width * 0.5f * cellSize) / aspect;
            _camera.orthographicSize = Mathf.Max(verticalExtent, horizontalExtent) + cellSize * 0.5f;
        }
    }
}

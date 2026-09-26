using System;
using System.Collections.Generic;

namespace Sokoban.Editor
{
    /// <summary>
    /// Headless, editor-only replay of an analyzer solution. It clones the source level exactly once
    /// and owns a separate <see cref="Board"/> built from that clone, so stepping can never touch the
    /// source asset or any window's working copy. Every position is rebuilt by replaying the solution
    /// from the start, which keeps forward, back and reset trivially correct for any grid size and
    /// lets the push flag of each step be derived from the real <see cref="Board.PushCount"/> delta.
    /// </summary>
    public sealed class SolutionPreviewSession
    {
        private readonly LevelDefinition _source;
        private readonly LevelDefinition _clone;
        private readonly AnalysisResult _result;
        private readonly List<bool> _pushFlags = new List<bool>();
        private Board _board;
        private int _step;

        public SolutionPreviewSession(LevelDefinition source, AnalysisResult result)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _result = result ?? throw new ArgumentNullException(nameof(result));
            _clone = LevelEditorDocument.Clone(_source);
            SetStep(0);
        }

        /// <summary>The level this session replays. Never mutated by the session.</summary>
        public LevelDefinition Source => _source;

        /// <summary>
        /// The detached in-memory clone the replay board is built from. Tests that construct a session
        /// own this instance's lifetime and must destroy it; it is never a shared asset.
        /// </summary>
        public LevelDefinition Clone => _clone;

        public AnalysisResult Result => _result;

        /// <summary>The separate replay board, always in sync with <see cref="Step"/>.</summary>
        public Board Board => _board;

        /// <summary>Number of solution moves applied so far (0 is the untouched start state).</summary>
        public int Step => _step;

        public int TotalSteps => _result.Solution != null ? _result.Solution.Count : 0;

        public bool CanStepForward => _step < TotalSteps;

        public bool CanStepBack => _step > 0;

        public bool IsComplete => _board != null && _board.IsComplete;

        public int MoveCount => _board != null ? _board.MoveCount : 0;

        public int PushCount => _board != null ? _board.PushCount : 0;

        public void Reset()
        {
            SetStep(0);
        }

        public bool StepForward()
        {
            return SetStep(_step + 1);
        }

        public bool StepBack()
        {
            return SetStep(_step - 1);
        }

        /// <summary>
        /// Rebuilds the board from the clone and replays exactly <paramref name="target"/> moves,
        /// recording whether each move pushed a box. Returns false for an out-of-range target or an
        /// unexpected rejected move (a valid solution never rejects).
        /// </summary>
        public bool SetStep(int target)
        {
            int total = TotalSteps;
            if (target < 0 || target > total)
            {
                return false;
            }

            _board = new Board(_clone);
            _pushFlags.Clear();

            for (int i = 0; i < target; i++)
            {
                int pushesBefore = _board.PushCount;
                if (!_board.TryMove(_result.Solution[i]))
                {
                    return false;
                }

                _pushFlags.Add(_board.PushCount - pushesBefore == 1);
            }

            _step = target;
            return true;
        }

        /// <summary>True when the move that produced the current step pushed a box.</summary>
        public bool CurrentStepIsPush =>
            _step > 0 && _step <= _pushFlags.Count && _pushFlags[_step - 1];

        /// <summary>Human label for the current position, e.g. "step 7/11: Left (push)".</summary>
        public string CurrentActionLabel()
        {
            if (TotalSteps == 0)
            {
                return "起点（关卡初始即完成）";
            }

            if (_step == 0)
            {
                return $"起点（0/{TotalSteps}）";
            }

            Direction direction = _result.Solution[_step - 1];
            string suffix = CurrentStepIsPush ? "（推箱）" : string.Empty;
            return $"第 {_step} / {TotalSteps} 步：{DirectionLabel(direction)}{suffix}";
        }

        private static string DirectionLabel(Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return "向上";
                case Direction.Down: return "向下";
                case Direction.Left: return "向左";
                case Direction.Right: return "向右";
                default: return direction.ToString();
            }
        }
    }
}

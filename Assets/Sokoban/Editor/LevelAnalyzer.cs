using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Sokoban.Editor
{
    /// <summary>Outcome of one editor-only solvability analysis.</summary>
    public enum AnalysisVerdict
    {
        /// <summary>A solution was found within budget; the reported counts describe that solution.</summary>
        Solvable,

        /// <summary>
        /// The reachable state space was exhaustively explored within budget and no goal state
        /// exists. Only a completed exhaustive search may produce this verdict.
        /// </summary>
        Unsolvable,

        /// <summary>
        /// The search stopped on a budget (state count or wall clock) before either outcome.
        /// A budget hit must never be reported as <see cref="Unsolvable"/>.
        /// </summary>
        Inconclusive
    }

    /// <summary>Result of one <see cref="LevelAnalyzer.Analyze"/> call.</summary>
    public sealed class AnalysisResult
    {
        public AnalysisVerdict Verdict;

        /// <summary>Number of unique states that were dequeued and expanded.</summary>
        public int StatesExplored;

        /// <summary>Wall-clock time the bounded search actually spent.</summary>
        public double ElapsedSeconds;

        /// <summary>Move count of the found (shortest-move) solution; -1 unless Solvable.</summary>
        public int SolutionMoves = -1;

        /// <summary>Push count of the found solution; -1 unless Solvable.</summary>
        public int SolutionPushes = -1;

        /// <summary>
        /// The replayable sequence of moves of the found (shortest-move) solution. Always non-null;
        /// empty unless <see cref="Verdict"/> is <see cref="AnalysisVerdict.Solvable"/> and the
        /// solution is non-trivial (an initially complete level is solvable in zero moves).
        /// </summary>
        public System.Collections.Generic.List<Direction> Solution = new System.Collections.Generic.List<Direction>();

        /// <summary>Human-readable one-liner describing the outcome.</summary>
        public string Detail;
    }

    /// <summary>
    /// Bounded, editor-only solver behind the Level Editor's "Analyze Solvability" button. It runs a
    /// breadth-first search over <see cref="Board"/> states, so it walks exactly the shipped movement
    /// and plate/door rules (a closed door rejects a move through the same <c>TryMove</c> the game
    /// uses), and it never mutates the <see cref="LevelDefinition"/> it is given: a single board
    /// instance owned by the analyzer is reused, restored from immutable snapshots per node.
    ///
    /// States are keyed by the player cell plus the sorted box cells. That is complete because the
    /// derived door state depends only on plate occupancy, and plate occupancy depends only on the
    /// player and box cells, so two states with the same key have identical legal moves.
    /// </summary>
    public static class LevelAnalyzer
    {
        public const int DefaultMaxStates = 200000;

        public static readonly TimeSpan DefaultMaxTime = TimeSpan.FromSeconds(10);

        private static readonly Direction[] Directions =
        {
            Direction.Up, Direction.Down, Direction.Left, Direction.Right
        };

        /// <summary>
        /// One edge of the BFS parent forest: the state key a child was first reached from and the
        /// move that produced it. The map holds only strings and this small value, never a
        /// <see cref="BoardSnapshot"/>, so memory stays flat even for large searches.
        /// </summary>
        private struct ParentLink
        {
            public readonly string ParentKey;
            public readonly Direction Direction;

            public ParentLink(string parentKey, Direction direction)
            {
                ParentKey = parentKey;
                Direction = direction;
            }
        }

        /// <summary>
        /// Explores the reachable state space from the level's start until a goal is found, the space
        /// is exhausted, or a budget is reached. A budget hit can only yield
        /// <see cref="AnalysisVerdict.Inconclusive"/>, never <see cref="AnalysisVerdict.Unsolvable"/>.
        /// </summary>
        public static AnalysisResult Analyze(
            LevelDefinition level,
            int maxStates = DefaultMaxStates,
            TimeSpan? maxTime = null)
        {
            TimeSpan timeBudget = maxTime ?? DefaultMaxTime;
            var clock = Stopwatch.StartNew();

            if (level == null)
            {
                return new AnalysisResult
                {
                    Verdict = AnalysisVerdict.Inconclusive,
                    StatesExplored = 0,
                    ElapsedSeconds = clock.Elapsed.TotalSeconds,
                    Detail = "无法分析：缺少关卡定义。"
                };
            }

            Board board;
            try
            {
                board = new Board(level);
            }
            catch (ArgumentException exception)
            {
                // A malformed working copy (wrong player count, bad cell counts, ...) cannot be
                // searched. That is "no answer yet", never "unsolvable".
                return new AnalysisResult
                {
                    Verdict = AnalysisVerdict.Inconclusive,
                    StatesExplored = 0,
                    ElapsedSeconds = clock.Elapsed.TotalSeconds,
                    Detail = "无法分析：" + exception.Message
                };
            }

            // A level that is already complete has the trivial zero-move solution.
            if (board.IsComplete)
            {
                return new AnalysisResult
                {
                    Verdict = AnalysisVerdict.Solvable,
                    StatesExplored = 0,
                    ElapsedSeconds = clock.Elapsed.TotalSeconds,
                    SolutionMoves = 0,
                    SolutionPushes = 0,
                    Detail = "可解，0 步 / 0 次推箱（关卡初始即完成）。"
                };
            }

            var visited = new HashSet<string>();
            var frontier = new Queue<BoardSnapshot>();
            var parents = new Dictionary<string, ParentLink>();

            BoardSnapshot start = board.CreateSnapshot();
            string startKey = StateKey(start);
            visited.Add(startKey);
            frontier.Enqueue(start);

            int statesExplored = 0;

            // A zero (or negative) time budget is already spent before the first expansion, so it can
            // never be mistaken for an exhaustive search.
            bool noTimeBudget = timeBudget <= TimeSpan.Zero;

            while (frontier.Count > 0)
            {
                if (statesExplored >= maxStates || noTimeBudget || clock.Elapsed > timeBudget)
                {
                    return new AnalysisResult
                    {
                        Verdict = AnalysisVerdict.Inconclusive,
                        StatesExplored = statesExplored,
                        ElapsedSeconds = clock.Elapsed.TotalSeconds,
                        Detail =
                            $"未确定：搜索 {statesExplored} 个状态后达到预算。这不代表关卡无解。"
                    };
                }

                BoardSnapshot current = frontier.Dequeue();
                statesExplored++;
                string currentKey = StateKey(current);

                foreach (Direction direction in Directions)
                {
                    // The same board instance is reused for every child; restoring the parent
                    // snapshot first means a rejected move never leaks into the next direction.
                    board.RestoreSnapshot(current);
                    if (!board.TryMove(direction, out bool completed))
                    {
                        continue;
                    }

                    if (completed)
                    {
                        BoardSnapshot goalSnap = board.CreateSnapshot();
                        string goalKey = StateKey(goalSnap);
                        parents[goalKey] = new ParentLink(currentKey, direction);

                        var solution = new List<Direction>();
                        string cursor = goalKey;
                        while (cursor != startKey)
                        {
                            ParentLink link = parents[cursor];
                            solution.Add(link.Direction);
                            cursor = link.ParentKey;
                        }

                        solution.Reverse();

                        return new AnalysisResult
                        {
                            Verdict = AnalysisVerdict.Solvable,
                            StatesExplored = statesExplored,
                            ElapsedSeconds = clock.Elapsed.TotalSeconds,
                            SolutionMoves = board.MoveCount,
                            SolutionPushes = board.PushCount,
                            Solution = solution,
                            Detail = $"可解，{board.MoveCount} 步 / {board.PushCount} 次推箱。"
                        };
                    }

                    BoardSnapshot child = board.CreateSnapshot();
                    string childKey = StateKey(child);
                    if (visited.Add(childKey))
                    {
                        parents[childKey] = new ParentLink(currentKey, direction);
                        frontier.Enqueue(child);
                    }
                }
            }

            return new AnalysisResult
            {
                Verdict = AnalysisVerdict.Unsolvable,
                StatesExplored = statesExplored,
                ElapsedSeconds = clock.Elapsed.TotalSeconds,
                Detail = $"无解：已穷举状态空间（{statesExplored} 个状态）。"
            };
        }

        /// <summary>
        /// Collision-free key of a board state: the player cell plus the sorted box cells.
        /// <see cref="Board.CreateSnapshot"/> emits box indices in ascending dense-index order, so the
        /// key is deterministic for a given state.
        /// </summary>
        private static string StateKey(BoardSnapshot snapshot)
        {
            return snapshot.PlayerX + "," + snapshot.PlayerY + "|" +
                   string.Join(",", snapshot.BoxIndices);
        }
    }
}

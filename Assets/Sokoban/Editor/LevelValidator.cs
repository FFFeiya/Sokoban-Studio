using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Editor
{
    /// <summary>
    /// Authoring/playtest validation entry point for the Sokoban level rules, shared by the
    /// level editor and the one-click playtest so both agree on what "valid" means. It reports every
    /// problem it can find (with coordinates where applicable) and never throws for malformed input.
    /// <see cref="Board"/> independently enforces the runtime safety invariants when it is
    /// constructed, so malformed data that bypasses this validator still cannot enter gameplay.
    /// </summary>
    public static class LevelValidator
    {
        /// <summary>
        /// Validates <paramref name="level"/> and returns the list of <em>blocking</em> rule
        /// violations. The list is empty exactly when the level is a playable level. This is the
        /// error-only view used by Save and Playtest gating; advisory warnings are filtered out.
        /// </summary>
        public static List<string> Validate(LevelDefinition level)
        {
            return ValidateDetailed(level)
                .Where(issue => issue.Severity == LevelIssueSeverity.Error)
                .Select(issue => issue.Message)
                .ToList();
        }

        /// <summary>
        /// Validates <paramref name="level"/> and returns every issue it can find, tagged with a
        /// severity. Errors keep the exact wording and order of <see cref="Validate"/>; warnings are
        /// advisory designer hints (for example the static corner deadlock check) that never block
        /// Save or Playtest.
        /// </summary>
        public static List<LevelIssue> ValidateDetailed(LevelDefinition level)
        {
            var issues = new List<LevelIssue>();

            if (level == null)
            {
                issues.Add(LevelIssue.Error("关卡为空。"));
                return issues;
            }

            if (level.width <= 0 || level.height <= 0)
            {
                issues.Add(LevelIssue.Error($"宽度 ({level.width}) 与高度 ({level.height}) 必须都大于 0。"));
            }

            int expected = level.width * level.height;

            bool cellsValid = level.cells != null && level.cells.Count == expected;
            if (!cellsValid)
            {
                issues.Add(LevelIssue.Error($"格子数量 ({level.cells?.Count ?? 0}) 必须等于宽度 × 高度 ({expected})。"));
            }

            bool occupantsValid = level.occupants != null && level.occupants.Count == expected;
            if (!occupantsValid)
            {
                issues.Add(LevelIssue.Error($"占据者数量 ({level.occupants?.Count ?? 0}) 必须等于宽度 × 高度 ({expected})。"));
            }

            // The group list is optional (old assets read as implicit group A), but when present and
            // dense it must be parallel to cells/occupants. Legacy assets authored before the field
            // existed deserialize with a non-null, empty list rather than null (Unity materializes
            // serialized List fields), so empty is treated exactly like absent; only a non-empty list
            // with the wrong length is an error.
            bool groupIdsDense = level.groupIds != null && level.groupIds.Count == expected;
            bool groupIdsValid = groupIdsDense || level.groupIds == null || level.groupIds.Count == 0;
            if (!groupIdsValid)
            {
                issues.Add(LevelIssue.Error(
                    $"分组编号数量 ({level.groupIds?.Count ?? 0}) 必须等于宽度 × 高度 ({expected})，或为 null（默认 A 组）。"));
            }

            // Field checks below index both lists by coordinate, so only run them on a well-formed grid.
            if (level.width > 0 && level.height > 0 && cellsValid && occupantsValid)
            {
                int players = 0;
                int boxes = 0;
                int goals = 0;

                // Per-group first plate/door cell, used for the per-group advisory door/plate hints.
                var firstPlateByGroup = new Dictionary<int, int>();
                var firstDoorByGroup = new Dictionary<int, int>();

                for (int i = 0; i < expected; i++)
                {
                    int x = i % level.width;
                    int y = i / level.width;

                    if (level.cells[i] == TileType.Wall && level.occupants[i] != OccupantType.None)
                    {
                        issues.Add(LevelIssue.Error($"墙格上有占据者，位置 ({x}, {y})。", x, y));
                    }

                    if (level.cells[i] == TileType.Door && level.occupants[i] != OccupantType.None)
                    {
                        issues.Add(LevelIssue.Error($"门格上有占据者，位置 ({x}, {y})。", x, y));
                    }

                    // Group ids are meaningful only while the list is dense; an absent (or legacy
                    // empty) list is implicit group A and needs no per-cell check.
                    if (groupIdsDense)
                    {
                        int groupId = level.groupIds[i];
                        if (groupId < 0 || groupId > 1)
                        {
                            issues.Add(LevelIssue.Error(
                                $"非法的分组编号 {groupId}，位置 ({x}, {y})；分组编号必须为 0（A）或 1（B）。", x, y));
                        }
                    }

                    if (level.cells[i] == TileType.Goal)
                    {
                        goals++;
                    }
                    else if (level.cells[i] == TileType.Plate)
                    {
                        int plateGroup = level.GetGroupId(i);
                        if (!firstPlateByGroup.ContainsKey(plateGroup))
                        {
                            firstPlateByGroup[plateGroup] = i;
                        }
                    }
                    else if (level.cells[i] == TileType.Door)
                    {
                        int doorGroup = level.GetGroupId(i);
                        if (!firstDoorByGroup.ContainsKey(doorGroup))
                        {
                            firstDoorByGroup[doorGroup] = i;
                        }
                    }

                    if (level.occupants[i] == OccupantType.Player)
                    {
                        players++;
                    }
                    else if (level.occupants[i] == OccupantType.Box)
                    {
                        boxes++;
                    }
                }

                if (players != 1)
                {
                    issues.Add(LevelIssue.Error($"必须且只能有 1 个玩家（当前 {players} 个）。"));
                }

                if (boxes <= 0)
                {
                    issues.Add(LevelIssue.Error("至少需要一个箱子。"));
                }

                if (goals <= 0)
                {
                    issues.Add(LevelIssue.Error("至少需要一个目标。"));
                }

                if (boxes != goals)
                {
                    issues.Add(LevelIssue.Error($"箱子数量 ({boxes}) 必须等于目标数量 ({goals})。"));
                }

                // Static corner-deadlock hint: a box that is not already on a goal and has two
                // orthogonal neighbours that are each a wall or the level border can never be pushed
                // out of that corner again, so the level cannot be completed with the current layout.
                for (int i = 0; i < expected; i++)
                {
                    if (level.occupants[i] != OccupantType.Box || level.cells[i] == TileType.Goal)
                    {
                        continue;
                    }

                    int x = i % level.width;
                    int y = i / level.width;

                    bool up = IsWallOrOutside(level, x, y - 1);
                    bool down = IsWallOrOutside(level, x, y + 1);
                    bool left = IsWallOrOutside(level, x - 1, y);
                    bool right = IsWallOrOutside(level, x + 1, y);

                    if ((up && left) || (up && right) || (down && left) || (down && right))
                    {
                        issues.Add(LevelIssue.Warning(
                            $"非目标格 ({x}, {y}) 上的箱子位于墙角，形成静态死锁，无法被解救。",
                            x,
                            y));
                    }
                }

                // Advisory door/plate hints, per group: plates and doors only gate each other inside the
                // same group, so an unmatched door/plate group is flagged. These are designer hints
                // only: the level can still be solved, so the wording must never claim it is unsolvable.
                foreach (int group in firstDoorByGroup.Keys.OrderBy(g => g))
                {
                    if (firstPlateByGroup.ContainsKey(group))
                    {
                        continue;
                    }

                    int index = firstDoorByGroup[group];
                    int x = index % level.width;
                    int y = index / level.width;
                    issues.Add(LevelIssue.Warning(
                        $"{GroupLetter(group)} 组门位于 ({x}, {y})，没有对应的压力板，将保持开启（无压力板组）。",
                        x,
                        y));
                }

                foreach (int group in firstPlateByGroup.Keys.OrderBy(g => g))
                {
                    if (firstDoorByGroup.ContainsKey(group))
                    {
                        continue;
                    }

                    int index = firstPlateByGroup[group];
                    int x = index % level.width;
                    int y = index / level.width;
                    issues.Add(LevelIssue.Warning(
                        $"{GroupLetter(group)} 组压力板位于 ({x}, {y})，其组内没有门，无法开启任何门。",
                        x,
                        y));
                }
            }

            // The board model enforces the same invariants and additionally catches malformed states
            // the field checks above can miss. Report any failure as an error instead of crashing.
            try
            {
                var _ = new Board(level);
            }
            catch (Exception exception)
            {
                issues.Add(LevelIssue.Error("无法构建棋盘：" + exception.Message));
            }

            return issues;
        }

        /// <summary>
        /// Display letter for a group id in advisory messages: <c>0</c> is group A, <c>1</c> is group
        /// B, and any other value is rendered as its raw number.
        /// </summary>
        private static string GroupLetter(int groupId)
        {
            switch (groupId)
            {
                case 0:
                    return "A";
                case 1:
                    return "B";
                default:
                    return groupId.ToString();
            }
        }

        /// <summary>
        /// True when the coordinate is outside the level bounds or carries a wall tile, i.e. an
        /// orthogonal direction a box can never be pushed into.
        /// </summary>
        private static bool IsWallOrOutside(LevelDefinition level, int x, int y)
        {
            if (!level.InBounds(x, y))
            {
                return true;
            }

            return level.cells[level.Index(x, y)] == TileType.Wall;
        }
    }
}

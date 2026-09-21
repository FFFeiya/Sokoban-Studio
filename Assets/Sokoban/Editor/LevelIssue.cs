namespace Sokoban.Editor
{
    /// <summary>
    /// Severity of a <see cref="LevelIssue"/>. <see cref="Error"/> blocks Save/Playtest;
    /// <see cref="Warning"/> is advisory (designer guidance) and never blocks the workflow.
    /// </summary>
    public enum LevelIssueSeverity
    {
        Error,
        Warning
    }

    /// <summary>
    /// A single validator result. Errors were previously reported as plain strings; carrying the
    /// severity (and an optional originating cell for editor highlighting) keeps the error-only
    /// contract of <see cref="LevelValidator.Validate"/> intact while letting the editor surface
    /// advisory warnings such as the static corner deadlock hint.
    /// </summary>
    public sealed class LevelIssue
    {
        public LevelIssueSeverity Severity;
        public string Message;

        /// <summary>Originating cell, only meaningful when <see cref="HasCell"/> is true.</summary>
        public int CellX;
        public int CellY;
        public bool HasCell;

        public LevelIssue(
            LevelIssueSeverity severity, string message, int cellX = 0, int cellY = 0, bool hasCell = false)
        {
            Severity = severity;
            Message = message;
            CellX = cellX;
            CellY = cellY;
            HasCell = hasCell;
        }

        /// <summary>An issue that blocks Save/Playtest and carries no cell location.</summary>
        public static LevelIssue Error(string message)
        {
            return new LevelIssue(LevelIssueSeverity.Error, message);
        }

        /// <summary>
        /// An issue that blocks Save/Playtest and is bound to a grid coordinate so the editor can
        /// highlight and navigate to the offending cell. The message text is unchanged from the
        /// coordinate-free <see cref="Error(string)"/> form.
        /// </summary>
        public static LevelIssue Error(string message, int cellX, int cellY)
        {
            return new LevelIssue(LevelIssueSeverity.Error, message, cellX, cellY, true);
        }

        /// <summary>An advisory issue bound to a grid coordinate so the editor can highlight it.</summary>
        public static LevelIssue Warning(string message, int cellX, int cellY)
        {
            return new LevelIssue(LevelIssueSeverity.Warning, message, cellX, cellY, true);
        }
    }
}

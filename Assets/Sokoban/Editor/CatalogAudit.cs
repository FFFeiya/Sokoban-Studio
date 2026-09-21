using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    /// <summary>
    /// Content-health classification of one catalog slot. <see cref="Valid"/> and
    /// <see cref="Warnings"/> describe validator content only; solvability is deliberately a
    /// separate dimension reported through <see cref="CatalogAuditRow.Verdict"/> so an analyzer
    /// budget hit is never conflated with a content error.
    /// </summary>
    public enum CatalogAuditStatus
    {
        /// <summary>Validated with no errors and no warnings.</summary>
        Valid,

        /// <summary>Validated with no errors but at least one advisory warning.</summary>
        Warnings,

        /// <summary>Validator errors, a duplicate reference to an earlier slot, or both.</summary>
        Error,

        /// <summary>The slot holds a null entry, so there is no level to report.</summary>
        Missing,

        /// <summary>The slot reuses a <see cref="LevelDefinition"/> already listed in an earlier slot.</summary>
        Duplicate
    }

    /// <summary>
    /// One audited catalog slot. Counts are computed from the raw level data; issues come from
    /// <see cref="LevelValidator.ValidateDetailed"/> so the audit never re-implements a validator
    /// rule. <see cref="Verdict"/> is null ("n/a") whenever the entry is null or has validator
    /// errors, because the analyzer cannot meaningfully run on it.
    /// </summary>
    public sealed class CatalogAuditRow
    {
        /// <summary>Zero-based position of the entry in the catalog.</summary>
        public int Slot;

        /// <summary>Authored level name, or null when the slot is missing.</summary>
        public string LevelName;

        public int PlayerCount;
        public int BoxCount;
        public int GoalCount;

        public int ErrorCount;
        public int WarningCount;

        /// <summary>First issue message from the validator, or empty when there is none.</summary>
        public string FirstIssue;

        /// <summary>Analyzer verdict, or null when analysis did not run ("n/a").</summary>
        public AnalysisVerdict? Verdict;

        /// <summary>Shortest-move solution length; -1 unless <see cref="Verdict"/> is Solvable.</summary>
        public int SolutionMoves = -1;

        /// <summary>Shortest-solution push count; -1 unless <see cref="Verdict"/> is Solvable.</summary>
        public int SolutionPushes = -1;

        /// <summary>Unique states dequeued and expanded by the analyzer; 0 when analysis did not run.</summary>
        public int StatesExplored;

        /// <summary>Wall-clock seconds the analyzer spent; 0 when analysis did not run.</summary>
        public double ElapsedSeconds;

        /// <summary>
        /// True when a cached verdict was invalidated by an edit (see <see cref="CatalogAudit.ResetAnalysis"/>)
        /// rather than never having been computed, so the Dashboard can distinguish "stale, re-analyze"
        /// from "not analyzed". Transient, in-memory-only state: this row is a plain projection and is
        /// never serialized. A fresh analysis clears it.
        /// </summary>
        public bool IsStaleAnalysis;

        /// <summary>True when the level's cell layer contains at least one pressure plate.</summary>
        public bool HasPlates;

        /// <summary>True when the level's cell layer contains at least one door tile.</summary>
        public bool HasDoors;

        /// <summary>True when the level contains at least one door tile in group A (group id 0).</summary>
        public bool HasGroupADoor;

        /// <summary>True when the level contains at least one door tile in group B (group id 1).</summary>
        public bool HasGroupBDoor;

        /// <summary>
        /// Compact mechanics label for the Dashboard/audit columns: <c>Core</c> when the level uses no
        /// plate/door mechanic, <c>Plate</c> for a plate with no door, and otherwise the door-group
        /// summary <c>Door A</c>, <c>Door B</c> or <c>Door A+B</c>.
        /// </summary>
        public string MechanicsLabel
        {
            get
            {
                if (!HasPlates && !HasDoors)
                {
                    return "基础";
                }

                if (!HasDoors)
                {
                    return "压力板";
                }

                if (HasGroupADoor && HasGroupBDoor)
                {
                    return "门 A 组+B 组";
                }

                return HasGroupBDoor ? "门 B 组" : "门 A 组";
            }
        }

        public bool IsMissing;
        public bool IsDuplicate;

        /// <summary>
        /// Deterministic hash of the level content this row's verdict was computed from. Zero when the
        /// slot is missing; <see cref="CatalogAudit.IsStale"/> compares it against the level's current
        /// content so an edited-and-resaved level drops back to "not analyzed".
        /// </summary>
        public int ContentHash;

        public CatalogAuditStatus Status;

        /// <summary>True when the slot validated cleanly with no errors, warnings or duplicate flag.</summary>
        public bool IsValid => Status == CatalogAuditStatus.Valid;

        /// <summary>Analyzer verdict as a short label, or "n/a" when analysis did not run.</summary>
        public string VerdictText => Verdict.HasValue ? Verdict.Value.ToString() : "n/a";
    }

    /// <summary>Aggregate counts for a set of <see cref="CatalogAuditRow"/> values.</summary>
    public sealed class CatalogAuditSummary
    {
        public int Total;
        public int Valid;
        public int WithWarnings;
        public int Errors;
        public int Missing;
        public int Duplicates;
        public int Solvable;
        public int Unsolvable;
        public int Inconclusive;
    }

    /// <summary>
    /// Editor-only content-health audit of the shipped <see cref="LevelCatalog"/>. It reports one
    /// row per catalog slot (validator counts/issues plus the bounded analyzer's truthful verdict)
    /// and can print the table to the console or export it as Markdown under <c>Logs/</c>.
    ///
    /// The audit never mutates the catalog or any level asset: it only reads them and runs the
    /// existing validator/analyzer. It is split into pure functions (<see cref="Audit"/>,
    /// <see cref="Summarize"/>, <see cref="FormatConsole"/>, <see cref="FormatMarkdown"/>) so the
    /// logic is testable in EditMode without scraping the console.
    /// </summary>
    public static class CatalogAudit
    {
        /// <summary>Project-root-relative Markdown export path.</summary>
        public const string ExportPath = "Logs/Agent/CatalogAudit.md";

        /// <summary>State budget for the per-entry solvability analysis (matches the analyzer default).</summary>
        public const int AuditMaxStates = 200000;

        /// <summary>Wall-clock budget for the per-entry solvability analysis (matches the analyzer default).</summary>
        public static readonly TimeSpan AuditMaxTime = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Audits every slot of <paramref name="catalog"/>. A null catalog (or null entry list)
        /// yields an empty list rather than throwing; a null entry inside the list yields a
        /// <see cref="CatalogAuditStatus.Missing"/> row.
        /// </summary>
        public static List<CatalogAuditRow> Audit(LevelCatalog catalog)
        {
            List<CatalogAuditRow> rows = BuildValidationRows(catalog);

            for (int slot = 0; slot < rows.Count; slot++)
            {
                CatalogAuditRow row = rows[slot];
                LevelDefinition level = catalog.levels[slot];

                // The analyzer can only run on a non-null, validator-clean entry; anything else is
                // reported as "n/a" rather than forced into a verdict.
                if (level != null && row.ErrorCount == 0)
                {
                    ApplyAnalysis(row, LevelAnalyzer.Analyze(level, AuditMaxStates, AuditMaxTime));
                }
            }

            return rows;
        }

        /// <summary>
        /// Builds one validation-only row per catalog slot (the analyzer is never run). This is the
        /// cheap projection the Content Dashboard shows on open; <see cref="Audit"/> runs it first and
        /// then applies the analyzer per slot, so the validation rules, classification and counts live
        /// in exactly one place.
        /// </summary>
        public static List<CatalogAuditRow> BuildValidationRows(LevelCatalog catalog)
        {
            var rows = new List<CatalogAuditRow>();
            if (catalog == null || catalog.levels == null)
            {
                return rows;
            }

            var seen = new HashSet<LevelDefinition>();

            for (int slot = 0; slot < catalog.levels.Count; slot++)
            {
                LevelDefinition level = catalog.levels[slot];
                rows.Add(BuildValidationRow(slot, level, level != null && !seen.Add(level)));
            }

            return rows;
        }

        /// <summary>Builds a validation-only row for one slot; the analyzer is never run here.</summary>
        public static CatalogAuditRow BuildValidationRow(int slot, LevelDefinition level, bool isDuplicate)
        {
            List<LevelIssue> issues = LevelValidator.ValidateDetailed(level);

            var row = new CatalogAuditRow
            {
                Slot = slot,
                LevelName = level == null ? null : level.levelName,
                IsMissing = level == null,
                IsDuplicate = isDuplicate,
                ErrorCount = issues.Count(i => i.Severity == LevelIssueSeverity.Error),
                WarningCount = issues.Count(i => i.Severity == LevelIssueSeverity.Warning),
                FirstIssue = issues.Count > 0 ? issues[0].Message : string.Empty
            };

            if (level != null)
            {
                CountContents(level, out int players, out int boxes, out int goals);
                row.PlayerCount = players;
                row.BoxCount = boxes;
                row.GoalCount = goals;

                CountMechanics(level, out bool hasPlates, out bool hasDoors, out bool hasGroupADoor, out bool hasGroupBDoor);
                row.HasPlates = hasPlates;
                row.HasDoors = hasDoors;
                row.HasGroupADoor = hasGroupADoor;
                row.HasGroupBDoor = hasGroupBDoor;
            }

            row.Status = Classify(row);
            row.ContentHash = ContentHash(level);
            return row;
        }

        /// <summary>
        /// Deterministic, allocation-light content hash of a level's authored data (name, dimensions,
        /// tile cells, occupants). This is deliberately not <see cref="string.GetHashCode"/> based,
        /// because that value is not guaranteed stable across processes. A null level returns 0.
        /// </summary>
        public static int ContentHash(LevelDefinition level)
        {
            if (level == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = 17;

                if (level.levelName != null)
                {
                    for (int i = 0; i < level.levelName.Length; i++)
                    {
                        hash = hash * 31 + level.levelName[i];
                    }
                }

                hash = hash * 31 + level.width;
                hash = hash * 31 + level.height;

                if (level.cells != null)
                {
                    for (int i = 0; i < level.cells.Count; i++)
                    {
                        hash = hash * 31 + (byte)level.cells[i];
                    }
                }

                if (level.occupants != null)
                {
                    for (int i = 0; i < level.occupants.Count; i++)
                    {
                        hash = hash * 31 + (byte)level.occupants[i];
                    }
                }

                if (level.groupIds != null)
                {
                    for (int i = 0; i < level.groupIds.Count; i++)
                    {
                        hash = hash * 31 + level.groupIds[i];
                    }
                }

                return hash;
            }
        }

        /// <summary>
        /// Clears a row's cached analysis back to "not analyzed" (verdict n/a, no solution counts) and
        /// flags it as stale (<see cref="CatalogAuditRow.IsStaleAnalysis"/>) so the Dashboard can tell an
        /// invalidated verdict apart from one that was never computed. Validation-derived fields
        /// (<see cref="CatalogAuditRow.Status"/>, counts, issues) are left untouched; those refresh on
        /// the explicit "Validate All". Null-safe.
        /// </summary>
        public static void ResetAnalysis(CatalogAuditRow row)
        {
            if (row == null)
            {
                return;
            }

            row.Verdict = null;
            row.SolutionMoves = -1;
            row.SolutionPushes = -1;
            row.StatesExplored = 0;
            row.ElapsedSeconds = 0;
            row.IsStaleAnalysis = true;
        }

        /// <summary>
        /// True when <paramref name="row"/> holds a cached verdict that no longer matches the level's
        /// current content. Null-safe: a null row or level is never stale.
        /// </summary>
        public static bool IsStale(CatalogAuditRow row, LevelDefinition level)
        {
            return level != null && row != null && row.ContentHash != ContentHash(level);
        }

        /// <summary>
        /// Copies the analyzer's outcome onto a validation-only row. This is a pure pass-through so the
        /// dashboard and the audit share the exact same verdict/moves/pushes/states/time fields.
        /// </summary>
        public static void ApplyAnalysis(CatalogAuditRow row, AnalysisResult result)
        {
            if (row == null || result == null)
            {
                return;
            }

            row.Verdict = result.Verdict;
            row.SolutionMoves = result.SolutionMoves;
            row.SolutionPushes = result.SolutionPushes;
            row.StatesExplored = result.StatesExplored;
            row.ElapsedSeconds = result.ElapsedSeconds;
            row.IsStaleAnalysis = false;
        }

        /// <summary>Aggregates the footer counters for <paramref name="rows"/>.</summary>
        public static CatalogAuditSummary Summarize(IReadOnlyList<CatalogAuditRow> rows)
        {
            var summary = new CatalogAuditSummary();
            if (rows == null)
            {
                return summary;
            }

            foreach (CatalogAuditRow row in rows)
            {
                summary.Total++;

                if (row.IsMissing)
                {
                    summary.Missing++;
                }

                if (row.IsDuplicate)
                {
                    summary.Duplicates++;
                }

                switch (row.Status)
                {
                    case CatalogAuditStatus.Valid:
                        summary.Valid++;
                        break;
                    case CatalogAuditStatus.Warnings:
                        summary.WithWarnings++;
                        break;
                    case CatalogAuditStatus.Error:
                    case CatalogAuditStatus.Missing:
                    case CatalogAuditStatus.Duplicate:
                        summary.Errors++;
                        break;
                }

                if (row.Verdict == AnalysisVerdict.Solvable)
                {
                    summary.Solvable++;
                }
                else if (row.Verdict == AnalysisVerdict.Unsolvable)
                {
                    summary.Unsolvable++;
                }
                else if (row.Verdict == AnalysisVerdict.Inconclusive)
                {
                    summary.Inconclusive++;
                }
            }

            return summary;
        }

        /// <summary>Renders the audit as a plain-text table plus summary footer.</summary>
        public static string FormatConsole(LevelCatalog catalog, IReadOnlyList<CatalogAuditRow> rows)
        {
            var text = new StringBuilder();
            text.AppendLine("Catalog audit: " + CatalogDisplayName(catalog));

            if (rows == null || rows.Count == 0)
            {
                text.AppendLine("(no entries)");
            }
            else
            {
                foreach (CatalogAuditRow row in rows)
                {
                    text.AppendLine(
                        $"[{row.Slot}] {DisplayName(row)} | " +
                        $"{row.PlayerCount}P {row.BoxCount}B {row.GoalCount}G | " +
                        $"errors={row.ErrorCount} warnings={row.WarningCount} | " +
                        $"{row.Status} | {VerdictLabel(row)} | " +
                        $"{row.FirstIssue} | states={StatesText(row)} " +
                        $"time={TimeText(row)} mechanics={MechanicsText(row)}");
                }
            }

            text.Append(SummaryLine(Summarize(rows)));
            return text.ToString();
        }

        /// <summary>Renders the audit as a Markdown document (table plus summary footer).</summary>
        public static string FormatMarkdown(LevelCatalog catalog, IReadOnlyList<CatalogAuditRow> rows)
        {
            var text = new StringBuilder();
            text.AppendLine("# Catalog Content Audit");
            text.AppendLine();
            text.AppendLine("- Catalog: `" + CatalogDisplayName(catalog) + "`");
            text.AppendLine("- Generated (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine();
            text.AppendLine("| Slot | Level | Players | Boxes | Goals | Errors | Warnings | Status | Verdict | Moves | Pushes | First issue | States | Time (s) | Mechanics |");
            text.AppendLine("|---:|---|---:|---:|---:|---:|---:|---|---|---:|---:|---|---:|---:|---|");

            if (rows == null || rows.Count == 0)
            {
                text.AppendLine("| - | _(no entries)_ | - | - | - | - | - | - | - | - | - | - | - | - | - |");
            }
            else
            {
                foreach (CatalogAuditRow row in rows)
                {
                    text.AppendLine(
                        $"| {row.Slot} | {Escape(DisplayName(row))} | {row.PlayerCount} | {row.BoxCount} | " +
                        $"{row.GoalCount} | {row.ErrorCount} | {row.WarningCount} | {row.Status} | " +
                        $"{VerdictLabel(row)} | {MovesText(row)} | {PushesText(row)} | {Escape(row.FirstIssue)} | " +
                        $"{StatesText(row)} | {TimeText(row)} | {MechanicsText(row)} |");
                }
            }

            text.AppendLine();
            text.AppendLine(SummaryLine(Summarize(rows)));
            return text.ToString();
        }

        /// <summary>Absolute path of the Markdown export, anchored at the project root.</summary>
        public static string ExportMarkdownPath()
        {
            // Application.dataPath is <project>/Assets, so its parent is the project root (which is
            // also Directory.GetCurrentDirectory() in batch mode). Logs/ lives outside Assets, so the
            // export must use System.IO and never AssetDatabase.
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(projectRoot, ExportPath));
        }

        /// <summary>Loads the catalog asset the game actually ships, or null when it is missing.</summary>
        public static LevelCatalog LoadShippedCatalog()
        {
            return AssetDatabase.LoadAssetAtPath<LevelCatalog>(LevelAssetFactory.CatalogPath);
        }

        /// <summary>Menu entry: audits the shipped catalog and prints the table to the console.</summary>
        [MenuItem("Sokoban/Audit Catalog")]
        public static void AuditCatalogMenu()
        {
            LevelCatalog catalog = LoadShippedCatalog();
            if (catalog == null)
            {
                Debug.LogError("Catalog audit: catalog asset not found at " + LevelAssetFactory.CatalogPath);
                return;
            }

            List<CatalogAuditRow> rows = Audit(catalog);
            Debug.Log(FormatConsole(catalog, rows));
        }

        /// <summary>Menu entry: audits the shipped catalog and exports the table to <c>Logs/Agent/CatalogAudit.md</c>.</summary>
        [MenuItem("Sokoban/Audit Catalog (Export Markdown)")]
        public static void ExportMarkdownMenu()
        {
            LevelCatalog catalog = LoadShippedCatalog();
            if (catalog == null)
            {
                Debug.LogError("Catalog audit: catalog asset not found at " + LevelAssetFactory.CatalogPath);
                return;
            }

            List<CatalogAuditRow> rows = Audit(catalog);
            string path = ExportMarkdownPath();

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, FormatMarkdown(catalog, rows));
            AssetDatabase.Refresh();
            Debug.Log("Catalog audit exported to " + path);
        }

        private static CatalogAuditStatus Classify(CatalogAuditRow row)
        {
            if (row.IsMissing)
            {
                return CatalogAuditStatus.Missing;
            }

            if (row.IsDuplicate)
            {
                return CatalogAuditStatus.Duplicate;
            }

            if (row.ErrorCount > 0)
            {
                return CatalogAuditStatus.Error;
            }

            return row.WarningCount > 0 ? CatalogAuditStatus.Warnings : CatalogAuditStatus.Valid;
        }

        /// <summary>
        /// Counts players, boxes and goals directly from the level data. Counts a malformed grid as
        /// zero rather than throwing; the validator reports the malformed grid separately.
        /// </summary>
        private static void CountContents(LevelDefinition level, out int players, out int boxes, out int goals)
        {
            players = 0;
            boxes = 0;
            goals = 0;

            if (level.cells != null)
            {
                foreach (TileType tile in level.cells)
                {
                    if (tile == TileType.Goal)
                    {
                        goals++;
                    }
                }
            }

            if (level.occupants == null)
            {
                return;
            }

            foreach (OccupantType occupant in level.occupants)
            {
                if (occupant == OccupantType.Player)
                {
                    players++;
                }
                else if (occupant == OccupantType.Box)
                {
                    boxes++;
                }
            }
        }

        /// <summary>
        /// Derives the plate/door presence flags directly from the level's cell layer (same
        /// direct-count style as <see cref="CountContents"/>). Door presence is also split by group id
        /// (0=A, 1=B) through the defensive <see cref="LevelDefinition.GetGroupId"/> so a door with a
        /// null/short/out-of-range group list still reads as group A. Null cell data yields all false.
        /// </summary>
        private static void CountMechanics(
            LevelDefinition level,
            out bool hasPlates,
            out bool hasDoors,
            out bool hasGroupADoor,
            out bool hasGroupBDoor)
        {
            hasPlates = false;
            hasDoors = false;
            hasGroupADoor = false;
            hasGroupBDoor = false;

            if (level.cells == null)
            {
                return;
            }

            for (int i = 0; i < level.cells.Count; i++)
            {
                TileType tile = level.cells[i];
                if (tile == TileType.Plate)
                {
                    hasPlates = true;
                }
                else if (tile == TileType.Door)
                {
                    hasDoors = true;
                    if (level.GetGroupId(i) == 1)
                    {
                        hasGroupBDoor = true;
                    }
                    else
                    {
                        hasGroupADoor = true;
                    }
                }
            }
        }

        private static string CatalogDisplayName(LevelCatalog catalog)
        {
            return catalog == null ? "(null catalog)" : LevelAssetFactory.CatalogPath;
        }

        private static string DisplayName(CatalogAuditRow row)
        {
            if (row.IsMissing)
            {
                return "<missing>";
            }

            return string.IsNullOrEmpty(row.LevelName) ? "<unnamed>" : row.LevelName;
        }

        private static string VerdictLabel(CatalogAuditRow row)
        {
            if (!row.Verdict.HasValue)
            {
                return "n/a";
            }

            if (row.Verdict.Value == AnalysisVerdict.Solvable)
            {
                return $"Solvable ({row.SolutionMoves}m/{row.SolutionPushes}p)";
            }

            return row.Verdict.Value.ToString();
        }

        private static string MovesText(CatalogAuditRow row)
        {
            return row.Verdict == AnalysisVerdict.Solvable ? row.SolutionMoves.ToString() : "n/a";
        }

        private static string PushesText(CatalogAuditRow row)
        {
            return row.Verdict == AnalysisVerdict.Solvable ? row.SolutionPushes.ToString() : "n/a";
        }

        private static string StatesText(CatalogAuditRow row)
        {
            return row.Verdict.HasValue ? row.StatesExplored.ToString() : "n/a";
        }

        private static string TimeText(CatalogAuditRow row)
        {
            return row.Verdict.HasValue
                ? row.ElapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                : "n/a";
        }

        private static string MechanicsText(CatalogAuditRow row)
        {
            return row.MechanicsLabel;
        }

        private static string SummaryLine(CatalogAuditSummary summary)
        {
            return
                $"Summary: entries={summary.Total} valid={summary.Valid} " +
                $"with-warnings={summary.WithWarnings} errors={summary.Errors} " +
                $"missing={summary.Missing} duplicates={summary.Duplicates} " +
                $"solvable={summary.Solvable} unsolvable={summary.Unsolvable} inconclusive={summary.Inconclusive}";
        }

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("|", "\\|");
        }
    }
}

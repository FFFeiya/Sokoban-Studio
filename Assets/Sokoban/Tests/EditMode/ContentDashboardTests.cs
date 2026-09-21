using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the T3 Content Dashboard projection headlessly: the window's validation-only row
    /// projection is slot-for-slot identical to <see cref="CatalogAudit.Audit"/> for every
    /// validation-derived field (proving no parallel audit logic), the additive audit row fields carry
    /// truthful analyzer search counts and mechanics flags derived from the shipped cells, and the
    /// Markdown formatter's additive States/Time/Mechanics columns sit beside the original columns.
    ///
    /// All levels come from the shipped catalog via <see cref="CatalogAudit.LoadShippedCatalog"/>, so
    /// no in-memory ScriptableObject is created and no shared asset is ever destroyed.
    /// </summary>
    public class ContentDashboardTests
    {
        [Test]
        public void Dashboard_Projection_MatchesAuditSlotBySlot()
        {
            LevelCatalog catalog = CatalogAudit.LoadShippedCatalog();
            Assert.IsNotNull(catalog, "The shipped catalog is required for this test.");
            Assert.IsNotEmpty(catalog.levels, "The shipped catalog must have at least one entry.");

            List<CatalogAuditRow> auditRows = CatalogAudit.Audit(catalog);
            List<CatalogAuditRow> projection = ContentDashboardWindow.BuildValidationRows(catalog);

            Assert.AreEqual(auditRows.Count, projection.Count, "One projected row per audit slot.");

            for (int i = 0; i < auditRows.Count; i++)
            {
                CatalogAuditRow expected = auditRows[i];
                CatalogAuditRow actual = projection[i];

                // Validation-derived fields must match the audit exactly (no parallel audit logic).
                Assert.AreEqual(expected.LevelName, actual.LevelName, $"Slot {i}: LevelName mismatch.");
                Assert.AreEqual(expected.PlayerCount, actual.PlayerCount, $"Slot {i}: PlayerCount mismatch.");
                Assert.AreEqual(expected.BoxCount, actual.BoxCount, $"Slot {i}: BoxCount mismatch.");
                Assert.AreEqual(expected.GoalCount, actual.GoalCount, $"Slot {i}: GoalCount mismatch.");
                Assert.AreEqual(expected.ErrorCount, actual.ErrorCount, $"Slot {i}: ErrorCount mismatch.");
                Assert.AreEqual(expected.WarningCount, actual.WarningCount, $"Slot {i}: WarningCount mismatch.");
                Assert.AreEqual(expected.Status, actual.Status, $"Slot {i}: Status mismatch.");
                Assert.AreEqual(expected.HasPlates, actual.HasPlates, $"Slot {i}: HasPlates mismatch.");
                Assert.AreEqual(expected.HasDoors, actual.HasDoors, $"Slot {i}: HasDoors mismatch.");
                Assert.AreEqual(expected.HasGroupADoor, actual.HasGroupADoor, $"Slot {i}: HasGroupADoor mismatch.");
                Assert.AreEqual(expected.HasGroupBDoor, actual.HasGroupBDoor, $"Slot {i}: HasGroupBDoor mismatch.");
                Assert.AreEqual(expected.MechanicsLabel, actual.MechanicsLabel, $"Slot {i}: MechanicsLabel mismatch.");

                // The validation-only projection must never run (or leak) the analyzer result.
                Assert.IsNull(actual.Verdict, $"Slot {i}: validation-only row must have no verdict.");
                Assert.AreEqual(-1, actual.SolutionMoves, $"Slot {i}: validation-only row has no solution moves.");
                Assert.AreEqual(-1, actual.SolutionPushes, $"Slot {i}: validation-only row has no solution pushes.");
                Assert.AreEqual(0, actual.StatesExplored, $"Slot {i}: validation-only row has no explored states.");
                Assert.AreEqual(0d, actual.ElapsedSeconds, $"Slot {i}: validation-only row has no elapsed time.");

                // Then apply the audit's own analysis outcome through the dashboard's pass-through
                // helper and assert the analysis fields match (proving ApplyAnalysis is faithful).
                if (expected.Verdict.HasValue)
                {
                    ContentDashboardWindow.ApplyAnalysis(actual, ResultFromRow(expected));

                    Assert.AreEqual(expected.Verdict, actual.Verdict, $"Slot {i}: Verdict mismatch.");
                    Assert.AreEqual(expected.SolutionMoves, actual.SolutionMoves, $"Slot {i}: SolutionMoves mismatch.");
                    Assert.AreEqual(expected.SolutionPushes, actual.SolutionPushes, $"Slot {i}: SolutionPushes mismatch.");
                    Assert.AreEqual(expected.StatesExplored, actual.StatesExplored, $"Slot {i}: StatesExplored mismatch.");
                    Assert.AreEqual(expected.ElapsedSeconds, actual.ElapsedSeconds, $"Slot {i}: ElapsedSeconds mismatch.");
                }
            }
        }

        [Test]
        public void Audit_AdditiveRowFields_SolvableRowsCarrySearchCountsAndMechanicsMatchCells()
        {
            LevelCatalog catalog = CatalogAudit.LoadShippedCatalog();
            Assert.IsNotNull(catalog, "The shipped catalog is required for this test.");

            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);

            foreach (CatalogAuditRow row in rows)
            {
                if (row.Verdict == AnalysisVerdict.Solvable)
                {
                    Assert.Greater(row.StatesExplored, 0, $"Slot {row.Slot} ({row.LevelName}) must have explored states.");
                    Assert.GreaterOrEqual(row.ElapsedSeconds, 0d, $"Slot {row.Slot} ({row.LevelName}) elapsed time must be non-negative.");
                }

                // Mechanics flags must equal what the level's own cell data says (never hardcoded).
                LevelDefinition level = catalog.levels[row.Slot];
                bool expectedPlates = level != null && level.cells != null && level.cells.Contains(TileType.Plate);
                bool expectedDoors = level != null && level.cells != null && level.cells.Contains(TileType.Door);

                Assert.AreEqual(expectedPlates, row.HasPlates, $"Slot {row.Slot} ({row.LevelName}): HasPlates must match the cell data.");
                Assert.AreEqual(expectedDoors, row.HasDoors, $"Slot {row.Slot} ({row.LevelName}): HasDoors must match the cell data.");
            }

            // Lock the shipped content contract: Levels 05/06 use the plate+door mechanic, 01-04 do not.
            CatalogAuditRow level05 = RowNamed(rows, "压力板与门");
            CatalogAuditRow level06 = RowNamed(rows, "持续触发");
            Assert.IsTrue(level05.HasPlates && level05.HasDoors, "Level 05 must use plate + door.");
            Assert.IsTrue(level06.HasPlates && level06.HasDoors, "Level 06 must use plate + door.");

            Assert.IsTrue(level05.HasGroupADoor, "Level 05's doors are group A.");
            Assert.IsFalse(level05.HasGroupBDoor, "Level 05 has no group-B door.");
            Assert.AreEqual("门 A 组", level05.MechanicsLabel, "第 05 关必须标注 门 A 组。");
            Assert.IsTrue(level06.HasGroupADoor, "Level 06's doors are group A.");
            Assert.IsFalse(level06.HasGroupBDoor, "Level 06 has no group-B door.");
            Assert.AreEqual("门 A 组", level06.MechanicsLabel, "第 06 关必须标注 门 A 组。");

            foreach (string name in new[] { "基础推动", "墙角陷阱", "推箱顺序", "空间规划" })
            {
                CatalogAuditRow row = RowNamed(rows, name);
                Assert.IsFalse(row.HasPlates, name + " must have no plates.");
                Assert.IsFalse(row.HasDoors, name + " must have no doors.");
                Assert.IsFalse(row.HasGroupADoor, name + " must have no group-A door.");
                Assert.IsFalse(row.HasGroupBDoor, name + " must have no group-B door.");
                Assert.AreEqual("基础", row.MechanicsLabel, name + " 必须标注 基础。");
            }
        }

        [Test]
        public void Format_AdditiveColumns_AppearInMarkdownAndConsole()
        {
            LevelCatalog catalog = CatalogAudit.LoadShippedCatalog();
            Assert.IsNotNull(catalog, "The shipped catalog is required for this test.");

            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);

            string markdown = CatalogAudit.FormatMarkdown(catalog, rows);
            string console = CatalogAudit.FormatConsole(catalog, rows);

            // New additive columns.
            StringAssert.Contains("States", markdown, "Markdown must carry the States column.");
            StringAssert.Contains("Time (s)", markdown, "Markdown must carry the Time (s) column.");
            StringAssert.Contains("Mechanics", markdown, "Markdown must carry the Mechanics column.");
            StringAssert.Contains("states=", console, "Console rows must carry the states field.");
            StringAssert.Contains("time=", console, "Console rows must carry the time field.");
            StringAssert.Contains("mechanics=", console, "Console rows must carry the mechanics field.");

            // Original columns/order must still be present.
            StringAssert.Contains("| Slot |", markdown);
            StringAssert.Contains("| Level |", markdown);
            StringAssert.Contains("| Players |", markdown);
            StringAssert.Contains("| Boxes |", markdown);
            StringAssert.Contains("| Goals |", markdown);
            StringAssert.Contains("| Errors |", markdown);
            StringAssert.Contains("| Warnings |", markdown);
            StringAssert.Contains("| Status |", markdown);
            StringAssert.Contains("| Verdict |", markdown);
            StringAssert.Contains("| Moves |", markdown);
            StringAssert.Contains("| Pushes |", markdown);
            StringAssert.Contains("| First issue |", markdown);
        }

        private static AnalysisResult ResultFromRow(CatalogAuditRow row)
        {
            return new AnalysisResult
            {
                Verdict = row.Verdict.Value,
                SolutionMoves = row.SolutionMoves,
                SolutionPushes = row.SolutionPushes,
                StatesExplored = row.StatesExplored,
                ElapsedSeconds = row.ElapsedSeconds
            };
        }

        private static CatalogAuditRow RowNamed(List<CatalogAuditRow> rows, string levelName)
        {
            foreach (CatalogAuditRow row in rows)
            {
                if (row.LevelName == levelName)
                {
                    return row;
                }
            }

            Assert.Fail("No shipped row named " + levelName + ".");
            return null;
        }
    }
}

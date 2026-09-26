using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the catalog content-health audit: a well-formed catalog produces VALID rows with
    /// correct counts and truthful analyzer verdicts, null/duplicate/box-goal-mismatch slots are each
    /// flagged on the row that owns the defect, an empty catalog is handled without throwing, and the
    /// console/Markdown renderers carry every slot plus the summary footer.
    ///
    /// Fixture catalogs are built in memory with <see cref="ScriptableObject.CreateInstance"/> so no
    /// test asset is ever written to disk.
    /// </summary>
    public class CatalogAuditTests
    {
        /// <summary>Builds a valid one-move level: player (1, 1), box (2, 1), goal (3, 1).</summary>
        private static LevelDefinition ValidLevel(string name)
        {
            return LevelAssetFactory.BuildLevel(name, new[] { "#####", "#PBG#", "#####" });
        }

        /// <summary>Builds a second valid one-push level: player (1, 1), box (3, 1), goal (4, 1).</summary>
        private static LevelDefinition ValidLevelTwo(string name)
        {
            return LevelAssetFactory.BuildLevel(name, new[] { "######", "#P.BG#", "######" });
        }

        private static LevelCatalog MakeCatalog(params LevelDefinition[] levels)
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.name = "CatalogAuditFixture";
            catalog.levels = new List<LevelDefinition>(levels);
            return catalog;
        }

        [Test]
        public void Audit_WellFormedCatalog_MarksAllRowsValidWithCountsAndSolvableVerdicts()
        {
            LevelCatalog catalog = MakeCatalog(ValidLevel("A"), ValidLevelTwo("B"));

            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);

            Assert.AreEqual(2, rows.Count, "One row per catalog slot is required.");
            foreach (CatalogAuditRow row in rows)
            {
                Assert.AreEqual(CatalogAuditStatus.Valid, row.Status, "A clean level must be VALID.");
                Assert.IsTrue(row.IsValid, "A VALID row must report IsValid.");
                Assert.IsFalse(row.IsMissing, "A present entry must not be marked missing.");
                Assert.IsFalse(row.IsDuplicate, "A unique entry must not be marked duplicate.");
                Assert.AreEqual(0, row.ErrorCount, "A clean level must have no errors.");
                Assert.AreEqual(0, row.WarningCount, "A clean level must have no warnings.");
                Assert.AreEqual(1, row.PlayerCount, "Both fixture levels have exactly one player.");
                Assert.AreEqual(1, row.BoxCount, "Both fixture levels have exactly one box.");
                Assert.AreEqual(1, row.GoalCount, "Both fixture levels have exactly one goal.");
                Assert.AreEqual(AnalysisVerdict.Solvable, row.Verdict, "Both fixture levels are solvable.");
                Assert.AreEqual(1, row.SolutionPushes, "Each fixture level is solved by a single push.");
            }

            // Slot 0 ("#PBG#") pushes immediately: 1 move / 1 push. Slot 1 ("#P.BG#") needs one step
            // to reach the pushing side first, so its shortest solution is 2 moves / 1 push.
            Assert.AreEqual(1, rows[0].SolutionMoves, "The adjacent box is one push away.");
            Assert.AreEqual(2, rows[1].SolutionMoves, "The box one cell over needs one step then a push.");

            CatalogAuditSummary summary = CatalogAudit.Summarize(rows);
            Assert.AreEqual(2, summary.Total);
            Assert.AreEqual(2, summary.Valid);
            Assert.AreEqual(0, summary.Errors);
            Assert.AreEqual(0, summary.Unsolvable);
            Assert.AreEqual(0, summary.Inconclusive);
        }

        [Test]
        public void Audit_NullEntry_IsFlaggedMissingWithoutAnalysis()
        {
            LevelCatalog catalog = MakeCatalog(ValidLevel("A"), null);

            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);

            Assert.AreEqual(2, rows.Count);
            CatalogAuditRow row = rows[1];
            Assert.IsTrue(row.IsMissing, "A null slot must be flagged missing.");
            Assert.AreEqual(CatalogAuditStatus.Missing, row.Status);
            Assert.IsNull(row.LevelName, "A missing slot has no level name.");
            Assert.Greater(row.ErrorCount, 0, "The validator reports the null entry as an error.");
            Assert.IsNull(row.Verdict, "Analysis cannot run on a missing entry, so the verdict is n/a.");
            Assert.AreEqual("n/a", row.VerdictText);
            Assert.AreEqual(0, row.PlayerCount, "A missing entry has no counts.");
        }

        [Test]
        public void Audit_DuplicateReference_IsFlaggedOnTheRepeatedSlot()
        {
            LevelDefinition shared = ValidLevel("A");
            LevelCatalog catalog = MakeCatalog(shared, shared);

            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);

            Assert.IsFalse(rows[0].IsDuplicate, "The first occurrence is not a duplicate.");
            Assert.AreEqual(CatalogAuditStatus.Valid, rows[0].Status);
            Assert.IsTrue(rows[1].IsDuplicate, "The second occurrence must be flagged as a duplicate.");
            Assert.AreEqual(CatalogAuditStatus.Duplicate, rows[1].Status);
            Assert.AreSame(
                rows[0].LevelName,
                rows[1].LevelName,
                "Both slots reference the same level asset.");

            CatalogAuditSummary summary = CatalogAudit.Summarize(rows);
            Assert.AreEqual(1, summary.Duplicates, "The duplicate must be counted in the summary.");
        }

        [Test]
        public void Audit_BoxGoalMismatch_ReportsValidatorErrorAndSkipsAnalysis()
        {
            // One box, zero goals: the validator reports the goal shortage and the count mismatch.
            LevelDefinition broken = LevelAssetFactory.BuildLevel("Broken", new[] { "#####", "#PB.#", "#####" });
            LevelCatalog catalog = MakeCatalog(broken);

            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);

            CatalogAuditRow row = rows[0];
            Assert.Greater(row.ErrorCount, 0, "The box/goal mismatch must surface as a validator error.");
            Assert.AreEqual(CatalogAuditStatus.Error, row.Status);
            Assert.IsFalse(row.IsValid, "An entry with validator errors is not VALID.");
            Assert.IsNotEmpty(row.FirstIssue, "The first validator issue message must be captured.");
            Assert.IsNull(row.Verdict, "Analysis cannot run on an invalid entry, so the verdict is n/a.");
            Assert.AreEqual("n/a", row.VerdictText);
            Assert.AreEqual(0, row.GoalCount);
            Assert.AreEqual(1, row.BoxCount);
        }

        [Test]
        public void Audit_EmptyCatalog_YieldsNoRowsAndZeroSummary()
        {
            LevelCatalog catalog = MakeCatalog();

            List<CatalogAuditRow> rows = null;
            Assert.DoesNotThrow(() => rows = CatalogAudit.Audit(catalog), "An empty catalog must not throw.");
            Assert.IsEmpty(rows, "An empty catalog has no slots.");

            CatalogAuditSummary summary = CatalogAudit.Summarize(rows);
            Assert.AreEqual(0, summary.Total);
            Assert.AreEqual(0, summary.Valid);
            Assert.AreEqual(0, summary.Errors);

            Assert.DoesNotThrow(() => CatalogAudit.FormatConsole(catalog, rows));
            Assert.DoesNotThrow(() => CatalogAudit.FormatMarkdown(catalog, rows));
        }

        [Test]
        public void Audit_UnsolvableLevel_ReportsUnsolvableVerdictTruthfully()
        {
            // Box (1, 1) is wedged against two walls and can never reach the goal at (3, 1). The level
            // is validator-clean (one player, one box, one goal), so only the analyzer can catch it.
            LevelDefinition unsolvable = LevelAssetFactory.BuildLevel(
                "Unsolvable",
                new[] { "#####", "#B.G#", "#.P.#", "#####" });
            LevelCatalog catalog = MakeCatalog(unsolvable);

            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);

            CatalogAuditRow row = rows[0];
            Assert.AreEqual(AnalysisVerdict.Unsolvable, row.Verdict, "The exhaustive verdict must be reported as-is.");
            Assert.AreEqual("Unsolvable", row.VerdictText, "An unsolvable verdict must not be relabelled.");
            Assert.AreEqual(-1, row.SolutionMoves, "An unsolvable level has no solution length.");

            CatalogAuditSummary summary = CatalogAudit.Summarize(rows);
            Assert.AreEqual(1, summary.Unsolvable, "The summary must count the unsolvable entry.");
        }

        [Test]
        public void Format_EmittedText_ContainsEverySlotAndSummaryFooter()
        {
            LevelCatalog catalog = MakeCatalog(ValidLevel("A"), null);

            List<CatalogAuditRow> rows = CatalogAudit.Audit(catalog);
            string console = CatalogAudit.FormatConsole(catalog, rows);
            string markdown = CatalogAudit.FormatMarkdown(catalog, rows);

            StringAssert.Contains("[0]", console, "The console table needs a line per slot.");
            StringAssert.Contains("[1]", console, "The console table needs a line per slot.");
            StringAssert.Contains("<missing>", console, "A missing slot needs a visible marker.");
            StringAssert.Contains("Summary: entries=2", console, "The console footer must summarize the rows.");

            StringAssert.Contains("| Slot |", markdown, "The Markdown export must be a table.");
            StringAssert.Contains("| 0 |", markdown, "The Markdown table must carry slot 0.");
            StringAssert.Contains("| 1 |", markdown, "The Markdown table must carry slot 1.");
            StringAssert.Contains("Summary: entries=2", markdown, "The Markdown footer must summarize the rows.");

            Assert.AreEqual(
                "Logs/CatalogAudit.md",
                CatalogAudit.ExportPath,
                "The export path must stay under Logs/ outside Assets.");
        }

        [Test]
        public void MechanicsLabel_ReflectsEachGroupCombination()
        {
            var row = new CatalogAuditRow();
            Assert.AreEqual("基础", row.MechanicsLabel, "无压力板/门时必须显示 基础。");

            row.HasPlates = true;
            Assert.AreEqual("压力板", row.MechanicsLabel, "有压力板无门时必须显示 压力板。");

            row.HasDoors = true;
            row.HasGroupADoor = true;
            Assert.AreEqual("门 A 组", row.MechanicsLabel, "A 组门必须显示 门 A 组。");

            row.HasGroupBDoor = true;
            Assert.AreEqual("门 A 组+B 组", row.MechanicsLabel, "两组都有门时必须显示 门 A 组+B 组。");

            row.HasGroupADoor = false;
            Assert.AreEqual("门 B 组", row.MechanicsLabel, "仅 B 组门时必须显示 门 B 组。");
        }

        [Test]
        public void ContentHash_ReflectsContentChanges()
        {
            LevelDefinition first = null;
            LevelDefinition second = null;
            LevelDefinition mutated = null;

            try
            {
                first = ValidLevel("A");
                second = ValidLevel("A");
                mutated = ValidLevel("A");

                Assert.AreEqual(
                    CatalogAudit.ContentHash(first),
                    CatalogAudit.ContentHash(second),
                    "Two structurally identical levels must hash equal.");

                Assert.AreNotEqual(0, CatalogAudit.ContentHash(first), "A present level must not hash to the null sentinel.");

                int before = CatalogAudit.ContentHash(mutated);
                mutated.cells[7] = TileType.Wall;
                Assert.AreNotEqual(
                    before,
                    CatalogAudit.ContentHash(mutated),
                    "Changing a cell must change the content hash.");

                int beforeGroup = CatalogAudit.ContentHash(mutated);
                mutated.groupIds[7] = 1;
                Assert.AreNotEqual(
                    beforeGroup,
                    CatalogAudit.ContentHash(mutated),
                    "Changing a group id must change the content hash.");
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(mutated);
            }
        }

        [Test]
        public void Summarize_CountsSolvable()
        {
            var rows = new List<CatalogAuditRow>
            {
                new CatalogAuditRow { Verdict = AnalysisVerdict.Solvable },
                new CatalogAuditRow { Verdict = AnalysisVerdict.Solvable },
                new CatalogAuditRow { Verdict = AnalysisVerdict.Unsolvable },
                new CatalogAuditRow { Verdict = AnalysisVerdict.Inconclusive },
                new CatalogAuditRow()
            };

            CatalogAuditSummary summary = CatalogAudit.Summarize(rows);

            Assert.AreEqual(5, summary.Total, "Every row must be counted.");
            Assert.AreEqual(2, summary.Solvable, "Exactly the Solvable rows must be counted.");
            Assert.AreEqual(1, summary.Unsolvable, "Exactly the Unsolvable rows must be counted.");
            Assert.AreEqual(1, summary.Inconclusive, "Exactly the Inconclusive rows must be counted.");
        }

        [Test]
        public void ResetAnalysis_ClearsVerdictAndCounts()
        {
            var row = new CatalogAuditRow
            {
                Verdict = AnalysisVerdict.Solvable,
                SolutionMoves = 12,
                SolutionPushes = 5,
                StatesExplored = 42,
                ElapsedSeconds = 1.5d
            };

            CatalogAudit.ResetAnalysis(row);

            Assert.IsNull(row.Verdict, "Reset must drop the cached verdict back to n/a.");
            Assert.AreEqual(-1, row.SolutionMoves, "Reset must clear the solution move count.");
            Assert.AreEqual(-1, row.SolutionPushes, "Reset must clear the solution push count.");
            Assert.AreEqual(0, row.StatesExplored, "Reset must clear the explored-state count.");
            Assert.AreEqual(0d, row.ElapsedSeconds, "Reset must clear the elapsed time.");
        }

        [Test]
        public void ResetAnalysis_MarksRowStaleSoAnEditIsNotMistakenForNeverAnalyzed()
        {
            var row = new CatalogAuditRow { Verdict = AnalysisVerdict.Solvable };
            Assert.IsFalse(row.IsStaleAnalysis, "A fresh row must not start out stale.");

            CatalogAudit.ResetAnalysis(row);

            Assert.IsTrue(
                row.IsStaleAnalysis,
                "Resetting an invalidated verdict must flag the row stale, so the Dashboard can tell it " +
                "apart from a row that was never analyzed.");
        }

        [Test]
        public void ApplyAnalysis_ClearsTheStaleMarker()
        {
            var row = new CatalogAuditRow();
            CatalogAudit.ResetAnalysis(row);
            Assert.IsTrue(row.IsStaleAnalysis, "Precondition: a reset row is stale.");
            Assert.IsFalse(row.Verdict.HasValue, "Precondition: a reset row carries no verdict.");

            CatalogAudit.ApplyAnalysis(
                row,
                new AnalysisResult
                {
                    Verdict = AnalysisVerdict.Solvable,
                    SolutionMoves = 4,
                    SolutionPushes = 2,
                    StatesExplored = 9,
                    ElapsedSeconds = 0.25d
                });

            Assert.IsFalse(row.IsStaleAnalysis, "A fresh analysis must clear the stale marker.");
            Assert.AreEqual(AnalysisVerdict.Solvable, row.Verdict, "The fresh verdict must be applied.");
            Assert.AreEqual(4, row.SolutionMoves, "The fresh solution move count must be applied.");
        }
    }
}

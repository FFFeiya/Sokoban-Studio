using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// T4 — Validation issue navigation (pure validator data). The window's click-to-focus and
    /// scroll-to-cell behaviour is IMGUI rendering and is therefore verified at the manual gate only
    /// (batchmode cannot render it). These EditMode tests pin the data the window consumes: the
    /// occupant-on-wall error, the occupant-on-door error and the corner-deadlock warning must each
    /// carry the exact originating cell, while <see cref="LevelValidator.Validate"/> (the error-only
    /// string list used by Save/Playtest gating) keeps returning the same message strings.
    /// </summary>
    public class ValidationNavigationTests
    {
        [Test]
        public void OccupantOnWall_ErrorCarriesExactCell()
        {
            LevelDefinition def = null;
            try
            {
                def = LevelAssetFactory.BuildLevel("NavWall", new[] { "#####", "#P.G#", "#####" });
                def.occupants[def.Index(0, 0)] = OccupantType.Box;

                LevelIssue issue = FindError(LevelValidator.ValidateDetailed(def), "墙格上有占据者");

                Assert.IsNotNull(issue, "An occupant-on-wall error is expected.");
                Assert.IsTrue(issue.HasCell, "The wall error must carry a cell location.");
                Assert.AreEqual(0, issue.CellX, "The wall error must reference the wall column.");
                Assert.AreEqual(0, issue.CellY, "The wall error must reference the wall row.");
            }
            finally
            {
                Destroy(def);
            }
        }

        [Test]
        public void OccupantOnDoor_ErrorCarriesExactCell()
        {
            LevelDefinition def = null;
            try
            {
                // 'p' paints the player directly onto the door at (1, 1); the box/goal pair is
                // balanced, so the occupant-on-door error is the only structural defect referenced
                // to that cell.
                def = LevelAssetFactory.BuildLevel(
                    "NavDoor",
                    new[] { "######", "#pD..#", "#.B.G#", "######" });

                LevelIssue issue = FindError(LevelValidator.ValidateDetailed(def), "门格上有占据者");

                Assert.IsNotNull(issue, "An occupant-on-door error is expected.");
                Assert.IsTrue(issue.HasCell, "The door error must carry a cell location.");
                Assert.AreEqual(1, issue.CellX, "The door error must reference the door column.");
                Assert.AreEqual(1, issue.CellY, "The door error must reference the door row.");
            }
            finally
            {
                Destroy(def);
            }
        }

        [Test]
        public void CornerDeadlock_WarningCarriesExactCell()
        {
            LevelDefinition def = null;
            try
            {
                // Box at (1, 1): wall above (1, 0) and wall to the left (0, 1), and it is not on a goal.
                def = LevelAssetFactory.BuildLevel(
                    "NavCorner",
                    new[] { "#####", "#B.G#", "#.P.#", "#####" });

                List<LevelIssue> warnings = LevelValidator.ValidateDetailed(def)
                    .FindAll(i => i.Severity == LevelIssueSeverity.Warning);

                Assert.AreEqual(1, warnings.Count, "Exactly one corner-deadlock warning is expected.");
                Assert.IsTrue(warnings[0].HasCell, "The warning must carry a cell location.");
                Assert.AreEqual(1, warnings[0].CellX, "The warning must reference the box column.");
                Assert.AreEqual(1, warnings[0].CellY, "The warning must reference the box row.");
            }
            finally
            {
                Destroy(def);
            }
        }

        [Test]
        public void Validate_ErrorOnlyMessages_AreUnchanged()
        {
            LevelDefinition wallDef = null;
            LevelDefinition doorDef = null;
            try
            {
                wallDef = LevelAssetFactory.BuildLevel("NavWallContract", new[] { "#####", "#P.G#", "#####" });
                wallDef.occupants[wallDef.Index(0, 0)] = OccupantType.Box;

                doorDef = LevelAssetFactory.BuildLevel(
                    "NavDoorContract",
                    new[] { "######", "#pD..#", "#.B.G#", "######" });

                List<string> wallErrors = LevelValidator.Validate(wallDef);
                List<string> doorErrors = LevelValidator.Validate(doorDef);

                Assert.IsTrue(
                    wallErrors.Contains("墙格上有占据者，位置 (0, 0)。"),
                    "The error-only wall message must be byte-identical. Got: " + string.Join(" | ", wallErrors));
                Assert.IsTrue(
                    doorErrors.Contains("门格上有占据者，位置 (1, 1)。"),
                    "The error-only door message must be byte-identical. Got: " + string.Join(" | ", doorErrors));
            }
            finally
            {
                Destroy(wallDef);
                Destroy(doorDef);
            }
        }

        private static LevelIssue FindError(List<LevelIssue> issues, string prefix)
        {
            return issues.Find(i => i.Severity == LevelIssueSeverity.Error && i.Message.StartsWith(prefix));
        }

        /// <summary>D015: destroys an in-memory ScriptableObject created by a test.</summary>
        private static void Destroy(LevelDefinition target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

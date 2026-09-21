using System.Collections.Generic;
using NUnit.Framework;
using Sokoban;
using Sokoban.Editor;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// Covers the T06 P0 level validator: every rule rejects a specifically-malformed level, a
    /// known-good level passes with zero errors, and a malformed level never throws (the validator
    /// catches the <see cref="Board"/> construction failure and reports it).
    /// </summary>
    public class ValidatorTests
    {
        private const string Level01Path = "Assets/Sokoban/Levels/Level01.asset";
        private const string Level02Path = "Assets/Sokoban/Levels/Level02.asset";

        /// <summary>Builds a level definition from ASCII rows (row 0 = top). See LevelAssetFactory.BuildLevel.</summary>
        private static LevelDefinition MakeLevel(params string[] rows)
        {
            return LevelAssetFactory.BuildLevel("ValidatorTest", rows);
        }

        [Test]
        public void Validate_NullLevel_IsRejected()
        {
            Assert.IsNotEmpty(LevelValidator.Validate(null), "A null level must be rejected.");
        }

        [Test]
        public void Validate_KnownGoodLevel_HasNoErrors()
        {
            Assert.IsEmpty(LevelValidator.Validate(MakeLevel("#####", "#PBG#", "#####")));
        }

        [Test]
        public void Validate_ShippedLevel01And02_HaveNoErrors()
        {
            var level01 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Path);
            var level02 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level02Path);
            Assert.IsNotNull(level01, "Level01 asset is required for this test.");
            Assert.IsNotNull(level02, "Level02 asset is required for this test.");

            Assert.IsEmpty(LevelValidator.Validate(level01), "Level01 must be valid.");
            Assert.IsEmpty(LevelValidator.Validate(level02), "Level02 must be valid.");
        }

        [Test]
        public void Validate_NoPlayer_IsRejected()
        {
            Assert.IsNotEmpty(LevelValidator.Validate(MakeLevel("#####", "#B.G#", "#####")));
        }

        [Test]
        public void Validate_TwoPlayers_IsRejected()
        {
            Assert.IsNotEmpty(LevelValidator.Validate(MakeLevel("######", "#PPBG#", "######")));
        }

        [Test]
        public void Validate_NoBoxes_IsRejected()
        {
            Assert.IsNotEmpty(LevelValidator.Validate(MakeLevel("#####", "#P.G#", "#####")));
        }

        [Test]
        public void Validate_NoGoals_IsRejected()
        {
            Assert.IsNotEmpty(LevelValidator.Validate(MakeLevel("#####", "#PB.#", "#####")));
        }

        [Test]
        public void Validate_BoxGoalMismatch_IsRejected()
        {
            Assert.IsNotEmpty(LevelValidator.Validate(MakeLevel("#####", "#PB.#", "#.GG#", "#####")));
        }

        [Test]
        public void Validate_OccupantOnWall_IsRejectedWithoutThrowing()
        {
            LevelDefinition def = MakeLevel("#####", "#P#G#", "#####");
            def.occupants[def.Index(0, 0)] = OccupantType.Box;

            List<string> errors = null;
            Assert.DoesNotThrow(() => errors = LevelValidator.Validate(def), "The validator must not throw.");
            Assert.IsNotEmpty(errors, "An occupant on a wall must be reported.");
        }

        [Test]
        public void Validate_OccupantOnDoor_IsRejectedWithCoordinates()
        {
            // 'p' paints the single player directly onto the door at (1, 1); the box/goal pair is
            // balanced, so the only structural defect is the occupant sitting on a door tile.
            LevelDefinition def = MakeLevel(
                "######",
                "#pD..#",
                "#.B.G#",
                "######");

            List<string> errors = null;
            Assert.DoesNotThrow(() => errors = LevelValidator.Validate(def), "The validator must not throw.");
            Assert.IsTrue(
                errors.Exists(e => e.Contains("门格上有占据者") && e.Contains("(1, 1)")),
                "An occupant on a door must be reported with its coordinates. Got: " + string.Join(" | ", errors));
        }

        [Test]
        public void Validate_EmptyDoor_HasNoOccupantOnDoorError()
        {
            // Same layout with the door unoccupied: the door itself is legal, so no door-occupant
            // error may be reported (and the level stays valid end to end).
            LevelDefinition def = MakeLevel(
                "######",
                "#PD..#",
                "#.B.G#",
                "######");

            List<string> errors = LevelValidator.Validate(def);

            Assert.IsFalse(
                errors.Exists(e => e.Contains("门格上有占据者")),
                "An empty door must never be reported as carrying an occupant. Got: " + string.Join(" | ", errors));
            Assert.IsEmpty(errors, "The unoccupied-door layout is a valid level.");
        }

        [Test]
        public void Validate_WrongCellCount_IsRejected()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = "BadCounts";
            def.levelName = "BadCounts";
            def.width = 4;
            def.height = 4;
            def.cells = new List<TileType> { TileType.Floor };
            def.occupants = new List<OccupantType> { OccupantType.Player };

            List<string> errors = null;
            Assert.DoesNotThrow(() => errors = LevelValidator.Validate(def), "The validator must not throw.");
            Assert.IsNotEmpty(errors, "A cell/occupant count mismatch must be reported.");
        }

        [Test]
        public void Validate_ZeroSize_IsRejected()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = "Zero";
            def.levelName = "Zero";
            def.width = 0;
            def.height = 0;
            def.cells = new List<TileType>();
            def.occupants = new List<OccupantType>();

            Assert.IsNotEmpty(LevelValidator.Validate(def));
        }

        [Test]
        public void CornerDeadlock_BoxOnNonGoal_WarnsWithCoordinates()
        {
            // Box at (1, 1): wall above (1, 0) and wall to the left (0, 1), and it is not on a goal.
            LevelDefinition def = MakeLevel("#####", "#B.G#", "#.P.#", "#####");

            List<LevelIssue> issues = LevelValidator.ValidateDetailed(def);

            var warnings = issues.FindAll(i => i.Severity == LevelIssueSeverity.Warning);
            Assert.AreEqual(1, warnings.Count, "Exactly one corner-deadlock warning is expected.");
            Assert.AreEqual(1, warnings[0].CellX, "Warning must reference the box column.");
            Assert.AreEqual(1, warnings[0].CellY, "Warning must reference the box row.");
            Assert.IsTrue(warnings[0].HasCell, "The warning must carry a cell location.");
            Assert.IsTrue(
                warnings[0].Message.Contains("(1, 1)"),
                "The warning message must mention the box coordinates. Got: " + warnings[0].Message);

            Assert.IsEmpty(
                LevelValidator.Validate(def),
                "A warning must never block Save/Playtest, so the error-only view must be empty.");
        }

        [Test]
        public void CornerDeadlock_BoxOnGoalInCorner_NoWarning()
        {
            // Same corner, but the box already sits on a goal ('*'), so it can never need freeing.
            LevelDefinition def = MakeLevel("#####", "#*..#", "#.P.#", "#####");

            List<LevelIssue> issues = LevelValidator.ValidateDetailed(def);

            Assert.IsEmpty(
                issues.FindAll(i => i.Severity == LevelIssueSeverity.Warning),
                "A box on a goal must never be reported as a corner deadlock.");
        }

        [Test]
        public void CornerDeadlock_BoxNotInCorner_NoWarning()
        {
            // Box at (2, 1) has free floor to the left and below: no blocked orthogonal pair.
            LevelDefinition def = MakeLevel("#####", "#.BG#", "#P..#", "#####");

            List<LevelIssue> issues = LevelValidator.ValidateDetailed(def);

            Assert.IsEmpty(
                issues.FindAll(i => i.Severity == LevelIssueSeverity.Warning),
                "A box with free orthogonal neighbours must not be warned.");
        }

        [Test]
        public void Validate_RemainsErrorOnly_ForCornerWarning()
        {
            LevelDefinition def = MakeLevel("#####", "#B.G#", "#.P.#", "#####");

            List<string> errors = LevelValidator.Validate(def);

            Assert.IsEmpty(errors, "A corner warning must not surface through the error-only wrapper.");
            foreach (string error in errors)
            {
                Assert.IsFalse(
                    error.Contains("静态死锁"),
                    "The error-only view must never contain the corner warning text.");
            }
        }

        [Test]
        public void PlateDoor_DoorWithoutPlate_Warns_ButDoesNotBlock()
        {
            // Player (1, 1), door (3, 1), one box (2, 2) and one goal (4, 2): no plate group exists.
            LevelDefinition def = MakeLevel(
                "######",
                "#P.D.#",
                "#.B.G#",
                "######");

            List<LevelIssue> issues = LevelValidator.ValidateDetailed(def);

            var warnings = issues.FindAll(i => i.Severity == LevelIssueSeverity.Warning);
            Assert.AreEqual(1, warnings.Count, "Exactly one plate-less-door warning is expected.");
            Assert.IsTrue(
                warnings[0].Message.Contains("组门"),
                "The warning must mention the door. Got: " + warnings[0].Message);
            Assert.IsTrue(warnings[0].HasCell, "The warning must carry a cell location.");
            Assert.AreEqual(3, warnings[0].CellX, "The warning must point at the first door column.");
            Assert.AreEqual(1, warnings[0].CellY, "The warning must point at the first door row.");

            Assert.IsEmpty(
                LevelValidator.Validate(def),
                "A warning must never block Save/Playtest, so the error-only view must be empty.");
        }

        [Test]
        public void PlateDoor_DoorWithPlate_NoWarning()
        {
            // Door (3, 1) and plate (2, 2) coexist, so the derived plate group exists.
            LevelDefinition def = MakeLevel(
                "#######",
                "#P.D..#",
                "#.T.BG#",
                "#######");

            List<LevelIssue> issues = LevelValidator.ValidateDetailed(def);

            Assert.IsEmpty(
                issues.FindAll(i => i.Message.Contains("组门")),
                "A door that has a pressure plate must not warn.");
            Assert.IsEmpty(LevelValidator.Validate(def));
        }

        // --- M4: group-consistency validation -----------------------------------------------------
        // LevelAssetFactory.BuildLevel materializes groupIds all-zero (group A); promoting a cell to
        // group B is `def.groupIds[def.Index(x, y)] = 1;` and `def.groupIds = null;` simulates an asset
        // authored before the field existed.

        [Test]
        public void GroupIds_WrongLength_IsError()
        {
            LevelDefinition def = null;
            try
            {
                def = MakeLevel("#####", "#PBG#", "#####");
                def.groupIds = new List<int> { 0 };

                List<string> errors = null;
                Assert.DoesNotThrow(() => errors = LevelValidator.Validate(def), "The validator must not throw.");
                Assert.IsNotEmpty(errors, "A group id count mismatch must be reported.");
                Assert.IsTrue(
                    errors.Exists(e => e.Contains("分组")),
                    "The group id count error must mention the group list. Got: " + string.Join(" | ", errors));
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void GroupIds_IllegalId_IsError()
        {
            LevelDefinition def = null;
            try
            {
                def = MakeLevel("#####", "#PBG#", "#####");
                def.groupIds[def.Index(1, 1)] = 2;

                List<string> errors = null;
                Assert.DoesNotThrow(() => errors = LevelValidator.Validate(def), "The validator must not throw.");
                Assert.IsNotEmpty(errors, "An out-of-range group id must be reported.");
                Assert.IsTrue(
                    errors.Exists(e => e.Contains("分组编号") && e.Contains("(1, 1)")),
                    "The illegal group id error must name the id and its cell. Got: " + string.Join(" | ", errors));
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void GroupIds_Null_IsValid_ImplicitGroupA()
        {
            LevelDefinition def = null;
            try
            {
                def = MakeLevel("#####", "#PBG#", "#####");
                def.groupIds = null;

                Assert.IsEmpty(
                    LevelValidator.Validate(def),
                    "A null group list must read as implicit group A and stay valid.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void DoorGroupWithNoPlate_Warns_PerGroup()
        {
            LevelDefinition def = null;
            try
            {
                // Group A: plate (3, 1) + door (4, 1), a matched pair. Group B: door (3, 2) only, so it
                // is the single orphaned door group. One box (4, 3), one goal (6, 3), one player (1, 1).
                def = MakeLevel(
                    "########",
                    "#P.TD..#",
                    "#..D...#",
                    "#...B.G#",
                    "########");
                def.groupIds[def.Index(3, 2)] = 1;

                List<LevelIssue> issues = LevelValidator.ValidateDetailed(def);

                var doorWarnings = issues.FindAll(
                    i => i.Severity == LevelIssueSeverity.Warning && i.Message.Contains("组门位于"));
                Assert.AreEqual(1, doorWarnings.Count, "Exactly one orphaned door group warning is expected.");
                Assert.IsTrue(
                    doorWarnings[0].Message.Contains("组门"),
                    "The warning must mention the door. Got: " + doorWarnings[0].Message);
                Assert.IsTrue(doorWarnings[0].HasCell, "The warning must carry a cell location.");
                Assert.AreEqual(3, doorWarnings[0].CellX, "The warning must point at the orphaned group-B door column.");
                Assert.AreEqual(2, doorWarnings[0].CellY, "The warning must point at the orphaned group-B door row.");

                Assert.IsEmpty(
                    LevelValidator.Validate(def),
                    "A per-group door warning must never block Save/Playtest.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void PlateGroupWithNoDoor_Warns_PerGroup()
        {
            LevelDefinition def = null;
            try
            {
                // Group A: plate (3, 1) + door (4, 1), matched. Group B: plate (3, 2) only, so it is the
                // single plate group that can never open anything.
                def = MakeLevel(
                    "########",
                    "#P.TD..#",
                    "#..T...#",
                    "#...B.G#",
                    "########");
                def.groupIds[def.Index(3, 2)] = 1;

                List<LevelIssue> issues = LevelValidator.ValidateDetailed(def);

                var plateWarnings = issues.FindAll(
                    i => i.Severity == LevelIssueSeverity.Warning && i.Message.Contains("组压力板位于"));
                Assert.AreEqual(1, plateWarnings.Count, "Exactly one unmatched plate group warning is expected.");
                Assert.IsTrue(
                    plateWarnings[0].Message.Contains("组压力板"),
                    "The warning must mention the plate. Got: " + plateWarnings[0].Message);
                Assert.IsTrue(plateWarnings[0].HasCell, "The warning must carry a cell location.");
                Assert.AreEqual(3, plateWarnings[0].CellX, "The warning must point at the unmatched group-B plate column.");
                Assert.AreEqual(2, plateWarnings[0].CellY, "The warning must point at the unmatched group-B plate row.");

                Assert.IsEmpty(
                    LevelValidator.Validate(def),
                    "A per-group plate warning must never block Save/Playtest.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void ValidTwoGroupLevel_HasNoGroupIssues()
        {
            LevelDefinition def = null;
            try
            {
                // Group A: plate (3, 1) + door (4, 1). Group B: plate (3, 2) + door (4, 2). Both groups
                // are matched, so a well-formed two-group level must be valid and warn about neither.
                def = MakeLevel(
                    "########",
                    "#P.TD..#",
                    "#..TD..#",
                    "#...B.G#",
                    "########");
                def.groupIds[def.Index(3, 2)] = 1;
                def.groupIds[def.Index(4, 2)] = 1;

                List<LevelIssue> issues = LevelValidator.ValidateDetailed(def);

                Assert.IsEmpty(
                    issues.FindAll(i => i.Message.Contains("组门") || i.Message.Contains("组压力板")),
                    "A matched two-group level must not warn about doors or plates.");
                Assert.IsEmpty(LevelValidator.Validate(def), "A matched two-group level must be valid.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }
    }
}

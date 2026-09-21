using NUnit.Framework;
using Sokoban;
using UnityEngine;

namespace Sokoban.Tests
{
    /// <summary>
    /// EditMode coverage for the runtime half of the fully automated submission capture pipeline (C3):
    /// the directive/done JSON contract and the pure move parser. The scene-driving driver itself
    /// (play-mode board resolution, ScreenCapture) needs a live visible editor and is not exercised here.
    /// </summary>
    public class SubmissionCaptureDriverTests
    {
        [Test]
        public void Directive_RoundTripsFromBridgeShapedJson()
        {
            // Same shape the editor bridge writes via JsonUtility.ToJson of its runtime directive.
            const string json =
                "{\"scenario\":\"RuntimeComplete\",\"outputPath\":\"C:\\\\repo\\\\Docs\\\\Screenshots\\\\runtime-complete.png\"," +
                "\"moves\":\"Up,Right,Down\",\"replayCount\":-1,\"markCompleted\":true,\"settleSeconds\":0.6}";

            var directive = JsonUtility.FromJson<CaptureRuntimeDirective>(json);

            Assert.IsNotNull(directive, "The directive must deserialize.");
            Assert.AreEqual("RuntimeComplete", directive.scenario, "scenario must survive.");
            Assert.AreEqual(
                @"C:\repo\Docs\Screenshots\runtime-complete.png",
                directive.outputPath,
                "outputPath must survive.");
            Assert.AreEqual("Up,Right,Down", directive.moves, "moves must survive.");
            Assert.AreEqual(-1, directive.replayCount, "replayCount -1 (replay all) must survive.");
            Assert.IsTrue(directive.markCompleted, "markCompleted must survive.");
            Assert.AreEqual(0.6f, directive.settleSeconds, 0.0001f, "settleSeconds must survive.");
        }

        [Test]
        public void ParseMoves_WithNegativeCount_ReturnsEveryMove()
        {
            Direction[] moves = SubmissionCaptureDriver.ParseMoves("Up,Right,Down", -1);
            CollectionAssert.AreEqual(
                new[] { Direction.Up, Direction.Right, Direction.Down },
                moves,
                "A negative replay count must return every parsed move.");
        }

        [Test]
        public void ParseMoves_WithPositiveCount_TruncatesToPrefix()
        {
            Direction[] moves = SubmissionCaptureDriver.ParseMoves("Up,Right,Down", 2);
            CollectionAssert.AreEqual(
                new[] { Direction.Up, Direction.Right },
                moves,
                "A positive replay count must return only the leading moves.");
        }

        [Test]
        public void ParseMoves_WithEmptyInput_ReturnsEmpty()
        {
            Assert.IsEmpty(
                SubmissionCaptureDriver.ParseMoves(string.Empty, -1),
                "An empty move string must yield no moves.");
        }

        [Test]
        public void ParseMoves_SkipsUnparsableTokens()
        {
            Direction[] moves = SubmissionCaptureDriver.ParseMoves("Up,bogus,Down", -1);
            CollectionAssert.AreEqual(
                new[] { Direction.Up, Direction.Down },
                moves,
                "Malformed tokens must be skipped, not abort the parse.");
        }

        [Test]
        public void DoneMarker_SerializesContractKeys()
        {
            string json = JsonUtility.ToJson(new CaptureRuntimeDone
            {
                scenario = "RuntimeComplete",
                ok = true,
                error = string.Empty
            });

            StringAssert.Contains("scenario", json, "The done marker must expose the scenario key.");
            StringAssert.Contains("ok", json, "The done marker must expose the ok key.");
        }
    }
}

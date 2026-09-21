using System;

namespace Sokoban.Editor
{
    /// <summary>
    /// Runtime render resolution requested for a capture run: the size the runtime screenshot driver
    /// is expected to render at. Deserialized from <c>Temp/AgentCapture/request.json</c> (written by
    /// the PowerShell orchestrator, never by this assembly).
    /// </summary>
    [Serializable]
    public class CaptureResolution
    {
        public int width;
        public int height;
    }

    /// <summary>
    /// Director file of one fully automated submission capture run. It is written by the PowerShell
    /// orchestrator to <c>Temp/AgentCapture/request.json</c>; the Unity editor bridge
    /// (<see cref="SubmissionCaptureService"/>) only reads it. <see cref="scenarios"/> holds the
    /// ordered scenario ids (see <see cref="SubmissionCaptureScenarios"/>) the bridge must process
    /// one at a time; scenarios that already produced a PNG are only re-captured when
    /// <see cref="overwrite"/> is true. <see cref="requestId"/> is the idempotency key: a run whose
    /// id was already completed is not restarted.
    /// </summary>
    [Serializable]
    public class SubmissionCaptureRequest
    {
        public string requestId;
        public string[] scenarios;
        public CaptureResolution runtimeResolution;
        public bool overwrite;
    }
}

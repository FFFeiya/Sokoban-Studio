using System;

namespace Sokoban.Editor
{
    /// <summary>
    /// One successfully captured scenario. <see cref="path"/> is an absolute PNG path;
    /// <see cref="captureMethod"/> records how it was produced ("UnityScreenCapture" for the runtime
    /// driver, or the value reported by the Win32 editor capture, e.g. "PrintWindow"/"CopyFromScreen").
    /// <see cref="validation"/> is a placeholder string ("pending") because the mechanical PNG QA runs
    /// later in <c>ValidateScreenshots.ps1</c> and owns the final verdict.
    /// </summary>
    [Serializable]
    public class CaptureOutput
    {
        public string scenario;
        public string path;
        public string captureMethod;
        public int width;
        public int height;
        public string validation;
    }

    /// <summary>
    /// Outcome file of one capture run, written to <c>Temp/AgentCapture/result.json</c> by the Unity
    /// editor bridge when every scenario has been processed. <see cref="status"/> is "complete" when
    /// no scenario failed and "partial" otherwise; <see cref="failed"/> lists the scenario ids that
    /// failed (the reason is appended to <c>Temp/AgentCapture/log.txt</c>).
    /// </summary>
    [Serializable]
    public class SubmissionCaptureResult
    {
        public string requestId;
        public string status;
        public CaptureOutput[] outputs;
        public string[] failed;
    }
}

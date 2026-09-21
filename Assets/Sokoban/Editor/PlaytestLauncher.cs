using System.Collections.Generic;
using UnityEditor;

namespace Sokoban.Editor
{
    /// <summary>
    /// Testable decision logic for the editor's one-click Playtest: validate the working copy,
    /// persist it when needed and arm the persistent <see cref="PlaytestRequest"/> asset with the
    /// <em>persisted</em> asset, so the request survives the Play Mode domain reload.
    /// The window only performs the editor-specific scene/play-mode switching around this decision,
    /// so the whole flow can be verified headlessly.
    /// </summary>
    public static class PlaytestLauncher
    {
        /// <summary>
        /// Validates <paramref name="doc"/>, saves it when it has unsaved edits, and arms the request
        /// asset with the saved level (never the working copy).
        /// Returns true when the caller may enter play mode; on failure it returns false, leaves the
        /// request asset untouched and fills <paramref name="error"/>.
        /// </summary>
        public static bool TryPreparePlaytest(LevelEditorDocument doc, out string error)
        {
            error = null;

            if (doc == null)
            {
                error = "没有打开任何关卡。";
                return false;
            }

            List<string> errors = LevelValidator.Validate(doc.Working);
            if (errors.Count > 0)
            {
                error = "试玩前请先解决以下问题：\n\n" + string.Join("\n", errors);
                return false;
            }

            string targetPath = doc.SourcePath;
            if (string.IsNullOrEmpty(targetPath))
            {
                error = "试玩前请先将关卡保存为资源。";
                return false;
            }

            if (doc.IsDirty && !doc.Save())
            {
                error = "试玩前无法保存关卡：" + targetPath;
                return false;
            }

            LevelDefinition asset = AssetDatabase.LoadAssetAtPath<LevelDefinition>(targetPath);
            if (asset == null)
            {
                error = "无法重新加载已保存的关卡资源：" + targetPath;
                return false;
            }

            Arm(asset);
            return true;
        }

        /// <summary>
        /// Validates an already-saved <paramref name="level"/> asset and arms the request asset with it.
        /// This is the Content Dashboard / <see cref="LevelEditorWindow.PlaytestLevel"/> path, which has
        /// no working-copy document to save first. Returns true when the caller may enter play mode; on
        /// failure it returns false, leaves the request asset untouched and fills <paramref name="error"/>.
        /// </summary>
        public static bool TryPreparePlaytest(LevelDefinition level, out string error)
        {
            error = null;

            if (level == null)
            {
                error = "未选择关卡。";
                return false;
            }

            List<string> errors = LevelValidator.Validate(level);
            if (errors.Count > 0)
            {
                error = "试玩前请先解决以下问题：\n\n" + string.Join("\n", errors);
                return false;
            }

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(level)))
            {
                error = "试玩前请先将关卡保存为资源。";
                return false;
            }

            Arm(level);
            return true;
        }

        /// <summary>
        /// Loads the persistent request asset, or null when it is missing from the project.
        /// </summary>
        public static PlaytestRequest LoadRequestAsset()
        {
            return AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
        }

        /// <summary>
        /// Writes <paramref name="level"/> into the persistent request asset and saves it to disk
        /// <em>before</em> play mode starts, so entering play mode (with its domain reload) cannot
        /// lose the request. Passing null disarms the request.
        /// </summary>
        public static void Arm(LevelDefinition level)
        {
            PlaytestRequest request = LoadRequestAsset();
            if (request == null)
            {
                throw new System.InvalidOperationException(
                    "The persistent playtest request asset is missing: " + PlaytestRequest.AssetPath);
            }

            request.Level = level;
            EditorUtility.SetDirty(request);
            AssetDatabase.SaveAssets();
        }
    }
}

using UnityEditor;

namespace Sokoban.Editor
{
    /// <summary>
    /// Keeps the persistent <see cref="PlaytestRequest"/> asset from leaking a stale request into the
    /// next normal Play. The runtime only clears its in-memory copy (it must not touch editor APIs or
    /// save assets), so when play mode exits this editor-side hook rewrites the on-disk asset with
    /// <c>Level = null</c> and saves it.
    /// The clear is unconditional on play-mode exit: even when the in-memory field already reads null
    /// (the runtime consumed it), the on-disk file must be forced back to the empty state.
    /// </summary>
    [InitializeOnLoad]
    public static class PlaytestRequestJanitor
    {
        static PlaytestRequestJanitor()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Clears the on-disk request asset. Returns true when a pending request was removed.
        /// Safe to call from tests and from editor workflows that abort a playtest.
        /// </summary>
        public static bool Clear()
        {
            PlaytestRequest request = Load();
            if (request == null || request.Level == null)
            {
                return false;
            }

            PersistEmpty(request);
            return true;
        }

        /// <summary>
        /// Clears the request when play mode ends, so the on-disk asset never keeps a stale request.
        /// </summary>
        public static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // ExitingPlayMode fires before the exit domain reload, EnteredEditMode after it; handling
            // both keeps the on-disk file correct whichever side the asset reload lands on.
            if (state != PlayModeStateChange.ExitingPlayMode &&
                state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            PlaytestRequest request = Load();
            if (request == null)
            {
                return;
            }

            // Always rewrite the asset: the runtime may already have nulled its in-memory reference
            // without saving, and the on-disk file is what the next Play would read after a reload.
            PersistEmpty(request);
        }

        private static PlaytestRequest Load()
        {
            return AssetDatabase.LoadAssetAtPath<PlaytestRequest>(PlaytestRequest.AssetPath);
        }

        private static void PersistEmpty(PlaytestRequest request)
        {
            request.Level = null;
            EditorUtility.SetDirty(request);
            AssetDatabase.SaveAssets();
        }
    }
}

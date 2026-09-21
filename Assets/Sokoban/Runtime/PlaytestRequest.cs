using UnityEngine;

namespace Sokoban
{
    /// <summary>
    /// Persistent one-shot hand-off channel from the editor's one-click Playtest to the gameplay
    /// scene. It is a <see cref="ScriptableObject"/> asset on purpose: unlike a plain static field it
    /// survives the default Play Mode domain reload, so a non-Catalog level armed in the Level Editor
    /// is the level the gameplay scene actually loads.
    /// The editor writes <see cref="Level"/> and saves the asset before entering play mode; the
    /// gameplay bootstrap reads it once and clears only its in-memory copy (runtime code never saves
    /// assets and never touches <c>UnityEditor</c>); an editor-side janitor clears the on-disk value
    /// when play mode exits, so a stale request cannot leak into the next normal Play.
    /// </summary>
    [CreateAssetMenu(menuName = "Sokoban/Playtest Request")]
    public class PlaytestRequest : ScriptableObject
    {
        /// <summary>Canonical path of the single request-channel asset used by the editor tooling.</summary>
        public const string AssetPath = "Assets/Sokoban/PlaytestRequest.asset";

        /// <summary>Level requested by an editor workflow, or null when there is no request.</summary>
        public LevelDefinition Level;
    }
}

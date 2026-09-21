using UnityEngine.SceneManagement;

namespace Sokoban
{
    /// <summary>
    /// Runtime navigation channel for the menu → level select → gameplay flow, and the scene names
    /// used by that flow. <see cref="NextLevel"/> is a one-shot hand-off: the menu/select screen or
    /// the completion overlay sets it right before loading the gameplay scene, and the gameplay
    /// bootstrap consumes it once (clearing it) so a direct Play uses the serialized level.
    /// </summary>
    public static class GameFlowRequest
    {
        public const string MainMenuScene = "MainMenu";
        public const string LevelSelectScene = "LevelSelect";
        public const string GameplayScene = "Gameplay";

        /// <summary>Level requested by the menu/select/completion flow, or null when there is none.</summary>
        public static LevelDefinition NextLevel;

        /// <summary>Requests <paramref name="level"/> and loads the gameplay scene.</summary>
        public static void LoadLevel(LevelDefinition level)
        {
            if (level == null)
            {
                return;
            }

            NextLevel = level;
            SceneManager.LoadScene(GameplayScene);
        }
    }
}

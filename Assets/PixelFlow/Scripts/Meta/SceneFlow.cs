using UnityEngine.SceneManagement;

namespace PixelFlow.Meta
{
    /// <summary>
    /// Scene names (Build Settings order Loading, Menu, Game) and the transitions between them.
    /// </summary>
    public static class SceneFlow
    {
        /// <summary>Name of the boot scene that loads <see cref="MenuScene"/>.</summary>
        public const string LoadingScene = "Loading";

        /// <summary>Name of the main menu scene.</summary>
        public const string MenuScene = "Menu";

        /// <summary>Name of the gameplay scene.</summary>
        public const string GameScene = "Game";

        /// <summary>Loads the menu (single mode).</summary>
        public static void LoadMenu() => SceneManager.LoadScene(MenuScene);

        /// <summary>Loads the game (single mode); it plays <see cref="PlayerProgress.LevelNumber"/>.</summary>
        public static void LoadGame() => SceneManager.LoadScene(GameScene);
    }
}

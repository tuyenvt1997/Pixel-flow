using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixelFlow.Meta
{
    /// <summary>
    /// Main menu: labels the level path nodes with the upcoming level numbers (node 0, at the bottom, is
    /// <see cref="PlayerProgress.LevelNumber"/>), starts the game with Play and opens the settings popup with the gear.
    /// </summary>
    public sealed class MenuScreen : MonoBehaviour
    {
        [Tooltip("Number labels of the level path nodes, bottom (current level) first.")]
        [SerializeField] private TMP_Text[] nodeLabels;

        [SerializeField] private Button playButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private SettingsPopup settingsPopup;

        private void Awake()
        {
            if (playButton != null)
                playButton.onClick.AddListener(SceneFlow.LoadGame);
            if (settingsButton != null)
                settingsButton.onClick.AddListener(OpenSettings);
        }

        private void Start()
        {
            Refresh();
        }

        private void OnDestroy()
        {
            if (playButton != null)
                playButton.onClick.RemoveListener(SceneFlow.LoadGame);
            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(OpenSettings);
        }

        /// <summary>
        /// Relabels the path nodes from the stored progress.
        /// </summary>
        public void Refresh()
        {
            if (nodeLabels == null)
                return;
            int number = MetaServices.Progress.LevelNumber;
            for (int i = 0; i < nodeLabels.Length; i++)
            {
                if (nodeLabels[i] != null)
                    nodeLabels[i].SetText("{0}", number + i);
            }
        }

        private void OpenSettings()
        {
            if (settingsPopup != null)
                settingsPopup.Open();
        }
    }
}

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixelFlow.View
{
    /// <summary>
    /// uGUI heads-up display: a level label plus win and lose popups. Button clicks are forwarded as
    /// <see cref="OnRetryClicked"/> / <see cref="OnNextClicked"/>; the view holds no game logic.
    /// All serialized references are optional so the scene builder can wire only what it creates.
    /// </summary>
    public sealed class GameHudView : MonoBehaviour
    {
        [Tooltip("Label showing the current level number.")]
        [SerializeField] private TextMeshProUGUI levelText;

        [Tooltip("Popup shown when the level is won.")]
        [SerializeField] private GameObject winPanel;

        [Tooltip("Popup shown when the level is lost.")]
        [SerializeField] private GameObject losePanel;

        [Tooltip("Buttons that restart the current level (e.g. on the lose popup and the HUD).")]
        [SerializeField] private Button[] retryButtons;

        [Tooltip("Buttons that advance to the next level (e.g. on the win popup).")]
        [SerializeField] private Button[] nextButtons;

        private UnityEngine.Events.UnityAction _retryHandler;
        private UnityEngine.Events.UnityAction _nextHandler;

        /// <summary>
        /// Raised when any retry button is clicked.
        /// </summary>
        public event Action OnRetryClicked;

        /// <summary>
        /// Raised when any next-level button is clicked.
        /// </summary>
        public event Action OnNextClicked;

        private void Awake()
        {
            _retryHandler = HandleRetry;
            _nextHandler = HandleNext;
            AddListeners(retryButtons, _retryHandler);
            AddListeners(nextButtons, _nextHandler);
        }

        private void OnDestroy()
        {
            RemoveListeners(retryButtons, _retryHandler);
            RemoveListeners(nextButtons, _nextHandler);
        }

        /// <summary>
        /// Shows <paramref name="levelNumber"/> in the level label as "Level N" (no string allocation).
        /// </summary>
        /// <param name="levelNumber">1-based level number.</param>
        public void SetLevel(int levelNumber)
        {
            if (levelText != null)
                levelText.SetText("Level {0}", levelNumber);
        }

        /// <summary>
        /// Shows the win popup and hides the lose popup.
        /// </summary>
        public void ShowWin()
        {
            SetActive(winPanel, true);
            SetActive(losePanel, false);
        }

        /// <summary>
        /// Shows the lose popup and hides the win popup.
        /// </summary>
        public void ShowLose()
        {
            SetActive(winPanel, false);
            SetActive(losePanel, true);
        }

        /// <summary>
        /// Hides both popups (the level label stays visible).
        /// </summary>
        public void HideAll()
        {
            SetActive(winPanel, false);
            SetActive(losePanel, false);
        }

        private void HandleRetry()
        {
            OnRetryClicked?.Invoke();
        }

        private void HandleNext()
        {
            OnNextClicked?.Invoke();
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }

        private static void AddListeners(Button[] buttons, UnityEngine.Events.UnityAction handler)
        {
            if (buttons == null)
                return;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].onClick.AddListener(handler);
            }
        }

        private static void RemoveListeners(Button[] buttons, UnityEngine.Events.UnityAction handler)
        {
            if (buttons == null || handler == null)
                return;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].onClick.RemoveListener(handler);
            }
        }
    }
}

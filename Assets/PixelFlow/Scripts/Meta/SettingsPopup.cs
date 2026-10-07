using System;
using UnityEngine;
using UnityEngine.UI;

namespace PixelFlow.Meta
{
    /// <summary>
    /// Settings popup shared by the menu and the game: music, sound effects and vibration switches bound to
    /// <see cref="MetaServices.Settings"/>, a close button and an optional Home button (game only) forwarded as
    /// <see cref="OnHomeClicked"/>. The popup is its own GameObject: open = active.
    /// </summary>
    public sealed class SettingsPopup : MonoBehaviour
    {
        [SerializeField] private ToggleSwitchView musicSwitch;
        [SerializeField] private ToggleSwitchView sfxSwitch;
        [SerializeField] private ToggleSwitchView vibrationSwitch;

        [Tooltip("Closes the popup.")]
        [SerializeField] private Button closeButton;

        [Tooltip("Optional: returns to the menu (only wired in the game scene).")]
        [SerializeField] private Button homeButton;

        private bool _wired;

        /// <summary>
        /// Raised when the Home button is clicked.
        /// </summary>
        public event Action OnHomeClicked;

        /// <summary>
        /// True while the popup is shown.
        /// </summary>
        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            Wire();
        }

        private void OnDestroy()
        {
            if (!_wired)
                return;
            if (musicSwitch != null)
                musicSwitch.Toggled -= HandleMusic;
            if (sfxSwitch != null)
                sfxSwitch.Toggled -= HandleSfx;
            if (vibrationSwitch != null)
                vibrationSwitch.Toggled -= HandleVibration;
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
            if (homeButton != null)
                homeButton.onClick.RemoveListener(HandleHome);
        }

        /// <summary>
        /// Shows the popup with the switches set from the stored settings.
        /// </summary>
        public void Open()
        {
            Wire();
            GameSettings settings = MetaServices.Settings;
            if (musicSwitch != null)
                musicSwitch.SetIsOn(settings.Music);
            if (sfxSwitch != null)
                sfxSwitch.SetIsOn(settings.Sfx);
            if (vibrationSwitch != null)
                vibrationSwitch.SetIsOn(settings.Vibration);
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Hides the popup.
        /// </summary>
        public void Close()
        {
            gameObject.SetActive(false);
        }

        // Open() can run before Awake (the popup is saved inactive), so wiring is idempotent.
        private void Wire()
        {
            if (_wired)
                return;
            _wired = true;
            if (musicSwitch != null)
                musicSwitch.Toggled += HandleMusic;
            if (sfxSwitch != null)
                sfxSwitch.Toggled += HandleSfx;
            if (vibrationSwitch != null)
                vibrationSwitch.Toggled += HandleVibration;
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (homeButton != null)
                homeButton.onClick.AddListener(HandleHome);
        }

        private static void HandleMusic(bool on) => MetaServices.Settings.Music = on;

        private static void HandleSfx(bool on) => MetaServices.Settings.Sfx = on;

        private static void HandleVibration(bool on)
        {
            MetaServices.Settings.Vibration = on;
            MetaServices.Settings.TryVibrate();
        }

        private void HandleHome()
        {
            OnHomeClicked?.Invoke();
        }
    }
}

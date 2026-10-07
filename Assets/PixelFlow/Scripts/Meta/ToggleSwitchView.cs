using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixelFlow.Meta
{
    /// <summary>
    /// The UI pack's ON/OFF switch: clicking the button flips the state, which shows the fill, slides the handle to
    /// the right (on) or left (off), relabels it and raises <see cref="Toggled"/>.
    /// All serialized references except the button are optional.
    /// </summary>
    public sealed class ToggleSwitchView : MonoBehaviour
    {
        [Tooltip("Button covering the switch.")]
        [SerializeField] private Button button;

        [Tooltip("Coloured fill shown only while on.")]
        [SerializeField] private GameObject fill;

        [Tooltip("Sliding knob.")]
        [SerializeField] private RectTransform handle;

        [Tooltip("ON/OFF label on the knob.")]
        [SerializeField] private TMP_Text label;

        [Tooltip("Knob x offset from the centre (positive when on, negative when off).")]
        [SerializeField] private float handleOffset = 56f;

        private bool _isOn = true;

        /// <summary>
        /// Raised with the new state when the user flips the switch (not by <see cref="SetIsOn"/>).
        /// </summary>
        public event Action<bool> Toggled;

        /// <summary>
        /// Current state.
        /// </summary>
        public bool IsOn => _isOn;

        private void Awake()
        {
            if (button != null)
                button.onClick.AddListener(HandleClick);
            Apply();
        }

        private void OnDestroy()
        {
            if (button != null)
                button.onClick.RemoveListener(HandleClick);
        }

        /// <summary>
        /// Shows <paramref name="isOn"/> without raising <see cref="Toggled"/>.
        /// </summary>
        public void SetIsOn(bool isOn)
        {
            _isOn = isOn;
            Apply();
        }

        private void HandleClick()
        {
            SetIsOn(!_isOn);
            Toggled?.Invoke(_isOn);
        }

        private void Apply()
        {
            if (fill != null && fill.activeSelf != _isOn)
                fill.SetActive(_isOn);
            if (handle != null)
                handle.anchoredPosition = new Vector2(_isOn ? handleOffset : -handleOffset, handle.anchoredPosition.y);
            if (label != null)
                label.text = _isOn ? "ON" : "OFF";
        }
    }
}

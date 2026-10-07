using System;
using UnityEngine;

namespace PixelFlow.Meta
{
    /// <summary>
    /// Persistent player settings: music, sound effects and vibration (all on by default). Every change is saved
    /// immediately and raises <see cref="Changed"/>. The game has no audio yet; <see cref="Music"/> and
    /// <see cref="Sfx"/> are the switches future audio reads.
    /// </summary>
    public sealed class GameSettings
    {
        /// <summary>Store key of <see cref="Music"/> (1 = on, 0 = off).</summary>
        public const string MusicKey = "pf.music";

        /// <summary>Store key of <see cref="Sfx"/> (1 = on, 0 = off).</summary>
        public const string SfxKey = "pf.sfx";

        /// <summary>Store key of <see cref="Vibration"/> (1 = on, 0 = off).</summary>
        public const string VibrationKey = "pf.vibration";

        private readonly IKeyValueStore _store;

        /// <summary>
        /// Raised after any setting changed value.
        /// </summary>
        public event Action Changed;

        /// <summary>
        /// Creates settings backed by <paramref name="store"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown if store is null.</exception>
        public GameSettings(IKeyValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Background music on/off.</summary>
        public bool Music
        {
            get => Get(MusicKey);
            set => Set(MusicKey, value);
        }

        /// <summary>Sound effects on/off.</summary>
        public bool Sfx
        {
            get => Get(SfxKey);
            set => Set(SfxKey, value);
        }

        /// <summary>Haptic feedback on/off.</summary>
        public bool Vibration
        {
            get => Get(VibrationKey);
            set => Set(VibrationKey, value);
        }

        /// <summary>
        /// Vibrates the device if <see cref="Vibration"/> is on and the platform supports it (Android / iOS).
        /// </summary>
        public void TryVibrate()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (Vibration)
                Handheld.Vibrate();
#endif
        }

        private bool Get(string key) => _store.GetInt(key, 1) != 0;

        private void Set(string key, bool value)
        {
            if (Get(key) == value)
                return;
            _store.SetInt(key, value ? 1 : 0);
            _store.Save();
            Changed?.Invoke();
        }
    }
}

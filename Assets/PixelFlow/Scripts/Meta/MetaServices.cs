using System;

namespace PixelFlow.Meta
{
    /// <summary>
    /// Shared access to the meta layer: one store with the <see cref="PlayerProgress"/> and <see cref="GameSettings"/>
    /// built on it, used by every scene. Backed by PlayerPrefs unless <see cref="Use"/> swaps the store (tests).
    /// </summary>
    public static class MetaServices
    {
        private static IKeyValueStore _store;
        private static PlayerProgress _progress;
        private static GameSettings _settings;

        /// <summary>
        /// The current store (a <see cref="PlayerPrefsStore"/> until <see cref="Use"/> is called).
        /// </summary>
        public static IKeyValueStore Store
        {
            get
            {
                if (_store == null)
                    Use(new PlayerPrefsStore());
                return _store;
            }
        }

        /// <summary>
        /// Level progress on <see cref="Store"/>.
        /// </summary>
        public static PlayerProgress Progress
        {
            get
            {
                if (_store == null)
                    Use(new PlayerPrefsStore());
                return _progress;
            }
        }

        /// <summary>
        /// Player settings on <see cref="Store"/>.
        /// </summary>
        public static GameSettings Settings
        {
            get
            {
                if (_store == null)
                    Use(new PlayerPrefsStore());
                return _settings;
            }
        }

        /// <summary>
        /// Replaces the store; <see cref="Progress"/> and <see cref="Settings"/> are rebuilt on it.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown if store is null.</exception>
        public static void Use(IKeyValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _progress = new PlayerProgress(store);
            _settings = new GameSettings(store);
        }
    }
}

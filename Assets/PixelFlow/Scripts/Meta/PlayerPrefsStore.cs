using UnityEngine;

namespace PixelFlow.Meta
{
    /// <summary>
    /// <see cref="IKeyValueStore"/> backed by <see cref="PlayerPrefs"/>.
    /// </summary>
    public sealed class PlayerPrefsStore : IKeyValueStore
    {
        /// <inheritdoc />
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);

        /// <inheritdoc />
        public int GetInt(string key, int defaultValue) => PlayerPrefs.GetInt(key, defaultValue);

        /// <inheritdoc />
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);

        /// <inheritdoc />
        public void Save() => PlayerPrefs.Save();
    }
}

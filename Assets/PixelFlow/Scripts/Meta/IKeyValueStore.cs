namespace PixelFlow.Meta
{
    /// <summary>
    /// Minimal int key/value persistence used by <see cref="PlayerProgress"/> and <see cref="GameSettings"/>, so the
    /// meta layer can run against PlayerPrefs in the game and against memory in tests.
    /// </summary>
    public interface IKeyValueStore
    {
        /// <summary>
        /// True if a value is stored under <paramref name="key"/>.
        /// </summary>
        bool HasKey(string key);

        /// <summary>
        /// The value stored under <paramref name="key"/>, or <paramref name="defaultValue"/> if none is.
        /// </summary>
        int GetInt(string key, int defaultValue);

        /// <summary>
        /// Stores <paramref name="value"/> under <paramref name="key"/>.
        /// </summary>
        void SetInt(string key, int value);

        /// <summary>
        /// Flushes pending writes to permanent storage (no-op for stores without one).
        /// </summary>
        void Save();
    }
}

using System.Collections.Generic;

namespace PixelFlow.Meta
{
    /// <summary>
    /// In-memory <see cref="IKeyValueStore"/> (tests, or a session that must not touch PlayerPrefs).
    /// </summary>
    public sealed class MemoryStore : IKeyValueStore
    {
        private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

        /// <inheritdoc />
        public bool HasKey(string key) => _values.ContainsKey(key);

        /// <inheritdoc />
        public int GetInt(string key, int defaultValue) => _values.TryGetValue(key, out int value) ? value : defaultValue;

        /// <inheritdoc />
        public void SetInt(string key, int value) => _values[key] = value;

        /// <inheritdoc />
        public void Save()
        {
        }
    }
}

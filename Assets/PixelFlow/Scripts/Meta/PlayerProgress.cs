using System;

namespace PixelFlow.Meta
{
    /// <summary>
    /// Persistent level progress: the 1-based number of the level the player plays next. The number is unbounded;
    /// once every level has been played the level data wraps around (<see cref="IndexOf"/>) while the number keeps
    /// growing.
    /// </summary>
    public sealed class PlayerProgress
    {
        /// <summary>
        /// Store key of <see cref="LevelNumber"/>.
        /// </summary>
        public const string LevelNumberKey = "pf.levelNumber";

        private readonly IKeyValueStore _store;

        /// <summary>
        /// Creates progress backed by <paramref name="store"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown if store is null.</exception>
        public PlayerProgress(IKeyValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// 1-based number of the next level to play (1 on a fresh install; corrupt values read as 1).
        /// </summary>
        public int LevelNumber => Math.Max(1, _store.GetInt(LevelNumberKey, 1));

        /// <summary>
        /// Index into a level list of <paramref name="levelCount"/> levels for <see cref="LevelNumber"/>.
        /// </summary>
        public int LevelIndex(int levelCount) => IndexOf(LevelNumber, levelCount);

        /// <summary>
        /// Index into a level list of <paramref name="levelCount"/> levels for the 1-based <paramref name="levelNumber"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if levelCount is not positive.</exception>
        public static int IndexOf(int levelNumber, int levelCount)
        {
            if (levelCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(levelCount));
            return (Math.Max(1, levelNumber) - 1) % levelCount;
        }

        /// <summary>
        /// Records that level <paramref name="levelNumber"/> was won: <see cref="LevelNumber"/> becomes
        /// <c>levelNumber + 1</c> unless it is already further (replaying an old level never moves progress back).
        /// </summary>
        public void CompleteLevel(int levelNumber)
        {
            int next = levelNumber + 1;
            if (next <= LevelNumber)
                return;
            _store.SetInt(LevelNumberKey, next);
            _store.Save();
        }
    }
}

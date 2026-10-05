using System;
using System.Collections.Generic;

namespace PixelFlow.Performance
{
    /// <summary>
    /// Zero-allocation object pool for reusing class instances.
    /// Tracks inactive items with a HashSet to detect double-release errors.
    /// </summary>
    /// <typeparam name="T">The type of objects to pool. Must be a reference type.</typeparam>
    public sealed class ObjectPool<T> where T : class
    {
        private readonly Func<T> _create;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Stack<T> _inactive;
        private readonly HashSet<T> _inactiveSet;
        private int _countAll;

        /// <summary>
        /// Gets the total number of objects managed by this pool (active and inactive).
        /// </summary>
        public int CountAll => _countAll;

        /// <summary>
        /// Gets the number of inactive objects currently available in the pool.
        /// </summary>
        public int CountInactive => _inactive.Count;

        /// <summary>
        /// Creates a new object pool.
        /// </summary>
        /// <param name="create">Factory function to create new instances.</param>
        /// <param name="onGet">Optional callback invoked when an item is retrieved from the pool.</param>
        /// <param name="onRelease">Optional callback invoked when an item is returned to the pool.</param>
        /// <param name="prewarm">Number of instances to create and add to the pool during construction.</param>
        public ObjectPool(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null, int prewarm = 0)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _onGet = onGet;
            _onRelease = onRelease;
            _inactive = new Stack<T>(prewarm);
            _inactiveSet = new HashSet<T>();
            _countAll = 0;

            // Prewarm the pool
            for (int i = 0; i < prewarm; i++)
            {
                T item = _create();
                _inactive.Push(item);
                _inactiveSet.Add(item);
                _countAll++;
            }
        }

        /// <summary>
        /// Creates inactive instances until <see cref="CountAll"/> is at least <paramref name="count"/>. Call it when
        /// building a level: once the pool owns every instance the level needs, its internal containers are
        /// already large enough, so later <see cref="Get"/>/<see cref="Release"/> calls never allocate.
        /// </summary>
        /// <param name="count">Total number of instances the pool should own.</param>
        public void Prewarm(int count)
        {
            while (_countAll < count)
            {
                T item = _create();
                _inactive.Push(item);
                _inactiveSet.Add(item);
                _countAll++;
            }
        }

        /// <summary>
        /// Gets an object from the pool. Creates a new one if the pool is empty.
        /// </summary>
        /// <returns>An object from the pool or a newly created one.</returns>
        public T Get()
        {
            T item;

            if (_inactive.Count > 0)
            {
                item = _inactive.Pop();
                _inactiveSet.Remove(item);
            }
            else
            {
                item = _create();
                _countAll++;
            }

            _onGet?.Invoke(item);
            return item;
        }

        /// <summary>
        /// Returns an object to the pool for reuse.
        /// </summary>
        /// <param name="item">The object to return to the pool.</param>
        /// <exception cref="InvalidOperationException">Thrown if the item is already in the pool (double release).</exception>
        public void Release(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (_inactiveSet.Contains(item))
                throw new InvalidOperationException("Cannot release an item that is already in the pool (double release detected).");

            _onRelease?.Invoke(item);
            _inactive.Push(item);
            _inactiveSet.Add(item);
        }
    }
}

using System;
using System.Collections.Generic;

namespace PixelFlow.Performance
{
    /// <summary>
    /// Zero-allocation FIFO queue that supports time-sliced processing with a budget.
    /// Useful for spreading expensive operations across multiple frames.
    /// </summary>
    /// <typeparam name="T">The type of items in the queue.</typeparam>
    public sealed class TimeSlicedQueue<T>
    {
        private readonly Queue<T> _items;

        /// <summary>
        /// Gets the current number of items in the queue.
        /// </summary>
        public int Count => _items.Count;

        /// <summary>
        /// Creates a new time-sliced queue.
        /// </summary>
        public TimeSlicedQueue()
        {
            _items = new Queue<T>();
        }

        /// <summary>
        /// Creates a new time-sliced queue that can hold <paramref name="capacity"/> items before it has to grow.
        /// </summary>
        /// <param name="capacity">Initial capacity (pre-sizing avoids GC allocations in Enqueue during play).</param>
        public TimeSlicedQueue(int capacity)
        {
            _items = new Queue<T>(capacity);
        }

        /// <summary>
        /// Adds an item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to enqueue.</param>
        public void Enqueue(T item)
        {
            _items.Enqueue(item);
        }

        /// <summary>
        /// Removes all items from the queue.
        /// </summary>
        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>
        /// Processes up to the specified number of items from the queue in FIFO order.
        /// </summary>
        /// <param name="budget">The maximum number of items to process.</param>
        /// <param name="handler">The callback invoked for each processed item.</param>
        /// <returns>The actual number of items processed (may be less than budget if queue is smaller).</returns>
        public int Process(int budget, Action<T> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            int processed = 0;
            int toProcess = Math.Min(budget, _items.Count);

            for (int i = 0; i < toProcess; i++)
            {
                T item = _items.Dequeue();
                handler(item);
                processed++;
            }

            return processed;
        }
    }
}

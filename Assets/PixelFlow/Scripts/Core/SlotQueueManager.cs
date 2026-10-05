using System;
using System.Collections.Generic;

namespace PixelFlow.Core
{
    /// <summary>
    /// Manages a fixed-capacity queue of color tanks in gameplay slots.
    /// Automatically removes depleted tanks and shifts remaining tanks left.
    /// </summary>
    public sealed class SlotQueueManager
    {
        private readonly List<ColorTankModel> _tanks;
        private readonly Action<ColorTankModel> _onTankDepletedHandler;

        /// <summary>
        /// Maximum number of tanks this manager can hold.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Current number of tanks in the queue.
        /// </summary>
        public int Count => _tanks.Count;

        /// <summary>
        /// Whether the queue is at full capacity.
        /// </summary>
        public bool IsFull => _tanks.Count >= Capacity;

        /// <summary>
        /// Raised when a tank is added to the queue. Parameters: (tank, slotIndex).
        /// </summary>
        public event Action<ColorTankModel, int> OnTankAdded;

        /// <summary>
        /// Raised when a tank is removed from the queue. Parameters: (tank, oldIndex).
        /// </summary>
        public event Action<ColorTankModel, int> OnTankRemoved;

        /// <summary>
        /// Gets the tank at the specified slot index.
        /// </summary>
        /// <param name="index">Slot index (0-based).</param>
        /// <returns>The tank at the specified index.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
        public ColorTankModel this[int index] => _tanks[index];

        /// <summary>
        /// Creates a new slot queue manager with the specified capacity.
        /// </summary>
        /// <param name="capacity">Maximum number of tanks that can be held.</param>
        public SlotQueueManager(int capacity)
        {
            Capacity = capacity;
            _tanks = new List<ColorTankModel>(capacity);
            _onTankDepletedHandler = HandleTankDepleted;
        }

        /// <summary>
        /// Gets the index of the specified tank in the queue.
        /// </summary>
        /// <param name="tank">The tank to find.</param>
        /// <returns>The index of the tank, or -1 if not found.</returns>
        public int IndexOf(ColorTankModel tank)
        {
            return _tanks.IndexOf(tank);
        }

        /// <summary>
        /// Attempts to add a tank to the end of the queue.
        /// Subscribes to the tank's OnDepleted event for automatic removal.
        /// </summary>
        /// <param name="tank">The tank to add.</param>
        /// <returns>True if the tank was added; false if the queue is full.</returns>
        public bool TryAdd(ColorTankModel tank)
        {
            if (IsFull)
            {
                return false;
            }

            int index = _tanks.Count;
            _tanks.Add(tank);
            tank.OnDepleted += _onTankDepletedHandler;
            OnTankAdded?.Invoke(tank, index);
            return true;
        }

        /// <summary>
        /// Handles automatic removal of depleted tanks.
        /// Called via event subscription when a tank depletes.
        /// </summary>
        private void HandleTankDepleted(ColorTankModel tank)
        {
            int index = _tanks.IndexOf(tank);
            if (index < 0)
            {
                return; // Tank not in this manager
            }

            tank.OnDepleted -= _onTankDepletedHandler;
            _tanks.RemoveAt(index);
            OnTankRemoved?.Invoke(tank, index);
        }
    }
}

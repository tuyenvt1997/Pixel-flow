using System;

namespace PixelFlow.Core
{
    /// <summary>
    /// Manages tanks on the conveyor belt and in the entrance queue.
    /// Tracks tank positions, visited counts, and handles belt capacity.
    /// </summary>
    public sealed class BeltModel
    {
        private readonly BeltPath _path;

        // Belt tanks (in entry order)
        private readonly ColorTankModel[] _tanks;
        private readonly int[] _positions;
        private readonly int[] _visited;
        private int _count;

        // Entrance queue (ring buffer)
        private readonly ColorTankModel[] _queue;
        private int _queueHead;
        private int _queueCount;

        /// <summary>
        /// Maximum number of tanks that can be on the belt or in the queue combined.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Current number of tanks on the belt.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Current number of tanks in the entrance queue.
        /// </summary>
        public int QueuedCount => _queueCount;

        /// <summary>
        /// Whether the belt is full (cannot accept more tanks).
        /// </summary>
        public bool IsFull => _count + _queueCount >= Capacity;

        /// <summary>
        /// Raised when a tank is added to the entrance queue.
        /// </summary>
        public event Action<ColorTankModel> OnTankQueued;

        /// <summary>
        /// Raised when a tank enters the belt from the queue.
        /// </summary>
        public event Action<ColorTankModel> OnTankEntered;

        /// <summary>
        /// Raised when a tank moves to a new position. Parameters: (tank, newPosition).
        /// </summary>
        public event Action<ColorTankModel, int> OnTankMoved;

        /// <summary>
        /// Raised when a tank leaves the belt. Parameters: (tank, lapCompleted).
        /// </summary>
        public event Action<ColorTankModel, bool> OnTankLeft;

        /// <summary>
        /// Creates a new belt model with the specified capacity and path.
        /// </summary>
        /// <param name="capacity">Maximum number of tanks on belt and in queue combined.</param>
        /// <param name="path">The belt path defining the loop geometry.</param>
        public BeltModel(int capacity, BeltPath path)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            }

            Capacity = capacity;
            _path = path;

            // Allocate fixed-size arrays
            _tanks = new ColorTankModel[capacity];
            _positions = new int[capacity];
            _visited = new int[capacity];
            _queue = new ColorTankModel[capacity];
        }

        /// <summary>
        /// Attempts to launch a tank by adding it to the entrance queue.
        /// </summary>
        /// <param name="tank">The tank to launch.</param>
        /// <returns>True if the tank was queued; false if the belt is full.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tank is null.</exception>
        /// <exception cref="ArgumentException">Thrown when tank is depleted or already on the belt/queue.</exception>
        public bool TryLaunch(ColorTankModel tank)
        {
            if (tank == null)
            {
                throw new ArgumentNullException(nameof(tank));
            }

            if (tank.IsDepleted)
            {
                throw new ArgumentException("Cannot launch a depleted tank.", nameof(tank));
            }

            // Check if tank is already on belt
            for (int i = 0; i < _count; i++)
            {
                if (_tanks[i] == tank)
                {
                    throw new ArgumentException("Tank is already on the belt.", nameof(tank));
                }
            }

            // Check if tank is already in queue
            for (int i = 0; i < _queueCount; i++)
            {
                int index = (_queueHead + i) % Capacity;
                if (_queue[index] == tank)
                {
                    throw new ArgumentException("Tank is already in the queue.", nameof(tank));
                }
            }

            if (IsFull)
            {
                return false;
            }

            // Add to queue
            int tailIndex = (_queueHead + _queueCount) % Capacity;
            _queue[tailIndex] = tank;
            _queueCount++;

            OnTankQueued?.Invoke(tank);
            return true;
        }

        /// <summary>
        /// Attempts to admit the first queued tank onto the belt at position 0.
        /// </summary>
        /// <returns>True if a tank was admitted; false if the queue is empty or position 0 is occupied.</returns>
        public bool TryAdmitFromQueue()
        {
            if (_queueCount == 0)
            {
                return false;
            }

            // Check if position 0 is occupied
            for (int i = 0; i < _count; i++)
            {
                if (_positions[i] == 0)
                {
                    return false;
                }
            }

            // Dequeue and add to belt
            ColorTankModel tank = _queue[_queueHead];
            _queue[_queueHead] = null;
            _queueHead = (_queueHead + 1) % Capacity;
            _queueCount--;

            // Add to belt at position 0
            _tanks[_count] = tank;
            _positions[_count] = 0;
            _visited[_count] = 0;
            _count++;

            OnTankEntered?.Invoke(tank);
            OnTankMoved?.Invoke(tank, 0);
            return true;
        }

        /// <summary>
        /// Gets the tank at the specified belt index (in entry order).
        /// </summary>
        /// <param name="index">Belt index in range [0, Count).</param>
        /// <returns>The tank at the specified index.</returns>
        public ColorTankModel TankAt(int index)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _tanks[index];
        }

        /// <summary>
        /// Gets the position of the tank at the specified belt index.
        /// </summary>
        /// <param name="index">Belt index in range [0, Count).</param>
        /// <returns>The belt position of the tank.</returns>
        public int PositionAt(int index)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _positions[index];
        }

        /// <summary>
        /// Gets the number of positions visited by the tank at the specified belt index.
        /// </summary>
        /// <param name="index">Belt index in range [0, Count).</param>
        /// <returns>The visited count.</returns>
        public int VisitedAt(int index)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _visited[index];
        }

        /// <summary>
        /// Attempts to get the position of a tank on the belt.
        /// </summary>
        /// <param name="tank">The tank to find.</param>
        /// <param name="position">Output: the tank's position if found.</param>
        /// <returns>True if the tank is on the belt; false otherwise.</returns>
        public bool TryGetPosition(ColorTankModel tank, out int position)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_tanks[i] == tank)
                {
                    position = _positions[i];
                    return true;
                }
            }

            position = 0;
            return false;
        }

        /// <summary>
        /// Increments the visited count for the tank at the specified index.
        /// </summary>
        /// <param name="index">Belt index in range [0, Count).</param>
        public void MarkVisited(int index)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            _visited[index]++;
        }

        /// <summary>
        /// Advances the tank at the specified index to the next position on the belt.
        /// </summary>
        /// <param name="index">Belt index in range [0, Count).</param>
        public void Advance(int index)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int newPosition = (_positions[index] + 1) % _path.Length;
            _positions[index] = newPosition;

            OnTankMoved?.Invoke(_tanks[index], newPosition);
        }

        /// <summary>
        /// Removes the tank at the specified index from the belt.
        /// </summary>
        /// <param name="index">Belt index in range [0, Count).</param>
        /// <param name="lapCompleted">Whether the tank completed a full lap.</param>
        public void RemoveAt(int index, bool lapCompleted)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            ColorTankModel tank = _tanks[index];

            // Shift remaining tanks down to maintain entry order
            for (int i = index; i < _count - 1; i++)
            {
                _tanks[i] = _tanks[i + 1];
                _positions[i] = _positions[i + 1];
                _visited[i] = _visited[i + 1];
            }

            // Clear the last slot
            _tanks[_count - 1] = null;
            _positions[_count - 1] = 0;
            _visited[_count - 1] = 0;
            _count--;

            OnTankLeft?.Invoke(tank, lapCompleted);
        }

        /// <summary>
        /// Clears all tanks from the belt and the entrance queue.
        /// </summary>
        public void Clear()
        {
            // Clear belt
            for (int i = 0; i < _count; i++)
            {
                _tanks[i] = null;
                _positions[i] = 0;
                _visited[i] = 0;
            }
            _count = 0;

            // Clear queue
            for (int i = 0; i < _queueCount; i++)
            {
                int index = (_queueHead + i) % Capacity;
                _queue[index] = null;
            }
            _queueHead = 0;
            _queueCount = 0;
        }
    }
}

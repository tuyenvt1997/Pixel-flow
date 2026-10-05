using System;
using System.Collections.Generic;

namespace PixelFlow.Core
{
    /// <summary>
    /// Manages supply lanes of color tanks distributed in round-robin fashion.
    /// Supports efficient front removal without shifting remaining tanks.
    /// </summary>
    public sealed class SupplyModel
    {
        private readonly Lane[] _lanes;
        private int _totalRemaining;

        /// <summary>
        /// Number of supply lanes.
        /// </summary>
        public int LaneCount { get; }

        /// <summary>
        /// Total number of tanks remaining across all lanes.
        /// </summary>
        public int TotalRemaining => _totalRemaining;

        /// <summary>
        /// Whether all lanes are empty.
        /// </summary>
        public bool IsEmpty => _totalRemaining == 0;

        /// <summary>
        /// Raised when a lane's contents change. Parameter: (laneIndex).
        /// </summary>
        public event Action<int> OnLaneChanged;

        /// <summary>
        /// Creates a supply model by distributing tanks round-robin into lanes.
        /// </summary>
        /// <param name="tanks">Tanks to distribute (in order).</param>
        /// <param name="laneCount">Number of lanes to distribute into.</param>
        public SupplyModel(IReadOnlyList<ColorTankModel> tanks, int laneCount)
        {
            LaneCount = laneCount;
            _lanes = new Lane[laneCount];
            _totalRemaining = tanks.Count;

            // Initialize lanes
            for (int i = 0; i < laneCount; i++)
            {
                _lanes[i] = new Lane();
            }

            // Distribute tanks round-robin
            for (int i = 0; i < tanks.Count; i++)
            {
                int laneIndex = i % laneCount;
                _lanes[laneIndex].Tanks.Add(tanks[i]);
            }
        }

        /// <summary>
        /// Gets a read-only view of the specified lane.
        /// Index 0 is the front of the lane (next tank to be taken).
        /// </summary>
        /// <param name="lane">Lane index (0-based).</param>
        /// <returns>Read-only list of tanks in the lane, front to back.</returns>
        public IReadOnlyList<ColorTankModel> GetLane(int lane)
        {
            return _lanes[lane].GetView();
        }

        /// <summary>
        /// Peeks at the front tank of the specified lane without removing it.
        /// </summary>
        /// <param name="lane">Lane index (0-based).</param>
        /// <returns>The front tank, or null if the lane is empty.</returns>
        public ColorTankModel PeekFront(int lane)
        {
            var laneData = _lanes[lane];
            if (laneData.HeadIndex >= laneData.Tanks.Count)
            {
                return null;
            }
            return laneData.Tanks[laneData.HeadIndex];
        }

        /// <summary>
        /// Finds the lane index where the specified tank is at the front.
        /// </summary>
        /// <param name="tank">The tank to search for.</param>
        /// <returns>Lane index if the tank is at the front of a lane; -1 otherwise.</returns>
        public int FindLaneWithFront(ColorTankModel tank)
        {
            for (int i = 0; i < _lanes.Length; i++)
            {
                var laneData = _lanes[i];
                if (laneData.HeadIndex < laneData.Tanks.Count &&
                    laneData.Tanks[laneData.HeadIndex] == tank)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Attempts to take the front tank from the specified lane.
        /// </summary>
        /// <param name="lane">Lane index (0-based).</param>
        /// <param name="tank">Output parameter receiving the removed tank, or null if the lane is empty.</param>
        /// <returns>True if a tank was removed; false if the lane was empty.</returns>
        public bool TryTakeFront(int lane, out ColorTankModel tank)
        {
            var laneData = _lanes[lane];
            if (laneData.HeadIndex >= laneData.Tanks.Count)
            {
                tank = null;
                return false;
            }

            tank = laneData.Tanks[laneData.HeadIndex];
            laneData.HeadIndex++;
            _totalRemaining--;
            OnLaneChanged?.Invoke(lane);
            return true;
        }

        /// <summary>
        /// Internal lane data structure.
        /// Uses a head index to avoid shifting on front removal.
        /// </summary>
        private sealed class Lane
        {
            public List<ColorTankModel> Tanks { get; }
            public int HeadIndex { get; set; }
            private LaneView _view;

            public Lane()
            {
                Tanks = new List<ColorTankModel>();
                HeadIndex = 0;
            }

            public IReadOnlyList<ColorTankModel> GetView()
            {
                if (_view == null)
                {
                    _view = new LaneView(this);
                }
                return _view;
            }
        }

        /// <summary>
        /// Read-only view of a lane that respects the head index.
        /// Index 0 maps to the front (head) of the lane.
        /// </summary>
        private sealed class LaneView : IReadOnlyList<ColorTankModel>
        {
            private readonly Lane _lane;

            public LaneView(Lane lane)
            {
                _lane = lane;
            }

            public int Count => Math.Max(0, _lane.Tanks.Count - _lane.HeadIndex);

            public ColorTankModel this[int index]
            {
                get
                {
                    if (index < 0 || index >= Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    return _lane.Tanks[_lane.HeadIndex + index];
                }
            }

            public IEnumerator<ColorTankModel> GetEnumerator()
            {
                for (int i = _lane.HeadIndex; i < _lane.Tanks.Count; i++)
                {
                    yield return _lane.Tanks[i];
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}

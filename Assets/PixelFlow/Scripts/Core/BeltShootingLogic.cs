using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Core
{
    /// <summary>
    /// Runs one belt tick: every belt tank fires at the front cell of its position, then leaves, goes to a
    /// waiting slot or advances, and finally one queued tank may enter. Allocation-free.
    /// </summary>
    public sealed class BeltShootingLogic
    {
        private readonly PixelGridModel _grid;
        private readonly BeltModel _belt;
        private readonly SlotQueueManager _waitingSlots;
        private readonly BeltPath _path;

        /// <summary>
        /// True once a tank finished its lap with ammo left while every waiting slot was full. Latched.
        /// </summary>
        public bool Overflowed { get; private set; }

        /// <summary>
        /// Raised when a tank finishes its lap with ammo left while every waiting slot is full. The tank has
        /// already left the belt (<see cref="BeltModel.OnTankLeft"/> with <c>lapCompleted = true</c>) and is in no
        /// container any more; <see cref="Overflowed"/> is already true when this is raised.
        /// </summary>
        public event Action<ColorTankModel> OnOverflow;

        /// <summary>
        /// Creates the belt shooting logic.
        /// </summary>
        /// <param name="grid">The pixel grid the tanks shoot at.</param>
        /// <param name="belt">The belt holding the riding and queued tanks; its <see cref="BeltModel.Path"/> is used.</param>
        /// <param name="waitingSlots">The waiting slots that receive tanks finishing a lap with ammo left.</param>
        /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the belt path's size does not match the grid's size.</exception>
        public BeltShootingLogic(PixelGridModel grid, BeltModel belt, SlotQueueManager waitingSlots)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _belt = belt ?? throw new ArgumentNullException(nameof(belt));
            _waitingSlots = waitingSlots ?? throw new ArgumentNullException(nameof(waitingSlots));
            _path = belt.Path;
            if (_path.Width != grid.Width || _path.Height != grid.Height)
            {
                throw new ArgumentException(
                    $"Belt path is {_path.Width}x{_path.Height} but the grid is {grid.Width}x{grid.Height}.",
                    nameof(belt));
            }
        }

        /// <summary>
        /// Processes every belt tank in entry order (fire, depletion, visit count, lap completion, advance),
        /// then admits at most one queued tank at position 0. Does not clear <paramref name="output"/>.
        /// </summary>
        /// <param name="output">List the shots fired this tick are appended to.</param>
        /// <returns>The number of shots fired this tick.</returns>
        public int Tick(List<ShotEvent> output)
        {
            int shots = 0;
            int lapLength = _path.Length;
            int i = 0;
            while (i < _belt.Count)
            {
                ColorTankModel tank = _belt.TankAt(i);
                int position = _belt.PositionAt(i);

                // (a) Fire at the front cell if its colour matches.
                _path.Resolve(position, out BoardSide side, out int line);
                if (_grid.TryGetFront(side, line, out Vector2Int cell) && _grid.GetCell(cell.x, cell.y) == tank.ColorId)
                {
                    _grid.RemoveCell(cell);
                    output.Add(new ShotEvent(tank, position, cell, tank.ColorId));
                    shots++;
                    tank.TryConsume();
                }

                // (b) Depleted: leave the belt. RemoveAt shifts the next tank into index i.
                if (tank.IsDepleted)
                {
                    _belt.RemoveAt(i, false);
                    continue;
                }

                // (c) Count the visit.
                _belt.MarkVisited(i);

                // (d) Lap done: leave with ammo and go to the first free waiting slot.
                if (_belt.VisitedAt(i) >= lapLength)
                {
                    _belt.RemoveAt(i, true);
                    if (!_waitingSlots.TryAdd(tank))
                    {
                        Overflowed = true;
                        OnOverflow?.Invoke(tank);
                    }

                    continue;
                }

                // (e) Advance.
                _belt.Advance(i);
                i++;
            }

            _belt.TryAdmitFromQueue();
            return shots;
        }

        /// <summary>
        /// True if some waiting-slot tank's colour is the colour of a front cell on any side. Allocation-free.
        /// </summary>
        /// <returns>Whether relaunching a waiting tank could still hit something.</returns>
        public bool CanAnyWaitingTankHit()
        {
            for (int i = 0; i < _waitingSlots.Count; i++)
            {
                if (_grid.HasAnyFront(_waitingSlots[i].ColorId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

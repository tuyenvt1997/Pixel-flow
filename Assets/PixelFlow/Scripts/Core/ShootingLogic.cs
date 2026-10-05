using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Core
{
    /// <summary>
    /// Handles shooting logic for tanks in the tray, firing at exposed cells.
    /// </summary>
    public sealed class ShootingLogic
    {
        private readonly PixelGridModel _grid;
        private readonly SlotQueueManager _tray;
        private readonly ColorTankModel[] _tankBuffer;

        /// <summary>
        /// Creates a new shooting logic instance.
        /// </summary>
        /// <param name="grid">The pixel grid model.</param>
        /// <param name="tray">The slot queue manager.</param>
        public ShootingLogic(PixelGridModel grid, SlotQueueManager tray)
        {
            _grid = grid;
            _tray = tray;
            _tankBuffer = new ColorTankModel[tray.Capacity];
        }

        /// <summary>
        /// Executes one shooting step where each tank in the tray attempts to fire once.
        /// Does not clear the output list (caller must clear between steps if reusing).
        /// </summary>
        /// <param name="output">List to append shot events to.</param>
        /// <returns>The number of shots fired.</returns>
        public int Step(List<ShotEvent> output)
        {
            // Snapshot current tanks to avoid issues with tray shrinking during iteration
            int tankCount = _tray.Count;
            for (int i = 0; i < tankCount; i++)
            {
                _tankBuffer[i] = _tray[i];
            }

            int shotCount = 0;

            // Process each tank in snapshot order
            for (int i = 0; i < tankCount; i++)
            {
                ColorTankModel tank = _tankBuffer[i];

                // Try to find an exposed cell of this tank's color
                if (_grid.TryGetExposedCell(tank.ColorId, out Vector2Int cell))
                {
                    // Remove the cell from the grid
                    _grid.RemoveCell(cell);

                    // Record the shot event with the snapshot slot index
                    output.Add(new ShotEvent(tank, i, cell, tank.ColorId));
                    shotCount++;

                    // Consume ammo (this may trigger depletion and auto-removal from tray)
                    tank.TryConsume();
                }
            }

            return shotCount;
        }

        /// <summary>
        /// Checks if any tank in the tray can fire at an exposed cell.
        /// Zero-allocation method suitable for per-frame evaluation.
        /// </summary>
        /// <returns>True if at least one tank can fire; false otherwise.</returns>
        public bool CanAnyTankFire()
        {
            for (int i = 0; i < _tray.Count; i++)
            {
                if (_grid.HasExposed(_tray[i].ColorId))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

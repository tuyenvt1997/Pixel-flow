using UnityEngine;

namespace PixelFlow.Core
{
    /// <summary>
    /// Represents a single shot fired by a tank at a grid cell.
    /// </summary>
    public readonly struct ShotEvent
    {
        /// <summary>
        /// The tank that fired this shot.
        /// </summary>
        public readonly ColorTankModel Tank;

        /// <summary>
        /// The slot index of the tank at the time of firing (snapshot index).
        /// </summary>
        public readonly int SlotIndex;

        /// <summary>
        /// The grid cell position that was hit.
        /// </summary>
        public readonly Vector2Int Cell;

        /// <summary>
        /// The color ID of the shot.
        /// </summary>
        public readonly byte ColorId;

        /// <summary>
        /// Creates a new shot event.
        /// </summary>
        /// <param name="tank">The tank that fired.</param>
        /// <param name="slotIndex">The slot index at time of firing.</param>
        /// <param name="cell">The cell position that was hit.</param>
        /// <param name="colorId">The color ID of the shot.</param>
        public ShotEvent(ColorTankModel tank, int slotIndex, Vector2Int cell, byte colorId)
        {
            Tank = tank;
            SlotIndex = slotIndex;
            Cell = cell;
            ColorId = colorId;
        }
    }
}

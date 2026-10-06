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
        /// The slot index of the tank at the time of firing (snapshot index), or -1 for a belt shot.
        /// </summary>
        public readonly int SlotIndex;

        /// <summary>
        /// The belt position of the tank at the time of firing, or -1 for a shot not fired from the belt.
        /// </summary>
        public readonly int BeltPosition;

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
            : this(tank, slotIndex, -1, cell, colorId)
        {
        }

        private ShotEvent(ColorTankModel tank, int slotIndex, int beltPosition, Vector2Int cell, byte colorId)
        {
            Tank = tank;
            SlotIndex = slotIndex;
            BeltPosition = beltPosition;
            Cell = cell;
            ColorId = colorId;
        }

        /// <summary>
        /// Creates a shot fired from the belt. <see cref="SlotIndex"/> is -1.
        /// </summary>
        /// <param name="tank">The tank that fired.</param>
        /// <param name="beltPosition">The tank's belt position at the time of firing.</param>
        /// <param name="cell">The cell position that was hit.</param>
        /// <param name="colorId">The color ID of the shot.</param>
        /// <returns>The shot event.</returns>
        public static ShotEvent FromBelt(ColorTankModel tank, int beltPosition, Vector2Int cell, byte colorId)
        {
            return new ShotEvent(tank, -1, beltPosition, cell, colorId);
        }
    }
}

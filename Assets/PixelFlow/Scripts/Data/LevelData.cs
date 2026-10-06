using UnityEngine;

namespace PixelFlow.Data
{
    /// <summary>
    /// ScriptableObject representing a Pixel Flow level with grid data, color palette, and supply tanks.
    /// </summary>
    [CreateAssetMenu(menuName = "PixelFlow/Level Data")]
    public class LevelData : ScriptableObject
    {
        /// <summary>
        /// Constant value representing an empty cell.
        /// </summary>
        public const byte EmptyCell = 255;

        /// <summary>
        /// Width of the level grid (in cells).
        /// </summary>
        public int width;

        /// <summary>
        /// Height of the level grid (in cells).
        /// </summary>
        public int height;

        /// <summary>
        /// Color palette for the level. Each colorId in cells/tanks indexes into this array.
        /// </summary>
        public Color32[] palette;

        /// <summary>
        /// Cell data in row-major order. Index = y * width + x, where y=0 is the bottom row.
        /// Values are palette indices (0-253) or EmptyCell (255).
        /// </summary>
        public byte[] cells;

        /// <summary>
        /// Color tanks available as supply for the player.
        /// </summary>
        public ColorTankData[] tanks;

        /// <summary>
        /// Number of supply lanes for distributing tanks.
        /// </summary>
        public int laneCount = 3;

        /// <summary>
        /// Number of waiting slots; also the belt capacity (tanks on the belt plus its entrance queue).
        /// </summary>
        public int slotCount = 5;

        /// <summary>
        /// Gets the cell value at the specified grid coordinates.
        /// </summary>
        /// <param name="x">X coordinate (0 to width-1).</param>
        /// <param name="y">Y coordinate (0 to height-1, where 0 is the bottom row).</param>
        /// <returns>The color ID at the specified position, or EmptyCell if empty.</returns>
        public byte GetCell(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return EmptyCell;

            return cells[y * width + x];
        }
    }
}

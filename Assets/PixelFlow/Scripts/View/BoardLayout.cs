using System;
using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Maps grid cells to world positions on the XY plane (z = 0). Cell (0, 0) is the bottom-left cell.
    /// </summary>
    [Serializable]
    public struct BoardLayout
    {
        /// <summary>
        /// World position of the bottom-left corner of cell (0, 0).
        /// </summary>
        public Vector3 Origin;

        /// <summary>
        /// World-space edge length of one cell.
        /// </summary>
        public float CellSize;

        /// <summary>
        /// Returns the world-space centre of the given cell, with z = 0.
        /// </summary>
        /// <param name="cell">Grid coordinates (x right, y up).</param>
        /// <returns>The cell centre in world space.</returns>
        public Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(
                Origin.x + (cell.x + 0.5f) * CellSize,
                Origin.y + (cell.y + 0.5f) * CellSize,
                0f);
        }

        /// <summary>
        /// Builds the largest square-cell layout for a <paramref name="width"/> x <paramref name="height"/> grid
        /// that fits inside <paramref name="worldArea"/>, centred in that area.
        /// CellSize = min(area.width / width, area.height / height).
        /// </summary>
        /// <param name="width">Grid width in cells (must be &gt; 0).</param>
        /// <param name="height">Grid height in cells (must be &gt; 0).</param>
        /// <param name="worldArea">World-space rectangle on the XY plane to fit the board into.</param>
        /// <returns>The fitted layout.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if width or height is not positive.</exception>
        public static BoardLayout Fit(int width, int height, Rect worldArea)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "width must be positive");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "height must be positive");

            float cellSize = Mathf.Min(worldArea.width / width, worldArea.height / height);
            float boardWidth = cellSize * width;
            float boardHeight = cellSize * height;

            return new BoardLayout
            {
                Origin = new Vector3(
                    worldArea.x + (worldArea.width - boardWidth) * 0.5f,
                    worldArea.y + (worldArea.height - boardHeight) * 0.5f,
                    0f),
                CellSize = cellSize,
            };
        }
    }
}

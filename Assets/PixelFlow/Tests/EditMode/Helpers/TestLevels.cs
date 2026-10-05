using PixelFlow.Data;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Helper for creating LevelData instances in tests.
    /// </summary>
    public static class TestLevels
    {
        /// <summary>
        /// Creates a LevelData instance from ASCII art rows.
        /// </summary>
        /// <param name="rows">Row strings where rows[0] is the TOP row, '.' = empty, '0'-'9' = colorId.</param>
        /// <param name="palette">Color palette for the level.</param>
        /// <param name="tanks">Color tanks for the level.</param>
        /// <param name="lanes">Number of supply lanes (default 1).</param>
        /// <param name="slots">Number of tray slots (default 5).</param>
        /// <returns>A configured LevelData instance.</returns>
        public static LevelData Create(string[] rows, Color32[] palette, ColorTankData[] tanks, int lanes = 1, int slots = 5)
        {
            if (rows == null || rows.Length == 0)
                throw new System.ArgumentException("Rows cannot be null or empty", nameof(rows));

            int width = rows[0].Length;
            int height = rows.Length;

            var level = ScriptableObject.CreateInstance<LevelData>();
            level.width = width;
            level.height = height;
            level.palette = palette;
            level.laneCount = lanes;
            level.slotCount = slots;
            level.tanks = tanks;

            // Allocate cells array (row-major, y=0 is bottom)
            level.cells = new byte[width * height];

            // Fill cells: rows[0] is TOP row, so flip it
            // When rowIndex=0 (top), that should be y=height-1 in cells
            for (int rowIndex = 0; rowIndex < height; rowIndex++)
            {
                int y = height - 1 - rowIndex; // flip: top row → y=height-1
                string row = rows[rowIndex];

                for (int x = 0; x < width; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    byte cellValue;

                    if (c == '.')
                    {
                        cellValue = LevelData.EmptyCell;
                    }
                    else if (c >= '0' && c <= '9')
                    {
                        cellValue = (byte)(c - '0');
                    }
                    else
                    {
                        throw new System.ArgumentException($"Invalid character '{c}' in row {rowIndex} at position {x}");
                    }

                    level.cells[y * width + x] = cellValue;
                }
            }

            return level;
        }
    }
}

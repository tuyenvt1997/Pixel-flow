using System;
using System.Collections.Generic;
using PixelFlow.Data;

namespace PixelFlow.Core
{
    /// <summary>
    /// Generates the supply tanks for a level from its cell layout.
    /// </summary>
    /// <remarks>
    /// The grid is "peeled" layer by layer: each round removes every currently exposed cell
    /// (the lowest non-empty cell of each column), visiting columns 0 to width-1, and gives each
    /// removed cell an increasing peel time <c>t</c>. For each color, its cells are taken in peel order
    /// and chunked into tanks of at most <c>ammoPerTank</c> shots. The sort key of a tank is the peel time
    /// of the first cell in its chunk; tanks are returned in ascending key order (ties broken by colorId),
    /// so colors reach the tray roughly when their cells become exposed.
    /// </remarks>
    public static class TankGenerator
    {
        private struct KeyedTank
        {
            public int Key;
            public ColorTankData Tank;
        }

        /// <summary>
        /// Builds the ordered list of tanks for the given cells.
        /// </summary>
        /// <param name="width">Grid width in cells (must be positive).</param>
        /// <param name="height">Grid height in cells (must be positive).</param>
        /// <param name="cells">Row-major cells (index = y * width + x, y = 0 is the bottom row); <see cref="LevelData.EmptyCell"/> = empty.</param>
        /// <param name="ammoPerTank">Maximum number of shots per tank (must be at least 1).</param>
        /// <returns>Tanks sorted by the peel time of their first cell; total ammo per color equals that color's cell count.</returns>
        /// <exception cref="ArgumentException">Thrown when dimensions, cell count or ammoPerTank are invalid.</exception>
        public static ColorTankData[] Generate(int width, int height, byte[] cells, int ammoPerTank)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"Grid size must be positive, got {width}x{height}.");
            if (cells == null || cells.Length != width * height)
                throw new ArgumentException($"Expected {width * height} cells.", nameof(cells));
            if (ammoPerTank < 1)
                throw new ArgumentException("ammoPerTank must be at least 1.", nameof(ammoPerTank));

            // Front index per column = y of the lowest non-empty cell not yet peeled (height = column empty).
            var front = new int[width];
            int remaining = 0;
            for (int x = 0; x < width; x++)
            {
                front[x] = NextFilled(cells, width, height, x, 0);
                for (int y = 0; y < height; y++)
                {
                    if (cells[y * width + x] != LevelData.EmptyCell) remaining++;
                }
            }

            // Peel round by round; t increases with each removed cell.
            var keyed = new List<KeyedTank>();
            var openTank = new int[256];       // index into keyed of the color's current tank, -1 = none
            for (int i = 0; i < openTank.Length; i++) openTank[i] = -1;

            int t = 0;
            while (remaining > 0)
            {
                for (int x = 0; x < width; x++)
                {
                    int y = front[x];
                    if (y >= height) continue;

                    byte color = cells[y * width + x];
                    int open = openTank[color];
                    if (open >= 0 && keyed[open].Tank.ammo < ammoPerTank)
                    {
                        KeyedTank k = keyed[open];
                        k.Tank.ammo++;
                        keyed[open] = k;
                    }
                    else
                    {
                        openTank[color] = keyed.Count;
                        keyed.Add(new KeyedTank { Key = t, Tank = new ColorTankData { colorId = color, ammo = 1 } });
                    }

                    t++;
                    remaining--;
                    front[x] = NextFilled(cells, width, height, x, y + 1);
                }
            }

            keyed.Sort(CompareKeyed);

            var result = new ColorTankData[keyed.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = keyed[i].Tank;
            }
            return result;
        }

        private static int NextFilled(byte[] cells, int width, int height, int x, int fromY)
        {
            int y = fromY;
            while (y < height && cells[y * width + x] == LevelData.EmptyCell) y++;
            return y;
        }

        private static int CompareKeyed(KeyedTank a, KeyedTank b)
        {
            int c = a.Key.CompareTo(b.Key);
            return c != 0 ? c : a.Tank.colorId.CompareTo(b.Tank.colorId);
        }
    }
}

using System;
using System.Collections.Generic;
using PixelFlow.Data;
using UnityEngine;

namespace PixelFlow.Core
{
    /// <summary>
    /// Generates the supply tanks for a level from its cell layout.
    /// </summary>
    /// <remarks>
    /// The grid is "peeled" by belt sweeps: each round visits positions <c>0..L-1</c> of
    /// <see cref="BeltPath"/>(width, height) and removes that position's current front cell, so a cell already
    /// removed earlier in the round is skipped and the next cell inward on that line is taken instead. Each removed
    /// cell gets an increasing peel time <c>t</c>; rounds repeat until the grid is empty. For each color, its cells
    /// are taken in peel order and chunked into tanks of at most <c>ammoPerTank</c> shots. The sort key of a tank is
    /// the peel time of the first cell in its chunk; tanks are returned in ascending key order (ties broken by
    /// colorId), so colors reach the belt roughly when their cells reach a front.
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

            // Scratch grid: its four-sided fronts give each belt position's current front cell.
            var grid = new PixelGridModel(width, height, cells);
            var path = new BeltPath(width, height);

            // Peel round by round; t increases with each removed cell.
            var keyed = new List<KeyedTank>();
            var openTank = new int[256];       // index into keyed of the color's current tank, -1 = none
            for (int i = 0; i < openTank.Length; i++) openTank[i] = -1;

            int t = 0;
            while (grid.RemainingCount > 0)
            {
                for (int position = 0; position < path.Length; position++)
                {
                    path.Resolve(position, out BoardSide side, out int line);
                    if (!grid.TryGetFront(side, line, out Vector2Int cell)) continue;

                    byte color = grid.GetCell(cell.x, cell.y);
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
                    grid.RemoveCell(cell);
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

        private static int CompareKeyed(KeyedTank a, KeyedTank b)
        {
            int c = a.Key.CompareTo(b.Key);
            return c != 0 ? c : a.Tank.colorId.CompareTo(b.Tank.colorId);
        }
    }
}

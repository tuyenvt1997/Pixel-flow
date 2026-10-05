using System;
using System.Collections.Generic;
using PixelFlow.Data;
using UnityEngine;

namespace PixelFlow.Core
{
    /// <summary>
    /// Core board model for Pixel Flow game. Maintains grid state and provides O(1) operations
    /// for querying exposed cells and removing them. Optimized for zero-allocation hot paths.
    /// </summary>
    public sealed class PixelGridModel
    {
        private readonly byte[] _cells;
        private readonly int _width;
        private readonly int _height;
        private int _remainingCount;

        // Color tracking: Dictionary-backed lists for GetCellsOfColor
        private readonly Dictionary<int, List<Vector2Int>> _cellsByColor;
        private readonly int[] _indexInColorList; // Maps cell position to index in its color list

        // Exposed cell tracking: O(1) lookup per column and per color
        private readonly int[] _frontRow; // Y-coordinate of exposed cell per column (-1 if empty)
        private readonly int[] _exposedColorInColumn; // Color ID of exposed cell per column (-1 if empty)
        private readonly Dictionary<int, List<int>> _exposedColumnsByColor; // Columns where each color is exposed
        private readonly int[] _columnIndexInExposedList; // Index of column in its color's exposed list

        // Shared empty list for colors with no cells (zero-alloc for missing colors)
        private static readonly List<Vector2Int> _emptyList = new List<Vector2Int>(0);

        /// <summary>
        /// Width of the grid in cells.
        /// </summary>
        public int Width => _width;

        /// <summary>
        /// Height of the grid in cells.
        /// </summary>
        public int Height => _height;

        /// <summary>
        /// Number of non-empty cells remaining in the grid.
        /// </summary>
        public int RemainingCount => _remainingCount;

        /// <summary>
        /// Raised when a cell is removed from the grid.
        /// </summary>
        public event Action<Vector2Int, int> OnCellRemoved;

        /// <summary>
        /// Raised when all cells have been removed (RemainingCount reaches 0).
        /// </summary>
        public event Action OnCleared;

        /// <summary>
        /// Constructs a new PixelGridModel from level data.
        /// </summary>
        /// <param name="width">Width of the grid.</param>
        /// <param name="height">Height of the grid.</param>
        /// <param name="cells">Cell data in row-major order (y * width + x). Will be copied.</param>
        /// <exception cref="ArgumentException">Thrown if cells.Length != width * height.</exception>
        public PixelGridModel(int width, int height, byte[] cells)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));

            if (cells.Length != width * height)
                throw new ArgumentException($"cells.Length ({cells.Length}) must equal width * height ({width * height})", nameof(cells));

            _width = width;
            _height = height;

            // Copy cells array
            _cells = new byte[cells.Length];
            Array.Copy(cells, _cells, cells.Length);

            // Initialize data structures
            _cellsByColor = new Dictionary<int, List<Vector2Int>>();
            _indexInColorList = new int[width * height];
            _frontRow = new int[width];
            _exposedColorInColumn = new int[width];
            _exposedColumnsByColor = new Dictionary<int, List<int>>();
            _columnIndexInExposedList = new int[width];

            // Initialize front row and exposed tracking
            for (int x = 0; x < width; x++)
            {
                _frontRow[x] = -1;
                _exposedColorInColumn[x] = -1;
                _columnIndexInExposedList[x] = -1;
            }

            // Build color lists and count
            _remainingCount = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    byte colorId = _cells[index];

                    if (colorId != LevelData.EmptyCell)
                    {
                        _remainingCount++;

                        // Add to color list
                        if (!_cellsByColor.TryGetValue(colorId, out var list))
                        {
                            list = new List<Vector2Int>();
                            _cellsByColor[colorId] = list;
                        }

                        var pos = new Vector2Int(x, y);
                        _indexInColorList[index] = list.Count;
                        list.Add(pos);
                    }
                }
            }

            // Build front row (exposed cells per column)
            for (int x = 0; x < width; x++)
            {
                // Find the bottommost non-empty cell in this column
                for (int y = 0; y < height; y++)
                {
                    byte colorId = _cells[y * width + x];
                    if (colorId != LevelData.EmptyCell)
                    {
                        _frontRow[x] = y;
                        _exposedColorInColumn[x] = colorId;

                        // Add this column to the exposed list for this color
                        if (!_exposedColumnsByColor.TryGetValue(colorId, out var exposedColumns))
                        {
                            exposedColumns = new List<int>();
                            _exposedColumnsByColor[colorId] = exposedColumns;
                        }

                        _columnIndexInExposedList[x] = exposedColumns.Count;
                        exposedColumns.Add(x);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the cell value at the specified grid coordinates.
        /// </summary>
        /// <param name="x">X coordinate (0 to width-1).</param>
        /// <param name="y">Y coordinate (0 to height-1, where 0 is the bottom row).</param>
        /// <returns>The color ID at the specified position, or EmptyCell if empty.</returns>
        public byte GetCell(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height)
                return LevelData.EmptyCell;

            return _cells[y * _width + x];
        }

        /// <summary>
        /// Gets the number of cells remaining with the specified color.
        /// </summary>
        /// <param name="colorId">The color ID to query.</param>
        /// <returns>The count of cells with that color.</returns>
        public int GetRemaining(int colorId)
        {
            if (_cellsByColor.TryGetValue(colorId, out var list))
                return list.Count;
            return 0;
        }

        /// <summary>
        /// Gets all cells with the specified color. The returned list is backed by internal state
        /// and must not be modified. Returns an empty list for colors with no cells (zero-alloc).
        /// </summary>
        /// <param name="colorId">The color ID to query.</param>
        /// <returns>A read-only view of cells with that color.</returns>
        public IReadOnlyList<Vector2Int> GetCellsOfColor(int colorId)
        {
            if (_cellsByColor.TryGetValue(colorId, out var list))
                return list;
            return _emptyList;
        }

        /// <summary>
        /// Checks if the specified color has any exposed cells.
        /// </summary>
        /// <param name="colorId">The color ID to check.</param>
        /// <returns>True if the color has at least one exposed cell.</returns>
        public bool HasExposed(int colorId)
        {
            return _exposedColumnsByColor.TryGetValue(colorId, out var columns) && columns.Count > 0;
        }

        /// <summary>
        /// Attempts to get an exposed cell of the specified color. O(1) and zero-allocation.
        /// Returns the last element of the exposed set for determinism.
        /// </summary>
        /// <param name="colorId">The color ID to query.</param>
        /// <param name="cell">Receives the cell position if found.</param>
        /// <returns>True if an exposed cell was found, false otherwise.</returns>
        public bool TryGetExposedCell(int colorId, out Vector2Int cell)
        {
            if (_exposedColumnsByColor.TryGetValue(colorId, out var columns) && columns.Count > 0)
            {
                // Return last element for determinism
                int columnIndex = columns[columns.Count - 1];
                int y = _frontRow[columnIndex];
                cell = new Vector2Int(columnIndex, y);
                return true;
            }

            cell = Vector2Int.zero;
            return false;
        }

        /// <summary>
        /// Removes the specified cell from the grid. The cell must be exposed, otherwise
        /// InvalidOperationException is thrown. Updates the grid state, exposed cells,
        /// and raises events.
        /// </summary>
        /// <param name="cell">The cell position to remove.</param>
        /// <exception cref="InvalidOperationException">Thrown if the cell is not exposed.</exception>
        public void RemoveCell(Vector2Int cell)
        {
            int x = cell.x;
            int y = cell.y;

            // Validate that this cell is exposed
            if (_frontRow[x] != y)
            {
                throw new InvalidOperationException($"Cell ({x}, {y}) is not exposed. Only exposed cells can be removed.");
            }

            int index = y * _width + x;
            byte colorId = _cells[index];

            if (colorId == LevelData.EmptyCell)
            {
                throw new InvalidOperationException($"Cell ({x}, {y}) is already empty.");
            }

            // Remove from color list (swap-remove)
            var colorList = _cellsByColor[colorId];
            int cellIndexInList = _indexInColorList[index];
            int lastIndex = colorList.Count - 1;

            if (cellIndexInList != lastIndex)
            {
                // Swap with last element
                Vector2Int lastCell = colorList[lastIndex];
                colorList[cellIndexInList] = lastCell;

                // Update index mapping for the swapped cell
                int lastCellIndex = lastCell.y * _width + lastCell.x;
                _indexInColorList[lastCellIndex] = cellIndexInList;
            }

            colorList.RemoveAt(lastIndex);

            // Remove from exposed list for this color (swap-remove)
            var exposedColumns = _exposedColumnsByColor[colorId];
            int columnIndexInList = _columnIndexInExposedList[x];
            int lastColumnIndex = exposedColumns.Count - 1;

            if (columnIndexInList != lastColumnIndex)
            {
                // Swap with last element
                int lastColumn = exposedColumns[lastColumnIndex];
                exposedColumns[columnIndexInList] = lastColumn;

                // Update index mapping for the swapped column
                _columnIndexInExposedList[lastColumn] = columnIndexInList;
            }

            exposedColumns.RemoveAt(lastColumnIndex);

            // Update cell to empty
            _cells[index] = LevelData.EmptyCell;
            _remainingCount--;

            // Find next exposed cell in this column (skip gaps)
            _frontRow[x] = -1;
            _exposedColorInColumn[x] = -1;
            _columnIndexInExposedList[x] = -1;

            for (int nextY = y + 1; nextY < _height; nextY++)
            {
                byte nextColorId = _cells[nextY * _width + x];
                if (nextColorId != LevelData.EmptyCell)
                {
                    // Found next exposed cell
                    _frontRow[x] = nextY;
                    _exposedColorInColumn[x] = nextColorId;

                    // Add to exposed list for new color
                    if (!_exposedColumnsByColor.TryGetValue(nextColorId, out var newExposedColumns))
                    {
                        newExposedColumns = new List<int>();
                        _exposedColumnsByColor[nextColorId] = newExposedColumns;
                    }

                    _columnIndexInExposedList[x] = newExposedColumns.Count;
                    newExposedColumns.Add(x);
                    break;
                }
            }

            // Raise events
            OnCellRemoved?.Invoke(cell, colorId);

            if (_remainingCount == 0)
            {
                OnCleared?.Invoke();
            }
        }
    }
}

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

        // Four-sided front tracking for conveyor belt
        private readonly int[] _bottomFront; // Min Y per column (-1 if empty)
        private readonly int[] _topFront;    // Max Y per column (-1 if empty)
        private readonly int[] _leftFront;   // Min X per row (-1 if empty)
        private readonly int[] _rightFront;  // Max X per row (-1 if empty)
        private readonly int[] _colorFrontCount; // Count of fronts per color ID (256 colors max)

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

            // Initialize four-sided front tracking
            _bottomFront = new int[width];
            _topFront = new int[width];
            _leftFront = new int[height];
            _rightFront = new int[height];
            _colorFrontCount = new int[256];

            // Initialize front row and exposed tracking
            for (int x = 0; x < width; x++)
            {
                _frontRow[x] = -1;
                _exposedColorInColumn[x] = -1;
                _columnIndexInExposedList[x] = -1;
                _bottomFront[x] = -1;
                _topFront[x] = -1;
            }

            for (int y = 0; y < height; y++)
            {
                _leftFront[y] = -1;
                _rightFront[y] = -1;
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

            // Pre-size one exposed-column list per present color: a color is exposed in at most `width` columns,
            // so RemoveCell never grows or creates a list (zero GC allocation while shooting).
            foreach (var pair in _cellsByColor)
            {
                _exposedColumnsByColor[pair.Key] = new List<int>(width);
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

            // Build four-sided fronts
            // Bottom and Top fronts (per column)
            for (int x = 0; x < width; x++)
            {
                // Bottom: find min Y (first non-empty from bottom)
                for (int y = 0; y < height; y++)
                {
                    byte colorId = _cells[y * width + x];
                    if (colorId != LevelData.EmptyCell)
                    {
                        _bottomFront[x] = y;
                        _colorFrontCount[colorId]++;
                        break;
                    }
                }

                // Top: find max Y (first non-empty from top)
                for (int y = height - 1; y >= 0; y--)
                {
                    byte colorId = _cells[y * width + x];
                    if (colorId != LevelData.EmptyCell)
                    {
                        _topFront[x] = y;
                        // Only increment if this is a different cell than bottom front
                        if (_topFront[x] != _bottomFront[x])
                        {
                            _colorFrontCount[colorId]++;
                        }
                        break;
                    }
                }
            }

            // Left and Right fronts (per row)
            for (int y = 0; y < height; y++)
            {
                // Left: find min X (first non-empty from left)
                for (int x = 0; x < width; x++)
                {
                    byte colorId = _cells[y * width + x];
                    if (colorId != LevelData.EmptyCell)
                    {
                        _leftFront[y] = x;
                        // Check if this cell is already counted as a front
                        Vector2Int cell = new Vector2Int(x, y);
                        bool alreadyCounted = (_bottomFront[x] == y) || (_topFront[x] == y);
                        if (!alreadyCounted)
                        {
                            _colorFrontCount[colorId]++;
                        }
                        break;
                    }
                }

                // Right: find max X (first non-empty from right)
                for (int x = width - 1; x >= 0; x--)
                {
                    byte colorId = _cells[y * width + x];
                    if (colorId != LevelData.EmptyCell)
                    {
                        _rightFront[y] = x;
                        // Only increment if this is a different cell than the other fronts
                        bool alreadyCounted = (_bottomFront[x] == y) || (_topFront[x] == y) || (_leftFront[y] == x);
                        if (!alreadyCounted)
                        {
                            _colorFrontCount[colorId]++;
                        }
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
        /// Attempts to get the front cell for the specified board side and line.
        /// O(1) and zero-allocation.
        /// </summary>
        /// <param name="side">The board side to query.</param>
        /// <param name="lineIndex">The column index for Bottom/Top, or row index for Left/Right.</param>
        /// <param name="cell">Receives the front cell position if found.</param>
        /// <returns>True if a front cell exists on that line, false if the line is empty.</returns>
        public bool TryGetFront(BoardSide side, int lineIndex, out Vector2Int cell)
        {
            switch (side)
            {
                case BoardSide.Bottom:
                    if (lineIndex >= 0 && lineIndex < _width && _bottomFront[lineIndex] >= 0)
                    {
                        cell = new Vector2Int(lineIndex, _bottomFront[lineIndex]);
                        return true;
                    }
                    break;

                case BoardSide.Top:
                    if (lineIndex >= 0 && lineIndex < _width && _topFront[lineIndex] >= 0)
                    {
                        cell = new Vector2Int(lineIndex, _topFront[lineIndex]);
                        return true;
                    }
                    break;

                case BoardSide.Left:
                    if (lineIndex >= 0 && lineIndex < _height && _leftFront[lineIndex] >= 0)
                    {
                        cell = new Vector2Int(_leftFront[lineIndex], lineIndex);
                        return true;
                    }
                    break;

                case BoardSide.Right:
                    if (lineIndex >= 0 && lineIndex < _height && _rightFront[lineIndex] >= 0)
                    {
                        cell = new Vector2Int(_rightFront[lineIndex], lineIndex);
                        return true;
                    }
                    break;
            }

            cell = Vector2Int.zero;
            return false;
        }

        /// <summary>
        /// Checks if the specified color has any front cells on any board side.
        /// O(1) and zero-allocation.
        /// </summary>
        /// <param name="colorId">The color ID to check.</param>
        /// <returns>True if the color appears in at least one front position.</returns>
        public bool HasAnyFront(int colorId)
        {
            if (colorId < 0 || colorId >= 256)
                return false;

            return _colorFrontCount[colorId] > 0;
        }

        /// <summary>
        /// Removes the specified cell from the grid. The cell must be a front from at least one side,
        /// otherwise InvalidOperationException is thrown. Updates all front pointers, color counts,
        /// and the legacy exposed-cell tracking.
        /// </summary>
        /// <param name="cell">The cell position to remove.</param>
        /// <exception cref="InvalidOperationException">Thrown if the cell is not a front.</exception>
        public void RemoveCell(Vector2Int cell)
        {
            int x = cell.x;
            int y = cell.y;

            // Validate bounds
            if (x < 0 || x >= _width || y < 0 || y >= _height)
            {
                throw new InvalidOperationException($"Cell ({x}, {y}) is out of bounds.");
            }

            int index = y * _width + x;
            byte colorId = _cells[index];

            if (colorId == LevelData.EmptyCell)
            {
                throw new InvalidOperationException($"Cell ({x}, {y}) is already empty.");
            }

            // Validate that this cell is a front from at least one side
            bool isBottomFront = (_bottomFront[x] == y);
            bool isTopFront = (_topFront[x] == y);
            bool isLeftFront = (_leftFront[y] == x);
            bool isRightFront = (_rightFront[y] == x);

            if (!isBottomFront && !isTopFront && !isLeftFront && !isRightFront)
            {
                throw new InvalidOperationException($"Cell ({x}, {y}) is not a front from any side. Only front cells can be removed.");
            }

            // Remove from color list (swap-remove)
            var colorList = _cellsByColor[colorId];
            int cellIndexInList = _indexInColorList[index];
            int lastListIndex = colorList.Count - 1;

            if (cellIndexInList != lastListIndex)
            {
                Vector2Int lastCell = colorList[lastListIndex];
                colorList[cellIndexInList] = lastCell;
                int lastCellIndex = lastCell.y * _width + lastCell.x;
                _indexInColorList[lastCellIndex] = cellIndexInList;
            }

            colorList.RemoveAt(lastListIndex);

            // Update legacy bottom-exposed tracking if this cell was bottom-exposed
            if (_frontRow[x] == y)
            {
                var exposedColumns = _exposedColumnsByColor[colorId];
                int columnIndexInList = _columnIndexInExposedList[x];
                int lastColumnIndex = exposedColumns.Count - 1;

                if (columnIndexInList != lastColumnIndex)
                {
                    int lastColumn = exposedColumns[lastColumnIndex];
                    exposedColumns[columnIndexInList] = lastColumn;
                    _columnIndexInExposedList[lastColumn] = columnIndexInList;
                }

                exposedColumns.RemoveAt(lastColumnIndex);

                _frontRow[x] = -1;
                _exposedColorInColumn[x] = -1;
                _columnIndexInExposedList[x] = -1;
            }

            // Clear the cell
            _cells[index] = LevelData.EmptyCell;
            _remainingCount--;

            // Decrement color front count for each front this cell was part of
            if (isBottomFront) _colorFrontCount[colorId]--;
            if (isTopFront && !isBottomFront) _colorFrontCount[colorId]--;
            if (isLeftFront && !isBottomFront && !isTopFront) _colorFrontCount[colorId]--;
            if (isRightFront && !isBottomFront && !isTopFront && !isLeftFront) _colorFrontCount[colorId]--;

            // Update bottom front if needed
            if (isBottomFront)
            {
                _bottomFront[x] = -1;
                for (int nextY = y + 1; nextY < _height; nextY++)
                {
                    byte nextColorId = _cells[nextY * _width + x];
                    if (nextColorId != LevelData.EmptyCell)
                    {
                        _bottomFront[x] = nextY;
                        _colorFrontCount[nextColorId]++;

                        // Update legacy bottom-exposed tracking
                        _frontRow[x] = nextY;
                        _exposedColorInColumn[x] = nextColorId;

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
            }

            // Update top front if needed
            if (isTopFront)
            {
                _topFront[x] = -1;
                for (int nextY = y - 1; nextY >= 0; nextY--)
                {
                    byte nextColorId = _cells[nextY * _width + x];
                    if (nextColorId != LevelData.EmptyCell)
                    {
                        _topFront[x] = nextY;
                        // Only increment if different from bottom front
                        if (_bottomFront[x] != nextY)
                        {
                            _colorFrontCount[nextColorId]++;
                        }
                        break;
                    }
                }
            }

            // Update left front if needed
            if (isLeftFront)
            {
                _leftFront[y] = -1;
                for (int nextX = x + 1; nextX < _width; nextX++)
                {
                    byte nextColorId = _cells[y * _width + nextX];
                    if (nextColorId != LevelData.EmptyCell)
                    {
                        _leftFront[y] = nextX;
                        // Only increment if not already counted as bottom or top front
                        bool alreadyCounted = (_bottomFront[nextX] == y) || (_topFront[nextX] == y);
                        if (!alreadyCounted)
                        {
                            _colorFrontCount[nextColorId]++;
                        }
                        break;
                    }
                }
            }

            // Update right front if needed
            if (isRightFront)
            {
                _rightFront[y] = -1;
                for (int nextX = x - 1; nextX >= 0; nextX--)
                {
                    byte nextColorId = _cells[y * _width + nextX];
                    if (nextColorId != LevelData.EmptyCell)
                    {
                        _rightFront[y] = nextX;
                        // Only increment if not already counted as any other front
                        bool alreadyCounted = (_bottomFront[nextX] == y) || (_topFront[nextX] == y) || (_leftFront[y] == nextX);
                        if (!alreadyCounted)
                        {
                            _colorFrontCount[nextColorId]++;
                        }
                        break;
                    }
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

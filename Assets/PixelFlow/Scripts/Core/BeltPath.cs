using System;

namespace PixelFlow.Core
{
    /// <summary>
    /// Represents the discrete path of positions around the game board's perimeter.
    /// Maps belt positions to board sides and line indices (columns or rows).
    /// </summary>
    public readonly struct BeltPath
    {
        private readonly int _width;
        private readonly int _height;

        /// <summary>
        /// Width of the board the belt runs around (number of columns).
        /// </summary>
        public int Width => _width;

        /// <summary>
        /// Height of the board the belt runs around (number of rows).
        /// </summary>
        public int Height => _height;

        /// <summary>
        /// Total number of positions on the belt path.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Creates a new belt path for a board of the specified dimensions.
        /// </summary>
        /// <param name="width">Board width (number of columns).</param>
        /// <param name="height">Board height (number of rows).</param>
        public BeltPath(int width, int height)
        {
            _width = width;
            _height = height;
            Length = 2 * width + 2 * height;
        }

        /// <summary>
        /// Resolves a belt position to its corresponding board side and line index.
        /// </summary>
        /// <param name="position">Belt position in range [0, Length).</param>
        /// <param name="side">Output: the board side (Bottom, Right, Top, or Left).</param>
        /// <param name="lineIndex">Output: the line index (column for Bottom/Top, row for Right/Left).</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when position is outside [0, Length).</exception>
        public void Resolve(int position, out BoardSide side, out int lineIndex)
        {
            if (position < 0 || position >= Length)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    $"Position must be in range [0, {Length})");
            }

            int w = _width;
            int h = _height;

            // Bottom edge: [0, W)
            if (position < w)
            {
                side = BoardSide.Bottom;
                lineIndex = position;
                return;
            }

            // Right edge: [W, W+H)
            if (position < w + h)
            {
                side = BoardSide.Right;
                lineIndex = position - w;
                return;
            }

            // Top edge: [W+H, 2W+H)
            if (position < 2 * w + h)
            {
                side = BoardSide.Top;
                lineIndex = 2 * w + h - 1 - position;
                return;
            }

            // Left edge: [2W+H, 2W+2H)
            side = BoardSide.Left;
            lineIndex = 2 * w + 2 * h - 1 - position;
        }
    }
}

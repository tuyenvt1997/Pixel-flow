namespace PixelFlow.Core
{
    /// <summary>
    /// Represents one of the four sides of the game board from which tanks can shoot.
    /// </summary>
    public enum BoardSide
    {
        /// <summary>
        /// Bottom edge. Fires upward along columns.
        /// </summary>
        Bottom = 0,

        /// <summary>
        /// Right edge. Fires leftward along rows.
        /// </summary>
        Right = 1,

        /// <summary>
        /// Top edge. Fires downward along columns.
        /// </summary>
        Top = 2,

        /// <summary>
        /// Left edge. Fires rightward along rows.
        /// </summary>
        Left = 3
    }
}

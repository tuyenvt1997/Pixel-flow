using System;

namespace PixelFlow.Data
{
    /// <summary>
    /// Represents a color tank with a specific color and ammunition count.
    /// </summary>
    [Serializable]
    public struct ColorTankData
    {
        /// <summary>
        /// The color ID from the level's palette.
        /// </summary>
        public byte colorId;

        /// <summary>
        /// The amount of ammunition (shots) available in this tank.
        /// </summary>
        public int ammo;
    }
}

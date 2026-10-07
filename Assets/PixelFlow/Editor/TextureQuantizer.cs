using System;
using System.Collections.Generic;
using PixelFlow.Data;
using UnityEngine;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// Converts raw texture pixels into a level palette and cell array.
    /// </summary>
    public static class TextureQuantizer
    {
        /// <summary>
        /// Maximum palette size (cell value 255 is reserved for <see cref="LevelData.EmptyCell"/>).
        /// </summary>
        public const int MaxColors = 254;

        /// <summary>
        /// Quantizes pixels into an exact-color palette.
        /// Pixels with alpha &lt; 128 become <see cref="LevelData.EmptyCell"/>; every distinct RGB value
        /// among the remaining pixels becomes one palette entry (opaque, in order of first appearance).
        /// </summary>
        /// <param name="pixels">Pixels in <c>Texture2D.GetPixels32</c> layout (row 0 = bottom), matching <see cref="LevelData.cells"/>.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="palette">Receives the palette (alpha forced to 255).</param>
        /// <param name="cells">Receives one palette index (or EmptyCell) per pixel.</param>
        /// <exception cref="ArgumentException">Thrown when the pixel count does not match the size or there are more than 254 colors.</exception>
        public static void Quantize(Color32[] pixels, int width, int height, out Color32[] palette, out byte[] cells)
        {
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length != width * height)
                throw new ArgumentException($"Expected {width * height} pixels for a {width}x{height} image.", nameof(pixels));

            var lookup = new Dictionary<int, byte>();
            var colors = new List<Color32>();
            cells = new byte[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.a < 128)
                {
                    cells[i] = LevelData.EmptyCell;
                    continue;
                }

                int rgb = (p.r << 16) | (p.g << 8) | p.b;
                if (!lookup.TryGetValue(rgb, out byte index))
                {
                    if (colors.Count >= MaxColors)
                        throw new ArgumentException($"Image has more than {MaxColors} distinct colors.", nameof(pixels));

                    index = (byte)colors.Count;
                    lookup.Add(rgb, index);
                    colors.Add(new Color32(p.r, p.g, p.b, 255));
                }
                cells[i] = index;
            }

            palette = colors.ToArray();
        }
    }
}


using Alco.Graphics;

namespace Alco.Rendering
{
    /// <summary>
    /// A CPU bitmap of 8-bit RGBA pixels.
    /// </summary>
    public class BitmapUIntRGBA : Bitmap<Color32>
    {
        /// <summary>
        /// Creates a bitmap of the given size, optionally filled with a default pixel.
        /// </summary>
        /// <param name="width">The width of the bitmap.</param>
        /// <param name="height">The height of the bitmap.</param>
        /// <param name="defaultValue">The pixel value to fill the bitmap with, or null to clear it.</param>
        public BitmapUIntRGBA(int width, int height, Color32? defaultValue = null) : base(width, height, defaultValue)
        {
        }

        /// <summary>
        /// Creates a bitmap of the given size, optionally filled with a default pixel.
        /// </summary>
        /// <param name="width">The width of the bitmap.</param>
        /// <param name="height">The height of the bitmap.</param>
        /// <param name="defaultValue">The pixel value to fill the bitmap with, or null to clear it.</param>
        public BitmapUIntRGBA(uint width, uint height, Color32? defaultValue = null) : base(width, height, defaultValue)
        {
        }
    }
}


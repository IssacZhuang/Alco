namespace Alco.Rendering
{
    /// <summary>
    /// A CPU bitmap of half-precision RGBA pixels.
    /// </summary>
    public class BitmapFloat16RGBA : Bitmap<Half4>
    {
        /// <summary>
        /// Creates a bitmap of the given size, optionally filled with a default pixel.
        /// </summary>
        /// <param name="width">The width of the bitmap.</param>
        /// <param name="height">The height of the bitmap.</param>
        /// <param name="defaultValue">The pixel value to fill the bitmap with, or null to clear it.</param>
        public BitmapFloat16RGBA(int width, int height, Half4? defaultValue = null) : base(width, height, defaultValue)
        {
        }

        /// <summary>
        /// Creates a bitmap of the given size, optionally filled with a default pixel.
        /// </summary>
        /// <param name="width">The width of the bitmap.</param>
        /// <param name="height">The height of the bitmap.</param>
        /// <param name="defaultValue">The pixel value to fill the bitmap with, or null to clear it.</param>
        public BitmapFloat16RGBA(uint width, uint height, Half4? defaultValue = null) : base(width, height, defaultValue)
        {
        }
    }
}


using System.Numerics;

namespace Alco.Rendering
{
    /// <summary>
    /// A CPU bitmap of single-precision RGBA pixels.
    /// </summary>
    public class BitmapFloat32RGBA : Bitmap<Vector4>
    {
        /// <summary>
        /// Creates a bitmap of the given size, optionally filled with a default pixel.
        /// </summary>
        /// <param name="width">The width of the bitmap.</param>
        /// <param name="height">The height of the bitmap.</param>
        /// <param name="defaultValue">The pixel value to fill the bitmap with, or null to clear it.</param>
        public BitmapFloat32RGBA(int width, int height, Vector4? defaultValue = null) : base(width, height, defaultValue)
        {
        }

        /// <summary>
        /// Creates a bitmap of the given size, optionally filled with a default pixel.
        /// </summary>
        /// <param name="width">The width of the bitmap.</param>
        /// <param name="height">The height of the bitmap.</param>
        /// <param name="defaultValue">The pixel value to fill the bitmap with, or null to clear it.</param>
        public BitmapFloat32RGBA(uint width, uint height, Vector4? defaultValue = null) : base(width, height, defaultValue)
        {
        }
        }
    }

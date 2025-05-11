using System.Numerics;

namespace Alco.Rendering
{
    public class BitmapFloat32RGBA : Bitmap<Vector4>
    {
        public BitmapFloat32RGBA(int width, int height, Vector4? defaultValue = null) : base(width, height, defaultValue)
        {
        }

        public BitmapFloat32RGBA(uint width, uint height, Vector4? defaultValue = null) : base(width, height, defaultValue)
        {
        }
        }
    }

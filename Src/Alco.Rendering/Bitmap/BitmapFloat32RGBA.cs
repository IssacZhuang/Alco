using System.Numerics;

namespace Alco.Rendering
{
    public class BitmapFloat32RGBA : Bitmap<Vector4>
    {
        public BitmapFloat32RGBA(int width, int height, bool clear = true) : base(width, height, clear)
        {
        }

        public BitmapFloat32RGBA(uint width, uint height, bool clear = true) : base(width, height, clear)
        {
        }
        }
    }

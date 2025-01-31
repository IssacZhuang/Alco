using System.Numerics;

namespace Alco.Rendering
{
    public class BitmapFloat32RGBA : Bitmap<Vector4>
    {
        public BitmapFloat32RGBA(int width, int height) : base(width, height)
        {
        }

        public BitmapFloat32RGBA(uint width, uint height) : base(width, height)
        {
        }
    }
}

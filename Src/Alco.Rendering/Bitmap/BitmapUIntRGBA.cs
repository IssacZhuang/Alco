
namespace Alco.Rendering
{
    public class BitmapUIntRGBA : Bitmap<uint>
    {
        public BitmapUIntRGBA(int width, int height, bool clear = true) : base(width, height, clear)
        {
        }

        public BitmapUIntRGBA(uint width, uint height, bool clear = true) : base(width, height, clear)
        {
        }
    }
}


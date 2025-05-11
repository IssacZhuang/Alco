
using Alco.Graphics;

namespace Alco.Rendering
{
    public class BitmapUIntRGBA : Bitmap<Color32>
    {
        public BitmapUIntRGBA(int width, int height, Color32? defaultValue = null) : base(width, height, defaultValue)
        {
        }

        public BitmapUIntRGBA(uint width, uint height, Color32? defaultValue = null) : base(width, height, defaultValue)
        {
        }
    }
}


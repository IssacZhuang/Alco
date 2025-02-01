namespace Alco.Rendering
{
    public class BitmapFloat16RGBA : Bitmap<Half4>
    {
        public BitmapFloat16RGBA(int width, int height, bool clear = true) : base(width, height, clear)
        {
        }

        public BitmapFloat16RGBA(uint width, uint height, bool clear = true) : base(width, height, clear)
        {
        }
    }
}


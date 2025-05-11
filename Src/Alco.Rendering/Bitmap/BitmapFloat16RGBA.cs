namespace Alco.Rendering
{
    public class BitmapFloat16RGBA : Bitmap<Half4>
    {
        public BitmapFloat16RGBA(int width, int height, Half4? defaultValue = null) : base(width, height, defaultValue)
        {
        }

        public BitmapFloat16RGBA(uint width, uint height, Half4? defaultValue = null) : base(width, height, defaultValue)
        {
        }
    }
}


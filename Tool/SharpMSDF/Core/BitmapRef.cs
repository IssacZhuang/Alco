using SharpMSDF.Core;
using System.Threading.Channels;

namespace SharpMSDF.Core
{
    public struct BitmapRef<T> where T : struct
    {
        /// <summary>
        /// Bitmap array that we reference from
        /// </summary>
        public readonly T[] Pixels;
        public readonly int OriginalWidth, OriginalHeight;
        public readonly int SubX, SubY, SubWidth, SubHeight; // This is added and not from original source.
        /// <summary>
        /// Number of channels
        /// </summary>
        public readonly int N;

        public BitmapRef(Bitmap<T> bitmap, int subX = 0, int subY = 0, int subW = -1, int subH = -1) : this(bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.N, subX, subY, subW, subH) { }
        public BitmapRef(T[] pixels, int width, int height, int n = 1, int subX = 0, int subY = 0, int subW = -1, int subH = -1)
        {
            OriginalWidth = width;
            OriginalHeight = height;
            SubX = subX;
            SubY = subY;
            SubWidth = (subW < 0) ? width : subW;
            SubHeight = (subH < 0) ? height : subH;
            Pixels = pixels;
            N = n;
        }

        public readonly Span<T> Slice(int x, int y) => Pixels.AsSpan(GetIndex(x, y), N);
        public readonly int GetIndex(int x, int y, int channel = 0) => N * (OriginalWidth * (SubY + y) + SubX + x) + channel;
        public ref T this[int x, int y, int channel = 0] => ref Pixels[GetIndex(x, y, channel)];
        
        public static implicit operator BitmapRef<T>(Bitmap<T> bitmapRef) => new BitmapRef<T>(bitmapRef);
    }


    /// Constant reference to a 2D image bitmap or a Buffer acting as one. Pixel _Storage not owned or managed by the object.
    public readonly struct BitmapConstRef<T> where T : struct
    {
        internal readonly T[] _Pixels;
        public readonly int OriginalWidth, OriginalHeight;
        public readonly int SubX, SubY, SubWidth, SubHeight; // This is added and not from original source.
        public readonly int N;

        public BitmapConstRef(BitmapRef<T> bitmapRef) : this(bitmapRef.Pixels, bitmapRef.OriginalWidth, bitmapRef.OriginalHeight, bitmapRef.N, bitmapRef.SubX, bitmapRef.SubY, bitmapRef.SubWidth, bitmapRef.SubHeight) { }
        public BitmapConstRef(Bitmap<T> bitmap, int subX = 0, int subY = 0, int subW = -1, int subH = -1) : this(bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.N, subX, subY, subW, subH) { }
        public BitmapConstRef(T[] pixels, int width, int height, int n = 1, int subX = 0, int subY = 0, int subW = -1, int subH = -1)
        {
            OriginalWidth = width;
            OriginalHeight = height;
            SubX = subX;
            SubY = subY;
            SubWidth = (subW < 0) ? width : subW;
            SubHeight = (subH < 0) ? height : subH;
            _Pixels = pixels;
            N = n;
        }

        public readonly ReadOnlySpan<T> Slice(int x, int y) => _Pixels.AsSpan(GetIndex(x, y), N);
        public readonly int GetIndex(int x, int y, int channel = 0) => N * (OriginalWidth * (SubY + y) + SubX + x) + channel;
        public T this[int x, int y, int channel = 0] => _Pixels[GetIndex(x, y, channel)];

        public static implicit operator BitmapConstRef<T>(BitmapRef<T> bitmapRef) => new BitmapConstRef<T>(bitmapRef);
        public static implicit operator BitmapConstRef<T>(Bitmap<T> bitmap) => new BitmapConstRef<T>(bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.N);
    }

}

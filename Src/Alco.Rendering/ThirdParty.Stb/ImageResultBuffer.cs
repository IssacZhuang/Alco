using Hebron.Runtime;
using Alco;


namespace StbImageSharp
{
#if !STBSHARP_INTERNAL
    public
#else
	internal
#endif
    /// <summary>
    /// Use unmanged memory to store the image result, less GC but requires manual disposal.
    /// </summary>
    unsafe class ImageResultBuffer : IDisposable
    {
        public int Width { get; }
        public int Height { get; }
        public ColorComponents SourceComp { get; }
        public ColorComponents Comp { get; }
        private readonly NativeBuffer<byte> _buffer;
        public MemoryRef<byte> Memory => _buffer.MemoryRef;

        internal ImageResultBuffer(byte* data, int size, int width, int height, ColorComponents comp, ColorComponents sourceComp)
        {
            Width = width;
            Height = height;
            Comp = comp;
            SourceComp = sourceComp;
            _buffer = new NativeBuffer<byte>(size);
            UtilsMemory.MemCopy(data, _buffer.UnsafePointer, (uint)size);
        }

        public void Dispose()
        {
            _buffer.Dispose();
        }

        internal static unsafe ImageResultBuffer FromResult(byte* result, int width, int height, ColorComponents comp,
            ColorComponents req_comp)
        {
            if (result == null)
                throw new InvalidOperationException(StbImage.stbi__g_failure_reason);

            int size = width * height * (int)req_comp;

            return new ImageResultBuffer(result, size, width, height, req_comp, comp);
        }

        public static unsafe ImageResultBuffer FromStream(Stream stream,
            ColorComponents requiredComponents = ColorComponents.Default)
        {
            byte* result = null;

            try
            {
                int x, y, comp;

                var context = new StbImage.stbi__context(stream);

                result = StbImage.stbi__load_and_postprocess_8bit(context, &x, &y, &comp, (int)requiredComponents);

                return FromResult(result, x, y, (ColorComponents)comp, requiredComponents);
            }
            finally
            {
                if (result != null)
                    CRuntime.free(result);
            }
        }

        public static ImageResultBuffer FromMemory(byte[] data, ColorComponents requiredComponents = ColorComponents.Default)
        {
            using (var stream = new MemoryStream(data))
            {
                return FromStream(stream, requiredComponents);
            }
        }

        public static ImageResultBuffer FromMemory(ReadOnlySpan<byte> data, ColorComponents requiredComponents = ColorComponents.Default)
        {
            fixed (byte* ptr = data)
            {
                using (var stream = new UnmanagedMemoryStream(ptr, data.Length))
                {
                    return FromStream(stream, requiredComponents);
                }
            }
        }
    }
}
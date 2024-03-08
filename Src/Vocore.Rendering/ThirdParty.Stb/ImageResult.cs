using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Hebron.Runtime;



namespace StbImageSharp
{
#if !STBSHARP_INTERNAL
	public
#else
	internal
#endif
	class ImageResult
	{
		public int Width { get; }
		public int Height { get; }
		public ColorComponents SourceComp { get; }
		public ColorComponents Comp { get; }
		public byte[] Data { get; }

		public ImageResult(byte[] data, int width, int height, ColorComponents comp, ColorComponents sourceComp)
		{
			Data = data;
			Width = width;
			Height = height;
			Comp = comp;
			SourceComp = sourceComp;
		}

		internal static unsafe ImageResult FromResult(byte* result, int width, int height, ColorComponents comp,
			ColorComponents req_comp)
		{
			if (result == null)
				throw new InvalidOperationException(StbImage.stbi__g_failure_reason);

			byte[] bytes = new byte[width * height * (int)req_comp];
			Marshal.Copy(new IntPtr(result), bytes, 0, bytes.Length);

			return new ImageResult(bytes, width, height, req_comp, comp);
		}

		public static unsafe ImageResult FromStream(Stream stream,
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

		public static ImageResult FromMemory(byte[] data, ColorComponents requiredComponents = ColorComponents.Default)
		{
			using (var stream = new MemoryStream(data))
			{
				return FromStream(stream, requiredComponents);
			}
		}

		public static IEnumerable<AnimatedFrameResult> AnimatedGifFramesFromStream(Stream stream,
			ColorComponents requiredComponents = ColorComponents.Default)
		{
			return new AnimatedGifEnumerable(stream, requiredComponents);
		}
	}
}
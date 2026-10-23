using System;
using System.Runtime.InteropServices;
using Hebron.Runtime;

namespace StbTrueTypeSharp
{
#if !STBSHARP_INTERNAL
	public
#else
	internal
#endif
	static unsafe partial class StbTrueType
	{
		internal static bool usedOldRasterizer = false;

		/// <summary>
		/// Gets the number of live native allocations.
		/// </summary>
		public static int NativeAllocations => MemoryStats.Allocations;

		/// <summary>
		/// Holds parsed TrueType font tables and the font data this instance owns.
		/// </summary>
		public class stbtt_fontinfo : IDisposable
		{
			public stbtt__buf cff;
			public stbtt__buf charstrings;
			public byte* data = null;
			public stbtt__buf fdselect;
			public stbtt__buf fontdicts;
			public int fontstart;
			public int glyf;
			public int gpos;
			public stbtt__buf gsubrs;
			public int head;
			public int hhea;
			public int hmtx;
			public int index_map;
			public int indexToLocFormat;
			public bool isDataCopy;
			public int kern;
			public int loca;
			public int numGlyphs;
			public stbtt__buf subrs;
			public int svg;
			public void* userdata;
			public bool useOldRasterizer;

			/// <summary>
			/// Releases the font data owned by this instance.
			/// </summary>
			public void Dispose()
			{
				Dispose(true);
			}

			~stbtt_fontinfo()
			{
				Dispose(false);
			}

			protected virtual void Dispose(bool disposing)
			{
				if (isDataCopy && data != null)
				{
					CRuntime.free(data);
					data = null;
				}
			}
		}

		/// <summary>
		/// Finds the offset of a table in the font, or 0 when the table is missing.
		/// </summary>
		/// <param name="data">The font data.</param>
		/// <param name="fontstart">The offset of the font's table directory.</param>
		/// <param name="tag">The four-character table tag.</param>
		/// <returns>The table offset, or 0 when not found.</returns>
		public static uint stbtt__find_table(byte* data, uint fontstart, string tag)
		{
			int num_tables = ttUSHORT(data + fontstart + 4);
			var tabledir = fontstart + 12;
			int i;
			for (i = 0; i < num_tables; ++i)
			{
				var loc = (uint)(tabledir + 16 * i);
				if ((data + loc + 0)[0] == tag[0] && (data + loc + 0)[1] == tag[1] &&
					(data + loc + 0)[2] == tag[2] && (data + loc + 0)[3] == tag[3])
					return ttULONG(data + loc + 8);
			}

			return 0;
		}

		/// <summary>
		/// Bakes a range of characters from the font into a bitmap.
		/// </summary>
		/// <param name="ttf">The font data.</param>
		/// <param name="offset">The offset of the font within the data.</param>
		/// <param name="pixel_height">The font pixel height.</param>
		/// <param name="pixels">The destination 8-bit alpha bitmap.</param>
		/// <param name="pw">The bitmap width in pixels.</param>
		/// <param name="ph">The bitmap height in pixels.</param>
		/// <param name="first_char">The first character code to bake.</param>
		/// <param name="num_chars">The number of character codes to bake.</param>
		/// <param name="chardata">Receives the baked character metrics.</param>
		/// <returns><c>true</c> when the characters fit in the bitmap; otherwise <c>false</c>.</returns>
		public static bool stbtt_BakeFontBitmap(byte[] ttf, int offset, float pixel_height, byte[] pixels, int pw,
			int ph,
			int first_char, int num_chars, stbtt_bakedchar[] chardata)
		{
			fixed (byte* ttfPtr = ttf)
			{
				fixed (byte* pixelsPtr = pixels)
				{
					fixed (stbtt_bakedchar* chardataPtr = chardata)
					{
						var result = stbtt_BakeFontBitmap(ttfPtr, offset, pixel_height, pixelsPtr, pw, ph, first_char,
							num_chars,
							chardataPtr);

						return result != 0;
					}
				}
			}
		}

		/// <summary>
		///     Creates and initializes a font from ttf/otf/ttc data
		/// </summary>
		/// <param name="data"></param>
		/// <param name="offset"></param>
		/// <returns>null if the data was invalid</returns>
		public static bool TryCreateFont(byte[] data, int offset, out stbtt_fontinfo fontinfo)
		{
			var dataCopy = (byte*)CRuntime.malloc(data.Length);
			Marshal.Copy(data, 0, new IntPtr(dataCopy), data.Length);

			var info = new stbtt_fontinfo
			{
				isDataCopy = true
			};

			if (stbtt_InitFont_internal(info, dataCopy, offset) == 0)
			{
				info.Dispose();
#pragma warning disable CS8625
				fontinfo = null;
#pragma warning restore CS8625
				return false;
			}

			fontinfo = info;

			return true;
		}

		/// <summary>
		///     Creates and initializes a font from ttf/otf/ttc data
		/// </summary>
		/// <param name="data"></param>
		/// <param name="offset"></param>
		/// <returns>null if the data was invalid</returns>
		public static bool TryCreateFont(ReadOnlySpan<byte> data, int offset, out stbtt_fontinfo fontinfo)
		{
			var dataCopy = (byte*)CRuntime.malloc(data.Length);
			// Marshal.Copy(data, 0, new IntPtr(dataCopy), data.Length);
			fixed (byte* dataPtr = data)
			{
				CRuntime.memcpy(dataCopy, dataPtr, data.Length);
			}

			var info = new stbtt_fontinfo
			{
				isDataCopy = true
			};

			if (stbtt_InitFont_internal(info, dataCopy, offset) == 0)
			{
				info.Dispose();
#pragma warning disable CS8625
				fontinfo = null;
#pragma warning restore CS8625
				return false;
			}

			fontinfo = info;

			return true;
		}
	}
}
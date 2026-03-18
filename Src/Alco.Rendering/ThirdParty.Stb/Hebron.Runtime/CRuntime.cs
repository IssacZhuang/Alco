using System;
using System.Runtime.InteropServices;



namespace Hebron.Runtime
{
	internal static unsafe class CRuntime
	{
		private static readonly string numbers = "0123456789";

		public static void* malloc(ulong size)
		{
			return malloc((long)size);
		}

		public static void* malloc(long size)
		{
			var ptr = Marshal.AllocHGlobal((int)size);

#if CHECK_ALLOC
			MemoryStats.Allocated();
#endif

			return ptr.ToPointer();
		}

		public static void free(void* a)
		{
			if (a == null)
				return;

			var ptr = new IntPtr(a);
			Marshal.FreeHGlobal(ptr);
#if CHECK_ALLOC
			MemoryStats.Freed();
#endif
		}

		public static void memcpy(void* a, void* b, long size)
		{
			// var ap = (byte*)a;
			// var bp = (byte*)b;
			// for (long i = 0; i < size; ++i)
			// 	*ap++ = *bp++;
			Buffer.MemoryCopy(b, a, size, size);
		}

		public static void memcpy(void* a, void* b, ulong size)
		{
			memcpy(a, b, (long)size);
		}

		public static void memmove(void* a, void* b, long size)
		{
			void* temp = null;

			try
			{
				temp = malloc(size);
				memcpy(temp, b, size);
				memcpy(a, temp, size);
			}

			finally
			{
				if (temp != null)
					free(temp);
			}
		}

		public static void memmove(void* a, void* b, ulong size)
		{
			memmove(a, b, (long)size);
		}

		public static int memcmp(void* a, void* b, long size)
		{
			var result = 0;
			var ap = (byte*)a;
			var bp = (byte*)b;
			for (long i = 0; i < size; ++i)
			{
				if (*ap != *bp)
					result += 1;

				ap++;
				bp++;
			}

			return result;
		}

		public static int memcmp(void* a, void* b, ulong size)
		{
			return memcmp(a, b, (long)size);
		}

		public static int memcmp(byte* a, byte[] b, ulong size)
		{
			fixed (void* bptr = b)
			{
				return memcmp(a, bptr, (long)size);
			}
		}

		public static void memset(void* ptr, int value, long size)
		{
			var bptr = (byte*)ptr;
			var bval = (byte)value;
			for (long i = 0; i < size; ++i)
				*bptr++ = bval;
		}

		public static void memset(void* ptr, int value, ulong size)
		{
			memset(ptr, value, (long)size);
		}

		public static uint _lrotl(uint x, int y)
		{
			return (x << y) | (x >> (32 - y));
		}

		public static void* realloc(void* a, long newSize)
		{
			if (a == null)
				return malloc(newSize);

			var ptr = new IntPtr(a);
			var result = Marshal.ReAllocHGlobal(ptr, new IntPtr(newSize));

			return result.ToPointer();
		}

		public static void* realloc(void* a, ulong newSize)
		{
			return realloc(a, (long)newSize);
		}

		public static float fabs(double a)
		{
			return (float)Math.Abs(a);
		}

		public static float fabs(float a)
		{
			return MathF.Abs(a);
		}

		public static float floor(float a)
		{
			return MathF.Floor(a);
		}

		public static float ceil(float a)
		{
			return MathF.Ceiling(a);
		}

		public static float sqrt(float val)
		{
			return MathF.Sqrt(val);
		}

		public static float cos(float value)
		{
			return MathF.Cos(value);
		}

		public static float acos(float value)
		{
			return MathF.Acos(value);
		}

		public static float sin(float value)
		{
			return MathF.Sin(value);
		}

		public static int abs(int v)
		{
			return Math.Abs(v);
		}

		public static float pow(float a, float b)
		{
			return MathF.Pow(a, b);
		}

		public static void SetArray<T>(T[] data, T value)
		{
			for (var i = 0; i < data.Length; ++i)
				data[i] = value;
		}

		public static float ldexp(float number, int exponent)
		{
			return number * MathF.Pow(2, exponent);
		}

		public static int strcmp(sbyte* src, string token)
		{
			var result = 0;

			for (var i = 0; i < token.Length; ++i)
			{
				if (src[i] != token[i])
				{
					++result;
				}
			}

			return result;
		}

		public static int strncmp(sbyte* src, string token, ulong size)
		{
			var result = 0;

			for (var i = 0; i < Math.Min(token.Length, (int)size); ++i)
			{
				if (src[i] != token[i])
				{
					++result;
				}
			}

			return result;
		}

		public static long strtol(sbyte* start, sbyte** end, int radix)
		{
			// First step - determine length
			var length = 0;
			sbyte* ptr = start;
			while (numbers.IndexOf((char)*ptr) != -1)
			{
				++ptr;
				++length;
			}

			long result = 0;

			// Now build up the number
			ptr = start;
			while (length > 0)
			{
				long num = numbers.IndexOf((char)*ptr);
				long pow = (long)Math.Pow(10, length - 1);
				result += num * pow;

				++ptr;
				--length;
			}

			if (end != null)
			{
				*end = ptr;
			}

			return result;
		}

		public static float fmod(float x, float y)
		{
			return x % y;
		}

		public static ulong strlen(sbyte* str)
		{
			var ptr = str;

			while (*ptr != '\0')
				ptr++;

			return (ulong)ptr - (ulong)str - 1;
		}

		public delegate int QSortComparer(void* a, void* b);

		private static void qsortSwap(byte* data, long size, long pos1, long pos2)
		{
			var a = data + size * pos1;
			var b = data + size * pos2;

			for (long k = 0; k < size; ++k)
			{
				var tmp = *a;
				*a = *b;
				*b = tmp;

				a++;
				b++;
			}
		}

		private static long qsortPartition(byte* data, long size, QSortComparer comparer, long left, long right)
		{
			void* pivot = data + size * left;
			var i = left - 1;
			var j = right + 1;
			for (; ; )
			{
				do
				{
					++i;
				} while (comparer(data + size * i, pivot) < 0);

				do
				{
					--j;
				} while (comparer(data + size * j, pivot) > 0);

				if (i >= j)
				{
					return j;
				}

				qsortSwap(data, size, i, j);
			}
		}


		private static void qsortInternal(byte* data, long size, QSortComparer comparer, long left, long right)
		{
			if (left < right)
			{
				var p = qsortPartition(data, size, comparer, left, right);

				qsortInternal(data, size, comparer, left, p);
				qsortInternal(data, size, comparer, p + 1, right);
			}
		}

		public static void qsort(void* data, ulong count, ulong size, QSortComparer comparer)
		{
			qsortInternal((byte*)data, (long)size, comparer, 0, (long)count - 1);
		}

		private const long DBL_EXP_MASK = 0x7ff0000000000000L;
		private const int DBL_MANT_BITS = 52;
		private const long DBL_SGN_MASK = -1 - 0x7fffffffffffffffL;
		private const long DBL_MANT_MASK = 0x000fffffffffffffL;
		private const long DBL_EXP_CLR_MASK = DBL_SGN_MASK | DBL_MANT_MASK;

		public static double frexp(double number, int* exponent)
		{
			var bits = BitConverter.DoubleToInt64Bits(number);
			var exp = (int)((bits & DBL_EXP_MASK) >> DBL_MANT_BITS);
			*exponent = 0;

			if (exp == 0x7ff || number == 0D)
				number += number;
			else
			{
				// Not zero and finite.
				*exponent = exp - 1022;
				if (exp == 0)
				{
					// Subnormal, scale number so that it is in [1, 2).
					number *= BitConverter.Int64BitsToDouble(0x4350000000000000L); // 2^54
					bits = BitConverter.DoubleToInt64Bits(number);
					exp = (int)((bits & DBL_EXP_MASK) >> DBL_MANT_BITS);
					*exponent = exp - 1022 - 54;
				}

				// Set exponent to -1 so that number is in [0.5, 1).
				number = BitConverter.Int64BitsToDouble((bits & DBL_EXP_CLR_MASK) | 0x3fe0000000000000L);
			}

			return number;
		}
	}
}
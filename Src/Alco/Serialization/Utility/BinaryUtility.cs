using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Globalization;
using System;


namespace Alco
{
    /// <summary>
    /// Encoding helpers of the binary serialization format. Primitive scalars and enums are
    /// canonical little-endian on disk. Composite unmanaged layouts are host-layout blits,
    /// which requires a little-endian host (every runtime .NET ships today is one).
    /// </summary>
    public static class BinaryUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] EncodeValue<T>(T value) where T : unmanaged
        {
            byte[] bytes = new byte[Unsafe.SizeOf<T>()];
            if (typeof(T).IsPrimitive || typeof(T).IsEnum)
            {
                WriteScalar(bytes, ref value);
            }
            else
            {
                ThrowIfBigEndianHost(typeof(T));
                MemoryMarshal.Write(bytes, in value);
            }
            return bytes;
        }

        // Nullable<T> can be simply cast to T? but less readablity
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static byte[] EncodeNullableValue<T>(Nullable<T> value) where T : unmanaged
        {
            byte[] bytes = new byte[sizeof(Nullable<T>)];
            ThrowIfBigEndianHost(typeof(Nullable<T>));
            fixed (byte* ptr = bytes)
            {
                *(Nullable<T>*)ptr = value;
            }
            return bytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T DecodeToValue<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
        {
            if (typeof(T).IsPrimitive || typeof(T).IsEnum)
            {
                return ReadScalar<T>(bytes);
            }
            ThrowIfBigEndianHost(typeof(T));
            return MemoryMarshal.Read<T>(bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static Nullable<T> DecodeToNullableValue<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
        {
            ThrowIfBigEndianHost(typeof(Nullable<T>));
            fixed (byte* ptr = bytes)
            {
                return *(Nullable<T>*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] EncodeString(string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] EncodeStringNullable(string str)
        {
            if (str == null)
            {
                return Array.Empty<byte>();
            }
            return Encoding.UTF8.GetBytes(str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string DecodeToString(ReadOnlySpan<byte> bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string DecodeToStringNullable(ReadOnlySpan<byte> bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Encodes an enum value into a byte array
        /// </summary>
        /// <typeparam name="T">Enum type</typeparam>
        /// <param name="value">Enum value to encode</param>
        /// <returns>Byte array containing the enum value</returns>
        public static byte[] EncodeEnum<T>(T value) where T : unmanaged, Enum
        {
            return EncodeValue(value);
        }

        /// <summary>
        /// Write a primitive or enum scalar in canonical little-endian byte order.
        /// The size switch covers every C# scalar width (1/2/4/8); Unsafe.As is safe because
        /// the destination type is picked to match sizeof(T).
        /// </summary>
        private static void WriteScalar<T>(Span<byte> bytes, ref T value) where T : unmanaged
        {
            // Unsafe.SizeOf instead of sizeof: sizeof(T) is only legal in an
            // unsafe context, and newer compilers reject the previous usage.
            switch (Unsafe.SizeOf<T>())
            {
                case 1:
                    bytes[0] = Unsafe.As<T, byte>(ref value);
                    break;
                case 2:
                    BinaryPrimitives.WriteInt16LittleEndian(bytes, Unsafe.As<T, short>(ref value));
                    break;
                case 4:
                    BinaryPrimitives.WriteInt32LittleEndian(bytes, Unsafe.As<T, int>(ref value));
                    break;
                case 8:
                    BinaryPrimitives.WriteInt64LittleEndian(bytes, Unsafe.As<T, long>(ref value));
                    break;
                default:
                    throw new NotSupportedException($"Unhandled scalar size {Unsafe.SizeOf<T>()} for {typeof(T).Name}.");
            }
        }

        /// <summary>
        /// Read a primitive or enum scalar in canonical little-endian byte order.
        /// A scalar shorter than sizeof(T) decodes zero-extended: the historical blit picked
        /// the value's missing high bytes from the zeroed tail of its heap allocation.
        /// </summary>
        private static unsafe T ReadScalar<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
        {
            int size = sizeof(T);
            if (bytes.Length < size)
            {
                byte* buffer = stackalloc byte[8];
                Unsafe.InitBlock(buffer, 0, 8);
                bytes.CopyTo(new Span<byte>(buffer, bytes.Length));
                bytes = new ReadOnlySpan<byte>(buffer, size);
            }
            switch (size)
            {
                case 1:
                    {
                        byte v = bytes[0];
                        return Unsafe.As<byte, T>(ref v);
                    }
                case 2:
                    {
                        short v = BinaryPrimitives.ReadInt16LittleEndian(bytes);
                        return Unsafe.As<short, T>(ref v);
                    }
                case 4:
                    {
                        int v = BinaryPrimitives.ReadInt32LittleEndian(bytes);
                        return Unsafe.As<int, T>(ref v);
                    }
                case 8:
                    {
                        long v = BinaryPrimitives.ReadInt64LittleEndian(bytes);
                        return Unsafe.As<long, T>(ref v);
                    }
                default:
                    throw new NotSupportedException($"Unhandled scalar size {sizeof(T)} for {typeof(T).Name}.");
            }
        }

        private static void ThrowIfBigEndianHost(Type type)
        {
            if (!BitConverter.IsLittleEndian)
            {
                throw new NotSupportedException($"Composite unmanaged type {type.Name} is serialized as a host-layout blit, which requires a little-endian host.");
            }
        }
    }
}

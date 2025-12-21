using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Globalization;
using System;


namespace Alco
{
    public static class BinaryUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static byte[] EncodeValue<T>(T value) where T : unmanaged
        {
            byte[] bytes = new byte[sizeof(T)];
            fixed (byte* ptr = bytes)
            {
                *(T*)ptr = value;
            }
            return bytes;
        }

        // Nullable<T> can be simply cast to T? but less readablity
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static byte[] EncodeNullableValue<T>(Nullable<T> value) where T : unmanaged
        {
            byte[] bytes = new byte[sizeof(Nullable<T>)];
            fixed (byte* ptr = bytes)
            {
                *(T?*)ptr = value;
            }
            return bytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static T DecodeToValue<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
        {
            fixed (byte* ptr = bytes)
            {
                return *(T*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static Nullable<T> DecodeToNullableValue<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
        {
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
        public static unsafe byte[] EncodeEnum<T>(T value) where T : struct, Enum
        {
            // Use underlying type of enum for encoding
            Type underlyingType = Enum.GetUnderlyingType(typeof(T));
            int size = Unsafe.SizeOf<T>();

            byte[] bytes = new byte[size];
            fixed (byte* ptr = bytes)
            {
                if (underlyingType == typeof(Int32))
                {
                    *(int*)ptr = Unsafe.As<T, int>(ref value);
                }
                else if (underlyingType == typeof(UInt32))
                {   
                    *(uint*)ptr = Unsafe.As<T, uint>(ref value);
                }
                else if (underlyingType == typeof(Int16))
                {
                    *(short*)ptr = Unsafe.As<T, short>(ref value);
                }
                else if (underlyingType == typeof(UInt16))
                {
                    *(ushort*)ptr = Unsafe.As<T, ushort>(ref value);
                }
                else if (underlyingType == typeof(Byte))
                {
                    *(byte*)ptr = Unsafe.As<T, byte>(ref value);
                }
                else if (underlyingType == typeof(SByte))
                {
                    *(sbyte*)ptr = Unsafe.As<T, sbyte>(ref value);
                }
                else if (underlyingType == typeof(Int64))
                {
                    *(long*)ptr = Unsafe.As<T, long>(ref value);
                }
                else if (underlyingType == typeof(UInt64))
                {
                    *(ulong*)ptr = Unsafe.As<T, ulong>(ref value);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported enum underlying type: {underlyingType.Name}");
                }
            }
            return bytes;
        }
    }
}
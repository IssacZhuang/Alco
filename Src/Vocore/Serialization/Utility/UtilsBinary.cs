using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Globalization;
using System;


namespace Vocore
{
    internal static class SizeOf<T>
    {
        public static readonly int Value = Marshal.SizeOf(typeof(T));
    }
    public static class UtilsBinary
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static byte[] EncodeValue<T>(T value) where T : unmanaged
        {
            byte[] bytes = new byte[SizeOf<T>.Value];
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
        public unsafe static T DecodeToValue<T>(byte[] bytes) where T : unmanaged
        {
            fixed (byte* ptr = bytes)
            {
                return *(T*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static Nullable<T> DecodeToNullableValue<T>(byte[] bytes) where T : unmanaged
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
        public static string DecodeToString(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string DecodeToStringNullable(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }
            return Encoding.UTF8.GetString(bytes);
        }

        public static unsafe byte[] FastStringToBytes(string str)
        {
            fixed (char* ptr = str)
            {
                int length = str.Length;
                byte[] bytes = new byte[length * 2];
                fixed (byte* ptr2 = bytes)
                {
                    ushort* ptr3 = (ushort*)ptr;
                    ushort* ptr4 = (ushort*)ptr2;
                    for (int i = 0; i < length; i++)
                    {
                        ptr4[i] = ptr3[i];
                    }
                }
                return bytes;
            }
        }

        public static unsafe string FastBytesToString(byte[] bytes)
        {
            int hafLength = bytes.Length / 2;
            string text = new string('\0', hafLength);
            fixed (char* ptr = text)
            {
                fixed (byte* ptr2 = bytes)
                {
                    ushort* ptr3 = (ushort*)ptr;
                    ushort* ptr4 = (ushort*)ptr2;
                    for (int i = 0; i < hafLength; i++)
                    {
                        ptr3[i] = ptr4[i];
                    }
                }
            }
            return text;
        }
    }
}
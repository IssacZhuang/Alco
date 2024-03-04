using System;
using System.Runtime.InteropServices;

namespace Vocore
{
    public static class UtilsInterop
    {
        public static unsafe T[] ReadNativeArray<T>(T* ptr, uint count) where T : unmanaged
        {
            T[] array = new T[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = ptr[i];
            }
            return array;
        }

        public unsafe static string ReadString(byte* ptrString)
        {
            return Marshal.PtrToStringAnsi((IntPtr)ptrString) ?? string.Empty;
        }
    }
}
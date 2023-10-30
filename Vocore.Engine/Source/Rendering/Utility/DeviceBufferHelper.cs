using System;
using System.Collections.Generic;
using Vocore.Unsafe;

namespace Vocore.Engine
{
    public static class DeviceBufferHelper
    {
        public static uint GetUniformBufferSize<T>(T type) where T : unmanaged
        {
            uint size = (uint)UtilsMemory.SizeOf<T>();
            uint remainder = size % 16;
            //Uniform buffer size must be a multiple of 16 bytes.
            size += remainder == 0 ? 0 : 16 - remainder;
            return size;
        }

    }
}


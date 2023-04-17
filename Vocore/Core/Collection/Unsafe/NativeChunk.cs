using System;
using System.Collections.Generic;

namespace Vocore
{
    public unsafe struct NativeChunk
    {
        private const int bufferSize = 16064;
        private void* _buffer;
        private NativeChunk* _prev;
        private NativeChunk* _next;



    }
}


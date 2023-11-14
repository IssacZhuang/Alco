using System;
using System.Collections.Generic;

using Vocore.Unsafe;

namespace Vocore
{
    public unsafe struct StructRef<T> where T : unmanaged
    {
        private void* _ptr;
        public T Value => *(T*)_ptr;
        public T* Ptr => (T*)_ptr;

        public StructRef(T* ptr)
        {
            _ptr = ptr;
        }

        public void ReferTo(T* ptr)
        {
            _ptr = ptr;
        }
    }
}


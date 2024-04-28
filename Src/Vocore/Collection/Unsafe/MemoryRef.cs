using System;
using System.Runtime.CompilerServices;

namespace Vocore
{
    /// <summary>
    /// A readonly reference to a memory block
    /// </summary>
    public unsafe readonly ref struct MemoryRef
    {
        public readonly Span<byte> Span => new((byte*)Pointer, (int)Size);
        public MemoryRef(void* pointer, uint size)
        {
            Pointer = pointer;
            Size = size;
        }
        public readonly void* Pointer { get; }
        public readonly uint Size { get; }
    }

    /// <summary>
    /// A readonly reference to a memory block
    /// </summary>
    public unsafe readonly ref struct MemoryRef<T> where T : unmanaged
    {
        public ReadOnlySpan<T> Span => new(Pointer, (int)Length);
        public MemoryRef(T* pointer, int length)
        {
            Pointer = pointer;
            Length = length;
        }

        //indexer
        /// <summary>
        /// Unsafe indexer
        /// </summary>
        /// <value></value>
        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref Pointer[index];
            }
        }

        public readonly T* Pointer { get; }
        public readonly int Length { get; }
    }
}
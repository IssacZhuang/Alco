namespace Vocore
{
    /// <summary>
    /// A readonly reference to a memory block
    /// </summary>
    public unsafe readonly struct MemoryRef
    {
        public MemoryRef(void* pointer, int size)
        {
            Pointer = pointer;
            Size = size;
        }
        public readonly void* Pointer { get; }
        public readonly int Size { get; }
    }

    /// <summary>
    /// A readonly reference to a memory block
    /// </summary>
    public unsafe readonly struct MemoryRef<T> where T : unmanaged
    {
        public MemoryRef(T* pointer, int length)
        {
            Pointer = pointer;
            Length = length;
        }
        public readonly T* Pointer { get; }
        public readonly int Length { get; }
    }
}
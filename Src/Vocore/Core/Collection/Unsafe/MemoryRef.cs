namespace Vocore
{
    /// <summary>
    /// A readonly reference to a memory block
    /// </summary>
    public unsafe readonly struct MemoryRef
    {
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
    public unsafe readonly struct MemoryRef<T> where T : unmanaged
    {
        public MemoryRef(T* pointer, uint length)
        {
            Pointer = pointer;
            Length = length;
        }
        public readonly T* Pointer { get; }
        public readonly uint Length { get; }
    }
}
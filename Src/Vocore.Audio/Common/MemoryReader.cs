using System.Runtime.CompilerServices;

namespace Vocore.Audio;

internal unsafe struct MemoryReader
{
    private readonly byte* _buffer;
    private readonly uint _size;
    private readonly byte* _end;

    private byte* _p;


    public readonly byte* CurrentPointer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _p;
    }

    public MemoryReader(byte* buffer, uint size)
    {
        _buffer = buffer;
        _size = size;
        _p = _buffer;
        _end = _buffer + _size;
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipBytes(uint size)
    {
        if (_p + size > _end)
        {
            throw new EndOfStreamException("The buffer has been read to end");
        }
        _p += size;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly T Peek<T>() where T : unmanaged
    {
        return *(T*)_p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip<T>() where T : unmanaged
    {
        if (_p + sizeof(T) > _end)
        {
            throw new EndOfStreamException("The buffer has been read to end");
        }
        _p += sizeof(T);
    }

    public T Read<T>() where T : unmanaged
    {
        T value = Peek<T>();
        Skip<T>();
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        return Read<byte>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBool()
    {
        return Read<bool>();
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadSByte()
    {
        return Read<sbyte>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt()
    {
        return Read<int>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt()
    {
        return Read<uint>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadShort()
    {
        return Read<short>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUShort()
    {
        return Read<ushort>();
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat()
    {
        return Read<float>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble()
    {
        return Read<double>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadULong()
    {
        return Read<ulong>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadLong()
    {
        return Read<long>();
    }




}
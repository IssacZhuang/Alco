using System.IO;

namespace Vocore.Audio;

internal unsafe class UnsafeStream : Stream
{
    private readonly byte* _pointer;
    private readonly long _size;

    private long _position;

    public UnsafeStream(byte* pointer, int size)
    {
        _pointer = pointer;
        _size = size;
        _position = 0;
    }

    public UnsafeStream(byte* pointer, long size)
    {
        _pointer = pointer;
        _size = size;
        _position = 0;
    }

    public override bool CanRead
    {
        get => true;
    }

    public override bool CanSeek
    {
        get => true;
    }

    public override bool CanWrite
    {
        get => false;
    }

    public override long Length
    {
        get => _size;
    }

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override void Flush()
    {
        
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _size)
        {
            return 0;
        }

        long readCount = count;
        if (_position + readCount > _size)
        {
            readCount = _size - _position;
        }

        for (int i = 0; i < readCount; i++)
        {
            buffer[offset + i] = _pointer[_position + i];
        }

        _position += readCount;
        return (int)readCount;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin:
                _position = offset;
                break;
            case SeekOrigin.Current:
                _position += offset;
                break;
            case SeekOrigin.End:
                _position = _size + offset;
                break;
        }

        return _position;
    }

    public override void SetLength(long value)
    {
        //_size = value;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {

    }
}
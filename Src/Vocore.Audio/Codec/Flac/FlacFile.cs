using System.Text;
using Vocore.Unsafe;

namespace Vocore.Audio;

internal unsafe struct FlacFile : IDisposable
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("fLaC");

    private MemoryReader _reader;
    private readonly FlacMetadataStreamInfo _streamInfo;
    private readonly float* _data;
    private int _dataIndex = 0;

    private FlacFrameHeader? _currentFrameHeader;
    private NativeBuffer<int> _dataBuffer;
    private NativeBuffer<int> _residualBuffer;

    private int _currentFrameIndex = 0;

    public readonly uint Channels => _streamInfo.Channels;
    public readonly uint SampleRate => _streamInfo.SampleRate;
    public readonly uint BitsPerSample => _streamInfo.BitsPerSample;
    public readonly ulong TotalSamples => _streamInfo.TotalSamples;

    public FlacFile(byte* ptr, uint size)
    {
        _dataBuffer = new NativeBuffer<int>(1024);
        _residualBuffer = new NativeBuffer<int>(1024);
        byte* p = ptr;
        CheckMagic(ref p);

        MemoryReader reader = new MemoryReader(p, size);
        FlacMetadataBlockHeader header = new FlacMetadataBlockHeader(reader.CurrentPointer);
        reader.SkipBytes(FlacMetadataBlockHeader.ChunkSize);
        if (header.Type != FlacMetadataType.StreamInfo)
        {
            throw new Exception("StreamInfo not found in Flac file.");
        }

        _streamInfo = new FlacMetadataStreamInfo(reader.CurrentPointer);

        _data = UtilsMemory.Alloc<float>((int)_streamInfo.TotalSamples);

        reader.SkipBytes(FlacMetadataStreamInfo.ChunckSize);

        //skip other metadata blocks
        while (!header.IsLastMetadata)
        {
            header = new FlacMetadataBlockHeader(reader.CurrentPointer);

            reader.SkipBytes(FlacMetadataBlockHeader.ChunkSize);
            reader.SkipBytes(header.Size);
        }
    }

    public int ReadSamples(Span<float> output)
    {
        //FlacFrameHeader frameHeader = new FlacFrameHeader(reader.CurrentPointer, streamInfo);
    
        throw new NotImplementedException();
    }

    private void ReadFrame()
    {
        FlacFrameHeader frameHeader = new FlacFrameHeader(_reader.CurrentPointer, _streamInfo);
        _reader.SkipBytes(FlacFrameHeader.ChunkSize);

        _currentFrameHeader = frameHeader;
        Span<float> output = new Span<float>(_data + _dataIndex, frameHeader.BlockSize);
        _dataIndex += frameHeader.BlockSize;

        int bufferSize = (int)frameHeader.Channels * frameHeader.BlockSize;

        _dataBuffer.EnsureSizeWithoutCopy(bufferSize);
        _residualBuffer.EnsureSizeWithoutCopy(bufferSize);

        for (int i = 0; i < frameHeader.Channels; i++)
        {
            Span<int> buffer = new Span<int>(_dataBuffer.UnsafePointer + i * frameHeader.BlockSize, frameHeader.BlockSize);
            Span<int> residual = new Span<int>(_residualBuffer.UnsafePointer + i * frameHeader.BlockSize, frameHeader.BlockSize);

            int bitsPerSample = (int)frameHeader.BitsPerSample;
            if (frameHeader.ChannelAssignment == FlacChannelAssignment.MidSide || frameHeader.ChannelAssignment == FlacChannelAssignment.LeftSide)
            {
                bitsPerSample += i;
            }
            else if (frameHeader.ChannelAssignment == FlacChannelAssignment.RightSide)
            {
                bitsPerSample += 1 - i;
            }

            DecodeSubframe(frameHeader, buffer, residual, output, bitsPerSample);

        }
    }

    private void DecodeSubframe(FlacFrameHeader frameHeader, Span<int> buffer, Span<int> residual, Span<float> output, int bitsPerSample)
    {
        BitReader reader = new BitReader(_reader.CurrentPointer);
        uint firstByte = reader.ReadBitsToUint(8);
        if ((firstByte & 0x80) != 0)
        {
            throw new Exception("Flac subframe got non-zero padding.");
        }

        int wastedBits = 0;
        bool hasWastedBits = (firstByte & 1) != 0;
        if (hasWastedBits)
        {
            int k = (int)reader.ReadUnary();
            wastedBits = k + 1;
            bitsPerSample -= wastedBits;
        }

        uint subframeType = (firstByte & 0x7E) >> 1;

        if (subframeType == 0)
        {
            DecodeSubframeConstant(ref reader, buffer, frameHeader.BlockSize, bitsPerSample);
        }
        else if (subframeType == 1)
        {
            DecodeSubframeVerbatim(ref reader, buffer, frameHeader.BlockSize, bitsPerSample);
        }
        else if ((subframeType & 0x08) != 0)
        {
            //DecodeSubframeFixed(ref reader, buffer, frameHeader.BlockSize, bitsPerSample);
        }
        else if ((subframeType & 0x20) != 0)
        {
            //DecodeSubframeLPC(ref reader, buffer, frameHeader.BlockSize, bitsPerSample);
        }
        else
        {
            throw new Exception("Invalid subframe type.");
        }

        short crc16 = (short)reader.ReadBitsToUint(16);
        //TODO: check crc16
        _reader.SkipBytes((uint)reader.Position);

    }

    public void Dispose()
    {
        if (_data != null)
        {
            UtilsMemory.Free(_data);
        }

        _dataBuffer.Dispose();
        _residualBuffer.Dispose();
    }

    private static void CheckMagic(ref byte* p)
    {
        for (int i = 0; i < 4; i++)
        {
            if (p[i] != Magic[i])
            {
                throw new Exception("Invalid FLAC header.");
            }
        }
        p += 4;
    }


    //subframe decoders
    private static void DecodeSubframeConstant(ref BitReader reader, Span<int> buffer, int blockSize, int bitsPerSample)
    {
        int value = reader.ReadBitsToInt(bitsPerSample);
        for (int i = 0; i < blockSize; i++)
        {
            buffer[i] = value;
        }

    }

    private static void DecodeSubframeVerbatim(ref BitReader reader, Span<int> buffer, int blockSize, int bitsPerSample)
    {
        fixed (int* pBuffer = buffer)
        {
            for (int i = 0; i < blockSize; i++)
            {
                int value = reader.ReadBitsToInt(bitsPerSample);
                pBuffer[i] = value;
            }
        }
    }
}
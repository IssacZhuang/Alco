using System.Text;
using Vocore.Unsafe;

namespace Vocore.Audio;

internal unsafe struct FlacFile : IDisposable
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("fLaC");

    private MemoryReader _reader;
    private readonly FlacMetadataStreamInfo _streamInfo;
    private readonly float* _data;
    private readonly int _dataLength;
    private int _dataIndex = 0;
    private int _dataReadIndex = 0;

    private FlacFrameHeader _currentFrameHeader;
    private NativeBuffer<int> _dataBuffer;
    private NativeBuffer<int> _residualBuffer;



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

        _dataLength = (int)(_streamInfo.TotalSamples * _streamInfo.Channels);
        _data = UtilsMemory.Alloc<float>(_dataLength);

        reader.SkipBytes(FlacMetadataStreamInfo.ChunckSize);

        //skip other metadata blocks
        while (!header.IsLastMetadata)
        {
            header = new FlacMetadataBlockHeader(reader.CurrentPointer);

            reader.SkipBytes(FlacMetadataBlockHeader.ChunkSize);
            reader.SkipBytes(header.Size);
        }

        _reader = reader;
    }

    public int ReadSamples(Span<float> output)
    {
        if (_dataIndex >= _dataLength && _dataReadIndex >= _dataIndex)
        {
            return 0;
        }

        if (_dataReadIndex >= _dataIndex)
        {
            ReadFrame();
        }

        int blockSize = _currentFrameHeader.BlockSize;

        int samplesToRead = Math.Min(output.Length, _dataIndex - _dataReadIndex);
        for (int i = 0; i < samplesToRead; i++)
        {
            output[i] = _data[_dataReadIndex++];
        }

        //tofix: the samplesToRead is 0 on second frame
        return samplesToRead;
    }

    private void ReadFrame()
    {
        BitReader bitReader = new BitReader(_reader.CurrentPointer);

        FlacFrameHeader frameHeader = new FlacFrameHeader(_reader.CurrentPointer, _streamInfo);
        _reader.SkipBytes(frameHeader.ChunkSize);

        _currentFrameHeader = frameHeader;
        int bufferSize = (int)frameHeader.Channels * frameHeader.BlockSize;

        Span<float> output = new Span<float>(_data + _dataIndex, bufferSize);
        _dataIndex += bufferSize;

        _dataBuffer.EnsureSizeWithoutCopy(bufferSize);
        _residualBuffer.EnsureSizeWithoutCopy(bufferSize);

        bitReader = new BitReader(_reader.CurrentPointer);

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

            DecodeSubframe(ref bitReader, frameHeader, buffer, residual, bitsPerSample);
        }

        bitReader.Flush();

        short crc16 = (short)bitReader.ReadBitsToUint(16);
        //TODO: check crc16
        _reader.SkipBytes((uint)bitReader.Position);

        Span<nint> buffers = stackalloc nint[(int)frameHeader.Channels];
        for (int i = 0; i < frameHeader.Channels; i++)
        {
            buffers[i] = (nint)_dataBuffer.UnsafePointer + i * frameHeader.BlockSize;
        }
        MapToChannels(buffers, frameHeader);
        SetOutputInterleaved(output, buffers, (int)frameHeader.BitsPerSample, frameHeader.BlockSize, (int)frameHeader.Channels);
    }

    private void DecodeSubframe(ref BitReader reader, FlacFrameHeader frameHeader, Span<int> buffer, Span<int> residual, int bitsPerSample)
    {
        //BitReader reader = new BitReader(_reader.CurrentPointer);
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
            int order = (int)(subframeType & 0x07);
            if (order > 4) throw new Exception("Invalid prediction order.");
            UtilsFlac.DecodeSubFrameFixed(ref reader, frameHeader, buffer, residual, bitsPerSample, order);
        }
        else if ((subframeType & 0x20) != 0)
        {
            int order = (int)(subframeType & 0x1F) + 1;
            UtilsFlac.DecodeSubFrameLPC(ref reader, frameHeader, buffer, residual, bitsPerSample, order);
        }
        else
        {
            throw new Exception("Invalid subframe type.");
        }

        if (hasWastedBits)
        {
            for (int i = 0; i < frameHeader.BlockSize; i++)
            {
                buffer[i] <<= wastedBits;
            }
        }
    }

    private unsafe static void MapToChannels(Span<nint> buffers, FlacFrameHeader header)
    {
        int* pBuffer0 = (int*)buffers[0];
        int* pBuffer1 = (int*)buffers[1];
        if (header.ChannelAssignment == FlacChannelAssignment.LeftSide)
        {
            for (int i = 0; i < header.BlockSize; i++)
            {
                pBuffer1[i] = pBuffer0[i] - pBuffer1[i];
            }
        }
        else if (header.ChannelAssignment == FlacChannelAssignment.RightSide)
        {
            for (int i = 0; i < header.BlockSize; i++)
            {
                pBuffer0[i] += pBuffer1[i];
            }
        }
        else if (header.ChannelAssignment == FlacChannelAssignment.MidSide)
        {
            for (int i = 0; i < header.BlockSize; i++)
            {
                int mid = pBuffer0[i] << 1;
                int side = pBuffer1[i];

                mid |= (side & 1);

                pBuffer0[i] = (mid + side) >> 1;
                pBuffer1[i] = (mid - side) >> 1;
            }
        }
    }

    private unsafe static void SetOutputInterleaved(Span<float> output, Span<nint> buffer, int bitsPerSample, int blockSize, int channels)
    {
        fixed (nint* intPtrBuffer = buffer)
        fixed (float* pOutput = output)
        {
            int** pBuffer = (int**)intPtrBuffer;

            float* ptr = pOutput;
            switch (bitsPerSample)
            {
                case 8:
                    float inv128 = 1.0f / 128.0f;
                    for (int i = 0; i < blockSize; i++)
                    {
                        for (int j = 0; j < channels; j++)
                        {
                            *ptr++ = pBuffer[j][i] * inv128;
                        }
                    }
                    break;
                case 16:
                    float inv32768 = 1.0f / 32768.0f;
                    for (int i = 0; i < blockSize; i++)
                    {
                        for (int j = 0; j < channels; j++)
                        {
                            *ptr++ = pBuffer[j][i] * inv32768;
                        }
                    }
                    break;
                case 24:
                    float inv8388608 = 1.0f / 8388608.0f;
                    for (int i = 0; i < blockSize; i++)
                    {
                        for (int j = 0; j < channels; j++)
                        {
                            *ptr++ = pBuffer[j][i] * inv8388608;
                        }
                    }
                    break;
                case 32:
                    float inv2147483648 = 1.0f / 2147483648.0f;
                    for (int i = 0; i < blockSize; i++)
                    {
                        for (int j = 0; j < channels; j++)
                        {
                            *ptr++ = pBuffer[j][i] * inv2147483648;
                        }
                    }
                    break;
                default:
                    throw new Exception("Invalid bits per sample: " + bitsPerSample);
            }
        }
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
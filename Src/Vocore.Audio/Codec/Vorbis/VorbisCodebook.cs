using static Vocore.Audio.UtilsVorbis;

namespace Vocore.Audio;

internal unsafe ref struct VorbisCodebook : IDisposable
{

    private readonly uint _check;
    private readonly ushort _dimensions;
    private readonly int _entries;
    private readonly byte _orderedFlag;

    //todo: replace with umanaged memory
    private readonly int[] _lengths;

    public VorbisCodebook(byte* p)
    {
        _check = (uint)Read<UInt24>(ref p);
        if (!IsCodebook(_check))
        {
            throw new Exception("Invalid Vorbis codebook");
        }

        _dimensions = Read<ushort>(ref p);
        _entries = (int)Read<UInt24>(ref p);
        _orderedFlag = Read<byte>(ref p);

        _lengths = new int[_entries];

        BitReader reader = new BitReader(p);

        bool sparse;
        int total = 0;
        int maxLength = 0;


        if (_orderedFlag > 0)
        {
            int length = (int)reader.ReadBit(5) + 1;
            for (int i = 0; i < _entries;)
            {
                int count = (int)reader.ReadBit(IntLog((byte)(_entries - i)));
                while (--count >= 0)
                {
                    _lengths[i++] = length;
                }

                ++length;
            }

            total = 0;
            sparse = false;
            maxLength = length;
        }
        else
        {
            maxLength = -1;
            sparse = reader.ReadBitBool();
            for (int i = 0; i < _entries; i++)
            {
                if (sparse || reader.ReadBitBool())
                {
                    _lengths[i] = (int)reader.ReadBit(5) + 1;
                    ++total;
                }
                else
                {
                    // mark the entry as unused
                    _lengths[i] = -1;
                }
                if (_lengths[i] > maxLength)
                {
                    maxLength = _lengths[i];
                }
            }
        }

        
    }


    public void Dispose()
    {

    }
}
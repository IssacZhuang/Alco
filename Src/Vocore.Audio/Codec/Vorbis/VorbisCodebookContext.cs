
namespace Vocore.Audio;

internal unsafe ref struct VorbisCodebookContext : IDisposable
{
    //todo: replace with umanaged memory
    private readonly int[] _lengths;
    private readonly int[] _lookupTable;

    public VorbisCodebookContext(ref byte* p)
    {
        if (!UtilsVorbis.IsCodebook(p))
        {
            throw new Exception("Invalid Vorbis codebook");
        }

        VorbisCodebookData data = *(VorbisCodebookData*)p;
        p += sizeof(VorbisCodebookData);

        _lengths = new int[data.CodebookEntries];
        if (data.OrderedFlag > 0)
        {
            
            
        }
    }


    public void Dispose()
    {

    }
}
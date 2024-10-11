namespace Vocore.Audio;

public enum VorbisHeaderType : byte
{
    Continuation = 0x01,
    Beginning = 0x02,
    End = 0x04
}
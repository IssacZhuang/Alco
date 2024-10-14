namespace Vocore.Audio;

public enum OggPageFlag : byte
{
    None = 0x00,
    Continuation = 0x01,
    Beginning = 0x02,
    End = 0x04
}
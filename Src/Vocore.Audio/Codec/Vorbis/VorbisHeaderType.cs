namespace Vocore.Audio;

internal enum VorbisHeaderType : byte
{
    Identification = 1,
    Comment = 3,
    Setup = 5,
    Audio = 0 // Audio packets do not have a specific type byte
}
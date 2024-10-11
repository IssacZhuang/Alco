namespace Vocore.Audio;

public enum VorbisPacketType: byte
{
    Identification = 1,
    Comment = 3,
    Setup = 5,
    Audio = 0 // Audio packets do not have a specific type byte
}
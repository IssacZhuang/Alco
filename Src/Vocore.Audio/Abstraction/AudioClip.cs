using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vocore.Audio;

public unsafe abstract class AudioClip : BaseAudioObject
{
    public abstract int Channel { get; }
    public abstract int SampleRate { get; }
    public abstract int SampleCount { get; }
}
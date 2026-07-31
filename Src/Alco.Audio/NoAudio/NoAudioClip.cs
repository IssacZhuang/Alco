namespace Alco.Audio.NoAudio;

internal class NoAudioClip : AudioClip
{
    private readonly string _name;

    public override string Name => _name;
    public override int Channel => 1;
    public override int SampleRate => 44100;
    public override int SampleCount => 0;

    public NoAudioClip(string? name = null)
    {
        _name = name ?? string.Empty;
    }

    public override void UnsafeHotReload(ReadOnlySpan<float> data, int channel, int sampleRate)
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}

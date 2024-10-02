namespace Vocore.Audio.OpenAL;

internal class OpenALSource : AudioSource
{
    public override AudioClip? AudioClip { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }

    protected override void PlayCore()
    {
        throw new NotImplementedException();
    }
}
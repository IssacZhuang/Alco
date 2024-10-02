using Silk.NET.OpenAL;

namespace Vocore.Audio.OpenAL;

internal class OpenALListener : AudioListener
{
    private static readonly ALContext ALC = ALContext.GetApi();


    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }
}
using Silk.NET.OpenAL;

namespace Vocore.Audio.OpenAL;

internal unsafe class OpenALDevice : AudioDevice
{
    private static readonly ALContext ALC = ALContext.GetApi();
    private static readonly AL AL = AL.GetApi();

    private readonly Device* _device;

    public OpenALDevice()
    {
        _device = ALC.OpenDevice(string.Empty);

    }

    protected override void Dispose(bool disposing)
    {
        ALC.CloseDevice(_device);
    }

    protected override AudioSource CreateSourceCore()
    {
        throw new NotImplementedException();
    }

}
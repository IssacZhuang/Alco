using System.Numerics;
using Silk.NET.OpenAL;

namespace Vocore.Audio.OpenAL;

internal unsafe class OpenALDevice : AudioDevice
{
    private static readonly ALContext ALC = ALContext.GetApi();
    private static readonly AL AL = AL.GetApi();

    private readonly Device* _device;
    private readonly Lock _lock = new Lock();

    public override Vector3 ListenerPosition
    {
        get
        {
            AL.GetListenerProperty(ListenerVector3.Position, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }
        set
        {
            AL.SetListenerProperty(ListenerVector3.Position, value.X, value.Y, value.Z);
        }
    }

    public override Vector3 ListenerVelocity
    {
        get
        {
            AL.GetListenerProperty(ListenerVector3.Velocity, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }
        set
        {
            AL.SetListenerProperty(ListenerVector3.Velocity, value.X, value.Y, value.Z);
        }
    }

    public OpenALDevice()
    {
        _device = ALC.OpenDevice(string.Empty);
        ALC.MakeContextCurrent(ALC.CreateContext(_device, null));

    }

    protected override void Dispose(bool disposing)
    {
        ALC.CloseDevice(_device);
    }

    protected override AudioSource CreateAudioSourceCore()
    {
        //lock
        lock (_lock)
        {
            return new OpenALSource();
        }
    }

    protected override AudioClip CreateAudioClipCore(ReadOnlySpan<float> data, int channel, int sampleRate)
    {
        lock (_lock)
        {
            return new OpenALAudioClip(data, channel, sampleRate);
        }
    }
}
using System.Numerics;
using System.Runtime.InteropServices;
using NVorbis;
using Silk.NET.OpenAL;
using Vocore.Unsafe;

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

    public override Vector3 ListenerDirection
    {
        get
        {
            float* forwardAndUp = stackalloc float[6];
            AL.GetListenerProperty(ListenerFloatArray.Orientation, forwardAndUp);
            return new Vector3(forwardAndUp[0], forwardAndUp[1], forwardAndUp[2]);
        }
        set
        {
            float* forwardAndUp = stackalloc float[6];
            forwardAndUp[0] = value.X;
            forwardAndUp[1] = value.Y;
            forwardAndUp[2] = value.Z;
            forwardAndUp[3] = Vector3.UnitY.X;
            forwardAndUp[4] = Vector3.UnitY.Y;
            forwardAndUp[5] = Vector3.UnitY.Z;
            AL.SetListenerProperty(ListenerFloatArray.Orientation, forwardAndUp);
        }
    }

    public OpenALDevice()
    {
        _device = ALC.OpenDevice(string.Empty);
        ALC.MakeContextCurrent(ALC.CreateContext(_device, null));
        
        AL.DistanceModel(DistanceModel.InverseDistance);
        // AL.DopplerFactor(1);
        // AL.SpeedOfSound(343.3f);

        ListenerPosition = Vector3.Zero;
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
        float* ptrMono = null;
        try
        {
            if (channel == 2)
            {
                ptrMono = (float*)UtilsMemory.Alloc(data.Length * sizeof(float) / 2);
                Span<float> spanMono = new(ptrMono, data.Length / 2);
                UtilsAudioDecode.StereoToMono(data, spanMono);
                channel = 1;
                data = spanMono;
            }

            AudioClip clip;
            lock (_lock)
            {
                clip = new OpenALAudioClip(data, channel, sampleRate);
            }
            return clip;
        }
        finally
        {
            if (ptrMono != null)
            {
                UtilsMemory.Free(ptrMono);
            }
        }



    }
}
using System.Numerics;
using System.Runtime.InteropServices;

using Silk.NET.OpenAL;
using Alco;
using System;

namespace Alco.Audio.OpenAL;

internal unsafe class OpenALDevice : AudioDevice
{
    private const string AL_SOFT_source_spatialize = "AL_SOFT_source_spatialize";
    private const string AL_SOFT_direct_channels = "AL_SOFT_direct_channels";
    private const float BaseVolumeMultiplier = 0.8f; // 1.0 volume maps to 0.8 OpenAL gain

    private static readonly ALContext ALC = ALContext.GetApi(true);
    private static readonly AL AL = AL.GetApi(true);

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

    public override float Volume
    {
        get
        {
            AL.GetListenerProperty(ListenerFloat.Gain, out float gain);
            float volume = gain / BaseVolumeMultiplier;
            return volume;
        }
        set
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            float targetGain = clamped * BaseVolumeMultiplier;
            AL.SetListenerProperty(ListenerFloat.Gain, targetGain);
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

    public OpenALDevice(IAudioDeviceHost host) : base(host)
    {
        _device = ALC.OpenDevice(string.Empty);
        ALC.MakeContextCurrent(ALC.CreateContext(_device, null));
        
        AL.DistanceModel(DistanceModel.InverseDistance);
        // AL.DopplerFactor(1);
        // AL.SpeedOfSound(343.3f);

        ListenerPosition = Vector3.Zero;
        Volume = 1f;

        if (!AL.IsExtensionPresent(AL_SOFT_source_spatialize))
        {
            _host.LogWarning("AL_SOFT_source_spatialize is not supported, the spatialization is not available");
        }

        if (!AL.IsExtensionPresent(AL_SOFT_direct_channels))
        {
            _host.LogWarning("AL_SOFT_direct_channels is not supported, the direct channels is not available");
        }

        _host.LogSuccess("OpenAL device created");
    }

    protected override void Dispose(bool disposing)
    {
        ALC.CloseDevice(_device);
        _host.LogInfo("OpenAL device closed");
    }

    protected override AudioSource CreateAudioSourceCore()
    {
        //lock
        lock (_lock)
        {
            return new OpenALSource();
        }
    }

    protected override AudioClip CreateAudioClipCore(ReadOnlySpan<float> data, int channel, int sampleRate, string? name)
    {
        float* ptrMono = null;
        try
        {
            // if (channel == 2)
            // {
            //     ptrMono = (float*)UtilsMemory.Alloc(data.Length * sizeof(float) / 2);
            //     Span<float> spanMono = new(ptrMono, data.Length / 2);
            //     UtilsAudioDecode.StereoToMono(data, spanMono);
            //     channel = 1;
            //     data = spanMono;
            // }

            AudioClip clip;
            lock (_lock)
            {
                clip = new OpenALAudioClip(this, data, channel, sampleRate, name);
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

    internal void LogWarning(ReadOnlySpan<char> message)
    {
        _host.LogWarning(message);
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco;
using Silk.NET.OpenAL;

namespace Alco.Audio.OpenAL;

internal unsafe class OpenALDevice : AudioDevice
{
    private class SourcePool
    {
        private readonly IAudioDeviceHost _host;
        private readonly Lock _lock = new Lock();
        private readonly Stack<uint> _freeSources = new Stack<uint>();
        private readonly UnorderedList<uint> _activeSources = new UnorderedList<uint>();
        private readonly Dictionary<uint, WeakReference> _lookup = new Dictionary<uint, WeakReference>();

        public int Count => _freeSources.Count;

        public SourcePool(IAudioDeviceHost host, int maxSources)
        {
            _host = host;
            for (int i = 0; i < maxSources; i++)
            {
                uint id = AL.GenSource();
                if (AL.GetError() == AudioError.NoError && id != 0)
                {
                    _freeSources.Push(id);
                    _lookup.Add(id, new WeakReference(null));
                }
                else
                {
                    break;
                }
            }
        }

        private void SetActive(uint id, OpenALSource source)
        {
            _activeSources.Add(id);
            _lookup[id] = new WeakReference(source);
        }

        private void SetFree(uint id)
        {
            if (_activeSources.Remove(id))
            {
                _freeSources.Push(id);
                _lookup[id].Target = null;
            }
        }

        private bool TryGetSource(uint id, out WeakReference weakReference, [NotNullWhen(true)] out OpenALSource? source)
        {
            WeakReference weakRef = _lookup[id];
            if (weakRef.IsAlive && weakRef.Target != null)
            {
                source = (OpenALSource)weakRef.Target;
                weakReference = weakRef;
                return true;
            }
            source = null;
            weakReference = weakRef;
            return false;
        }

        public uint AllocateSource(OpenALSource owner)
        {
            lock (_lock)
            {
                if (_freeSources.Count > 0)
                {
                    uint id = _freeSources.Pop();
                    SetActive(id, owner);
                    return id;
                }

                for (int i = 0; i < _activeSources.Count; i++)
                {
                    uint id = _activeSources[i];
                    if (!TryGetSource(id, out WeakReference weakReference, out OpenALSource? activeOwner))
                    {
                        //already recycle byGC
                        weakReference.Target = owner;
                        return id;
                    }

                    AL.GetSourceProperty(id, GetSourceInteger.SourceState, out int state);
                    if (state != (int)SourceState.Playing && state != (int)SourceState.Paused)
                    {
                        // Source is stopped, we can reclaim it
                        activeOwner.DetachSource();
                        weakReference.Target = owner;
                        return id;
                    }
                }


                _host.LogWarning("OpenAL source limit reached and all sources are active.");
                return 0;
            }
        }

        public void FreeSource(OpenALSource expectedOwner, uint sourceId)
        {
            lock (_lock)
            {
                // Only free if the source is still owned by the requester
                if (_lookup[sourceId].Target == expectedOwner)
                {
                    SetFree(sourceId);
                }
            }
        }

        public void RecoverLoopingSources()
        {
            lock (_lock)
            {
                for (int i = 0; i < _activeSources.Count; i++)
                {
                    uint id = _activeSources[i];
                    if (TryGetSource(id, out _, out OpenALSource? source) && source.IsLooping)
                    {
                        AL.SourcePlay(id);
                        _host.LogInfo($"Recovered looping source {id}");
                    }
                }
            }
        }
    }

    private const string AL_SOFT_source_spatialize = "AL_SOFT_source_spatialize";
    private const string AL_SOFT_direct_channels = "AL_SOFT_direct_channels";
    private const float BaseVolumeMultiplier = 0.8f; // 1.0 volume maps to 0.8 OpenAL gain

    private static readonly ALContext ALC = ALContext.GetApi(true);
    private static readonly AL AL = AL.GetApi(true);

    private readonly Device* _device;
    private readonly Context* _context;
    private readonly Lock _lock = new Lock();
    private AudioAttenuationMode _attenuationMode = AudioAttenuationMode.Inverse;

    private readonly ALContextHelper _alcExt;
    private float _deviceCheckTimer;
    private bool _disconnected;
    private const float DeviceCheckInterval = 1.0f;

    private const int MaxSources = 256;
    private readonly SourcePool _sourcePool;

    public bool SupportsSpatialize { get; }
    public bool SupportsDirectChannels { get; }

    public override Vector3 ListenerPosition
    {
        get
        {
            AL.GetListenerProperty(ListenerVector3.Position, out float x, out float y, out float z);
            // Convert from OpenAL RH (Z forward negative) to engine LH (Z forward positive)
            return new Vector3(x, y, -z);
        }
        set
        {
            // Convert from engine LH to OpenAL RH by flipping Z
            AL.SetListenerProperty(ListenerVector3.Position, value.X, value.Y, -value.Z);
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

    public override AudioAttenuationMode AttenuationMode
    {
        get => _attenuationMode;
        set
        {
            if (_attenuationMode == value) return;

            _attenuationMode = value;
            UpdateDistanceModel();
        }
    }

    public override Vector3 ListenerVelocity
    {
        get
        {
            AL.GetListenerProperty(ListenerVector3.Velocity, out float x, out float y, out float z);
            // Convert from OpenAL RH to engine LH
            return new Vector3(x, y, -z);
        }
        set
        {
            // Convert from engine LH to OpenAL RH by flipping Z
            AL.SetListenerProperty(ListenerVector3.Velocity, value.X, value.Y, -value.Z);
        }
    }

    public override void GetListenerOrientation(out Vector3 forward, out Vector3 up)
    {
        float* forwardAndUp = stackalloc float[6];
        AL.GetListenerProperty(ListenerFloatArray.Orientation, forwardAndUp);
        // Convert from OpenAL RH to engine LH by flipping Z components
        forward = new Vector3(forwardAndUp[0], forwardAndUp[1], -forwardAndUp[2]);
        up = new Vector3(forwardAndUp[3], forwardAndUp[4], -forwardAndUp[5]);
    }

    public override void SetListenerOrientation(in Vector3 forward, in Vector3 up)
    {
        float* forwardAndUp = stackalloc float[6];
        // Convert from engine LH to OpenAL RH by flipping Z components
        forwardAndUp[0] = forward.X;
        forwardAndUp[1] = forward.Y;
        forwardAndUp[2] = -forward.Z;
        forwardAndUp[3] = up.X;
        forwardAndUp[4] = up.Y;
        forwardAndUp[5] = -up.Z;
        AL.SetListenerProperty(ListenerFloatArray.Orientation, forwardAndUp);
    }

    public OpenALDevice(IAudioDeviceHost host) : base(host)
    {
        _device = ALC.OpenDevice(string.Empty);
        _context = ALC.CreateContext(_device, null);
        ALC.MakeContextCurrent(_context);

        // AL.DopplerFactor(1);
        // AL.SpeedOfSound(343.3f);

        ListenerPosition = Vector3.Zero;
        Volume = 1f;
        AttenuationMode = AudioAttenuationMode.Inverse; // This will call UpdateDistanceModel()

        SupportsSpatialize = AL.IsExtensionPresent(AL_SOFT_source_spatialize);
        if (!SupportsSpatialize)
        {
            _host.LogWarning("AL_SOFT_source_spatialize is not supported, the spatialization is not available");
        }

        SupportsDirectChannels = AL.IsExtensionPresent(AL_SOFT_direct_channels);
        if (!SupportsDirectChannels)
        {
            _host.LogWarning("AL_SOFT_direct_channels is not supported, the direct channels is not available");
        }

        _alcExt = new ALContextHelper(_device);
        if (_alcExt.IsReopenSupported)
        {
            _host.LogInfo("ALC_SOFT_reopen_device extension loaded");
        }
        else
        {
            _host.LogWarning("ALC_SOFT_reopen_device is not supported, hot-plug recovery will not be available");
        }

        // Pre-allocate sources
        _sourcePool = new SourcePool(_host, MaxSources);

        _host.LogSuccess($"OpenAL device created with {_sourcePool.Count} sources allocated");
    }

    private void UpdateDistanceModel()
    {
        DistanceModel model = _attenuationMode switch
        {
            AudioAttenuationMode.Inverse => DistanceModel.InverseDistanceClamped,
            AudioAttenuationMode.Linear => DistanceModel.LinearDistanceClamped,
            AudioAttenuationMode.Exponent => DistanceModel.ExponentDistanceClamped,
            _ => DistanceModel.InverseDistanceClamped
        };

        AL.DistanceModel(model);
    }

    public override void Poll(float delta)
    {
        _deviceCheckTimer += delta;
        if (_deviceCheckTimer < DeviceCheckInterval) return;
        _deviceCheckTimer = 0f;

        if (!_alcExt.IsDeviceConnected(_device))
        {
            if (!_disconnected)
            {
                _disconnected = true;
                _host.LogWarning("Audio device disconnected");
            }
            TryReopenDevice(null);
        }
        else if (_disconnected)
        {
            _disconnected = false;
        }
    }

    public override void NotifyDefaultDeviceChanged()
    {
        if (_alcExt.IsReopenSupported)
        {
            TryReopenDevice(null);
        }
    }

    /// <summary>
    /// Checks whether the underlying audio output device is still connected.
    /// </summary>
    public bool IsDeviceConnected()
    {
        return _alcExt.IsDeviceConnected(_device);
    }

    /// <summary>
    /// Attempts to reopen the device on a different output.
    /// Pass null to reopen on the current default device.
    /// </summary>
    /// <param name="deviceName">Target device name, or null for default.</param>
    /// <returns>True if the device was successfully reopened.</returns>
    public bool ReopenDevice(string? deviceName)
    {
        return TryReopenDevice(deviceName);
    }

    private bool TryReopenDevice(string? deviceName)
    {
        if (_alcExt.TryReopenDevice(_device, deviceName))
        {
            _disconnected = false;
            _host.LogSuccess($"Audio device reopened successfully");
            _sourcePool.RecoverLoopingSources();
            return true;
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        ALC.MakeContextCurrent(null);
        ALC.DestroyContext(_context);
        ALC.CloseDevice(_device);
        _host.LogInfo("OpenAL device closed");
    }

    protected override AudioSource CreateAudioSourceCore()
    {
        //lock
        lock (_lock)
        {
            return new OpenALSource(this);
        }
    }

    protected override AudioClip CreateAudioClipCore(ReadOnlySpan<float> data, int channel, int sampleRate, string? name)
    {
        float* ptrMono = null;
        try
        {
            // if (channel == 2)
            // {
            //     ptrMono = (float*)MemoryUtility.Alloc(data.Length * sizeof(float) / 2);
            //     Span<float> spanMono = new(ptrMono, data.Length / 2);
            //     AudioDecodeUtility.StereoToMono(data, spanMono);
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
                MemoryUtility.Free(ptrMono);
            }
        }



    }

    internal uint AllocateSource(OpenALSource owner)
    {
        return _sourcePool.AllocateSource(owner);
    }

    internal void FreeSource(OpenALSource owner, uint sourceId)
    {
        _sourcePool.FreeSource(owner, sourceId);
    }

    internal void LogWarning(ReadOnlySpan<char> message)
    {
        _host.LogWarning(message);
    }
}
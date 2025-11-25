using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenAL;

namespace Alco.Audio.OpenAL;

internal class OpenALSource : AudioSource
{
    private const string AL_SOFT_direct_channels = "AL_SOFT_direct_channels";
    private const string AL_SOFT_source_spatialize = "AL_SOFT_source_spatialize";



    private static readonly ALContext ALC = ALContext.GetApi(true);
    private static readonly AL AL = AL.GetApi(true);

    private static readonly int AL_DIRECT_CHANNELS_SOFT = AL.GetEnumValue("AL_DIRECT_CHANNELS_SOFT");
    private static readonly int AL_SOURCE_SPATIALIZE_SOFT = AL.GetEnumValue("AL_SOURCE_SPATIALIZE_SOFT");
    private static readonly int AL_TRUE = AL.GetEnumValue("AL_TRUE");
    private static readonly int AL_FALSE = AL.GetEnumValue("AL_FALSE");

    private uint _sourceId;
    private readonly OpenALDevice _device;

    // Shadow state
    private float _gain = 1f;
    private float _pitch = 1f;
    private float _rolloff = 1f;
    private Vector3 _position = Vector3.Zero;
    private Vector3 _velocity = Vector3.Zero;
    private bool _isLooping = false;

    private AudioClip? _clip;

    private bool _isSpatial = true;

    public override float Gain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gain;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _gain = value;
            if (_sourceId != 0)
                AL.SetSourceProperty(_sourceId, SourceFloat.Gain, value);
        }
    }

    public override float Pitch
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pitch;
        set
        {
            _pitch = value;
            if (_sourceId != 0)
                AL.SetSourceProperty(_sourceId, SourceFloat.Pitch, value);
        }
    }

    public override float Rolloff
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _rolloff;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _rolloff = value;
            if (_sourceId != 0)
                AL.SetSourceProperty(_sourceId, SourceFloat.RolloffFactor, value);
        }
    }

    public override Vector3 Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _position;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _position = value;
            if (_sourceId != 0)
                // Convert from engine LH to OpenAL RH by flipping Z
                AL.SetSourceProperty(_sourceId, SourceVector3.Position, new Vector3(value.X, value.Y, -value.Z));
        }
    }
    public override Vector3 Velocity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _velocity;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _velocity = value;
            if (_sourceId != 0)
                // Convert from engine LH to OpenAL RH by flipping Z
                AL.SetSourceProperty(_sourceId, SourceVector3.Velocity, new Vector3(value.X, value.Y, -value.Z));
        }
    }

    public override bool IsLooping
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isLooping;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _isLooping = value;
            if (_sourceId != 0)
                AL.SetSourceProperty(_sourceId, SourceBoolean.Looping, value);
        }
    }

    public override bool IsPlaying
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_sourceId == 0) return false;
            AL.GetSourceProperty(_sourceId, GetSourceInteger.SourceState, out int state);
            return state == (int)SourceState.Playing;
        }
    }

    public override bool IsSpatial
    {
        get => _isSpatial;
        set
        {
            if (_isSpatial == value) return;
            _isSpatial = value;
            SetSpatialSetting(_isSpatial);
        }
    }


    public OpenALSource(OpenALDevice device) : base()
    {
        _device = device;
        SetSpatialSetting(_isSpatial);
    }

    protected override void Dispose(bool disposing)
    {
        StopCore();
    }

    public override AudioClip? AudioClip
    {
        get => _clip;
        set
        {
            Stop();
            if (value == _clip) return;
            _clip = value;
        }
    }

    protected override void PlayCore()
    {
        if (_sourceId == 0)
        {
            _sourceId = _device.AllocateSource(this);
        }

        if (_sourceId == 0) return;

        RestoreState();
        AL.SourcePlay(_sourceId);
    }

    

    protected override void StopCore()
    {
        if (_sourceId != 0)
        {
            AL.SourceStop(_sourceId);
            AL.SetSourceProperty(_sourceId, SourceInteger.Buffer, 0);
            _device.FreeSource(this, _sourceId);
            _sourceId = 0;
        }
    }

    internal void DetachSource()
    {
        if (_sourceId == 0) return;
        AL.SetSourceProperty(_sourceId, SourceInteger.Buffer, 0);
        _sourceId = 0;
    }

    private unsafe void TryBufferClip()
    {
        if (_clip == null) return;
        AL.SetSourceProperty(_sourceId, SourceInteger.Buffer, ((OpenALAudioClip)_clip).Buffer);
    }

    private void RestoreState()
    {
        if (_sourceId == 0) return;

        AL.SetSourceProperty(_sourceId, SourceFloat.Gain, _gain);
        AL.SetSourceProperty(_sourceId, SourceFloat.Pitch, _pitch);
        AL.SetSourceProperty(_sourceId, SourceFloat.RolloffFactor, _rolloff);
        AL.SetSourceProperty(_sourceId, SourceVector3.Position, new Vector3(_position.X, _position.Y, -_position.Z));
        AL.SetSourceProperty(_sourceId, SourceVector3.Velocity, new Vector3(_velocity.X, _velocity.Y, -_velocity.Z));
        AL.SetSourceProperty(_sourceId, SourceBoolean.Looping, _isLooping);

        SetSpatialSetting(_isSpatial);
        TryBufferClip();
    }

    private void SetSpatialSetting(bool isSpatial)
    {
        if (_sourceId == 0) return;

        if (AL.IsExtensionPresent(AL_SOFT_source_spatialize))//always be true is the assembly is OpenAL soft
        {
            int value = isSpatial ? AL_TRUE : AL_FALSE;
            AL.SetSourceProperty(_sourceId, (SourceInteger)AL_SOURCE_SPATIALIZE_SOFT, value);
        }


        AL.SetSourceProperty(_sourceId, SourceBoolean.SourceRelative, !isSpatial);
        if (AL.IsExtensionPresent(AL_SOFT_direct_channels))//always be true is the assembly is OpenAL soft
        {
            AL.SetSourceProperty(_sourceId, (SourceBoolean)AL_DIRECT_CHANNELS_SOFT, !isSpatial);
        }

    }
}
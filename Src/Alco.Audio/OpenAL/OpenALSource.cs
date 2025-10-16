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

    private readonly uint _source;

    private AudioClip? _clip;
    private bool _isClipSet;

    private bool _isSpatial = true;

    public override float Gain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, SourceFloat.Gain, out float value);
            return value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            AL.SetSourceProperty(_source, SourceFloat.Gain, value);
        }
    }

    public override float Pitch
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, SourceFloat.Pitch, out float value);
            return value;
        }
        set
        {
            AL.SetSourceProperty(_source, SourceFloat.Pitch, value);
        }
    }

    public override float Rolloff
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, SourceFloat.RolloffFactor, out float value);
            return value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            AL.SetSourceProperty(_source, SourceFloat.RolloffFactor, value);
        }
    }

    public override Vector3 Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, SourceVector3.Position, out Vector3 value);
            // Convert from OpenAL RH to engine LH by flipping Z
            return new Vector3(value.X, value.Y, -value.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            // Convert from engine LH to OpenAL RH by flipping Z
            AL.SetSourceProperty(_source, SourceVector3.Position, new Vector3(value.X, value.Y, -value.Z));
        }
    }
    public override Vector3 Velocity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, SourceVector3.Velocity, out Vector3 value);
            // Convert from OpenAL RH to engine LH by flipping Z
            return new Vector3(value.X, value.Y, -value.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            // Convert from engine LH to OpenAL RH by flipping Z
            AL.SetSourceProperty(_source, SourceVector3.Velocity, new Vector3(value.X, value.Y, -value.Z));
        }
    }

    public override bool IsLooping
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, SourceBoolean.Looping, out bool value);
            return value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            AL.SetSourceProperty(_source, SourceBoolean.Looping, value);
        }
    }

    public override bool IsPlaying
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            return state == (int)SourceState.Playing;
        }
    }

    public override bool IsSpatial
    {
        get => _isSpatial;
        set
        {
            if(_isSpatial == value) return;
            _isSpatial = value;
            SetSpatialSetting(_isSpatial);
        }
    }


    public OpenALSource() : base()
    {
        _source = AL.GenSource();

        Gain = 1f;
        Pitch = 1f;
        Rolloff = 1f;
        Position = Vector3.Zero;
        Velocity = Vector3.Zero;
        IsLooping = false;

        SetSpatialSetting(_isSpatial);
    }

    protected override void Dispose(bool disposing)
    {
        AL.DeleteSource(_source);
    }

    public override AudioClip? AudioClip
    {
        get => _clip;
        set
        {
            Stop();
            if (value == _clip) return;
            _clip = value;
            _isClipSet = false;
        }
    }

    protected override void PlayCore()
    {
        TryBufferClip();
        AL.SourcePlay(_source);
    }

    private unsafe void TryBufferClip()
    {
        if (_isClipSet)
        {
            return;
        }

        if (_clip == null)
        {
            return;
        }

        _isClipSet = true;
        AL.SetSourceProperty(_source, SourceInteger.Buffer, ((OpenALAudioClip)_clip).Buffer);
    }

    protected override void StopCore()
    {
        if (_clip == null)
        {
            return;
        }

        AL.SourceStop(_source);
    }

    private void SetSpatialSetting(bool isSpatial){
        if (AL.IsExtensionPresent(AL_SOFT_source_spatialize))//always be true is the assembly is OpenAL soft
        {
            int value = isSpatial ? AL_TRUE : AL_FALSE;
            AL.SetSourceProperty(_source, (SourceInteger)AL_SOURCE_SPATIALIZE_SOFT, value);
        }


        AL.SetSourceProperty(_source, SourceBoolean.SourceRelative, !isSpatial);
        if (AL.IsExtensionPresent(AL_SOFT_direct_channels))//always be true is the assembly is OpenAL soft
        {
            AL.SetSourceProperty(_source, (SourceBoolean)AL_DIRECT_CHANNELS_SOFT, !isSpatial);
        }
        
    }
}
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenAL;

namespace Vocore.Audio.OpenAL;

internal class OpenALSource : AudioSource
{
    private static readonly ALContext ALC = ALContext.GetApi();
    private static readonly AL AL = AL.GetApi();

    private readonly uint _source;

    private AudioClip? _clip;
    private bool _isClipSet;

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

    public override Vector3 Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, SourceVector3.Position, out Vector3 value);
            return value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            AL.SetSourceProperty(_source, SourceVector3.Position, value);
        }
    }
    public override Vector3 Velocity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AL.GetSourceProperty(_source, SourceVector3.Velocity, out Vector3 value);
            return value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            AL.SetSourceProperty(_source, SourceVector3.Velocity, value);
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


    public OpenALSource() : base()
    {
        _source = AL.GenSource();

        Gain = 1f;
        Pitch = 1f;
        Position = Vector3.Zero;
        Velocity = Vector3.Zero;
        IsLooping = false;        
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
}
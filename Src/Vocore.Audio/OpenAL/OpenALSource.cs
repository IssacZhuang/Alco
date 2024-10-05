using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenAL;

namespace Vocore.Audio.OpenAL;

internal class OpenALSource : AudioSource
{
    private static readonly ALContext ALC = ALContext.GetApi();
    private static readonly AL AL = AL.GetApi();

    private readonly uint _buffer;
    private readonly uint _source;

    private AudioClip? _clip;
    private bool _isClipBuffered;

    public override float Volume
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


    public OpenALSource() : base()
    {
        _buffer = AL.GenBuffer();
        _source = AL.GenSource();
    }

    protected override void Dispose(bool disposing)
    {
        AL.DeleteBuffer(_buffer);
        AL.DeleteSource(_source);
    }

    public override AudioClip? AudioClip
    {
        get => _clip;
        set
        {
            if (value == _clip) return;
            _clip = value;
            _isClipBuffered = false;
        }
    }



    protected override void PlayCore()
    {
        TryBufferClip();
        AL.SourcePlay(_source);
    }

    private unsafe void TryBufferClip()
    {
        if (_isClipBuffered)
        {
            return;
        }

        if (_clip == null)
        {
            return;
        }

        _isClipBuffered = true;
        AL.BufferData(_buffer, UtilsOpenAL.GetBufferFormat(_clip.Channel),
        _clip.UnsafePointer,
        _clip.Size,
        _clip.SampleRate
        );

        AL.SetSourceProperty(_source, SourceInteger.Buffer, _buffer);
    }
}
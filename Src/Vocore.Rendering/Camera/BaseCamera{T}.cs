using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The instance of a camera data in GPU.
/// </summary>
/// <typeparam name="T"> The type of the camera data. </typeparam>
public abstract class BaseCamera<T> : AutoDisposable, ICamera where T : unmanaged, ICameraData
{
    private readonly GraphicsValueBuffer<Matrix4x4> _buffer;
    protected T _data;
    protected bool _dirty;

    public BaseCamera(RenderingSystem renderingSystem)
    {
        _buffer = renderingSystem.CreateGraphicsValueBuffer<Matrix4x4>("camera_buffer");
        _dirty = true;
    }

    public GraphicsValueBuffer<Matrix4x4> Buffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public GPUResourceGroup EntryViewProjection
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_dirty)
            {
                _buffer.UpdateBuffer(_data.ViewProjectionMatrix);
                _dirty = false;
            }
            return _buffer.EntryReadonly;
        }
    }

    public T Data
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _data = value;
            _dirty = true;
        }
    }

    public void UpdateData()
    {
        _buffer.UpdateBuffer(_data.ViewProjectionMatrix);
        _dirty = false;
    }

    protected override void Dispose(bool disposing)
    {
        _buffer.Dispose();
    }
}
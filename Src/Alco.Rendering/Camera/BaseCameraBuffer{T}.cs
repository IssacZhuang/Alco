using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The instance of a camera data in GPU.
/// </summary>
/// <typeparam name="T"> The type of the camera data. </typeparam>
public abstract class BaseCameraBuffer<T> : GraphicsValueBuffer<Matrix4x4> where T : unmanaged, ICamera
{
    protected T _data;
    protected bool _dirty;

    public BaseCameraBuffer(RenderingSystem renderingSystem, string name) : base(renderingSystem, name)
    {
        _dirty = true;
    }

    public override GPUResourceGroup EntryReadonly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            FlushDirty();
            return base.EntryReadonly;
        }
    }

    // Bind-group assembly reads through this property, so the pending matrix upload flushes here.
    public override GPUBuffer NativeBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            FlushDirty();
            return base.NativeBuffer;
        }
    }

    private readonly Lock _flushLock = new();

    private void FlushDirty()
    {
        if (_dirty)
        {
            // Materials on any number of threads read this buffer during bind
            // group assembly; without the guard several of them would issue the
            // same pending upload concurrently (buffer writes are externally
            // synchronized in the native layer).
            lock (_flushLock)
            {
                if (_dirty)
                {
                    UpdateBuffer(_data.ViewProjectionMatrix);
                    _dirty = false;
                }
            }
        }
    }

    /// <summary>
    /// The camera data.
    /// </summary>
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

    /// <summary>
    /// Update the camera matrix on the GPU by writing the current view-projection matrix to the buffer.
    /// </summary>
    public void UpdateMatrixToGPU()
    {
        UpdateBuffer(_data.ViewProjectionMatrix);
        _dirty = false;
    }
}
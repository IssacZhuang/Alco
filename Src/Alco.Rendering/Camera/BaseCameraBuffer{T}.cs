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
            if (_dirty)
            {
                UpdateBuffer(_data.ViewProjectionMatrix);
                _dirty = false;
            }
            return base.EntryReadonly;
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
    /// Update the camera matrix to GPU. This method is uses <see cref="GraphicsBuffer.UpdateBuffer<T> to update the buffer./>.
    /// </summary>
    public void UpdateMatrixToGPU()
    {
        UpdateBuffer(_data.ViewProjectionMatrix);
        _dirty = false;
    }
}
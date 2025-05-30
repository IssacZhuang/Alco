using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// The GPU buffer that represents a 2D camera for rendering.
/// </summary>
public class Camera2DBuffer : BaseCameraBuffer<CameraData2D>
{
    internal Camera2DBuffer(RenderingSystem renderingSystem, Vector2 size, float near, float far, string name) : base(renderingSystem, name)
    {
        _data = new CameraData2D();
        ViewSize = size;
        Near = near;
        Far = far;
    }

    /// <summary>
    /// Gets or sets the position of the camera.
    /// </summary>
    public Vector2 Position
    {
        get => _data.Transform.Position;
        set
        {
            _data.Transform.Position = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the view size of the camera.
    /// </summary>
    public Vector2 ViewSize
    {
        get => _data.Size;
        set
        {
            _data.Size = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the width of the camera view.
    /// </summary>
    public float Width
    {
        get => _data.Size.X;
        set
        {
            _data.Size = new Vector2(value, _data.Size.Y);
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the height of the camera view.
    /// </summary>
    public float Height
    {
        get => _data.Size.Y;
        set
        {
            _data.Size = new Vector2(_data.Size.X, value);
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the near clipping plane distance.
    /// </summary>
    public float Near
    {
        get => _data.Near;
        set
        {
            _data.Near = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the far clipping plane distance.
    /// </summary>
    public float Far
    {
        get => _data.Far;
        set
        {
            _data.Far = value;
            _dirty = true;
        }
    }
}
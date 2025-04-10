using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Represents an orthographic camera for 2D rendering with adjustable view dimensions.
/// </summary>
public class CameraOrthographic : BaseCamera<CameraDataOrthographic>
{
    internal CameraOrthographic(RenderingSystem renderingSystem, string name) : base(renderingSystem, name)
    {
        _data.Transform = Transform3D.Identity;
    }

    /// <summary>
    /// Gets or sets the size of the camera's view rectangle.
    /// </summary>
    /// <value>A Vector2 where X represents width and Y represents height.</value>
    public Vector2 ViewSize
    {
        get => new Vector2(_data.Width, _data.Height);
        set
        {
            _data.Width = value.X;
            _data.Height = value.Y;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the width of the camera's view rectangle.
    /// </summary>
    /// <value>The width value of the orthographic camera.</value>
    public float Width
    {
        get => _data.Width;
        set
        {
            _data.Width = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the height of the camera's view rectangle.
    /// </summary>
    /// <value>The height value of the orthographic camera.</value>
    public float Height
    {
        get => _data.Height;
        set
        {
            _data.Height = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the far clipping plane distance.
    /// </summary>
    /// <value>The distance to the far clipping plane.</value>
    public float Far
    {
        get => _data.Far;
        set
        {
            _data.Far = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the near clipping plane distance.
    /// </summary>
    /// <value>The distance to the near clipping plane.</value>
    public float Near
    {
        get => _data.Near;
        set
        {
            _data.Near = value;
            _dirty = true;
        }
    }
}
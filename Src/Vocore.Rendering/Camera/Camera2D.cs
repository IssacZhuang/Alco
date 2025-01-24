using System.Numerics;

namespace Vocore.Rendering;

public class Camera2D : BaseCamera<CameraData2D>
{
    internal Camera2D(RenderingSystem renderingSystem, Vector2 size, float near, float far, string name) : base(renderingSystem, name)
    {
        _data = new CameraData2D();
        ViewSize = size;
        Near = near;
        Far = far;
    }

    public Vector2 Position
    {
        get => _data.transform.position;
        set
        {
            _data.transform.position = value;
            _dirty = true;
        }
    }

    public Vector2 ViewSize
    {
        get => _data.Size;
        set
        {
            _data.Size = value;
            _dirty = true;
        }
    }

    public float Width
    {
        get => _data.Size.X;
        set
        {
            _data.Size = new Vector2(value, _data.Size.Y);
            _dirty = true;
        }
    }

    public float Height
    {
        get => _data.Size.Y;
        set
        {
            _data.Size = new Vector2(_data.Size.X, value);
            _dirty = true;
        }
    }

    public float Near
    {
        get => _data.near;
        set
        {
            _data.near = value;
            _dirty = true;
        }
    }

    public float Far
    {
        get => _data.far;
        set
        {
            _data.far = value;
            _dirty = true;
        }
    }
}
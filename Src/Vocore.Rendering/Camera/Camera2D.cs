using System.Numerics;

namespace Vocore.Rendering;

public class Camera2D : BaseCamera<CameraData2D>
{
    internal Camera2D(RenderingSystem renderingSystem, string name) : base(renderingSystem, name)
    {
        _data = new CameraData2D();
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

    public Vector2 Size
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

    public float Depth
    {
        get => _data.depth;
        set
        {
            _data.depth = value;
            _dirty = true;
        }
    }
}
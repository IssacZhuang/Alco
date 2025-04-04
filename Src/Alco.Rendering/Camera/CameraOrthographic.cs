using System.Numerics;

namespace Alco.Rendering;

public class CameraOrthographic : BaseCamera<CameraDataOrthographic>
{
    internal CameraOrthographic(RenderingSystem renderingSystem, string name) : base(renderingSystem, name)
    {
        _data.Transform = Transform3D.Identity;
    }

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

    public float Width
    {
        get => _data.Width;
        set
        {
            _data.Width = value;
            _dirty = true;
        }
    }

    public float Height
    {
        get => _data.Height;
        set
        {
            _data.Height = value;
            _dirty = true;
        }
    }

    public float Far
    {
        get => _data.Far;
        set
        {
            _data.Far = value;
            _dirty = true;
        }
    }

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
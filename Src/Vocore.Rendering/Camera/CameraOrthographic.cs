using System.Numerics;

namespace Vocore.Rendering;

public class CameraOrthographic : BaseCamera<CameraDataOrthographic>
{
    internal CameraOrthographic(RenderingSystem renderingSystem, string name) : base(renderingSystem, name)
    {
        _data.tranform = Transform3D.Identity;
    }

    public Vector2 Size
    {
        get => new Vector2(_data.width, _data.height);
        set
        {
            _data.width = value.X;
            _data.height = value.Y;
            _dirty = true;
        }
    }

    public float Width
    {
        get => _data.width;
        set
        {
            _data.width = value;
            _dirty = true;
        }
    }

    public float Height
    {
        get => _data.height;
        set
        {
            _data.height = value;
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

    public float Near
    {
        get => _data.near;
        set
        {
            _data.near = value;
            _dirty = true;
        }
    }
}
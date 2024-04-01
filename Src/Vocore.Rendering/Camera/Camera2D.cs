using System.Numerics;

namespace Vocore.Rendering;

public class Camera2D : BaseCamera<CameraData2D>
{
    public Camera2D()
    {
        _data = new CameraData2D();
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
}
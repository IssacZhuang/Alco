namespace Vocore.Rendering;

public abstract class RendererWithCamera : Renderer
{
    private ICamera _camera;

    public ICamera Camera
    {
        get => _camera;
        set
        {
            _camera = value;
        }
    }

    protected RendererWithCamera(ICamera camera)
    {
        _camera = camera;
    }
}
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class Canvas : AutoDisposable
{
    private readonly CanvasRenderer _renderer;
    private readonly Camera2D _camera;
    private Font _font;
    public CanvasRenderer Renderer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderer;
    }

    public Font Font
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _font;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _font = value;
        }
    }

    public Canvas(RenderingSystem system, Shader shaderSprite, Shader shaderText, Font font)
    {
        _camera = system.CreateCamera2D(640, 360, 1);
        _renderer = system.CreateCanvasRenderer(_camera, shaderSprite, shaderText);
        _font = font;
    }

    public void Render(GPUFrameBuffer renderTarget, UINode root, float delta)
    {
        _renderer.Begin(renderTarget);
        root.Update(this, delta);
        _renderer.End();
    }

    protected override void Dispose(bool disposing)
    {

        _renderer.Dispose();
        _camera.Dispose();

    }
}
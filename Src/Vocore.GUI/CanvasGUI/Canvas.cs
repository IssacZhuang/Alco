using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class Canvas : AutoDisposable
{
    private class MousePointCaster : ICollisionCaster
    {
        public void OnHit(object hitObject, int userData)
        {

        }
    }
    private readonly CanvasRenderer _renderer;
    private readonly Camera2D _camera;
    private readonly CollisionWorld2D _collisionWorld; // for mouse events
    private readonly MousePointCaster _mousePointCaster;
    private IUIInputTracker? _inputTracker;
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

    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _camera.Size;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _camera.Size = value;
        }
    }

    public IUIInputTracker? InputTracker
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _inputTracker;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _inputTracker = value;
        }
    }

    public Canvas(RenderingSystem system, Shader shaderSprite, Shader shaderText, Font font)
    {
        _camera = system.CreateCamera2D(640, 360, 1);
        _renderer = system.CreateCanvasRenderer(_camera, shaderSprite, shaderText);
        _collisionWorld = new CollisionWorld2D();
        _mousePointCaster = new MousePointCaster();
        _font = font;
    }

    public void Update(GPUFrameBuffer renderTarget, UINode root, float delta)
    {
        _collisionWorld.ClearAll();

        _renderer.Begin(renderTarget);
        root.Update(this, delta);
        _renderer.End();

        if (_inputTracker != null && _inputTracker.IsMouseClicked)
        {
            //the mosue position is in screen space, the origin is at the top left corner
            Vector2 mousePosition = _inputTracker.MousePosition;
            Vector2 worldPosition = UtilsCameraMath.ScreenPointToWorld2D(mousePosition, _camera.Size, _camera.Data.ViewProjectionMatrix);
            _collisionWorld.BuildTree();
            _collisionWorld.CastPoint(_mousePointCaster, worldPosition);
        }
    }

    public void AddClickReciever(UINode node, ShapeBox2D shape)
    {
        _collisionWorld.PushTarget(node, shape);
    }

    protected override void Dispose(bool disposing)
    {
        _collisionWorld.Dispose();
        _renderer.Dispose();
        _camera.Dispose();

    }
}
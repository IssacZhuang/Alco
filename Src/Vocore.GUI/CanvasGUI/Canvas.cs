using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class Canvas : AutoDisposable
{
    private class MousePointCaster : ICollisionCaster
    {
        public ISelectable? hitSelectable;
        public IScrollable? hitScrollable;
        public void OnHit(object hitObject, int userData)
        {
            if (hitSelectable == null && hitObject is ISelectable node)
            {
                hitSelectable = node;
            }

            if (hitScrollable == null && hitObject is IScrollable scrollable)
            {
                hitScrollable = scrollable;
            }
        }

        public void Clear()
        {
            hitSelectable = null;
            hitScrollable = null;
        }
    }

    // for rendering

    private readonly WireframeRenderer? _debugRenderer;
    private readonly Camera2D _camera;
    private Vector2 _invCameraSize;
    private BoundingBox2D _bound;

    //for debug
    private readonly CanvasRenderer _renderer;
    private readonly Stack<UINode> _nodeStack = new Stack<UINode>();
    private bool _hasDebugDraw;

    // for event handling
    private readonly CollisionWorld2D _collisionWorld; // for mouse events
    private readonly MousePointCaster _mousePointCaster;
    private IUIInputTracker? _inputTracker;
    private ISelectable? _holded;
    private ISelectable? _hovered;
    private ISelectable? _selected;



    public CanvasRenderer Renderer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderer;
    }

    public BoundingBox2D Bound
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bound;
    }

    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _camera.Size;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _camera.Size = value;
            _invCameraSize = Vector2.One / new Vector2(value.X, value.Y);
            _bound = new BoundingBox2D(_camera.Position - value * 0.5f, _camera.Position + value * 0.5f);
        }
    }

    public Vector2 InvSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _invCameraSize;
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

    public ISelectable? Holded
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _holded;
    }

    public ISelectable? Hovered
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _hovered;
    }

    public ISelectable? Selected
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _selected;
    }

    public bool HasDebugDraw
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _hasDebugDraw;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (value && _debugRenderer == null)
            {
                Log.Warning("Canvas: Debug draw is not available because the wireframe shader is not provided.");
                return;
            }
            _hasDebugDraw = value;
        }
    }

    public Vector4 DebugDrawColor { get; set; }

    public Canvas(RenderingSystem system, Shader shaderSprite, Shader shaderText, Shader? shaderWireframe)
    {
        _camera = system.CreateCamera2D(640, 360, 1);
        _invCameraSize = Vector2.One / new Vector2(640, 360);
        _bound = new BoundingBox2D(_camera.Position - new Vector2(640, 360) * 0.5f, _camera.Position + new Vector2(640, 360) * 0.5f);
        _renderer = system.CreateCanvasRenderer(_camera, shaderSprite, shaderText);
        _collisionWorld = new CollisionWorld2D();
        _mousePointCaster = new MousePointCaster();

        if (shaderWireframe != null)
        {
            _debugRenderer = system.CreateWireframeRenderer(_camera, shaderWireframe);
        }
    }

    public void Tick(UINode root, float delta)
    {
        root.Tick(this, delta);
    }

    public void Update(GPUFrameBuffer renderTarget, UINode root, float delta)
    {
        _collisionWorld.ClearAll();

        _renderer.Begin(renderTarget);
        root.Update(this, delta);
        _renderer.End();

        //DebugDraw(renderTarget, root);

        if (_inputTracker == null)
        {

            return;
        }

        _hovered = null;
        //the mosue position is in screen space, the origin is at the top left corner
        
        Vector2 mousePosition = _inputTracker.MousePosition;
        Vector2 worldPosition = UtilsCameraMath.ScreenPointToWorld2D(mousePosition, _inputTracker.WindowSize, _camera.Data.ViewProjectionMatrix);
        
        _mousePointCaster.Clear();
        _collisionWorld.BuildTree();
        _collisionWorld.CastPoint(_mousePointCaster, worldPosition);

        ISelectable? selectable = _mousePointCaster.hitSelectable;
        _hovered = selectable;

        _holded?.OnDrag(worldPosition);

        if (_inputTracker.IsMouseDown)
        {
            OnMouseDown(selectable);
        }
        else if (_inputTracker.IsMouseUp)
        {
            OnMouseUp(selectable);
        }
        else if (_inputTracker.IsMousePressing)
        {
            selectable?.OnPressing();
        }
        else
        {
            selectable?.OnHover();
        }

        IScrollable? scrollable = _mousePointCaster.hitScrollable;

        if (_inputTracker.IsMouseScrolling(out Vector2 scrollDelta))
        {
            scrollable?.OnScroll(scrollDelta);
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
        _debugRenderer?.Dispose();
    }

    private void OnMouseDown(ISelectable? node)
    {
        if (node == null)
        {
            return;
        }
        _holded = node;
        _selected = node;
        node.OnPressDown();
    }

    private void OnMouseUp(ISelectable? node)
    {
        _holded?.OnPressUp();
        if (node == _holded)
        {
            _holded?.OnClick();
        }
        
        _holded = null;
    }

    private void DebugDraw(GPUFrameBuffer target, UINode root)
    {
        if (!_hasDebugDraw)
        {
            return;
        }
        if (_debugRenderer == null)
        {
            return;
        }

        _debugRenderer.Begin(target);
        //dfs the nodes using while loop
        _nodeStack.Clear();
        _nodeStack.Push(root);
        while (_nodeStack.Count > 0)
        {
            UINode node = _nodeStack.Pop();
            DrawNode(node);
            for (int i = 0; i < node.Children.Count; i++)
            {
                _nodeStack.Push(node.Children[i]);
            }
        }

        _debugRenderer.End();
    }

    private void DrawNode(UINode node)
    {
        _debugRenderer?.DrawBound(node.Bound, DebugDrawColor);
    }
}
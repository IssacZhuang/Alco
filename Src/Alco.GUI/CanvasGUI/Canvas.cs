using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public partial class Canvas : AutoDisposable
{
    private class MousePointCaster : ICollisionCaster
    {
        public UINode? hitSelectable;
        public void OnHit(object hitObject, int userData)
        {
            if (hitSelectable == null && hitObject is UINode node)
            {
                hitSelectable = node;
            }
        }

        public void Clear()
        {
            hitSelectable = null;
        }
    }

    // for rendering

    private readonly Camera2D _camera;
    private Vector2 _invCameraSize;
    private BoundingBox2D _bound;

    //for debug
    private readonly RenderingSystem _renderingSystem;
    private readonly RenderContext _renderContext;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly TextRenderer _textRenderer;

    private readonly Stack<UINode> _nodeStack;

    // for event handling
    private readonly CollisionWorld2D _collisionWorld; // for mouse events
    private readonly MousePointCaster _mousePointCaster;
    private readonly IUIInputTracker _inputTracker;


    private UINode? _holded;
    private UINode? _hovered;
    private UINode? _selected;
    private ITextInput? _textInput;

    public BoundingBox2D Bound
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bound;
    }

    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _camera.ViewSize;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _camera.ViewSize = value;
            _camera.UpdateMatrixToGPU();
            _invCameraSize = Vector2.One / new Vector2(value.X, value.Y);
            _bound = new BoundingBox2D(_camera.Position - value * 0.5f, _camera.Position + value * 0.5f);
        }
    }

    public Vector2 InvSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _invCameraSize;
    }

    public UINode? Holded
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _holded;
    }

    public UINode? Hovered
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _hovered;
    }

    public UINode? Selected
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _selected;
    }

    public Vector4 DebugDrawColor { get; set; }

    public Action<Exception>? ErrorHandler;

    public Canvas(RenderingSystem system, IUIInputTracker inputTracker, Material defaultSpriteMaterial, Material defaultTextMaterial)
    {
        _renderingSystem = system;

        _inputTracker = inputTracker;
        _inputTracker.RegisterTextInput(OnTextInput);
        _camera = system.CreateCamera2D(640, 360, 1);
        _invCameraSize = Vector2.One / new Vector2(640, 360);
        _bound = new BoundingBox2D(_camera.Position - new Vector2(640, 360) * 0.5f, _camera.Position + new Vector2(640, 360) * 0.5f);
        _renderContext = system.CreateRenderContext();

        MaterialInstance defaultSpriteMaterialInstance = defaultSpriteMaterial.CreateInstance();
        defaultSpriteMaterialInstance.TrySetBuffer(ShaderResourceId.Camera, _camera);
        MaterialInstance defaultTextMaterialInstance = defaultTextMaterial.CreateInstance();
        defaultTextMaterialInstance.TrySetBuffer(ShaderResourceId.Camera, _camera);

        _spriteRenderer = system.CreateSpriteRenderer(_renderContext, defaultSpriteMaterialInstance);
        _textRenderer = system.CreateTextRenderer(_renderContext, defaultTextMaterialInstance);

        _collisionWorld = new CollisionWorld2D();
        _mousePointCaster = new MousePointCaster();

        _nodeStack = new Stack<UINode>();
    }

    public void Tick(UINode root, float delta)
    {
        _nodeStack.Clear();
        _nodeStack.Push(root);
        while (_nodeStack.Count > 0)
        {
            UINode node = _nodeStack.Pop();
            if (!node.IsEnable)
            {
                continue;
            }

            try
            {
                node.Tick(this, delta);
            }
            catch (Exception e)
            {
                if (ErrorHandler != null)
                {
                    ErrorHandler(e);
                }
                else
                {
                    Log.Error($"Error in ticking {node.Name}: {e}");
                }
            }

            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                _nodeStack.Push(node.Children[i]);
            }
        }
    }

    public void Update(GPUFrameBuffer renderTarget, UINode root, float delta)
    {
        _collisionWorld.ClearAll();

        _renderContext.Begin(renderTarget);
        _nodeStack.Clear();
        _nodeStack.Push(root);
        while (_nodeStack.Count > 0)
        {
            UINode node = _nodeStack.Pop();

            if (!node.IsEnable)
            {
                continue;
            }

            try
            {
                node.Update(this, delta);
            }
            catch (Exception e)
            {
                if (ErrorHandler != null)
                {
                    ErrorHandler(e);
                }
                else
                {
                    Log.Error($"Error in updating {node.Name}: {e}");
                }
            }

            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                _nodeStack.Push(node.Children[i]);
            }
        }
        _renderContext.End();

        //DebugDraw(renderTarget, root);

        if (_inputTracker == null)
        {

            return;
        }

        _hovered = null;
        //the mosue position is in screen space, the origin is at the top left corner
        
        Vector2 mousePosition = _inputTracker.MousePosition;
        Vector2 mouseWorldPosition = UtilsCameraMath.ScreenPointToWorld2D(mousePosition, _inputTracker.WindowSize, _camera.Data.ViewProjectionMatrix);
        
        _mousePointCaster.Clear();
        _collisionWorld.BuildTree();
        _collisionWorld.CastPoint(_mousePointCaster, mouseWorldPosition);

        UINode? selectable = _mousePointCaster.hitSelectable;
        _hovered = selectable;

        

        if (_inputTracker.IsMouseDown)
        {
            OnMouseDown(selectable, mouseWorldPosition);
        }
        else if (_inputTracker.IsMouseUp)
        {
            OnMouseUp(selectable, mouseWorldPosition);
        }
        else if (_inputTracker.IsMousePressing)
        {
            selectable?.OnPressing(this, mouseWorldPosition);
        }
        else
        {
            selectable?.OnHover(this, mouseWorldPosition);
        }

        _holded?.OnDrag(this, mouseWorldPosition);


        //input box shortcut
        if (_inputTracker.IsKeyBackspaceDown)
        {
            _textInput?.HandleKeyBackspace();
        }
        else if (_inputTracker.IsKeyDeleteDown)
        {
            _textInput?.HandleKeyDelete();
        }
        else if (_inputTracker.IsKeyEnterDown)
        {
            _textInput?.OnTextInput(this, "\n");
        }
        else if (_inputTracker.IsKeyTabDown)
        {
            _textInput?.HandleKeyTab();
        }
        else if (_inputTracker.IsKeyLeftDown)
        {
            _textInput?.HandleKeyArrowLeft();
        }
        else if (_inputTracker.IsKeyRightDown)
        {
            _textInput?.HandleKeyArrowRight();
        }
        else if (_inputTracker.IsKeyUp)
        {
            _textInput?.HandleKeyArrowUp();
        }
        else if (_inputTracker.IsKeyDown)
        {
            _textInput?.HandleKeyArrowDown();
        }
        else if (_inputTracker.IsKeySelectAllDown)
        {
            _textInput?.SelectAll();
        }
        else if (_inputTracker.IsKeyCopyDown)
        {
            if (_textInput != null)
            {
                _inputTracker.CopyToClipboard(_textInput.GetSelectedText());
            }
        }else if (_inputTracker.IsKeyPasteDown)
        {
            ReadOnlySpan<char> text = _inputTracker.GetClipboardText();
            if (text.Length > 0)
            {
                _textInput?.OnTextInput(this, text);
            }
        }
    }

    public void AddClickReciever(UINode node, ShapeBox2D shape)
    {
        _collisionWorld.PushTarget(node, shape);
    }

    public void SetTextInputArea(ITextInput node, BoundingBox2D inputArea, int cursor)
    {
        _textInput = node;

        Vector2 position = inputArea.Center;
        Vector2 size = inputArea.Size;

        float widthNorm = size.X * _invCameraSize.X;
        float heightNorm = size.Y * _invCameraSize.Y;

        float x = position.X - size.X * 0.5f;
        float y = position.Y + size.Y * 0.5f;

        float xNorm = x * _invCameraSize.X + 0.5f;
        float yNorm = 0.5f - y * _invCameraSize.Y;

        _inputTracker?.SetTextInput(xNorm, yNorm, widthNorm, heightNorm, cursor);
    }


    protected override void Dispose(bool disposing)
    {
        _inputTracker?.UnregisterTextInput(OnTextInput);
        _collisionWorld.Dispose();
        _renderContext.Dispose();
        _spriteRenderer.Dispose();
        _textRenderer.Dispose();
        _camera.Dispose();
    }

    private void OnMouseDown(UINode? node, Vector2 mousePosition)
    {
        _holded = node;

        node?.OnPressDown(this, mousePosition);

        if (_selected != null && _selected != node)
        {
            _selected.OnDeselect(this, mousePosition);
        }
        _selected = node;
        _selected?.OnSelect(this, mousePosition);
    }

    private void OnMouseUp(UINode? node, Vector2 mousePosition)
    {
        _holded?.OnPressUp(this, mousePosition);
        if (node == _holded)
        {
            _holded?.OnClick(this, mousePosition);
        }
        
        _holded = null;
    }

    private void OnTextInput(string text)
    {
        _textInput?.OnTextInput(this, text);
    }
}
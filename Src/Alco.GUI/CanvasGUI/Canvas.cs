using System.Numerics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public partial class Canvas : AutoDisposable
{

    private struct MaskContext
    {
        public Texture2D? texture;
        public Matrix4x4 matrix;
        public Rect uvRect;
    }

    private struct InputState
    {
        public bool IsDown { get; private set; }
        public bool IsUp { get; private set; }
        public bool IsPressing { get; private set; }

        public void SetState(bool pressing)
        {
            bool wasPressing = IsPressing;
            IsPressing = pressing;
            IsDown = pressing && !wasPressing;
            IsUp = !pressing && wasPressing;
        }
    }


    // for rendering

    private readonly Camera2DBuffer _camera;
    private Vector2 _invCameraSize;
    private BoundingBox2D _bound;

    //for debug
    private readonly RenderingSystem _renderingSystem;
    private readonly RenderContext _renderContext;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly TextRenderer _textRenderer;
    private readonly DynamicMeshRenderer _dynamicMeshRenderer;

    private readonly Material _textMaterial;
    private readonly Material _spriteMaterial;
    private readonly Material _stencilIncreaseMaterial;
    private readonly Material _stencilDecreaseMaterial;
    private readonly uint _shaderId_texture;

    private uint _mask = 0;

    // for event handling
    private readonly CollisionWorld2D _collisionWorld; // for mouse events
    private readonly List<UINode> _hitNodes = new List<UINode>(64);
    private readonly IUIInputTracker _inputTracker;

    public Font DefaultFont { get; }


    private UINode? _holded;
    private UINode? _hovered;
    private UINode? _selected;
    private ITextInput? _textInput;
    private InputState _mouseLeftState;
    private InputState _keyBackspaceState;
    private InputState _keyDeleteState;
    private InputState _keyEnterState;
    private InputState _keyTabState;
    private InputState _keyLeftState;
    private InputState _keyRightState;
    private InputState _keyUpState;
    private InputState _keyDownState;
    private InputState _selectAllState;
    private InputState _copyState;
    private InputState _pasteState;

    public bool IsCapturingMouse => _hovered != null || _holded != null;

    public bool IsCapturingKeyboard => _textInput != null;

    public Vector2 MousePosition {get;private set;}

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
            if (_camera.ViewSize == value)
            {
                return;
            }
            _camera.ViewSize = value;
            _camera.UpdateMatrixToGPU();
            _invCameraSize = Vector2.One / new Vector2(value.X, value.Y);
            _bound = new BoundingBox2D(_camera.Position - value * 0.5f, _camera.Position + value * 0.5f);
            // propagate size change to root node so layout matches canvas
            Root.Size = value;
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

    /// <summary>
    /// The root node of this canvas. All UI nodes should be added under this node.
    /// </summary>
    public UIRoot Root { get; }

    public Canvas(
        RenderingSystem system,
        IUIInputTracker inputTracker,
        Material defaultSpriteMaterial,
        Material defaultTextMaterial,
        Font defaultFont
        )
    {

        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(inputTracker);
        ArgumentNullException.ThrowIfNull(defaultSpriteMaterial);
        ArgumentNullException.ThrowIfNull(defaultTextMaterial);
        ArgumentNullException.ThrowIfNull(defaultFont);

        _renderingSystem = system;

        DefaultFont = defaultFont;

        _inputTracker = inputTracker;
        _inputTracker.RegisterTextInput(OnTextInput);
        _camera = system.CreateCamera2D(640, 360, 1);
        _invCameraSize = Vector2.One / new Vector2(640, 360);
        _bound = new BoundingBox2D(_camera.Position - new Vector2(640, 360) * 0.5f, _camera.Position + new Vector2(640, 360) * 0.5f);
        _renderContext = system.CreateRenderContext();

        _spriteMaterial = defaultSpriteMaterial.CreateInstance();
        _spriteMaterial.TrySetBuffer(ShaderResourceId.Camera, _camera);
        _spriteMaterial.DepthStencilState = DepthStencilState.Default with
        {
            FrontFace = StencilFaceState.CompareEqual,
            BackFace = StencilFaceState.CompareEqual,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };

        _textMaterial = defaultTextMaterial.CreateInstance();
        _textMaterial.TrySetBuffer(ShaderResourceId.Camera, _camera);
        _textMaterial.DepthStencilState = DepthStencilState.Default with
        {
            FrontFace = StencilFaceState.CompareEqual,
            BackFace = StencilFaceState.CompareEqual,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };

        //stencil write
        _stencilIncreaseMaterial = defaultSpriteMaterial.CreateInstance();
        _stencilIncreaseMaterial.TrySetBuffer(ShaderResourceId.Camera, _camera);

        _shaderId_texture = _stencilIncreaseMaterial.GetResourceId(ShaderResourceId.Texture);

        StencilFaceState stencilIncrease = new StencilFaceState(
            CompareFunction.Equal,
            StencilOperation.IncrementWrap,
            StencilOperation.Keep,
            StencilOperation.Keep
            );

        _stencilIncreaseMaterial.DepthStencilState = DepthStencilState.Default with
        {
            FrontFace = stencilIncrease,
            BackFace = stencilIncrease,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };

        StencilFaceState stencilDecrease = new StencilFaceState(
            CompareFunction.Equal,
            StencilOperation.DecrementWrap,
            StencilOperation.Keep,
            StencilOperation.Keep
            );

        _stencilDecreaseMaterial = defaultSpriteMaterial.CreateInstance();
        _stencilDecreaseMaterial.TrySetBuffer(ShaderResourceId.Camera, _camera);
        _stencilDecreaseMaterial.DepthStencilState = DepthStencilState.Default with
        {
            FrontFace = stencilDecrease,
            BackFace = stencilDecrease,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };


        _shaderId_texture = _stencilDecreaseMaterial.GetResourceId(ShaderResourceId.Texture);



        _spriteRenderer = system.CreateSpriteRenderer(_renderContext, _spriteMaterial);
        _textRenderer = system.CreateTextRenderer(_renderContext, _textMaterial);

        // sliced mesh: 16 vertices (size of vertex is 20 bytes, 320 in total), 54 indices (2 byte per index, 108 in total)
        // support 1024 sliced mesh per chunk in dynamic mesh renderer
        _dynamicMeshRenderer = system.CreateDynamicMeshRenderer(_renderContext, 320 * 1024, 108 * 1024);

        _collisionWorld = new CollisionWorld2D();

        // initialize root node owned by canvas
        Root = new UIRoot(this)
        {
            Name = "Root"
        };
    }

    /// <summary>
    /// Ticks the UI tree rooted at <see cref="Root"/>.
    /// </summary>
    /// <param name="delta">Delta time in seconds.</param>
    public void Tick(float delta)
    {
        _collisionWorld.ClearAll();
        TickNode(Root, delta);
        HandleInput();
    }

    /// <summary>
    /// Updates and renders the UI tree rooted at <see cref="Root"/> to the render target.
    /// </summary>
    /// <param name="renderTarget">The render target.</param>
    /// <param name="delta">Delta time in seconds.</param>
    public void Update(GPUFrameBuffer renderTarget, float delta)
    {
        _renderContext.Begin(renderTarget);
        _mask = 0;
        UpdateNode(Root, delta);
        _renderContext.End();

        //DebugDraw(renderTarget, Root);
    }



    public void AddClickReciever(UINode node, ShapeBox2D shape)
    {
        _collisionWorld.PushCollisionTarget(node, shape);
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
        if (disposing)
        {
            _inputTracker?.UnregisterTextInput(OnTextInput);
            _collisionWorld.Dispose();
            _renderContext.Dispose();
            _spriteRenderer.Dispose();
            _textRenderer.Dispose();
            _dynamicMeshRenderer.Dispose();
            _camera.Dispose();
        }
    }



    private void OnMouseDown(UINode? node, Vector2 mousePosition)
    {
        if (node == null || !CheckMask(node, mousePosition))
        {
            return;
        }

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

    private void OnTextInput(ReadOnlySpan<char> text)
    {
        _textInput?.OnTextInput(this, text);
    }

    private void HandleInput()
    {
        if (_inputTracker == null)
        {
            return;
        }

        _hovered = null;
        //the mouse position is in screen space, the origin is at the top left corner

        Vector2 mousePosition = _inputTracker.MousePosition;
        Vector2 mouseWorldPosition = UtilsCameraMath.ScreenPointToWorld2D(mousePosition, _inputTracker.WindowSize, _camera.Data.ViewProjectionMatrix);

        MousePosition = mouseWorldPosition;

        _hitNodes.Clear();
        _collisionWorld.BuildTree();
        _collisionWorld.CastPoint(_hitNodes, mouseWorldPosition);

        UINode? selectable = null;
        for (int i = 0; i < _hitNodes.Count; i++)
        {
            UINode node = _hitNodes[i];
            if (CheckMask(node, mouseWorldPosition))
            {
                selectable = node;
                break;
            }
        }
        _hovered = selectable;

        _mouseLeftState.SetState(_inputTracker.IsMousePressing);

        if (_mouseLeftState.IsDown)
        {
            OnMouseDown(selectable, mouseWorldPosition);
        }
        else if (_mouseLeftState.IsUp)
        {
            OnMouseUp(selectable, mouseWorldPosition);
        }
        else if (_mouseLeftState.IsPressing)
        {
            if (selectable != null)
            {
                selectable.OnPressing(this, mouseWorldPosition);
            }
        }
        else
        {
            if (selectable != null)
            {
                selectable.OnHover(this, mouseWorldPosition);
            }
        }

        _holded?.OnDrag(this, mouseWorldPosition);

        // update key states (pressing => edge detection here)
        _keyBackspaceState.SetState(_inputTracker.IsKeyBackspacePressing);
        _keyDeleteState.SetState(_inputTracker.IsKeyDeletePressing);
        _keyEnterState.SetState(_inputTracker.IsKeyEnterPressing);
        _keyTabState.SetState(_inputTracker.IsKeyTabPressing);
        _keyLeftState.SetState(_inputTracker.IsKeyLeftPressing);
        _keyRightState.SetState(_inputTracker.IsKeyRightPressing);
        _keyUpState.SetState(_inputTracker.IsKeyUpPressing);
        _keyDownState.SetState(_inputTracker.IsKeyDownPressing);
        _selectAllState.SetState(_inputTracker.IsKeySelectAllPressing);
        _copyState.SetState(_inputTracker.IsKeyCopyPressing);
        _pasteState.SetState(_inputTracker.IsKeyPastePressing);

        //input box shortcuts (trigger on down edge)
        if (_keyBackspaceState.IsDown)
        {
            _textInput?.HandleKeyBackspace();
        }
        else if (_keyDeleteState.IsDown)
        {
            _textInput?.HandleKeyDelete();
        }
        else if (_keyEnterState.IsDown)
        {
            _textInput?.OnTextInput(this, "\n");
        }
        else if (_keyTabState.IsDown)
        {
            _textInput?.HandleKeyTab();
        }
        else if (_keyLeftState.IsDown)
        {
            _textInput?.HandleKeyArrowLeft();
        }
        else if (_keyRightState.IsDown)
        {
            _textInput?.HandleKeyArrowRight();
        }
        else if (_keyUpState.IsDown)
        {
            _textInput?.HandleKeyArrowUp();
        }
        else if (_keyDownState.IsDown)
        {
            _textInput?.HandleKeyArrowDown();
        }
        else if (_selectAllState.IsDown)
        {
            _textInput?.SelectAll();
        }
        else if (_copyState.IsDown)
        {
            if (_textInput != null)
            {
                _inputTracker.CopyToClipboard(_textInput.GetSelectedText());
            }
        }
        else if (_pasteState.IsDown)
        {
            ReadOnlySpan<char> text = _inputTracker.GetClipboardText();
            if (text.Length > 0)
            {
                _textInput?.OnTextInput(this, text);
            }
        }
    }

    private void UpdateNode(UINode node, float delta)
    {
        uint mask = _mask;
        MaskContext? maskContext = null;
        if (node.IsEnable)
        {
            //increase stencil value if the node is a mask
            if (node is IUIMask maskNode)
            {
                maskContext = new MaskContext
                {
                    texture = maskNode.MaskTexture,
                    matrix = maskNode.MaskTransform.Matrix,
                    uvRect = maskNode.MaskTextureUvRect
                };
                IncreaceStencil(maskNode.MaskTexture, maskNode.MaskTransform.Matrix, maskNode.MaskTextureUvRect);
            }

            try
            {
                node.TryRefreshRenderData(this, delta);
            }
            catch (Exception e)
            {
                HandleError(e, "refreshing render data", node);
            }

            try
            {

                node.Render(this, delta);
            }
            catch (Exception e)
            {
                HandleError(e, "updating", node);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                UpdateNode(node.Children[i], delta);
            }
        }



        //recover stencil buffer
        if (maskContext != null)
        {
            DecreaseMask(maskContext.Value.texture, maskContext.Value.matrix, maskContext.Value.uvRect);
        }

        //recover stencil value in CPU
        _mask = mask;
    }

    private void TickNode(UINode node, float delta)
    {
        if (node.IsEnable)
        {
            try
            {
                node.Tick(this, delta);
            }
            catch (Exception e)
            {
                HandleError(e, "ticking", node);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                TickNode(node.Children[i], delta);
            }
        }


    }

    private static bool CheckMask(UINode node, Vector2 mousePosition)
    {
        UINode? parent = node.Parent;
        while (parent != null)
        {
            if (parent is IUIMask maskNode)
            {
                Transform2D maskTransform = maskNode.MaskTransform;
                ShapeBox2D shape = new ShapeBox2D(maskTransform.Position, maskTransform.Rotation, maskTransform.Scale);
                if (!UtilsCollision2D.PointBox(mousePosition, shape))
                {
                    return false;
                }
            }
            parent = parent.Parent;
        }
        return true;
    }

    public void HandleError(Exception exception, string operation, UINode node)
    {
        if (ErrorHandler != null)
        {
            ErrorHandler(exception);
        }
        else
        {
            string nodeName = node.Name;
            Log.Error($"Error in {operation} of {nodeName}: {exception}");
        }
    }
}
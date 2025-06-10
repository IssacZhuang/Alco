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

    private struct MaskContext
    {
        public Texture2D? texture;
        public Matrix4x4 matrix;
        public Rect uvRect;
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

    private readonly Material _textMaterial;
    private readonly Material _spriteMaterial;
    private readonly Material _stencilIncreaseMaterial;
    private readonly Material _stencilDecreaseMaterial;
    private readonly uint _shaderId_texture;

    private uint _mask = 0;

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

        _collisionWorld = new CollisionWorld2D();
        _mousePointCaster = new MousePointCaster();
    }

    public void Tick(UINode root, float delta)
    {
        TickNode(root, delta);
    }

    public void Update(GPUFrameBuffer renderTarget, UINode root, float delta)
    {
        _collisionWorld.ClearAll();

        _renderContext.Begin(renderTarget);
        _mask = 0;
        UpdateNode(root, delta);
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
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            UpdateNode(node.Children[i], delta);
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
                if (ErrorHandler != null)
                {
                    ErrorHandler(e);
                }
                else
                {
                    Log.Error($"Error in ticking {node.Name}: {e}");
                }
            }
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            TickNode(node.Children[i], delta);
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
}
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>UI canvas that lays out, ticks, batches, and renders a tree of <see cref="UINode"/> objects.</summary>
public partial class Canvas : AutoDisposable, INavigationContext
{
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

    private struct PendingSvgUpload(SvgRasterizeData data, Texture target)
    {
        public SvgRasterizeData Data = data;
        public Texture Target = target;
    }

    private struct RetiredTexture(RenderContext? context, ulong frameId, Texture texture)
    {
        public RenderContext? Context = context;
        public ulong FrameId = frameId;
        public Texture Texture = texture;
    }

    private readonly RenderingSystem _renderingSystem;
    private readonly GlyphCache _glyphCache;
    private readonly bool _ownsGlyphCache;
    private readonly UIBatchRenderer _batchRenderer;
    private SvgRenderer? _svgRenderer;
    private readonly List<PendingSvgUpload> _pendingSvgUploads = new(8);
    private readonly List<RetiredTexture> _retiredTextures = new(8);
    private RenderContext? _lastUploadContext;
    private ulong _lastUploadFrameId;

    private Vector2 _size;
    private Vector2 _invSize;
    private BoundingBox2D _bound;

    private readonly CollisionWorld2D _collisionWorld;
    private readonly List<UINode> _hitNodes = new(64);
    private int _tickCounter;
    private readonly IUIInputTracker _inputTracker;
    private INavigationFocusable? _navigationFocus;
    private bool _hoverDirty;

    private UINode? _holded;
    private Vector2 _pressPosition;
    private UINode? _hovered;
    private UINode? _selected;
    private ITextInput? _textInput;
    private Vector2 _lastCursorPosition;
    private InputState _mouseLeftState;
    private InputState _confirmState;
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

    /// <summary>Gets the default font used by text nodes on this canvas.</summary>
    public Font DefaultFont { get; }

    /// <summary>Gets the SDF glyph atlas cache used by this canvas.</summary>
    public GlyphCache GlyphCache => _glyphCache;

    /// <summary>Gets the underlying graphics rendering system.</summary>
    public RenderingSystem RenderingSystem => _renderingSystem;

    /// <summary>Gets the input tracker used by this canvas.</summary>
    public IUIInputTracker InputTracker
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _inputTracker;
    }

    /// <summary>Gets the navigable control that owns directional input this frame.</summary>
    public INavigationFocusable? NavigationFocus
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _navigationFocus;
    }

    /// <summary>Gets or sets the maximum pointer travel in canvas units that still counts as a click.</summary>
    public float DragClickThreshold { get; set; } = 10.0f;

    /// <summary>Gets whether a node currently owns mouse hover or press capture.</summary>
    public bool IsCapturingMouse => _hovered != null || _holded != null;

    /// <summary>Gets whether a text input control currently owns keyboard input.</summary>
    public bool IsCapturingKeyboard => _textInput != null;

    /// <summary>When false, skips pointer/key input handling while still rendering.</summary>
    public bool IsInputEnabled { get; set; } = true;

    /// <summary>When true, disables draw-call merging so every quad draws separately for visual parity diagnostics.</summary>
    public bool SplitDraws
    {
        get => _batchRenderer.SplitDraws;
        set => _batchRenderer.SplitDraws = value;
    }

    /// <summary>Statistics recorded during the most recent <see cref="Record"/> or <see cref="Draw"/> call.</summary>
    public UIFrameStats LastFrameStats => _batchRenderer.LastStats;

    /// <summary>Gets the current pointer position in canvas coordinates.</summary>
    public Vector2 CursorPosition { get; private set; }

    /// <summary>Gets the axis-aligned canvas bounds centered at the origin.</summary>
    public BoundingBox2D Bound
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bound;
    }

    /// <summary>Gets or sets the logical canvas size in UI units (centered at the origin, Y+ up).</summary>
    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _size;
        set
        {
            if (value.X <= 0f || value.Y <= 0f || _size == value)
                return;
            _size = value;
            _invSize = Vector2.One / value;
            _bound = new BoundingBox2D(-value * 0.5f, value * 0.5f);
            Root.Size = value;
        }
    }

    /// <summary>Gets the reciprocal of <see cref="Size"/>.</summary>
    public Vector2 InvSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _invSize;
    }

    /// <summary>Currently pressed/dragged node, if any.</summary>
    public UINode? Holded
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _holded;
    }

    /// <summary>Currently hovered node, if any.</summary>
    public UINode? Hovered
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _hovered;
    }

    /// <summary>Currently selected node, if any.</summary>
    public UINode? Selected
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _selected;
    }

    /// <summary>Gets or sets the sound player used for hover and click UI feedback.</summary>
    public IUISoundPlayer? SoundPlayer { get; set; }

    /// <summary>Gets or sets the debug outline color.</summary>
    public Vector4 DebugDrawColor { get; set; }

    /// <summary>Optional callback invoked instead of logging when a node handler throws.</summary>
    public Action<Exception>? ErrorHandler;

    /// <summary>The root node of this canvas; all UI controls attach beneath it.</summary>
    public UIRoot Root { get; }

    /// <summary>Shared bindless SVG rasterizer created on demand for <see cref="UIGeometryIcon"/> nodes.</summary>
    public SvgRenderer SvgRenderer => _svgRenderer ??= new SvgRenderer(_renderingSystem);

    /// <summary>Creates a bindless batched UI canvas targeting <paramref name="system"/>.</summary>
    /// <param name="system">The rendering system owning shaders, textures, and transient buffers.</param>
    /// <param name="inputTracker">Input source tracking pointer and keyboard state.</param>
    /// <param name="defaultFont">Default font asset assigned to child text nodes without an explicit font.</param>
    /// <param name="size">Optional initial logical canvas size; defaults to 640x360.</param>
    /// <param name="glyphCache">Optional shared SDF glyph cache; when <see langword="null"/>, the canvas creates and owns a private instance.</param>
    public Canvas(
        RenderingSystem system,
        IUIInputTracker inputTracker,
        Font defaultFont,
        Vector2? size = null,
        GlyphCache? glyphCache = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(inputTracker);
        ArgumentNullException.ThrowIfNull(defaultFont);

        _renderingSystem = system;
        DefaultFont = defaultFont;
        _ownsGlyphCache = glyphCache == null;
        _glyphCache = glyphCache ?? new GlyphCache(system);
        try
        {
            _batchRenderer = new UIBatchRenderer(system, _glyphCache);
            _collisionWorld = new CollisionWorld2D();

            Vector2 initialSize = size is { X: > 0f, Y: > 0f } s ? s : new Vector2(640f, 360f);
            _size = initialSize;
            _invSize = Vector2.One / initialSize;
            _bound = new BoundingBox2D(-initialSize * 0.5f, initialSize * 0.5f);

            _inputTracker = inputTracker;
            _inputTracker.RegisterTextInput(OnTextInput);

            Root = new UIRoot(this)
            {
                Name = "Root",
                Size = initialSize,
            };
        }
        catch
        {
            if (_ownsGlyphCache)
                _glyphCache.Dispose();
            throw;
        }
    }

    /// <summary>Ticks layout, hit-test shapes, navigation focus, and node logic for the UI tree.</summary>
    /// <param name="delta">Elapsed frame time in seconds.</param>
    public void Tick(float delta)
    {
        _collisionWorld.ClearAll();
        _tickCounter = 0;
        _navigationFocus = null;
        ScanNavigationFocus(Root);
        TickNode(Root, delta);
    }

    /// <summary>Processes input and traverses the UI tree to build batched draws for the current frame.</summary>
    /// <param name="delta">Elapsed frame time in seconds.</param>
    public void PrepareRender(float delta = 0f)
    {
        HandleInput();
        _batchRenderer.Begin(_size);
        UpdateNode(Root, delta);
        _batchRenderer.EndCollection();
    }

    /// <summary>Uploads pending SVG rasterizations, dirty font pages, and batched GPU buffers before opening a render pass.</summary>
    /// <param name="context">Active recording frame context (between scopes).</param>
    public void Upload(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _lastUploadContext = context;
        _lastUploadFrameId = context.CurrentFrameId;

        PurgeRetiredTextures();

        if (_pendingSvgUploads.Count > 0)
        {
            SvgRenderer svg = SvgRenderer;
            for (int i = 0; i < _pendingSvgUploads.Count; ++i)
            {
                PendingSvgUpload item = _pendingSvgUploads[i];
                svg.RecordRasterize(context, in item.Data, item.Target);
            }
            _pendingSvgUploads.Clear();
        }

        _batchRenderer.Upload(context);
    }

    /// <summary>Records all batched UI draws into an already-open render pass.</summary>
    /// <param name="context">Active frame context owning the render pass.</param>
    /// <param name="render">Active render pass scope.</param>
    /// <param name="viewportExtent">Render target dimensions in pixels.</param>
    /// <param name="hasStencil">True when the active render pass includes a stencil attachment for rotated/textured masks.</param>
    /// <returns>Frame statistics describing emitted draws, scissor switches, masks, and font uploads.</returns>
    public UIFrameStats Record(RenderContext context, in RenderContext.RenderScope render, Extent2D viewportExtent, bool hasStencil)
    {
        return _batchRenderer.Record(context, in render, viewportExtent, hasStencil);
    }

    /// <summary>Uploads pending GPU data, opens a render pass onto <paramref name="colorView"/>, and records all batched UI draws.</summary>
    /// <param name="context">Active recording frame context (between scopes).</param>
    /// <param name="colorView">Target color render view.</param>
    /// <param name="colorFormat">Target color attachment format.</param>
    /// <param name="extent">Render area dimensions in pixels.</param>
    /// <param name="stencilView">Optional stencil attachment view for rotated or textured masks.</param>
    /// <param name="stencilFormat">Stencil format matching <paramref name="stencilView"/>, or <see cref="Format.Undefined"/>.</param>
    /// <param name="colorLoadOp">Color load op (<see cref="LoadOp.Load"/> overlays onto scene; <see cref="LoadOp.Clear"/> clears first).</param>
    /// <param name="clearColor">Clear color used when <paramref name="colorLoadOp"/> is <see cref="LoadOp.Clear"/>.</param>
    /// <returns>Frame statistics for the recorded pass.</returns>
    public UIFrameStats Draw(
        RenderContext context,
        GPURenderView colorView,
        Format colorFormat,
        Extent2D extent,
        GPURenderView? stencilView = null,
        Format stencilFormat = Format.Undefined,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(colorView);

        Upload(context);

        bool hasStencil = stencilView != null && stencilFormat != Format.Undefined;
        RenderingDesc desc = new()
        {
            Colors =
            [
                new ColorAttachment
                {
                    RenderView = colorView,
                    Load = colorLoadOp,
                    Store = StoreOp.Store,
                    Clear = clearColor,
                },
            ],
            Stencil = hasStencil
                ? new StencilAttachment { RenderView = stencilView, Load = LoadOp.Clear, Store = StoreOp.Discard, Clear = 0 }
                : default,
        };

        RenderPassSignature signature = new([colorFormat], Format.Undefined, hasStencil ? stencilFormat : Format.Undefined);
        using RenderContext.RenderScope render = context.BeginRenderPass(desc, signature);
        return Record(context, in render, extent, hasStencil);
    }

    /// <summary>Programmatically sets the hovered node (for keyboard/gamepad navigation).</summary>
    /// <param name="node">Target node to hover, or null to clear hover.</param>
    public void SetHovered(UINode? node)
    {
        if (_hovered == node)
            return;

        UINode? oldHovered = _hovered;
        _hovered = node;

        if (oldHovered != null)
            DispatchBubble(oldHovered, "OnUnhover", static (n, c, pos) => n.OnUnhover(c, pos), CursorPosition);

        if (node != null)
        {
            if ((node.SoundType & UISoundType.Hover) != 0)
                SoundPlayer?.PlayOnHoverSound();
            DispatchBubble(node, "OnHover", static (n, c, pos) => n.OnHover(c, pos), CursorPosition);
        }
    }

    /// <inheritdoc/>
    public void RecomputeHoverFromCursor()
    {
        _hoverDirty = true;
    }

    /// <summary>Allocates a target texture and schedules its compute SVG rasterization during the next <see cref="Upload"/>.</summary>
    /// <summary>Queues GPU compute rasterization for flattened SVG data executed during the next <see cref="End"/>.</summary>
    /// <param name="data">Flattened CPU SVG geometry.</param>
    /// <returns>The pre-allocated destination GPU texture ready for quad emission within the same frame.</returns>
    public Texture EnqueueSvgRasterize(in SvgRasterizeData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Texture target = SvgRenderer.AllocateTargetTexture(data.Width, data.Height);
        _pendingSvgUploads.Add(new PendingSvgUpload(data, target));
        return target;
    }

    /// <summary>Retires an ephemeral texture so it is destroyed only after its last recording frame completes on GPU.</summary>
    /// <param name="texture">Texture to retire, or null.</param>
    public void RetireTexture(Texture? texture)
    {
        if (texture == null || texture.IsDestroyed)
            return;
        _retiredTextures.Add(new RetiredTexture(_lastUploadContext, _lastUploadFrameId, texture));
    }

    /// <summary>Registers a node as a pointer hit target with the specified shape.</summary>
    /// <param name="node">Target node.</param>
    /// <param name="shape">World-space hit shape.</param>
    public void AddClickReciever(UINode node, ShapeBox2D shape)
    {
        _collisionWorld.PushCollisionTarget(node, shape);
    }

    /// <summary>Gives text-input focus to a control and updates platform IME rectangle.</summary>
    /// <param name="node">The text input node.</param>
    /// <param name="inputArea">World-space text input area.</param>
    /// <param name="cursor">Current cursor index.</param>
    public void SetTextInputArea(ITextInput node, BoundingBox2D inputArea, int cursor)
    {
        bool wasTextInput = _textInput != null;
        bool isTextInput = node != null;

        if (!wasTextInput && isTextInput)
            _inputTracker.RequestTextInput();
        else if (wasTextInput && !isTextInput)
            _inputTracker.ReleaseTextInput();

        _textInput = node;

        Vector2 position = inputArea.Center;
        Vector2 size = inputArea.Size;
        float widthNorm = size.X * _invSize.X;
        float heightNorm = size.Y * _invSize.Y;
        float x = position.X - size.X * 0.5f;
        float y = position.Y + size.Y * 0.5f;
        float xNorm = x * _invSize.X + 0.5f;
        float yNorm = 0.5f - y * _invSize.Y;
        _inputTracker?.SetTextInput(xNorm, yNorm, widthNorm, heightNorm, cursor);
    }

    /// <summary>Clears current text-input focus and releases keyboard capture.</summary>
    public void ClearTextInput()
    {
        if (_textInput != null)
        {
            _inputTracker.ReleaseTextInput();
            _textInput = null;
        }
    }

    /// <summary>Reports an exception raised while processing a node callback.</summary>
    /// <param name="exception">Thrown exception.</param>
    /// <param name="operation">Operation description.</param>
    /// <param name="node">Node involved, if available.</param>
    public void HandleError(Exception exception, string operation, UINode? node)
    {
        if (ErrorHandler != null)
        {
            ErrorHandler(exception);
        }
        else
        {
            string nodeName = node?.Name ?? "Unknown";
            Log.Error($"Error in {operation} of {nodeName}: {exception}");
        }
    }

    internal void ClearNodeReference(UINode node)
    {
        if (_hovered == node)
        {
            DispatchBubble(node, "OnUnhover", static (n, c, pos) => n.OnUnhover(c, pos), CursorPosition);
            _hovered = null;
            _hoverDirty = true;
        }
        if (_holded == node)
        {
            _holded = null;
            _hoverDirty = true;
        }
        if (_selected == node)
            _selected = null;
        if (_textInput == node)
            _textInput = null;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearTextInput();
            while (Root.Children.Count > 0)
                Root.Remove(Root.Children[^1]);

            for (int i = 0; i < _pendingSvgUploads.Count; ++i)
            {
                Texture pending = _pendingSvgUploads[i].Target;
                if (!pending.IsDestroyed)
                    pending.Destroy();
            }
            _pendingSvgUploads.Clear();

            for (int i = 0; i < _retiredTextures.Count; ++i)
            {
                RetiredTexture retired = _retiredTextures[i];
                if (retired.Context != null && !retired.Context.IsDestroyed && retired.Context.IsFramePending(retired.FrameId))
                    retired.Context.Wait();
                if (!retired.Texture.IsDestroyed)
                    retired.Texture.Destroy();
            }
            _retiredTextures.Clear();

            _inputTracker?.UnregisterTextInput(OnTextInput);
            _collisionWorld.Dispose();
            _svgRenderer?.Dispose();
            _batchRenderer.Dispose();
            if (_ownsGlyphCache)
            {
                if (_lastUploadContext != null && !_lastUploadContext.IsDestroyed && _lastUploadContext.IsFramePending(_lastUploadFrameId))
                    _lastUploadContext.Wait();
                _glyphCache.Dispose();
            }
        }

        _hovered = null;
        _holded = null;
        _selected = null;
        _textInput = null;
    }

    private void PurgeRetiredTextures()
    {
        for (int i = _retiredTextures.Count - 1; i >= 0; --i)
        {
            RetiredTexture retired = _retiredTextures[i];
            if (retired.Context != null && !retired.Context.IsDestroyed && retired.Context.IsFramePending(retired.FrameId))
                continue;

            if (!retired.Texture.IsDestroyed)
                retired.Texture.Destroy();
            _retiredTextures.RemoveAt(i);
        }
    }

    private void OnConfirmDown(UINode? node, Vector2 cursorPosition, bool checkMask)
    {
        if (node == null)
        {
            if (checkMask)
                ClearTextInput();
            return;
        }

        if (checkMask && !CheckMask(node, cursorPosition))
            return;

        _holded = node;
        _pressPosition = cursorPosition;
        DispatchBubble(node, "OnPressDown", static (n, c, pos) => n.OnPressDown(c, pos), cursorPosition);

        if (checkMask)
        {
            if (_selected != null && _selected != node)
                DispatchBubble(_selected, "OnDeselect", static (n, c, pos) => n.OnDeselect(c, pos), cursorPosition);
            _selected = node;
            if (_selected != null)
                DispatchBubble(_selected, "OnSelect", static (n, c, pos) => n.OnSelect(c, pos), cursorPosition);

            if (node is not ITextInput)
                ClearTextInput();
        }
    }

    private void OnConfirmUp(UINode? node, Vector2 cursorPosition)
    {
        if (_holded == null)
            return;

        UINode holded = _holded;
        DispatchBubble(holded, "OnPressUp", static (n, c, pos) => n.OnPressUp(c, pos), cursorPosition);

        if (holded == node && Vector2.Distance(cursorPosition, _pressPosition) < DragClickThreshold)
        {
            DispatchBubble(holded, "OnClick", static (n, c, pos) => n.OnClick(c, pos), cursorPosition);
            if ((holded.SoundType & UISoundType.Click) != 0)
                SoundPlayer?.PlayOnClickSound();
        }

        _holded = null;
    }

    private void OnTextInput(ReadOnlySpan<char> text)
    {
        if (_textInput == null)
            return;
        try
        {
            _textInput.OnTextInput(this, text);
        }
        catch (Exception e)
        {
            HandleError(e, "OnTextInput", _textInput as UINode);
        }
    }

    private Vector2 ScreenToCanvasWorld(Vector2 screenPoint, Vector2 windowSize)
    {
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
            return Vector2.Zero;
        return new Vector2(
            (screenPoint.X / windowSize.X - 0.5f) * _size.X,
            (0.5f - screenPoint.Y / windowSize.Y) * _size.Y);
    }

    private void HandleInput()
    {
        if (_inputTracker == null || !IsInputEnabled)
            return;

        Vector2 cursorPosition = _inputTracker.CursorPosition;
        bool cursorMoved = cursorPosition != _lastCursorPosition;
        _lastCursorPosition = cursorPosition;

        Vector2 mouseWorldPosition = ScreenToCanvasWorld(cursorPosition, _inputTracker.WindowSize);
        CursorPosition = mouseWorldPosition;

        UINode? selectable = null;
        UINode? oldHovered = null;
        bool shouldUpdateHover = !_inputTracker.IsGamepadInputting || cursorMoved || _hoverDirty;
        _hoverDirty = false;
        if (shouldUpdateHover)
        {
            _hitNodes.Clear();
            _collisionWorld.BuildTree();
            NodeCollector collector = new(_hitNodes);
            _collisionWorld.CastPoint(ref collector, mouseWorldPosition);

            int bestOrder = -1;
            for (int i = 0; i < _hitNodes.Count; ++i)
            {
                UINode node = _hitNodes[i];
                if (!CheckMask(node, mouseWorldPosition))
                    continue;
                if (node.TickOrder > bestOrder)
                {
                    bestOrder = node.TickOrder;
                    selectable = node;
                }
            }

            if (selectable != _hovered && selectable != null && (selectable.SoundType & UISoundType.Hover) != 0)
                SoundPlayer?.PlayOnHoverSound();

            oldHovered = _hovered;
            _hovered = selectable;
        }
        else
        {
            selectable = _hovered;
            oldHovered = _hovered;
            if (selectable != null && !IsEnabledInHierarchy(selectable))
            {
                DispatchBubble(selectable, "OnUnhover", static (n, c, pos) => n.OnUnhover(c, pos), mouseWorldPosition);
                _hovered = null;
                selectable = null;
            }
        }

        _mouseLeftState.SetState(_inputTracker.IsMouseLeftPressing);
        _confirmState.SetState(_inputTracker.IsConfirmPressing);

        if (_mouseLeftState.IsDown)
        {
            OnConfirmDown(selectable, mouseWorldPosition, checkMask: true);
        }
        else if (_mouseLeftState.IsUp)
        {
            OnConfirmUp(selectable, mouseWorldPosition);
        }
        else if (_mouseLeftState.IsPressing)
        {
            if (selectable != null)
                DispatchBubble(selectable, "OnPressing", static (n, c, pos) => n.OnPressing(c, pos), mouseWorldPosition);
        }
        else if (shouldUpdateHover)
        {
            if (oldHovered != null && oldHovered != selectable)
                DispatchBubble(oldHovered, "OnUnhover", static (n, c, pos) => n.OnUnhover(c, pos), mouseWorldPosition);
            if (selectable != null)
                DispatchBubble(selectable, "OnHover", static (n, c, pos) => n.OnHover(c, pos), mouseWorldPosition);
        }

        if (_confirmState.IsDown)
            OnConfirmDown(selectable, mouseWorldPosition, checkMask: false);
        else if (_confirmState.IsUp)
            OnConfirmUp(selectable, mouseWorldPosition);
        else if (_confirmState.IsPressing && selectable != null)
            DispatchBubble(selectable, "OnPressing", static (n, c, pos) => n.OnPressing(c, pos), mouseWorldPosition);

        if (_holded != null)
            DispatchBubble(_holded, "OnDrag", static (n, c, pos) => n.OnDrag(c, pos), mouseWorldPosition);

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

        if (_textInput != null)
            DispatchTextShortcuts();

        if (_pasteState.IsDown)
        {
            ReadOnlySpan<char> text = _inputTracker.GetClipboardText();
            if (text.Length > 0 && _textInput != null)
            {
                try
                {
                    _textInput.OnTextInput(this, text);
                }
                catch (Exception e)
                {
                    HandleError(e, "OnTextInput", _textInput as UINode);
                }
            }
        }

        if (_inputTracker.IsScrolling(out Vector2 scrollDelta) && _hovered != null)
            DispatchBubble(_hovered, "OnScroll", static (n, c, delta) => n.OnScroll(c, delta), scrollDelta);
    }

    private void DispatchTextShortcuts()
    {
        if (_textInput == null)
            return;

        UINode? textNode = _textInput as UINode;
        try
        {
            if (_keyBackspaceState.IsDown) _textInput.HandleKeyBackspace();
            else if (_keyDeleteState.IsDown) _textInput.HandleKeyDelete();
            else if (_keyEnterState.IsDown) _textInput.OnTextInput(this, "\n");
            else if (_keyTabState.IsDown) _textInput.HandleKeyTab();
            else if (_keyLeftState.IsDown) _textInput.HandleKeyArrowLeft();
            else if (_keyRightState.IsDown) _textInput.HandleKeyArrowRight();
            else if (_keyUpState.IsDown) _textInput.HandleKeyArrowUp();
            else if (_keyDownState.IsDown) _textInput.HandleKeyArrowDown();
            else if (_selectAllState.IsDown) _textInput.SelectAll();
            else if (_copyState.IsDown) _inputTracker.CopyToClipboard(_textInput.GetSelectedText());
        }
        catch (Exception e)
        {
            HandleError(e, "TextInputShortcut", textNode);
        }
    }

    private void UpdateNode(UINode node, float delta)
    {
        if (!node.IsEnable)
            return;

        IUIMask? maskNode = node as IUIMask;
        if (maskNode != null)
        {
            _batchRenderer.PushMask(
                maskNode.MaskTransform.Matrix,
                maskNode.MaskTexture,
                maskNode.MaskTextureUvRect);
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

        for (int i = 0; i < node.Children.Count; ++i)
            UpdateNode(node.Children[i], delta);

        if (maskNode != null)
            _batchRenderer.PopMask();
    }

    private void ScanNavigationFocus(UINode node)
    {
        if (!node.IsEnable)
            return;
        if (node is INavigationFocusable focusable && focusable.CanNavigate)
            _navigationFocus = focusable;
        for (int i = 0; i < node.Children.Count; ++i)
            ScanNavigationFocus(node.Children[i]);
    }

    private void TickNode(UINode node, float delta)
    {
        if (!node.IsEnable)
            return;

        node.TickOrder = _tickCounter++;
        try
        {
            node.Tick(this, delta);
        }
        catch (Exception e)
        {
            HandleError(e, "ticking", node);
        }

        for (int i = 0; i < node.Children.Count; ++i)
            TickNode(node.Children[i], delta);
    }

    private static bool IsEnabledInHierarchy(UINode node)
    {
        UINode? current = node;
        while (current != null)
        {
            if (!current.IsEnable)
                return false;
            current = current.Parent;
        }
        return true;
    }

    private struct NodeCollector(List<UINode> hitNodes) : ICollisionCastCollector
    {
        public bool OnHit(object target)
        {
            if (target is UINode node)
                hitNodes.Add(node);
            return true;
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
                ShapeBox2D shape = new(maskTransform.Position, maskTransform.Rotation, maskTransform.Scale);
                if (!CollisionUtility2D.PointBox(mousePosition, shape))
                    return false;
            }
            parent = parent.Parent;
        }
        return true;
    }

    private void DispatchBubble(UINode? target, string operation, Action<UINode, Canvas, Vector2> action, Vector2 position)
    {
        UINode? current = target;
        while (current != null)
        {
            try
            {
                action(current, this, position);
            }
            catch (Exception e)
            {
                HandleError(e, operation, current);
            }

            if (!current.BubbleEvent)
                break;
            current = current.Parent;
        }
    }
}

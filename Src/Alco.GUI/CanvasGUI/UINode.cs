using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.GUI;

/// <summary>
/// The base class for all UI nodes.
/// </summary>
public class UINode : IEnumerable<UINode>
{
    private readonly List<UINode> _children = new();
    private Vector2 _sizeDelta = Vector2.Zero;
    private Anchor _anchor = Anchor.Center;
    private Pivot _pivot = Pivot.Center;

    private Transform2D _transform = Transform2D.Identity;
    private Transform2D _worldTransform = Transform2D.Identity;
    private bool _isTransformDirty = true;

    private bool _isRenderDataDirty = true;

    private MaskState _maskState = MaskState.None;

    public virtual bool BubbleEvent { get; set; } = true;

    public event Action<Canvas, Vector2>? EventOnClick;
    public event Action<Canvas, Vector2>? EventOnHover;
    public event Action<Canvas, Vector2>? EventOnPressDown;
    public event Action<Canvas, Vector2>? EventOnPressUp;
    public event Action<Canvas, Vector2>? EventOnPressing;
    public event Action<Canvas, Vector2>? EventOnDrag;
    public event Action<Canvas, Vector2>? EventOnSelect;
    public event Action<Canvas, Vector2>? EventOnDeselect;

    public UINode()
    {
        Children = new UINodeChildCollection(this);
        Name = GetType().Name;
    }

    /// <summary>
    /// The parent of the node. Must be null if the node is a root node.
    /// </summary>
    /// <value></value>
    public UINode? Parent { get; private set; } = null;


    /// <summary>
    /// The name of the node.
    /// </summary>
    /// <value></value>
    public string Name { get; set; }

    /// <summary>
    /// Whether the node is enabled.
    /// </summary>
    /// <value></value>
    public bool IsEnable { get; set; } = true;

    /// <summary>
    /// Whether the node is affected by the layout.
    /// </summary>
    public bool IsLayoutAffected { get; set; } = true;

    #region  Transform Properties

    /// <summary>
    /// The position of the node in the local space.
    /// </summary>
    /// <value></value>
    public Vector2 Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _transform.Position;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _transform.Position = value;
            SetTransformDirty();
        }
    }

    /// <summary>
    /// The rotation of the node in the local space.
    /// </summary>
    /// <value></value>
    public Rotation2D Rotation
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _transform.Rotation;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _transform.Rotation = value;
            SetTransformDirty();
        }
    }

    /// <summary>
    /// The scale of the node in the local space.
    /// </summary>
    /// <value></value>
    public Vector2 Scale
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _transform.Scale;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _transform.Scale = value;
            SetTransformDirty();
        }
    }



    /// <summary>
    /// The size of the node in the local space.
    /// </summary>
    /// <value></value>
    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetParentSize() * GetAnchorSizeMultiplier() + _sizeDelta;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _sizeDelta = value - GetParentSize() * GetAnchorSizeMultiplier();
            SetTransformDirty();
            SetRenderDataDirty();
        }
    }

    public Vector2 SizeDelta
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _sizeDelta;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _sizeDelta = value;
            SetTransformDirty();
            SetRenderDataDirty();
        }
    }

    /// <summary>
    /// The pivot point of the node. Only affect the content related to self
    /// </summary>
    public Pivot Pivot
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pivot;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pivot = value;
            SetTransformDirty();
        }
    }

    /// <summary>
    /// The anchors related to the parent
    /// </summary>
    public Anchor Anchor
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _anchor;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _anchor = value;
            SetTransformDirty();
        }
    }

    public Transform2D LocalTransform
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _transform;
        set
        {
            _transform = value;
            SetTransformDirty();
        }
    }

    /// <summary>
    /// The transform of the node in the world space.
    /// </summary>
    /// <value></value>
    public Transform2D WorldTransform
    {
        get
        {
            TryRefreshTransform();
            return _worldTransform;
        }
        set
        {
            if (Parent != null)
            {
                _transform = math.tolocal(Parent.WorldTransform, value);
                Vector2 realSize = Size * _transform.Scale;
                _transform.Position -= math.rotate(realSize * _pivot.value, _transform.Rotation);
                Vector2 parentRealSize = Parent.Size * Parent.WorldTransform.Scale;
                _transform.Position -= parentRealSize * _anchor.CenterPoint;
            }
            else
            {
                _transform = value;
            }
            SetTransformDirty();
        }
    }

    /// <summary>
    /// The world transform that been transformed by size of node.
    /// </summary>
    /// <value></value>
    public Transform2D RenderTransform
    {
        get
        {
            Transform2D transform = WorldTransform;
            transform.Scale *= Size;
            return transform;
        }
    }

    /// <summary>
    /// The actual render size of the node in world space, including parent scale transformations.
    /// This is the final size after all transformations are applied.
    /// </summary>
    public Vector2 RenderSize
    {
        get
        {
            return RenderTransform.Scale;
        }
    }

    /// <summary>
    /// The bounding box of the node in the world space.
    /// </summary>
    /// <value></value>
    public BoundingBox2D Bound
    {
        get
        {
            Transform2D transform = RenderTransform;
            Vector2 halfSize = transform.Scale * 0.5f;
            return new BoundingBox2D(transform.Position - halfSize, transform.Position + halfSize);
        }
    }

    #endregion

    /// <summary>
    /// Stretches this node horizontally within its parent using the specified padding.
    /// Updates <see cref="SizeDelta"/>.X to subtract left and right padding from the width,
    /// and adjusts <see cref="Position"/>.X to keep the element centered according to asymmetric padding.
    /// </summary>
    /// <param name="padding">X: left padding, Y: right padding.</param>
    public void SetHorizontalStretch(float leftPadding, float rightPadding)
    {
        // Ensure horizontal stretch while preserving vertical anchor
        Anchor = new Anchor(new Vector2(-0.5f, _anchor.min.Y), new Vector2(0.5f, _anchor.max.Y));

        // Neutralize horizontal pivot so padding aligns to parent edges
        Pivot = new Pivot(0f, _pivot.Y);

        // Width = ParentWidth - (left + right) => SizeDelta.X = - (left + right)
        Vector2 sizeDelta = SizeDelta;
        sizeDelta.X = -(leftPadding + rightPadding);
        SizeDelta = sizeDelta;

        // Shift center when padding is asymmetric: +right shrinks right side, +left shrinks left side
        // Center offset = (left - right) * 0.5
        Vector2 pos = Position;
        pos.X = (leftPadding - rightPadding) * 0.5f;
        Position = pos;
    }

    /// <summary>
    /// Stretches this node vertically within its parent using the specified padding.
    /// Updates <see cref="SizeDelta"/>.Y to subtract top and bottom padding from the height,
    /// and adjusts <see cref="Position"/>.Y to keep the element centered according to asymmetric padding.
    /// </summary>
    /// <param name="padding">X: top padding, Y: bottom padding.</param>
    public void SetVerticalStretch(float topPadding, float bottomPadding)
    {
        // Ensure vertical stretch while preserving horizontal anchor
        Anchor = new Anchor(new Vector2(_anchor.min.X, -0.5f), new Vector2(_anchor.max.X, 0.5f));

        // Neutralize vertical pivot so padding aligns to parent edges
        Pivot = new Pivot(_pivot.X, 0f);

        // Height = ParentHeight - (top + bottom) => SizeDelta.Y = - (top + bottom)
        Vector2 sizeDelta = SizeDelta;
        sizeDelta.Y = -(topPadding + bottomPadding);
        SizeDelta = sizeDelta;

        // In this coordinate system, +Y is up. More top padding shifts the rect downward.
        // Center offset = (bottom - top) * 0.5
        Vector2 pos = Position;
        pos.Y = (bottomPadding - topPadding) * 0.5f;
        Position = pos;
    }

    #region Mask Properties


    /// <summary>
    /// Is the node has mask.
    /// </summary>
    /// <value></value>
    public bool HasMask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _maskState != MaskState.None;
    }

    #endregion

    /// <summary>
    /// The children of the node. Supports both read access and collection initializer syntax.
    /// Use like: new UINode { Name = "Root", Children = { child1, child2 } }
    /// </summary>
    /// <value></value>
    public UINodeChildCollection Children { get; }

    public bool HasRoot([NotNullWhen(true)] out UIRoot? root)
    {
        UINode? node = this;
        while (node != null)
        {
            if (node is UIRoot r)
            {
                root = r;
                return true;
            }
            node = node.Parent;
        }
        root = null;
        return false;
    }

    /// <summary>
    /// Collection wrapper that supports both read access (IReadOnlyList) and 
    /// collection initializer syntax while ensuring proper Add logic.
    /// </summary>
    public class UINodeChildCollection : IReadOnlyList<UINode>
    {
        private readonly UINode Parent;

        public UINodeChildCollection(UINode parent)
        {
            Parent = parent;
        }

        // Collection initializer support
        public void Add(UINode node)
        {
            Parent.Add(node);
        }

        // IReadOnlyList implementation
        public UINode this[int index] => Parent._children[index];
        public int Count => Parent._children.Count;

        public IEnumerator<UINode> GetEnumerator()
        {
            return Parent._children.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Parent._children.GetEnumerator();
        }
    }

    /// <summary>
    /// Access the child node by it name.
    /// </summary>
    /// <value></value>
    public UINode this[string name]
    {
        get
        {
            UINode? node = _children.FirstOrDefault(x => x.Name == name) ?? throw new KeyNotFoundException($"The name {name} does not exist in the list.");
            return node;
        }
    }

    #region Add/Remove

    /// <summary>
    /// Add a child node to the node.
    /// </summary>
    /// <param name="node">The child node to add.</param>
    /// <param name="keepWorldTransform">Whether to keep the world transform of the child node.</param>
    /// <exception cref="ArgumentException">The node already has a parent or is already a child of this node.</exception>
    /// <summary>
    public virtual void Add(UINode node, bool keepWorldTransform = true)
    {
        if (node.Parent != null)
        {
            throw new ArgumentException($"The node {node.Name} already has a parent.");
        }

        if (node.Parent == this)
        {
            throw new ArgumentException($"The node {node.Name} is already a child of this node.");
        }

        AddCore(node, keepWorldTransform);
        if (HasRoot(out UIRoot? root))
        {
            AttachToTreeCore(root.Canvas, node);
        }
    }

    /// <summary>
    /// Try to add a child node to the node.
    /// </summary>
    /// <param name="node">The child node to add.</param>
    /// <param name="keepWorldTransform">Whether to keep the world transform of the child node.</param>
    /// <returns><c>True</c> if the node is added successfully; otherwise, <c>false</c>.</returns>
    public bool TryAdd(UINode node, bool keepWorldTransform = true)
    {
        if (node.Parent != null)
        {
            return false;
        }

        if (node.Parent == this)
        {
            return false;
        }

        AddCore(node, keepWorldTransform);
        if (HasRoot(out UIRoot? root))
        {
            AttachToTreeCore(root.Canvas, node);
        }
        return true;
    }

    /// <summary>
    /// Remove a child node from the node.
    /// </summary>
    /// <param name="node">The child node to remove.</param>
    /// <exception cref="ArgumentException">The node is not a child of this node.</exception>
    /// <summary>
    public void Remove(UINode node)
    {
        if (node.Parent != this)
        {
            throw new ArgumentException($"The node {node.Name} is not a child of this node.");
        }

        RemoveCore(node);
        if (HasRoot(out UIRoot? root))
        {
            DetachFromTreeCore(root.Canvas, node);
        }
    }

    /// <summary>
    /// Try to remove a child node from the node.
    /// </summary>
    /// <param name="node">The child node to remove.</param>
    /// <returns><c>True</c> if the node is removed successfully; otherwise, <c>false</c>.</returns>
    public bool TryRemove(UINode node)
    {
        if (node.Parent != this)
        {
            return false;
        }

        RemoveCore(node);
        if (HasRoot(out UIRoot? root))
        {
            DetachFromTreeCore(root.Canvas, node);
        }
        return true;
    }

    /// <summary>
    /// Change the parent of the node. It will do nothing if the new parent is the same as the current parent.
    /// </summary>
    /// <param name="newParent">The new parent node.</param>
    /// <param name="keepWorldTransform">Whether to keep the world transform of the node.</param>
    public void SetParent(UINode newParent, bool keepWorldTransform = true)
    {
        if (Parent == newParent)
        {
            return;
        }

        bool hasRootBefore = HasRoot(out UIRoot? rootBefore);
        Canvas? canvasBefore = hasRootBefore ? rootBefore!.Canvas : null;

        Parent?.RemoveCore(this);
        newParent.AddCore(this, keepWorldTransform);

        bool hasRootAfter = HasRoot(out UIRoot? rootAfter);
        Canvas? canvasAfter = hasRootAfter ? rootAfter!.Canvas : null;

        if (hasRootBefore && !hasRootAfter)
        {
            DetachFromTreeCore(canvasBefore!, this);
        }
        else if (!hasRootBefore && hasRootAfter)
        {
            AttachToTreeCore(canvasAfter!, this);
        }
        else if (hasRootBefore && hasRootAfter && canvasBefore != canvasAfter)
        {
            // Moved across different roots: detach from old, then attach to new
            DetachFromTreeCore(canvasBefore!, this);
            AttachToTreeCore(canvasAfter!, this);
        }

    }

    private void RemoveCore(UINode node)
    {
        node.Parent = null;
        _children.Remove(node);
    }

    private void AddCore(UINode child, bool keepWorldTransform = true)
    {
        if (keepWorldTransform)
        {
            Transform2D worldTransform = child.WorldTransform;
            Vector2 size = child.Size;
            child.Parent = this;
            _children.Add(child);
            child.WorldTransform = worldTransform;
            child.Size = size;
        }
        else
        {
            child.Parent = this;
            _children.Add(child);
        }

        child.SetTransformDirty();
    }

    private static void AttachToTreeCore(Canvas canvas, UINode node)
    {
        try
        {
            node.OnAttachToTree(canvas);
        }
        catch (Exception e)
        {
            canvas.HandleError(e, "attaching to tree", node);
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            AttachToTreeCore(canvas, node.Children[i]);
        }
    }


    private static void DetachFromTreeCore(Canvas canvas, UINode node)
    {
        try
        {
            node.OnDetachFromTree(canvas);
        }
        catch (Exception e)
        {
            canvas.HandleError(e, "detaching from tree", node);
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            DetachFromTreeCore(canvas, node.Children[i]);
        }
    }

    #endregion

    #region Get

    /// <summary>
    /// Get the child node by name.
    /// </summary>
    /// <param name="name">The name of the child node.</param>
    /// <returns>The child node.</returns>
    public UINode Get(string name)
    {
        UINode? node = _children.FirstOrDefault(x => x.Name == name) ?? throw new KeyNotFoundException($"The name {name} does not exist in the list.");
        return node;
    }

    /// <summary>
    /// Get the child node by index.
    /// </summary>
    /// <param name="index">The index of the child node.</param>
    /// <returns>The child node.</returns>
    public UINode Get(int index)
    {
        return _children[index];//throw inside the list
    }

    /// <summary>
    /// Try to get the child node by name.
    /// </summary>
    /// <param name="name">The name of the child node.</param>
    /// <param name="node">The child node.</param>
    /// <returns><c>True</c> if the node is found; otherwise, <c>false</c>.</returns>
    public bool TryGet(string name, [MaybeNullWhen(false)] out UINode? node)
    {
        node = _children.FirstOrDefault(x => x.Name == name);
        return node != null;
    }

    /// <summary>
    /// Try to get the child node by index.
    /// </summary>
    /// <param name="index">The index of the child node.</param>
    /// <param name="node">The child node.</param>
    /// <returns><c>True</c> if the node is found; otherwise, <c>false</c>.</returns>
    public bool TryGet(int index, [MaybeNullWhen(false)] out UINode? node)
    {
        if (index < 0 || index >= _children.Count)
        {
            node = null;
            return false;
        }
        node = _children[index];
        return true;
    }

    public void RemoveAllChildren()
    {
        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].Parent = null;
        }
        _children.Clear();
    }

    //generic

    /// <summary>
    /// Get the child node by name and cast it to the specified type.
    /// </summary>
    /// <param name="name">The name of the child node.</param>
    /// <typeparam name="T">The type of the child node.</typeparam>
    /// <returns>The child node.</returns>
    public T Get<T>(string name) where T : UINode
    {
        UINode node = Get(name);
        if (node is T t)
        {
            return t;
        }
        throw new InvalidCastException($"The node {name} is not of type {typeof(T).Name}.");
    }

    /// <summary>
    /// Get the child node by index and cast it to the specified type.
    /// </summary>
    /// <param name="index">The index of the child node.</param>
    /// <typeparam name="T">The type of the child node.</typeparam>
    /// <returns>The child node.</returns>
    public T Get<T>(int index) where T : UINode
    {
        UINode node = Get(index);
        if (node is T t)
        {
            return t;
        }
        throw new InvalidCastException($"The node at index {index} is not of type {typeof(T).Name}.");
    }

    /// <summary>
    /// Try to get the child node by name and cast it to the specified type.
    /// </summary>
    /// <param name="name">The name of the child node.</param>
    /// <param name="node">The child node.</param>
    /// <typeparam name="T">The type of the child node.</typeparam>
    /// <returns><c>True</c> if the node is found; otherwise, <c>false</c>.</returns>
    public bool TryGet<T>(string name, [MaybeNullWhen(false)] out T? node) where T : UINode
    {
        if (TryGet(name, out UINode? n) && n is T t)
        {
            node = t;
            return true;
        }
        node = null;
        return false;
    }

    /// <summary>
    /// Try to get the child node by index and cast it to the specified type.
    /// </summary>
    /// <param name="index">The index of the child node.</param>
    /// <param name="node">The child node.</param>
    /// <typeparam name="T">The type of the child node.</typeparam>
    /// <returns><c>True</c> if the node is found; otherwise, <c>false</c>.</returns>
    public bool TryGet<T>(int index, [MaybeNullWhen(false)] out T? node) where T : UINode
    {
        if (TryGet(index, out UINode? n) && n is T t)
        {
            node = t;
            return true;
        }
        node = null;
        return false;
    }

    #endregion

    protected virtual void OnTick(Canvas canvas, float delta)
    {

    }
    protected virtual void OnUpdate(Canvas canvas, float delta)
    {

    }

    protected virtual void OnUpdateRenderData(Canvas canvas, float delta)
    {

    }

    protected virtual void OnAttachToTree(Canvas canvas)
    {

    }

    protected virtual void OnDetachFromTree(Canvas canvas)
    {

    }

    protected virtual void OnTransformRefreshed()
    {

    }

    public bool TryRefreshRenderData(Canvas canvas, float delta)
    {
        if (!_isRenderDataDirty)
        {
            return false;
        }
        OnUpdateRenderData(canvas, delta);
        _isRenderDataDirty = false;
        return true;
    }


    protected bool TryRefreshTransform()
    {
        if (!_isTransformDirty)
        {
            return false;
        }
        ForceRefreshTransform();

        return true;
    }

    protected void ForceRefreshTransform()
    {
        _worldTransform = _transform;
        Vector2 realSize = Size * _transform.Scale;
        _worldTransform.Position -= math.rotate(realSize * _pivot.value, _transform.Rotation);
        if (Parent != null)
        {
            Vector2 parentRealSize = Parent.Size * Parent.WorldTransform.Scale;
            _worldTransform.Position += parentRealSize * _anchor.CenterPoint;
            _worldTransform = math.transform(Parent.WorldTransform, _worldTransform);
        }

        SpreadTransformDirty();
        _isTransformDirty = false;

        try
        {
            OnTransformRefreshed();
        }
        catch (Exception e)
        {
            Log.Error($"Error on refreshing transform of {Name}", e);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void SetRenderDataDirty()
    {
        _isRenderDataDirty = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void SetTransformDirty()
    {
        _isTransformDirty = true;
    }

    protected void SpreadTransformDirty()
    {

        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].SetTransformDirty();
        }

    }


    private Vector2 GetParentSize()
    {
        return Parent?.Size ?? Vector2.Zero;
    }

    private Vector2 GetAnchorSizeMultiplier()
    {
        return _anchor.max - _anchor.min;
    }

    internal void Tick(Canvas canvas, float delta)
    {
        OnTick(canvas, delta);
    }


    internal void Render(Canvas canvas, float delta)
    {
        if (!IsEnable)
        {
            return;
        }

        OnUpdate(canvas, delta);
        TryRefreshTransform();
    }

    #region Event

    public virtual void OnClick(Canvas canvas, Vector2 mousePosition)
    {
        EventOnClick?.Invoke(canvas, mousePosition);
        if (BubbleEvent && Parent != null)
        {
            Parent.OnClick(canvas, mousePosition);
        }
    }

    public virtual void OnHover(Canvas canvas, Vector2 mousePosition)
    {
        EventOnHover?.Invoke(canvas, mousePosition);
        if (BubbleEvent && Parent != null)
        {
            Parent.OnHover(canvas, mousePosition);
        }
    }

    public virtual void OnPressing(Canvas canvas, Vector2 mousePosition)
    {
        EventOnPressing?.Invoke(canvas, mousePosition);
        if (BubbleEvent && Parent != null)
        {
            Parent.OnPressing(canvas, mousePosition);
        }
    }

    public virtual void OnPressDown(Canvas canvas, Vector2 mousePosition)
    {
        EventOnPressDown?.Invoke(canvas, mousePosition);
        if (BubbleEvent && Parent != null)
        {
            Parent.OnPressDown(canvas, mousePosition);
        }
    }

    public virtual void OnPressUp(Canvas canvas, Vector2 mousePosition)
    {
        EventOnPressUp?.Invoke(canvas, mousePosition);
        if (BubbleEvent && Parent != null)
        {
            Parent.OnPressUp(canvas, mousePosition);
        }
    }

    public virtual void OnDrag(Canvas canvas, Vector2 mousePosition)
    {
        EventOnDrag?.Invoke(canvas, mousePosition);
        if (BubbleEvent && Parent != null)
        {
            Parent.OnDrag(canvas, mousePosition);
        }
    }

    public virtual void OnSelect(Canvas canvas, Vector2 mousePosition)
    {
        EventOnSelect?.Invoke(canvas, mousePosition);
        if (BubbleEvent && Parent != null)
        {
            Parent.OnSelect(canvas, mousePosition);
        }
    }

    public virtual void OnDeselect(Canvas canvas, Vector2 mousePosition)
    {
        EventOnDeselect?.Invoke(canvas, mousePosition);
        if (BubbleEvent && Parent != null)
        {
            Parent.OnDeselect(canvas, mousePosition);
        }
    }

    #endregion

    #region IEnumerable Implementation

    /// <summary>
    /// Returns an enumerator that iterates through the children.
    /// </summary>
    /// <returns>An enumerator for the children collection.</returns>
    public IEnumerator<UINode> GetEnumerator()
    {
        return _children.GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the children.
    /// </summary>
    /// <returns>An enumerator for the children collection.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return _children.GetEnumerator();
    }

    #endregion
}
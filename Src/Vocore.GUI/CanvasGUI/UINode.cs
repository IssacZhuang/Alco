using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.GUI;

/// <summary>
/// The base class for all UI nodes.
/// </summary>
public abstract class UINode
{
    private readonly List<UINode> _children = new();
    private Vector2 _sizeDelta = Vector2.Zero;
    private Anchor _anchor = Anchor.Center;
    private Pivot _pivot = Pivot.Center;
    private Transform2D _transform = Transform2D.Identity;
    private Transform2D _worldTransform = Transform2D.Identity;
    private bool _isDirty = true;

    /// <summary>
    /// The parent of the node. Must be null if the node is a root node.
    /// </summary>
    /// <value></value>
    public UINode? Parent { get; set; } = null;

    /// <summary>
    /// The name of the node.
    /// </summary>
    /// <value></value>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether to render the node.
    /// </summary>
    /// <value></value>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// The position of the node in the local space.
    /// </summary>
    /// <value></value>
    public Vector2 Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _transform.position;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _transform.position = value;
            SetDirty();
        }
    }

    /// <summary>
    /// The rotation of the node in the local space.
    /// </summary>
    /// <value></value>
    public Rotation2D Rotation
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _transform.rotation;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _transform.rotation = value;
            SetDirty();
        }
    }

    /// <summary>
    /// The scale of the node in the local space.
    /// </summary>
    /// <value></value>
    public Vector2 Scale
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _transform.scale;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _transform.scale = value;
            SetDirty();
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
        set
        {
            _sizeDelta = value - GetParentSize() * GetAnchorSizeMultiplier();
            SetDirty();
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
            SetDirty();
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
            SetDirty();
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
                _transform.position -= Size * _pivot.value;
                _transform.position -= Parent.Size * _anchor.CenterPoint;
            }
            else
            {
                _transform = value;
            }
            SetDirty();
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
            transform.scale *= Size;
            return transform;
        }
    }

    public IReadOnlyList<UINode> Children
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _children;
    }

    public UINode this[string name]
    {
        get
        {
            UINode? node = _children.FirstOrDefault(x => x.Name == name) ?? throw new KeyNotFoundException($"The name {name} does not exist in the list.");
            return node;
        }
    }

    #region Add/Remove

    public void Add(UINode node, bool keepWorldTransform = true)
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
    }

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
        return true;
    }

    public void Remove(UINode node)
    {
        if (node.Parent != this)
        {
            throw new ArgumentException($"The node {node.Name} is not a child of this node.");
        }

        node.Parent = null;
        _children.Remove(node);
    }

    public bool TryRemove(UINode node)
    {
        if (node.Parent != this)
        {
            return false;
        }

        node.Parent = null;
        _children.Remove(node);
        return true;
    }

    public void SetParent(UINode newParent, bool keepWorldTransform = true)
    {
        if (Parent == newParent)
        {
            return;
        }

        Parent?.Remove(this);

        newParent.Add(this, keepWorldTransform);
    }

    private void AddCore(UINode child, bool keepWorldTransform = true)
    {
        if (keepWorldTransform)
        {
            Transform2D worldTransform = child.WorldTransform;
            child.Parent = this;
            _children.Add(child);
            child.WorldTransform = worldTransform;
        }
        else
        {
            child.Parent = this;
            _children.Add(child);
        }

        child.SetDirty();
    }

    #endregion

    #region Get

    public UINode Get(string name)
    {
        UINode? node = _children.FirstOrDefault(x => x.Name == name) ?? throw new KeyNotFoundException($"The name {name} does not exist in the list.");
        return node;
    }

    public UINode Get(int index)
    {
        return _children[index];//throw inside the list
    }

    public bool TryGet(string name, [MaybeNullWhen(false)] out UINode? node)
    {
        node = _children.FirstOrDefault(x => x.Name == name);
        return node != null;
    }

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

    //generic

    public T Get<T>(string name) where T : UINode
    {
        UINode node = Get(name);
        if (node is T t)
        {
            return t;
        }
        throw new InvalidCastException($"The node {name} is not of type {typeof(T).Name}.");
    }

    public T Get<T>(int index) where T : UINode
    {
        UINode node = Get(index);
        if (node is T t)
        {
            return t;
        }
        throw new InvalidCastException($"The node at index {index} is not of type {typeof(T).Name}.");
    }

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

    protected abstract void OnTick(float delta);
    protected abstract void OnUpdate(float delta);

    public bool TryRefreshTransform()
    {
        if (!_isDirty)
        {
            return false;
        }
        ForceRefreshTransform();
        _isDirty = false;
        return true;
    }

    public void ForceRefreshTransform()
    {
        _worldTransform = _transform;
        _worldTransform.position += Size * _pivot.value;
        if (Parent != null)
        {
            _worldTransform.position += Parent.Size * _anchor.CenterPoint;
            _worldTransform = math.transform(Parent.WorldTransform, _worldTransform);
        }

        SpreadDirty();
    }

    public void SetDirty()
    {
        _isDirty = true;
    }

    private void SpreadDirty()
    {
        if (_isDirty)
        {
            for (int i = 0; i < _children.Count; i++)
            {
                _children[i].SetDirty();
            }
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

    public void Tick(float delta)
    {
        OnTick(delta);
        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].Tick(delta);
        }
    }


    public void Update(float delta)
    {
        if (!IsVisible)
        {
            return;
        }
        
        OnUpdate(delta);

        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].Update(delta);
        }
    }

}
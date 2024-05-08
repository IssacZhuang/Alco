using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.GUI;

public abstract class UINode
{
    private readonly List<UINode> _children = new();
    private Vector2 _size = Vector2.One;
    public Transform2D transform = Transform2D.Identity;
    public Pivot pivot = Pivot.Center;
    public Anchor anchor = Anchor.Center;

    public UINode? Parent { get; set; } = null;
    public string Name { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;

    public Matrix4x4 TransformMatrix
    {
        get
        {
            Matrix4x4 matrix = transform.Matrix;
            if (Parent != null)
            {
                matrix *= Parent.TransformMatrix;
            }
            return matrix;
        }
    }

    public Matrix4x4 SizeMatrix
    {
        get
        {
            return math.matrix4scale(_size);
        }
    }

    public IReadOnlyList<UINode> Children
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _children;
    }

    public Vector2 Size
    {
        get => _size;
        set
        {
            _size = value;
        }
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

    public void Add(UINode node)
    {
        if (node.Parent != null)
        {
            throw new ArgumentException($"The node {node.Name} already has a parent.");
        }

        if (node.Parent == this)
        {
            throw new ArgumentException($"The node {node.Name} is already a child of this node.");
        }

        node.Parent = this;
        _children.Add(node);
    }

    public bool TryAdd(UINode node)
    {
        if (node.Parent != null)
        {
            return false;
        }

        if (node.Parent == this)
        {
            return false;
        }

        node.Parent = this;
        _children.Add(node);
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

    public abstract void OnTick(float delta);
    public abstract void OnUpdate(float delta);

    internal void InternalTick(float delta)
    {
        OnTick(delta);
        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].InternalTick(delta);
        }
    }

    

    internal void InternalUpdate(float delta)
    {
        if (!IsVisible)
        {
            return;
        }
        
        OnUpdate(delta);

        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].InternalUpdate(delta);
        }
    }

    internal void InternalResize(Vector2 size)
    {
        
        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].InternalResize(size);
        }
    }
}
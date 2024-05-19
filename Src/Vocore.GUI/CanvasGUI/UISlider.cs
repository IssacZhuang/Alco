using System.Numerics;

namespace Vocore.GUI;

public class UISlider : UINode
{
    private float _value;
    private UISelectable? _handle;


    public UISelectable? Handle
    {
        get => _handle;
        set
        {
            if (_handle == value)
            {
                return;
            }

            UnregisterHandle(_handle);
            RegisterHandle(value);
            UpdateHandlePosition(_value);
            _handle = value;
        }
    }

    public float Value
    {
        get => _value;
        set
        {
            UpdateHandlePosition(value);
        }
    }

    private void RegisterHandle(UISelectable? node)
    {
        if (node == null)
        {
            return;
        }
        node.OnDragEvent += OnHandleDrag;
    }

    private void UnregisterHandle(UISelectable? node)
    {
        if (node == null)
        {
            return;
        }
        node.OnDragEvent -= OnHandleDrag;
    }

    private void UpdateHandlePosition(float value)
    {
        if (_handle == null)
        {
            return;
        }

        UINode? handleParent = _handle.Parent;
        if (handleParent == null)
        {
            return;
        }

        value = math.clamp(value, 0, 1);
        _value = value;
        Vector2 parentSize = handleParent.Size;
        _handle.Position = new Vector2(parentSize.X * (value - 0.5f), 0);
    }

    private void OnHandleDrag(Vector2 mousePosition)
    {
        if (_handle == null)
        {
            return;
        }

        UINode? parent = _handle.Parent;
        if (parent == null)
        {
            return;
        }
        Vector2 handleParentSize = parent.Size;
        float left = mousePosition.X - WorldTransform.position.X + handleParentSize.X * 0.5f;
        UpdateHandlePosition(left / handleParentSize.X);
    }

}
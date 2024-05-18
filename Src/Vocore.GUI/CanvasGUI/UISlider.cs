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
            _value = value;
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

        Vector2 parentSize = handleParent.Size;
        _handle.Position = new Vector2(parentSize.X * (value - 0.5f), 0);
    }

    private void OnHandleDrag(Vector2 mousePosition)
    {

    }

}
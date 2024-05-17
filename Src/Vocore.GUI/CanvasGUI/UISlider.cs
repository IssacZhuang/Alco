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
            _handle = value;
        }
    }

    public float Value
    {
        get => _value;
        set
        {
            _value = value;
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
        
    }

    private void OnHandleDrag(Vector2 mousePosition)
    {

    }

}
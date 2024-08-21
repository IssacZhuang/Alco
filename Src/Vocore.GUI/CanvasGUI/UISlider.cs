using System.Numerics;

namespace Vocore.GUI;

public class UISlider : UINode
{
    private float _value;
    private UISelectable? _handle;
    private UIText? _valueText;


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
            UpdateValue(_value);
            _handle = value;
        }
    }

    public UIText? ValueText
    {
        get => _valueText;
        set
        {
            _valueText = value;
            UpdateValueText();
        }
    }

    public float Value
    {
        get => _value;
        set
        {
            UpdateValue(value);
        }
    }

    private void RegisterHandle(UISelectable? node)
    {
        if (node == null)
        {
            return;
        }
        node.EventOnDrag += OnHandleDrag;
    }

    private void UnregisterHandle(UISelectable? node)
    {
        if (node == null)
        {
            return;
        }
        node.EventOnDrag -= OnHandleDrag;
    }

    private void UpdateValue(float value)
    {
        value = math.clamp(value, 0, 1);
        _value = value;

        UpdateValueText();

        // Update handle position
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

    private void UpdateValueText()
    {
        if (_valueText == null)
        {
            return;
        }

        _valueText.SetText(UtilsText.ToCharSpan(_value));
    }

    private void OnHandleDrag(Canvas canvas, Vector2 mousePosition)
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
        UpdateValue(left / handleParentSize.X);
    }

}
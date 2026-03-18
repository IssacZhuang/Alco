using System.Numerics;

namespace Alco.GUI;

/// <summary>
/// A slider UI control that allows users to select a value by dragging a handle or clicking on the track.
/// </summary>
public class UISlider : UIButton
{
    private float _value;
    private UISelectable? _handle;
    private UIText? _valueText;

    /// <summary>
    /// Occurs when the slider value changes.
    /// </summary>
    public event Action<float>? EventOnValueChanged;

    /// <summary>
    /// Gets or sets whether dragging the handle is required to change the value.
    /// When false, the value can only be changed by dragging the handle.
    /// When true, clicking anywhere on the slider sets the value directly.
    /// </summary>
    public bool ClickToSetValue { get; set; } = false;

    /// <summary>
    /// Gets or sets the handle selectable that represents the draggable thumb of the slider.
    /// </summary>
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


    public override void OnPressing(Canvas canvas, Vector2 mousePosition)
    {
        if (!ClickToSetValue) return;

        // Convert world mouse position to slider's local space
        Vector2 local = math.tolocal(WorldTransform, mousePosition);
        float t = local.X / Size.X + 0.5f; // map [-w/2, +w/2] -> [0, 1]
        UpdateValue(t);
    }


    private void UpdateValue(float value)
    {
        value = math.clamp(value, 0, 1);
        if (MathF.Abs(_value - value) < 0.000001f)
        {
            // keep text/handle in sync even if tiny change filtered out
            UpdateValueText();
            if (_handle != null && _handle.Parent != null)
            {
                Vector2 parentSizeInner = _handle.Parent.Size;
                _handle.Position = new Vector2(parentSizeInner.X * (value - 0.5f), 0);
            }
            return;
        }
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

        EventOnValueChanged?.Invoke(_value);
    }

    private void UpdateValueText()
    {
        if (_valueText == null)
        {
            return;
        }

        FixedString32 fixedString = new FixedString32();
        fixedString.Append(_value);// append as ISpanFormattable, no allocation
        _valueText.SetText(fixedString);
    }

    private void OnHandleDrag(Canvas canvas, Vector2 mousePosition)
    {
        if (ClickToSetValue) return;

        if (_handle == null)
        {
            return;
        }

        UINode? parent = _handle.Parent;
        if (parent == null)
        {
            return;
        }
        // Convert world mouse position to the handle track's local space to respect rotation
        Vector2 local = math.tolocal(parent.WorldTransform, mousePosition);
        float t = local.X / parent.Size.X + 0.5f; // map [-w/2, +w/2] -> [0, 1]
        UpdateValue(t);
    }
}
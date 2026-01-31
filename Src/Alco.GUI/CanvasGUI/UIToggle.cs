using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.GUI;

/// <summary>
/// The toggle UI node.
/// </summary>
public class UIToggle : UIButton
{
    private bool _isOn;
    private event Action<bool>? _onToggleEvent;
    private UINode? _checkmark;

    /// <summary>
    /// The value of the toggle.
    /// </summary>
    /// <value></value>
    public bool IsChecked
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            OnToggle(value);
        }
    }

    /// <summary>
    /// The checkmark of the toggle. It will be enabled when the toggle is checked.
    /// </summary>
    /// <value></value>
    public UINode? Checkmark
    {
        get => _checkmark;
        set
        {
            if (_checkmark == value) return;
            _checkmark = value;
            if (_checkmark != null)
            {
                _checkmark.IsEnable = _isOn;
            }
        }
    }

    /// <summary>
    /// Called when the toggle value is changed.
    /// </summary>
    public event Action<bool> OnToggleEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onToggleEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onToggleEvent -= value;
    }

    /// <summary>
    /// Called when the toggle value is changed.
    /// </summary>
    /// <param name="isOn">The value of the toggle.</param>
    public virtual void OnToggle(bool isOn)
    {
        _isOn = isOn;
        if (Checkmark != null)
        {
            Checkmark.IsEnable = isOn;
        }

        _onToggleEvent?.Invoke(isOn);
    }

    public override void OnClick(Canvas canvas, Vector2 mousePosition)
    {
        base.OnClick(canvas, mousePosition);
        OnToggle(!_isOn);
    }
}
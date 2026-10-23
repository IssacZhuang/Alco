using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>Style settings used by <see cref="CanvasUIFactory"/> when creating UI controls.</summary>
public struct CavanUIFactoryStyle
{
    /// <summary>Font used for control text.</summary>
    public Font Font { get; set; }
    /// <summary>Font size used for control text.</summary>
    public float FontSize { get; set; }
    /// <summary>Color of text on sliders.</summary>
    public ColorFloat TextColor { get; set; }


    /// <summary>Size of a slider's background.</summary>
    public Vector2 SliderSize { get; set; }
    /// <summary>Size of a slider's draggable handle.</summary>
    public Vector2 SliderHandleSize { get; set; }
    /// <summary>Color of a slider's background.</summary>
    public ColorFloat SliderColor { get; set; }
    /// <summary>Color of a slider's handle in its normal state.</summary>
    public ColorFloat SliderHandleColor { get; set; }
    /// <summary>Color of a slider's handle while hovered.</summary>
    public ColorFloat SliderHandleHoverColor { get; set; }
    /// <summary>Color of a slider's handle while dragged.</summary>
    public ColorFloat SliderHandleDragColor { get; set; }


    /// <summary>Text assigned to buttons created without an explicit label.</summary>
    public string DefaultButtonText { get; set; }
    /// <summary>Size of a button.</summary>
    public Vector2 ButtonSize { get; set; }

    /// <summary>Color of a button's text.</summary>
    public ColorFloat ButtonTextColor { get; set; }
    /// <summary>Color of a button in its normal state.</summary>
    public ColorFloat ButtonColor { get; set; }
    /// <summary>Color of a button while pressed.</summary>
    public ColorFloat ButtonPressedColor { get; set; }
    /// <summary>Color of a button while hovered.</summary>
    public ColorFloat ButtonHoverColor { get; set; }


    /// <summary>Color of a checkbox in its normal state.</summary>
    public ColorFloat CheckBoxColor { get; set; }
    /// <summary>Color of a checkbox while hovered.</summary>
    public ColorFloat CheckBoxHoverColor { get; set; }
    /// <summary>Color of a checkbox while pressed.</summary>
    public ColorFloat CheckBoxPressedColor { get; set; }
}

/// <summary>Creates UI controls configured with a shared <see cref="CavanUIFactoryStyle"/>.</summary>
public class CanvasUIFactory
{
    private CavanUIFactoryStyle _style;


    /// <summary>Style applied to created controls.</summary>
    public CavanUIFactoryStyle Style
    {
        get => _style;
        set => _style = value;
    }


    /// <summary>Initializes a factory with the given style.</summary>
    /// <param name="style">Style applied to created controls; its font must not be null.</param>
    public CanvasUIFactory(CavanUIFactoryStyle style)
    {
        if (style.Font == null)
        {
            throw new ArgumentNullException(nameof(style.Font));
        }
        _style = style;
    }

    /// <summary>Creates a button labeled with <see cref="CavanUIFactoryStyle.DefaultButtonText"/>.</summary>
    /// <returns>The created button.</returns>
    public UIButton CreateButton()
    {
        return CreateButton(_style.DefaultButtonText);
    }

    /// <summary>Creates a button with the given label.</summary>
    /// <param name="str">Text displayed on the button.</param>
    /// <returns>The created button.</returns>
    public UIButton CreateButton(string str)
    {
        UIImage bg = new UIImage()
        {
            Color = _style.ButtonColor,
            Size = _style.ButtonSize,
            Anchor = Anchor.Stretch
        };

        UIText text = new UIText()
        {
            Font = _style.Font,
            FontSize = _style.FontSize,
            Color = _style.ButtonTextColor,
            Text = str,
            Anchor = Anchor.Stretch
        };

        bg.Add(text, true);

        UIButton button = new UIButton
        {
            Size = _style.ButtonSize,
            TransitionTarget = bg,
            ColorNormal = _style.ButtonColor,
            ColorHover = _style.ButtonHoverColor,
            ColorPressing = _style.ButtonPressedColor,
            TransitionMode = TransitionMode.ColorTint,
        };

        button.Add(bg, true);
        return button;
    }

    /// <summary>Creates a slider with a value label.</summary>
    /// <returns>The created slider.</returns>
    public UISlider CreateSlider()
    {
        UINode handleParent = new UINode(){
            Size = new Vector2(_style.SliderSize.X - _style.SliderHandleSize.X, _style.SliderSize.Y),
            Anchor = Anchor.Stretch
        };

        UIImage bgHandle = new UIImage
        {
            Color = _style.SliderHandleColor,
            Size = _style.SliderHandleSize,
            Anchor = Anchor.Stretch
        };

        UIButton handle = new UIButton
        {
            Size = _style.SliderHandleSize,
            TransitionTarget = bgHandle,
            ColorNormal = _style.SliderHandleColor,
            ColorHover = _style.SliderHandleHoverColor,
            ColorPressing = _style.SliderHandleDragColor,
            TransitionMode = TransitionMode.ColorTint,
            Anchor = Anchor.CenterVerticalStretch
        };

        handle.Add(bgHandle, true);
        handleParent.Add(handle, true);

        UIImage bgSlider = new UIImage
        {
            Color = _style.SliderColor,
            Size = _style.SliderSize,
            Anchor = Anchor.Stretch
        };

        UIText text = new UIText
        {
            Font = _style.Font,
            FontSize = _style.FontSize,
            Color = _style.TextColor,
            Size = _style.SliderSize,
            Text = "0"
        };

        UISlider slider = new UISlider
        {
            Handle = handle,
            ValueText = text,
            Size = _style.SliderSize,
            Value = 0
        };

        slider.Add(bgSlider, true);
        slider.Add(handleParent, true);
        slider.Add(text, true);

        return slider;
    }
}
using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public struct CavanUIFactoryStyle
{
    public Font Font { get; set; }
    public float FontSize { get; set; }
    public ColorFloat TextColor { get; set; }

    public ColorFloat SliderColor { get; set; }
    public ColorFloat SliderHandleColor { get; set; }
    public ColorFloat SliderHandleHoverColor { get; set; }
    public ColorFloat SliderHandleDragColor { get; set; }


    public string DefaultButtonText { get; set; }
    public Vector2 ButtonSize { get; set; }

    public ColorFloat ButtonTextColor { get; set; }
    public ColorFloat ButtonColor { get; set; }
    public ColorFloat ButtonPressedColor { get; set; }
    public ColorFloat ButtonHoverColor { get; set; }

    public ColorFloat CheckBoxColor { get; set; }
    public ColorFloat CheckBoxHoverColor { get; set; }
    public ColorFloat CheckBoxPressedColor { get; set; }
}

public class CanvasUIFactory
{
    private CavanUIFactoryStyle _style;


    public CavanUIFactoryStyle Style
    {
        get => _style;
        set => _style = value;
    }


    public CanvasUIFactory(CavanUIFactoryStyle style)
    {
        if (style.Font == null)
        {
            throw new ArgumentNullException(nameof(style.Font));
        }
        _style = style;
    }

    public UIButton CreateButton()
    {
        return CreateButton(_style.DefaultButtonText);
    }

    public UIButton CreateButton(string str)
    {
        UISprite bg = new UISprite()
        {
            Color = _style.ButtonColor,
            Size = _style.ButtonSize
        };

        UIText text = new UIText()
        {
            Font = _style.Font,
            FontSize = _style.FontSize,
            Color = _style.ButtonTextColor,
            Text = str
        };

        bg.Add(text);

        UIButton button = new UIButton
        {
            Size = _style.ButtonSize,
            TransitionTarget = bg,
            ColorNormal = _style.ButtonColor,
            ColorHover = _style.ButtonHoverColor,
            ColorPressing = _style.ButtonPressedColor,
            TransitionMode = TransitionMode.ColorTint,
        };

        button.Add(bg);
        return button;
    }
}
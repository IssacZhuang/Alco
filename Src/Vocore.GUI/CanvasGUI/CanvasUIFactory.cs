using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public struct CavanUIFactoryStyle
{
    public Font Font { get; set; }
    public float FontSize { get; set; }

    public ColorFloat SliderColor { get; set; }
    public ColorFloat SliderThumbColor { get; set; }
    public ColorFloat SliderThumbHoverColor { get; set; }
    public ColorFloat SliderThumbDragColor { get; set; }

    public ColorFloat TextColor { get; set; }

    public string DefaultButtonText { get; set; }
    public Vector2 ButtonSize { get; set; }
    public ColorFloat ButtonColor { get; set; }
    public ColorFloat ButtonPressedColor { get; set; }
    public ColorFloat ButtonHoverColor { get; set; }

    public ColorFloat CheckBoxColor { get; set; }
    public ColorFloat CheckBoxHoverColor { get; set; }
    public ColorFloat CheckBoxCheckColor { get; set; }
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
            Color = _style.TextColor,
            Text = str
        };

        bg.Add(text);

        UIButton button = new UIButton
        {
            Size = _style.ButtonSize
        };

        button.Add(bg);
        return button;
    }
}
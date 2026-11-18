using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Alco.Profiler;

/// <summary>Maps the Alco editor's ImGui style to Avalonia without loading the engine.</summary>
internal static class ImGuiTheme
{
    private const double TextSize = 16;
    private const double ChartTextSize = 14;

    /// <summary>Rounding for frames and tabs.</summary>
    public const float FrameRounding = 4;

    /// <summary>Background of a window or plot.</summary>
    public static readonly ImmutableSolidColorBrush WindowBackground = new(0xFF191919);

    /// <summary>Neutral fill of a control frame.</summary>
    public static readonly ImmutableSolidColorBrush Frame = new(0xFF343434);

    /// <summary>Foreground of ordinary text.</summary>
    public static readonly ImmutableSolidColorBrush Text = new(0xFFDEDEDE);

    /// <summary>Foreground of secondary information that is not disabled.</summary>
    public static readonly ImmutableSolidColorBrush Muted = new(0xFFA3A3A3);

    /// <summary>Accent for focus and active plot marks.</summary>
    public static readonly ImmutableSolidColorBrush Accent = new(0xFF35C2A1);

    /// <summary>Translucent selection fill.</summary>
    public static readonly ImmutableSolidColorBrush Selection = new(0x5935C2A1);

    /// <summary>Outline for keyboard focus and plot selections.</summary>
    public static readonly ImmutablePen AccentPen = new(Accent);

    /// <summary>Applies the editor palette and control metrics to the profiler.</summary>
    public static void Apply(Application app)
    {
        app.RequestedThemeVariant = ThemeVariant.Dark;
        var theme = new FluentTheme { DensityStyle = DensityStyle.Compact };
        theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Accent.Color };
        app.Styles.Add(theme);

        var child = new ImmutableSolidColorBrush(0xFF252525);
        var popup = new ImmutableSolidColorBrush(0xFF2D2D2D);
        var disabled = new ImmutableSolidColorBrush(0xFF646464);
        var selected = new ImmutableSolidColorBrush(0xFF344541);
        var rowHovered = new ImmutableSolidColorBrush(0xFF3E655C);
        var rowPressed = new ImmutableSolidColorBrush(0xFF3C7769);
        IResourceDictionary resources = app.Resources;
        Set(resources, WindowBackground, "ImGuiWindowBrush");
        Set(resources, child, "ImGuiChildBrush", "TabControlBackground", "TabItemHeaderBackgroundUnselected",
            "TabItemHeaderBackgroundDisabled", "ButtonBackgroundDisabled", "TextControlBackgroundDisabled", "ComboBoxBackgroundDisabled");
        Set(resources, popup, "ComboBoxDropDownBackground", "ToolTipBackground");
        Set(resources, Frame, "ImGuiFrameBrush", "ButtonBackground", "TextControlBackground", "ComboBoxBackground",
            "ComboBoxBackgroundUnfocused");
        Set(resources, Text, "ImGuiTextBrush", "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
            "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused", "ToolTipForeground",
            "ComboBoxForeground", "ComboBoxDropDownGlyphForeground", "TabItemHeaderForegroundUnselected",
            "TabItemHeaderForegroundSelected", "TabItemHeaderForegroundUnselectedPointerOver", "TabItemHeaderForegroundSelectedPointerOver",
            "TabItemHeaderForegroundUnselectedPressed", "TabItemHeaderForegroundSelectedPressed", "SystemControlHighlightAltBaseHighBrush");
        Set(resources, Muted, "ImGuiMutedBrush", "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
            "TextControlPlaceholderForegroundFocused");
        Set(resources, disabled, "ButtonForegroundDisabled", "TextControlForegroundDisabled", "TextControlPlaceholderForegroundDisabled",
            "ComboBoxForegroundDisabled", "ComboBoxDropDownGlyphForegroundDisabled", "TabItemHeaderForegroundDisabled");
        Set(resources, Accent, "ImGuiAccentBrush", "SystemControlFocusVisualPrimaryBrush");
        Set(resources, Selection, "TextControlSelectionHighlightColor");
        Set(resources, new ImmutableSolidColorBrush(0xFF3E635A), "ButtonBackgroundPointerOver");
        Set(resources, new ImmutableSolidColorBrush(0xFF3C7567), "ButtonBackgroundPressed");
        Set(resources, new ImmutableSolidColorBrush(0xFF40514D), "TextControlBackgroundPointerOver", "ComboBoxBackgroundPointerOver");
        Set(resources, new ImmutableSolidColorBrush(0xFF3E6058), "TextControlBackgroundFocused", "ComboBoxBackgroundPressed");
        Set(resources, selected, "TabItemHeaderBackgroundSelected", "ComboBoxItemBackgroundSelected", "TreeViewItemBackgroundSelected",
            "SystemControlHighlightListAccentLowBrush");
        Set(resources, rowHovered, "ComboBoxItemBackgroundPointerOver", "ComboBoxItemBackgroundSelectedPointerOver",
            "TreeViewItemBackgroundPointerOver", "TreeViewItemBackgroundSelectedPointerOver",
            "SystemControlHighlightListLowBrush", "SystemControlHighlightListAccentMediumBrush");
        Set(resources, rowPressed, "ComboBoxItemBackgroundPressed", "ComboBoxItemBackgroundSelectedPressed",
            "TreeViewItemBackgroundPressed", "TreeViewItemBackgroundSelectedPressed", "TabItemHeaderBackgroundUnselectedPressed",
            "TabItemHeaderBackgroundSelectedPressed", "SystemControlHighlightListMediumBrush", "SystemControlHighlightListAccentHighBrush");
        Set(resources, new ImmutableSolidColorBrush(0xFF3F5B54), "TabItemHeaderBackgroundUnselectedPointerOver",
            "TabItemHeaderBackgroundSelectedPointerOver");
        Set(resources, new ImmutableSolidColorBrush(0x0AFFFFFF), "ImGuiAlternateRowBrush");
        Set(resources, new ImmutableSolidColorBrush(0xFF3F3F3F), "ScrollBarThumbBackgroundColor", "ScrollBarPanningThumbBackgroundColor");
        Set(resources, new ImmutableSolidColorBrush(0xFF555555), "ScrollBarThumbFillPointerOver");
        Set(resources, new ImmutableSolidColorBrush(0xB335C2A1), "ScrollBarThumbFillPressed");
        Set(resources, Brushes.Transparent, "SystemControlFocusVisualSecondaryBrush", "TabItemHeaderSelectedPipeFill",
            "ScrollBarTrackFill", "ScrollBarTrackFillPointerOver");

        Set(resources, new CornerRadius(FrameRounding), "ControlCornerRadius");
        Set(resources, new CornerRadius(6), "OverlayCornerRadius");
        Set(resources, new Thickness(0), "ButtonBorderThemeThickness", "TextControlBorderThemeThickness",
            "TextControlBorderThemeThicknessFocused", "ComboBoxBorderThemeThickness", "ComboBoxDropdownBorderThickness",
            "ToolTipBorderThemeThickness", "TreeViewItemBorderThemeThickness");
        Set(resources, new Thickness(8, 3), "ButtonPadding", "TextControlThemePadding", "TabItemHeaderMargin", "ToolTipBorderThemePadding");
        Set(resources, new Thickness(8, 3, 0, 3), "ComboBoxPadding");
        Set(resources, new Thickness(4), "ListBoxItemPadding");
        Set(resources, new Thickness(8, 4), "ComboBoxItemThemePadding");
        Set(resources, TextSize, "ControlContentThemeFontSize", "TabItemHeaderFontSize");
        Set(resources, ChartTextSize, "ToolTipContentThemeFontSize");
        Set(resources, 26d, "TextControlThemeMinHeight", "ComboBoxMinHeight", "TabItemMinHeight", "TreeViewItemMinHeight");
        Set(resources, 8d, "ScrollBarSize");
    }

    /// <summary>Creates a chart label with the profiler's compact text size.</summary>
    public static FormattedText CreateChartLabel(string text, IBrush brush, double? width = null)
    {
        var label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, ChartTextSize, brush);
        if (width is { } maximum)
        {
            label.MaxTextWidth = maximum;
            label.MaxLineCount = 1;
            label.Trimming = TextTrimming.CharacterEllipsis;
        }
        return label;
    }

    private static void Set(IResourceDictionary resources, object value, params string[] keys)
    {
        foreach (string key in keys)
        {
            resources[key] = value;
        }
    }
}

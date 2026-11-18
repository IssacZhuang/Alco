using System;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Alco.Profiler;

/// <summary>Shows sampled methods by time bucket and lets the user select a time range.</summary>
internal sealed class TraceHeatmapView : Control
{
    private const double _preferredHeight = 185;
    private const double _padding = 8;
    private const double _axisHeight = 26;
    private const double _footerHeight = 25;
    private const double _maxRowHeight = 22;
    private const string _emptyMessage = "No trace samples to display.";

    private static readonly ImmutablePen _axisPen = new(0xFF787878);
    private static readonly ImmutablePen _rangePen = new(ImGuiTheme.Text);

    private readonly ImmutableSolidColorBrush[] _palette = CreatePalette();
    private readonly TextBlock _tooltip = new() { MaxWidth = 560, TextWrapping = TextWrapping.Wrap };
    private HeatmapData? _data;
    private TraceRange? _selection;
    private FormattedText[] _rowLabels = [];
    private FormattedText[] _tickLabels = [];
    private FormattedText? _heading;
    private FormattedText? _footer;
    private FormattedText? _emptyLabel;
    private double _labelWidth;
    private double _intensityScale;
    private IPointer? _dragPointer;
    private int _anchorColumn;
    private int _dragColumn;
    private (int Row, int Column)? _hoverCell;

    static TraceHeatmapView() => AffectsRender<TraceHeatmapView>(IsFocusedProperty);

    /// <summary>Creates an empty, keyboard-focusable heatmap.</summary>
    public TraceHeatmapView()
    {
        ClipToBounds = true;
        Focusable = true;
        AutomationProperties.SetName(this, "Trace heatmap");
        AutomationProperties.SetHelpText(this, _emptyMessage);
    }

    /// <summary>Reports a committed bucket range, or null when the selection is cleared.</summary>
    public event Action<TraceRange?>? RangeSelected;

    /// <summary>Displays capture buckets and the current externally applied selection.</summary>
    public void Show(HeatmapData? data, TraceRange? selection)
    {
        if (data is not { ColumnCount: > 0, DurationMs: > 0, Rows.Count: > 0 } || !double.IsFinite(data.DurationMs))
        {
            data = null;
            selection = null;
        }

        if (!ReferenceEquals(_data, data))
        {
            EndDrag();
            ClearHover();
            _data = data;
            _rowLabels = [];
            _tickLabels = [];
            _intensityScale = data is null ? 0 : GetIntensityScale(data);
            AutomationProperties.SetHelpText(this, data is null ? _emptyMessage
                : "Click or drag across time to select buckets. Use the arrow keys to select adjacent buckets. Double-click or press Escape to clear.");
        }
        else if (_selection == selection)
        {
            return;
        }

        _selection = selection;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(ImGuiTheme.WindowBackground, bounds, ImGuiTheme.FrameRounding);
        if (IsFocused && bounds.Width > 1 && bounds.Height > 1)
        {
            context.DrawRectangle(null, ImGuiTheme.AccentPen, new Rect(0.5, 0.5, bounds.Width - 1, bounds.Height - 1));
        }

        if (_data is not { } data)
        {
            context.DrawText(_emptyLabel ??= ImGuiTheme.CreateChartLabel(_emptyMessage, ImGuiTheme.Muted), new Point(_padding, _padding));
            return;
        }

        Rect plot = GetPlot(data);
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        DrawAxis(context, plot, data);
        DrawRows(context, plot, data);
        TraceRange? range = _dragPointer is null ? _selection : GetRange(data, _anchorColumn, _dragColumn);
        if (range is { } selected)
        {
            DrawSelection(context, plot, data.DurationMs, selected);
        }

        context.DrawText(_footer ??= ImGuiTheme.CreateChartLabel("Click or drag to select · Double-click to clear", ImGuiTheme.Muted),
            new Point(_padding, plot.Bottom + 6));
    }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(640, availableSize.Width), _preferredHeight);

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (_dragPointer is null && e.Pointer.Type != PointerType.Touch)
        {
            UpdateHover(e.GetPosition(this));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_data is not { } data || _dragPointer is not null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            ClearHover();
            e.Handled = true;
            RangeSelected?.Invoke(null);
            return;
        }

        Rect plot = GetPlot(data);
        Point position = point.Position;
        if (plot.Width <= 0 || plot.Height <= 0 || position.X < plot.Left || position.X >= plot.Right
            || position.Y < 0 || position.Y >= plot.Bottom)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        ClearHover();
        _anchorColumn = _dragColumn = GetColumn(data, plot, position.X);
        _dragPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        if (_dragPointer is null)
        {
            if (e.Pointer.Type != PointerType.Touch)
            {
                UpdateHover(position);
            }
            return;
        }

        if (!ReferenceEquals(_dragPointer, e.Pointer) || _data is not { } data)
        {
            return;
        }

        Rect plot = GetPlot(data);
        if (plot.Width <= 0)
        {
            return;
        }

        int column = GetColumn(data, plot, position.X);
        if (_dragColumn != column)
        {
            _dragColumn = column;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!ReferenceEquals(_dragPointer, e.Pointer) || _data is not { } data)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Rect plot = GetPlot(data);
        int column = plot.Width > 0 ? GetColumn(data, plot, point.Position.X) : _dragColumn;
        TraceRange selected = GetRange(data, _anchorColumn, column);
        EndDrag();
        e.Handled = true;
        RangeSelected?.Invoke(selected);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHover();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (ReferenceEquals(_dragPointer, e.Pointer))
        {
            _dragPointer = null;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_data is not { } data)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            EndDrag();
            e.Handled = true;
            RangeSelected?.Invoke(null);
            return;
        }

        if (_dragPointer is not null || e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        bool left = e.Key == Key.Left;
        int column = left ? data.ColumnCount - 1 : 0;
        if (_selection is { } selection)
        {
            double edge = Math.Clamp(left ? selection.StartMs : selection.EndMs, 0, data.DurationMs);
            int boundary = (int)Math.Round(edge / data.DurationMs * data.ColumnCount);
            column = Math.Clamp(left ? boundary - 1 : boundary, 0, data.ColumnCount - 1);
        }

        e.Handled = true;
        RangeSelected?.Invoke(GetRange(data, column, column));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndDrag();
        ClearHover();
        base.OnDetachedFromVisualTree(e);
    }

    private Rect GetPlot(HeatmapData data)
    {
        double left = Math.Min(216, Bounds.Width * 0.31);
        double width = Math.Max(0, Bounds.Width - left - _padding);
        double height = Math.Clamp(Bounds.Height - _axisHeight - _footerHeight, 0, data.Rows.Count * _maxRowHeight);
        return new Rect(left, _axisHeight, width, height);
    }

    private void DrawAxis(DrawingContext context, Rect plot, HeatmapData data)
    {
        if (plot.Left > 58)
        {
            context.DrawText(_heading ??= ImGuiTheme.CreateChartLabel("Method", ImGuiTheme.Muted), new Point(_padding, 4));
        }

        int divisions = Math.Clamp((int)(plot.Width / 94), 1, 8);
        if (_tickLabels.Length != divisions + 1)
        {
            _tickLabels = new FormattedText[divisions + 1];
            for (int i = 0; i <= divisions; i++)
            {
                _tickLabels[i] = ImGuiTheme.CreateChartLabel($"{FormatTime(data.DurationMs * ((double)i / divisions))} ms", ImGuiTheme.Muted);
            }
        }

        double lastRight = plot.Left - 6;
        double endLeft = plot.Right - _tickLabels[^1].Width;
        for (int i = 0; i <= divisions; i++)
        {
            double x = plot.Left + plot.Width * i / divisions;
            context.DrawLine(_axisPen, new Point(x, plot.Top - 4), new Point(x, plot.Top));
            FormattedText label = _tickLabels[i];
            if (label.Width > plot.Width)
            {
                continue;
            }

            double textX = Math.Clamp(x - label.Width / 2, plot.Left, Math.Max(plot.Left, plot.Right - label.Width));
            if (textX >= lastRight + 6 && (i == 0 || i == divisions || textX + label.Width + 6 <= endLeft))
            {
                context.DrawText(label, new Point(textX, 4));
                lastRight = textX + label.Width;
            }
        }
        context.DrawLine(_axisPen, new Point(plot.Left, plot.Top - 0.5), new Point(plot.Right, plot.Top - 0.5));
    }

    private void DrawRows(DrawingContext context, Rect plot, HeatmapData data)
    {
        double labelWidth = plot.Left - 2 * _padding;
        bool showLabels = labelWidth >= 12;
        if (showLabels && (_labelWidth != labelWidth || _rowLabels.Length != data.Rows.Count))
        {
            _labelWidth = labelWidth;
            _rowLabels = new FormattedText[data.Rows.Count];
            for (int row = 0; row < _rowLabels.Length; row++)
            {
                _rowLabels[row] = ImGuiTheme.CreateChartLabel(MethodNameUtility.ShortName(data.Rows[row].Method), ImGuiTheme.Text, labelWidth);
            }
        }

        double rowHeight = plot.Height / data.Rows.Count;
        double cellHeight = rowHeight - Math.Min(1, rowHeight * 0.15);
        double columnWidth = plot.Width / data.ColumnCount;
        double cellWidth = columnWidth - Math.Min(1, columnWidth * 0.15);
        for (int row = 0; row < data.Rows.Count; row++)
        {
            double y = plot.Top + row * rowHeight;
            if (showLabels && _rowLabels[row].Height <= rowHeight)
            {
                context.DrawText(_rowLabels[row], new Point(_padding, y + (rowHeight - _rowLabels[row].Height) / 2));
            }

            int[] samples = data.Rows[row].Samples;
            for (int col = 0; col < data.ColumnCount; col++)
            {
                int count = samples[col];
                int intensity = Math.Min(_palette.Length - 1, (int)Math.Ceiling(Math.Sqrt(count) * _intensityScale));
                context.FillRectangle(_palette[intensity], new Rect(plot.Left + col * columnWidth, y, cellWidth, cellHeight));
            }
        }
    }

    private static void DrawSelection(DrawingContext context, Rect plot, double duration, TraceRange range)
    {
        double left = plot.Left + Math.Clamp(range.StartMs / duration, 0, 1) * plot.Width;
        double right = plot.Left + Math.Clamp(range.EndMs / duration, 0, 1) * plot.Width;
        double width = Math.Min(plot.Width, Math.Max(1, right - left));
        using (context.PushClip(plot))
        {
            context.DrawRectangle(ImGuiTheme.Selection, _rangePen,
                new Rect(Math.Min(left, plot.Right - width), plot.Top, width, plot.Height));
        }
    }

    private void UpdateHover(Point position)
    {
        if (_data is not { } data)
        {
            return;
        }

        Rect plot = GetPlot(data);
        if (plot.Width <= 0 || plot.Height <= 0 || position.X < plot.Left || position.X >= plot.Right
            || position.Y < plot.Top || position.Y >= plot.Bottom)
        {
            ClearHover();
            return;
        }

        int row = Math.Min(data.Rows.Count - 1, (int)((position.Y - plot.Top) / plot.Height * data.Rows.Count));
        int col = GetColumn(data, plot, position.X);
        if (_hoverCell == (row, col))
        {
            return;
        }

        _hoverCell = (row, col);
        HeatmapRow method = data.Rows[row];
        TraceRange bucket = GetRange(data, col, col);
        _tooltip.Text = $"{method.Method}\n{method.Samples[col]:N0} samples · {FormatTime(bucket.StartMs)}–{FormatTime(bucket.EndMs)} ms";
        ToolTip.SetTip(this, _tooltip);
        ToolTip.SetIsOpen(this, true);
    }

    private void ClearHover()
    {
        if (_hoverCell is null)
        {
            return;
        }

        _hoverCell = null;
        ToolTip.SetTip(this, null);
    }

    private void EndDrag()
    {
        if (_dragPointer is not { } pointer)
        {
            return;
        }

        _dragPointer = null;
        if (ReferenceEquals(pointer.Captured, this))
        {
            pointer.Capture(null);
        }
        InvalidateVisual();
    }

    private double GetIntensityScale(HeatmapData data)
    {
        int peak = 0;
        for (int row = 0; row < data.Rows.Count; row++)
        {
            int[] samples = data.Rows[row].Samples;
            for (int col = 0; col < samples.Length; col++)
            {
                peak = Math.Max(peak, samples[col]);
            }
        }
        return peak == 0 ? 0 : (_palette.Length - 1) / Math.Sqrt(peak);
    }

    private static int GetColumn(HeatmapData data, Rect plot, double x)
    {
        double fraction = Math.Clamp((x - plot.Left) / plot.Width, 0, 1);
        return Math.Min(data.ColumnCount - 1, (int)(fraction * data.ColumnCount));
    }

    private static TraceRange GetRange(HeatmapData data, int first, int last) => new(
        data.DurationMs * ((double)Math.Min(first, last) / data.ColumnCount),
        data.DurationMs * ((double)(Math.Max(first, last) + 1) / data.ColumnCount));

    private static string FormatTime(double milliseconds) => milliseconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static ImmutableSolidColorBrush[] CreatePalette()
    {
        var palette = new ImmutableSolidColorBrush[16];
        palette[0] = ImGuiTheme.Frame;
        Color start = ImGuiTheme.Frame.Color;
        Color end = ImGuiTheme.Accent.Color;
        for (int i = 1; i < palette.Length; i++)
        {
            double t = 0.15 + 0.85 * (i - 1) / (palette.Length - 2);
            var color = Color.FromRgb((byte)(start.R + (end.R - start.R) * t),
                (byte)(start.G + (end.G - start.G) * t), (byte)(start.B + (end.B - start.B) * t));
            palette[i] = new ImmutableSolidColorBrush(color);
        }
        return palette;
    }
}

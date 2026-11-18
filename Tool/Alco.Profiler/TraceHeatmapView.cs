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

using Rect = Avalonia.Rect;

/// <summary>Shows sampled methods by slice and lets the user select columns.</summary>
internal sealed class TraceHeatmapView : Control
{
    private const double _preferredHeight = 185;
    private const double _padding = 8;
    private const double _axisHeight = 26;
    private const double _footerHeight = 25;
    private const double _maxRowHeight = 22;
    private const string _emptyMessage = "No trace slices to display.";

    private static readonly ImmutablePen _axisPen = new(0xFF787878);
    private static readonly ImmutablePen _rangePen = new(ImGuiTheme.Text);

    private readonly ImmutableSolidColorBrush[] _palette = CreatePalette();
    private readonly TextBlock _tooltip = new() { MaxWidth = 560, TextWrapping = TextWrapping.Wrap };
    private HeatmapData? _data;
    private (int First, int Last)? _selection;
    private FormattedText[] _rowLabels = [];
    private (double Position, FormattedText Label)[] _axisTicks = [];
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

    /// <summary>Reports the inclusive, ordered column indices, or <see langword="null"/> when cleared.</summary>
    public event Action<(int First, int Last)?>? ColumnsSelected;

    /// <summary>Displays trace slices and the current page-local selection.</summary>
    public void Show(HeatmapData? data, (int First, int Last)? selection)
    {
        if (data is not { ColumnCount: > 0 })
        {
            data = null;
            selection = null;
        }
        else if (selection is { } columns)
        {
            var (first, last) = OrderColumns(columns.First, columns.Last);
            selection = first >= 0 && last < data.ColumnCount ? (first, data.IsPerInvocation ? first : last) : null;
        }

        if (!ReferenceEquals(_data, data))
        {
            EndDrag();
            ClearHover();
            _data = data;
            _rowLabels = [];
            _axisTicks = [];
            _footer = null;
            _intensityScale = data is null ? 0 : GetIntensityScale(data);
            AutomationProperties.SetHelpText(this, data switch
            {
                null => _emptyMessage,
                { IsPerInvocation: true } => "Click a column or use the arrow keys to select one invocation. Double-click or press Escape to clear.",
                _ => "Click or drag to select slices. Use the arrow keys to select adjacent slices. Double-click or press Escape to clear."
            });
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
        var selection = _dragPointer is null ? _selection : OrderColumns(_anchorColumn, _dragColumn);
        if (selection is { } selected)
        {
            DrawSelection(context, plot, data.ColumnCount, selected);
        }

        context.DrawText(_footer ??= ImGuiTheme.CreateChartLabel(data.IsPerInvocation
                ? "Click to select · Double-click to clear" : "Click or drag to select · Double-click to clear", ImGuiTheme.Muted),
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
            ColumnsSelected?.Invoke(null);
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
        int column = GetColumn(data, plot, position.X);
        e.Handled = true;
        if (data.IsPerInvocation)
        {
            ColumnsSelected?.Invoke((column, column));
            return;
        }

        _anchorColumn = _dragColumn = column;
        _dragPointer = e.Pointer;
        e.Pointer.Capture(this);
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
        var selected = OrderColumns(_anchorColumn, column);
        EndDrag();
        e.Handled = true;
        ColumnsSelected?.Invoke(selected);
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
            ColumnsSelected?.Invoke(null);
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
            column = Math.Clamp(left ? selection.First - 1 : selection.Last + 1, 0, data.ColumnCount - 1);
        }

        e.Handled = true;
        ColumnsSelected?.Invoke((column, column));
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
        double height = Math.Clamp(Bounds.Height - _axisHeight - _footerHeight, 0, Math.Max(1, data.Rows.Count) * _maxRowHeight);
        return new Rect(left, _axisHeight, width, height);
    }

    private void DrawAxis(DrawingContext context, Rect plot, HeatmapData data)
    {
        if (plot.Left > 58)
        {
            context.DrawText(_heading ??= ImGuiTheme.CreateChartLabel("Method", ImGuiTheme.Muted), new Point(_padding, 4));
        }

        bool perInvocation = data.IsPerInvocation;
        int lastTick = perInvocation ? data.ColumnCount - 1 : data.ColumnCount;
        int divisions = Math.Min(lastTick, Math.Clamp((int)(plot.Width / 94), 1, 8));
        if (_axisTicks.Length != divisions + 1)
        {
            _axisTicks = new (double Position, FormattedText Label)[divisions + 1];
            for (int i = 0; i <= divisions; i++)
            {
                int index = divisions == 0 ? 0 : (int)Math.Round((double)i * lastTick / divisions);
                double position = (index + (perInvocation ? 0.5 : 0)) / data.ColumnCount;
                string text = perInvocation ? FormatSequence(data.Slices[index].Sequence)
                    : $"{FormatTime(index == 0 ? data.Slices[0].Range.StartMs : data.Slices[index - 1].Range.EndMs)} ms";
                _axisTicks[i] = (position, ImGuiTheme.CreateChartLabel(text, ImGuiTheme.Muted));
            }
        }

        double lastRight = plot.Left - 6;
        var (endPosition, endLabel) = _axisTicks[^1];
        double endLeft = GetLabelLeft(plot, plot.Left + plot.Width * endPosition, endLabel.Width);
        for (int i = 0; i <= divisions; i++)
        {
            var (position, label) = _axisTicks[i];
            double x = plot.Left + plot.Width * position;
            context.DrawLine(_axisPen, new Point(x, plot.Top - 4), new Point(x, plot.Top));
            if (label.Width > plot.Width)
            {
                continue;
            }

            double textX = GetLabelLeft(plot, x, label.Width);
            if (textX >= lastRight + 6 && (i == 0 || i == divisions || textX + label.Width + 6 <= endLeft))
            {
                context.DrawText(label, new Point(textX, 4));
                lastRight = textX + label.Width;
            }
        }

        if (!perInvocation)
        {
            context.DrawLine(_axisPen, new Point(plot.Left, plot.Top - 0.5), new Point(plot.Right, plot.Top - 0.5));
        }
    }

    private static double GetLabelLeft(Rect plot, double x, double width) =>
        Math.Clamp(x - width / 2, plot.Left, Math.Max(plot.Left, plot.Right - width));

    private void DrawRows(DrawingContext context, Rect plot, HeatmapData data)
    {
        bool hasRows = data.Rows.Count > 0;
        int rowCount = Math.Max(1, data.Rows.Count);
        double labelWidth = plot.Left - 2 * _padding;
        bool showLabels = labelWidth >= 12;
        if (showLabels && (_labelWidth != labelWidth || _rowLabels.Length != rowCount))
        {
            _labelWidth = labelWidth;
            _rowLabels = new FormattedText[rowCount];
            for (int row = 0; row < _rowLabels.Length; row++)
            {
                _rowLabels[row] = hasRows
                    ? ImGuiTheme.CreateChartLabel(MethodNameUtility.ShortName(data.Rows[row].Method), ImGuiTheme.Text, labelWidth)
                    : ImGuiTheme.CreateChartLabel("No stack samples", ImGuiTheme.Muted, labelWidth);
            }
        }

        double rowHeight = plot.Height / rowCount;
        double cellHeight = rowHeight - Math.Min(1, rowHeight * 0.15);
        double columnWidth = plot.Width / data.ColumnCount;
        double cellWidth = columnWidth - Math.Min(1, columnWidth * 0.15);
        for (int row = 0; row < rowCount; row++)
        {
            double y = plot.Top + row * rowHeight;
            if (showLabels && _rowLabels[row].Height <= rowHeight)
            {
                context.DrawText(_rowLabels[row], new Point(_padding, y + (rowHeight - _rowLabels[row].Height) / 2));
            }

            int[]? samples = hasRows ? data.Rows[row].Samples : null;
            for (int col = 0; col < data.ColumnCount; col++)
            {
                int count = samples?[col] ?? 0;
                int intensity = Math.Min(_palette.Length - 1, (int)Math.Ceiling(Math.Sqrt(count) * _intensityScale));
                context.FillRectangle(_palette[intensity], new Rect(plot.Left + col * columnWidth, y, cellWidth, cellHeight));
            }
        }
    }

    private static void DrawSelection(DrawingContext context, Rect plot, int columnCount, (int First, int Last) selection)
    {
        double columnWidth = plot.Width / columnCount;
        double left = plot.Left + selection.First * columnWidth;
        double width = Math.Min(plot.Width, Math.Max(1, (selection.Last - selection.First + 1) * columnWidth));
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

        int rowCount = data.Rows.Count;
        int row = rowCount == 0 ? -1 : Math.Min(rowCount - 1, (int)((position.Y - plot.Top) / plot.Height * rowCount));
        int col = GetColumn(data, plot, position.X);
        if (_hoverCell == (row, col))
        {
            return;
        }

        _hoverCell = (row, col);
        TraceSlice slice = data.Slices[col];
        TraceRange range = slice.Range;
        string time = $"{FormatTime(range.StartMs)}–{FormatTime(range.EndMs)} ms";
        if (data.IsPerInvocation)
        {
            time = $"{data.Mode} {FormatSequence(slice.Sequence)} · {time} · {FormatTime(range.EndMs - range.StartMs)} ms elapsed";
        }

        if (row >= 0)
        {
            HeatmapRow method = data.Rows[row];
            _tooltip.Text = $"{method.Method}\n{method.Samples[col]:N0} samples · {time}";
        }
        else
        {
            _tooltip.Text = $"No stack samples on this page\n{time}";
        }

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

    private static (int First, int Last) OrderColumns(int first, int last) => (Math.Min(first, last), Math.Max(first, last));

    private static string FormatSequence(long sequence) => $"#{sequence.ToString(CultureInfo.InvariantCulture)}";

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

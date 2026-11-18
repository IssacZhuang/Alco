using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Alco.Profiler;

using Rect = Avalonia.Rect;

/// <summary>Displays sample-weighted call paths with interactive subtree zoom.</summary>
internal sealed class FlameGraphView : Control
{
    private const double _padding = 8;
    private const double _headerHeight = 34;
    private const double _rowHeight = 24;
    private const double _rowPitch = 26;
    private const double _labelInset = 8;
    private const string _emptyMessage = "No sampled call paths to display.";

    private readonly ImmutableSolidColorBrush[] _palette =
    [
        new(0xFF335A51), new(0xFF3D4D63), new(0xFF51465F), new(0xFF5E4150),
        new(0xFF60513B), new(0xFF664743), new(0xFF40565E), new(0xFF3F5843),
    ];
    private readonly List<Bar> _bars = [];
    private readonly List<int> _levelStarts = [];
    private readonly Queue<LayoutEntry> _pending = new();
    private readonly TextBlock _tooltip = new() { MaxWidth = 560, TextWrapping = TextWrapping.Wrap };
    private CallNode? _root;
    private CallNode? _zoomedRoot;
    private double _layoutWidth = -1;
    private int _unboundedDepth;
    private int _hoveredBar = -1;
    private FormattedText? _emptyLabel;
    private FormattedText? _hintLabel;
    private FormattedText? _zoomedLabel;
    private FormattedText? _resetLabel;

    private readonly record struct LayoutEntry(CallNode Node, int Start, int Depth);
    private readonly record struct Bar(CallNode Node, Rect Bounds, IBrush Brush, FormattedText? Label);

    static FlameGraphView() => AffectsRender<FlameGraphView>(IsFocusedProperty);

    /// <summary>Creates an empty, keyboard-focusable flame graph.</summary>
    public FlameGraphView()
    {
        ClipToBounds = true;
        Focusable = true;
        AutomationProperties.SetName(this, "Flame graph");
        AutomationProperties.SetHelpText(this, _emptyMessage);
    }

    /// <summary>Displays an aggregated call tree and resets zoom when the tree changes.</summary>
    public void Show(CallNode? root)
    {
        if (root is not { TotalSamples: > 0 })
        {
            root = null;
        }
        if (ReferenceEquals(_root, root))
        {
            return;
        }

        _root = root;
        _zoomedRoot = null;
        AutomationProperties.SetHelpText(this, root is null ? _emptyMessage
            : "Click a call path to zoom. Choose Reset zoom, double-click, right-click, or press Escape to restore all call paths.");
        ResetLayout();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(ImGuiTheme.WindowBackground, bounds, ImGuiTheme.FrameRounding);
        if (IsFocused && bounds.Width > 1 && bounds.Height > 1)
        {
            DrawOutline(context, bounds);
        }

        DrawHeader(context, bounds.Width);
        for (int i = 0; i < _bars.Count; i++)
        {
            Bar bar = _bars[i];
            Rect rect = bar.Bounds;
            context.FillRectangle(bar.Brush, rect);
            if (i == _hoveredBar)
            {
                DrawOutline(context, rect);
            }

            if (bar.Label is { } label)
            {
                context.DrawText(label, new Point(rect.X + _labelInset, rect.Y + (rect.Height - label.Height) / 2));
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        bool unconstrained = double.IsPositiveInfinity(availableSize.Width);
        double width = unconstrained ? 640 : availableSize.Width;
        EnsureLayout(width);
        int depth = unconstrained ? GetUnboundedDepth() : _levelStarts.Count;
        return new Size(width, Math.Max(64, _headerHeight + depth * _rowPitch + _padding));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        EnsureLayout(finalSize.Width);
        return finalSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (e.Pointer.Type != PointerType.Touch)
        {
            SetHovered(FindBar(e.GetPosition(this)));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Type != PointerType.Touch)
        {
            SetHovered(FindBar(e.GetPosition(this)));
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHovered(-1);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        SetHovered(-1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_root is null)
        {
            return;
        }

        PointerPoint pointer = e.GetCurrentPoint(this);
        PointerUpdateKind kind = pointer.Properties.PointerUpdateKind;
        bool left = kind == PointerUpdateKind.LeftButtonPressed;
        if (_zoomedRoot is not null && (kind == PointerUpdateKind.RightButtonPressed
            || (left && (e.ClickCount >= 2 || GetResetBounds(Bounds.Width).Contains(pointer.Position)))))
        {
            SetZoom(null);
            e.Handled = true;
            return;
        }
        if (!left)
        {
            return;
        }

        int index = FindBar(pointer.Position);
        if (index < 0)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        CallNode node = _bars[index].Node;
        if (!ReferenceEquals(node, _root))
        {
            SetZoom(node);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _zoomedRoot is not null)
        {
            SetZoom(null);
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetHovered(-1);
        base.OnDetachedFromVisualTree(e);
    }

    private void SetZoom(CallNode? node)
    {
        if (ReferenceEquals(_zoomedRoot, node))
        {
            return;
        }

        _zoomedRoot = node;
        ResetLayout();
        this.BringIntoView(new Rect(0, 0, Bounds.Width, _headerHeight));
    }

    private void ResetLayout()
    {
        _layoutWidth = -1;
        _unboundedDepth = 0;
        _bars.Clear();
        _levelStarts.Clear();
        SetHovered(-1);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void EnsureLayout(double width)
    {
        if (_layoutWidth == width)
        {
            return;
        }

        _layoutWidth = width;
        _bars.Clear();
        _levelStarts.Clear();
        SetHovered(-1);
        InvalidateVisual();
        double plotWidth = width - 2 * _padding;
        if (plotWidth < 1 || (_zoomedRoot ?? _root) is not { } root)
        {
            return;
        }

        // Hit testing requires bars to stay ordered by depth, then by x.
        double scale = plotWidth / root.TotalSamples;
        _pending.Enqueue(new LayoutEntry(root, 0, 0));
        while (_pending.TryDequeue(out var entry))
        {
            if (_levelStarts.Count == entry.Depth)
            {
                _levelStarts.Add(_bars.Count);
            }

            double barWidth = entry.Node.TotalSamples * scale;
            double visibleWidth = barWidth - Math.Clamp((barWidth - 1) * 0.2, 0, 1);
            var rect = new Rect(_padding + entry.Start * scale, _headerHeight + entry.Depth * _rowPitch,
                visibleWidth, _rowHeight);
            string method = entry.Node.Method;
            uint color = (uint)StringComparer.Ordinal.GetHashCode(method) % (uint)_palette.Length;
            FormattedText? label = visibleWidth >= 28
                ? ImGuiTheme.CreateChartLabel(MethodNameUtility.ShortName(method), ImGuiTheme.Text, visibleWidth - 2 * _labelInset)
                : null;
            _bars.Add(new Bar(entry.Node, rect, _palette[color], label));
            EnqueueChildren(entry, scale);
        }
    }

    private int GetUnboundedDepth()
    {
        if (_unboundedDepth != 0 || (_zoomedRoot ?? _root) is not { } root)
        {
            return _unboundedDepth;
        }

        // When measured without a width, any positive-width branch might fit at arrange time.
        _pending.Enqueue(new LayoutEntry(root, 0, 0));
        while (_pending.TryDequeue(out var entry))
        {
            _unboundedDepth = entry.Depth + 1;
            EnqueueChildren(entry, 1);
        }
        return _unboundedDepth;
    }

    private void EnqueueChildren(LayoutEntry parent, double scale)
    {
        int start = parent.Start;
        IReadOnlyList<CallNode> children = parent.Node.Children;
        for (int i = 0; i < children.Count; i++)
        {
            CallNode child = children[i];
            if (child.TotalSamples * scale >= 1)
            {
                _pending.Enqueue(new LayoutEntry(child, start, parent.Depth + 1));
            }
            // Hidden branches still occupy their share of the parent's sample span.
            start += child.TotalSamples;
        }
    }

    private int FindBar(Point point)
    {
        if (point.Y < _headerHeight)
        {
            return -1;
        }

        int depth = (int)((point.Y - _headerHeight) / _rowPitch);
        if (depth >= _levelStarts.Count)
        {
            return -1;
        }

        int lo = _levelStarts[depth];
        int hi = (depth + 1 < _levelStarts.Count ? _levelStarts[depth + 1] : _bars.Count) - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            Rect box = _bars[mid].Bounds;
            if (point.X < box.Left)
            {
                hi = mid - 1;
            }
            else if (point.X >= box.Right)
            {
                lo = mid + 1;
            }
            else
            {
                return point.Y < box.Bottom ? mid : -1;
            }
        }
        return -1;
    }

    private void SetHovered(int index)
    {
        if (_hoveredBar == index)
        {
            return;
        }

        _hoveredBar = index;
        if (index < 0)
        {
            ToolTip.SetTip(this, null);
        }
        else
        {
            CallNode node = _bars[index].Node;
            _tooltip.Text = $"{node.Method}\nTotal samples: {node.TotalSamples:N0} · Self samples: {node.SelfSamples:N0}";
            ToolTip.SetTip(this, _tooltip);
            ToolTip.SetIsOpen(this, true);
        }
        InvalidateVisual();
    }

    private void DrawHeader(DrawingContext context, double width)
    {
        if (_root is null)
        {
            context.DrawText(_emptyLabel ??= ImGuiTheme.CreateChartLabel(_emptyMessage, ImGuiTheme.Muted), new Point(_padding, _padding));
            return;
        }
        if (_zoomedRoot is null)
        {
            context.DrawText(_hintLabel ??= ImGuiTheme.CreateChartLabel("Flame graph · Click a bar to zoom", ImGuiTheme.Muted), new Point(_padding, _padding));
            return;
        }

        Rect reset = GetResetBounds(width);
        if (reset.Left > 2 * _padding)
        {
            using (context.PushClip(new Rect(_padding, 0, reset.Left - 2 * _padding, _headerHeight)))
            {
                context.DrawText(_zoomedLabel ??= ImGuiTheme.CreateChartLabel("Zoomed call path · Click a bar to zoom further", ImGuiTheme.Muted),
                    new Point(_padding, _padding));
            }
        }
        if (reset.Width <= 0)
        {
            return;
        }

        context.FillRectangle(ImGuiTheme.Frame, reset, ImGuiTheme.FrameRounding);
        FormattedText label = _resetLabel ??= ImGuiTheme.CreateChartLabel("Reset zoom", ImGuiTheme.Text);
        if (label.Width <= reset.Width)
        {
            context.DrawText(label, new Point(reset.X + (reset.Width - label.Width) / 2,
                reset.Y + (reset.Height - label.Height) / 2));
        }
    }

    private static Rect GetResetBounds(double width)
    {
        double buttonWidth = Math.Clamp(width - 2 * _padding, 0, 100);
        return new Rect(Math.Max(_padding, width - _padding - buttonWidth), 5, buttonWidth, 24);
    }

    private static void DrawOutline(DrawingContext context, Rect rect)
    {
        if (rect.Width <= 2)
        {
            context.FillRectangle(ImGuiTheme.Accent, rect);
            return;
        }
        context.DrawRectangle(null, ImGuiTheme.AccentPen, new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1));
    }
}

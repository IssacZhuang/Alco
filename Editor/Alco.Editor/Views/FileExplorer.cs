using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Alco.Editor.Views;

public class FileTreeNodeToggleButton : ToggleButton
{
    protected override Type StyleKeyOverride => typeof(ToggleButton);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            DataContext is ViewModels.FileTreeNode { IsFolder: true } node)
        {
            var tree = this.FindAncestorOfType<TreeFileExplorer>();
            tree?.ToggleNodeIsExpanded(node);
        }

        e.Handled = true;
    }
}

public class FileTreeNodeIcon : UserControl
{
    public static readonly StyledProperty<ViewModels.FileTreeNode> NodeProperty =
        AvaloniaProperty.Register<FileTreeNodeIcon, ViewModels.FileTreeNode>(nameof(Node));

    public ViewModels.FileTreeNode Node
    {
        get => GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<FileTreeNodeIcon, bool>(nameof(IsExpanded));

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    static FileTreeNodeIcon()
    {
        NodeProperty.Changed.AddClassHandler<FileTreeNodeIcon>((icon, _) => icon.UpdateContent());
        IsExpandedProperty.Changed.AddClassHandler<FileTreeNodeIcon>((icon, _) => icon.UpdateContent());
    }

    private void UpdateContent()
    {
        var node = Node;
        if (node?.Backend == null)
        {
            Content = null;
            return;
        }

        var obj = node.Backend;
        switch (obj.Type)
        {
            case Models.FileSystemItemType.File:
                CreateContent("Icons.File", new Thickness(0, 0, 0, 0), Brushes.White);
                break;
            default:
                CreateContent(node.IsExpanded ? "Icons.Folder.Open" : "Icons.Folder", new Thickness(0, 2, 0, 0), Brushes.Goldenrod);
                break;
        }
    }

    private void CreateContent(string iconKey, Thickness margin, IBrush? fill = null)
    {
        StreamGeometry? geometry = this.FindResource(iconKey) as StreamGeometry;
        if (geometry == null)
            return;

        var icon = new Avalonia.Controls.Shapes.Path()
        {
            Width = 14,
            Height = 14,
            Margin = margin,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Data = geometry,
        };

        if (fill != null)
            icon.Fill = fill;

        Content = icon;
    }
}

public class FileRowsListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (SelectedItem is ViewModels.FileTreeNode { IsFolder: true } node && e.KeyModifiers == KeyModifiers.None)
        {
            if ((node.IsExpanded && e.Key == Key.Left) || (!node.IsExpanded && e.Key == Key.Right))
            {
                this.FindAncestorOfType<TreeFileExplorer>()?.ToggleNodeIsExpanded(node);
                e.Handled = true;
            }
        }

        if (!e.Handled)
            base.OnKeyDown(e);
    }
}
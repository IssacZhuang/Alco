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
            var tree = this.FindAncestorOfType<FileTree>();
            tree?.ToggleNodeIsExpanded(node);
        }

        e.Handled = true;
    }
}

public class RevisionTreeNodeIcon : UserControl
{
    public static readonly StyledProperty<ViewModels.FileTreeNode> NodeProperty =
        AvaloniaProperty.Register<RevisionTreeNodeIcon, ViewModels.FileTreeNode>(nameof(Node));

    public ViewModels.FileTreeNode Node
    {
        get => GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<RevisionTreeNodeIcon, bool>(nameof(IsExpanded));

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    static RevisionTreeNodeIcon()
    {
        NodeProperty.Changed.AddClassHandler<RevisionTreeNodeIcon>((icon, _) => icon.UpdateContent());
        IsExpandedProperty.Changed.AddClassHandler<RevisionTreeNodeIcon>((icon, _) => icon.UpdateContent());
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
            case Models.FileSystemItemType.Folder:
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

public class RevisionFileRowsListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (SelectedItem is ViewModels.FileTreeNode { IsFolder: true } node && e.KeyModifiers == KeyModifiers.None)
        {
            if ((node.IsExpanded && e.Key == Key.Left) || (!node.IsExpanded && e.Key == Key.Right))
            {
                this.FindAncestorOfType<FileTree>()?.ToggleNodeIsExpanded(node);
                e.Handled = true;
            }
        }

        if (!e.Handled)
            base.OnKeyDown(e);
    }
}

public partial class FileTree : UserControl
{

    private List<ViewModels.FileTreeNode> _tree = [];
    private AvaloniaList<ViewModels.FileTreeNode> _rows = [];
    private bool _disableSelectionChangingEvent = false;
    private List<ViewModels.FileTreeNode> _searchResult = [];


    public AvaloniaList<ViewModels.FileTreeNode> Rows
    {
        get => _rows;
    }

    public FileTree()
    {
        InitializeComponent();
    }

    public void SetSearchResult(string file)
    {
        _rows.Clear();
        _searchResult.Clear();

        var rows = new List<ViewModels.FileTreeNode>();
        if (string.IsNullOrEmpty(file))
        {
            MakeRows(rows, _tree, 0);
        }
        else
        {
            if (DataContext is not ViewModels.FileTree vm)
                return;

            var objects = vm.GetRevisionFilesUnderFolder(file);
            if (objects == null || objects.Count != 1)
                return;

            var routes = file.Split('/', StringSplitOptions.None);
            if (routes.Length == 1)
            {
                _searchResult.Add(new ViewModels.FileTreeNode
                {
                    Backend = objects[0]
                });
            }
            else
            {
                var last = _searchResult;
                var prefix = string.Empty;
                for (var i = 0; i < routes.Length - 1; i++)
                {
                    var folder = new ViewModels.FileTreeNode
                    {
                        Backend = new Models.FileSystemItem
                        {
                            Type = Models.FileSystemItemType.File,
                            Path = prefix + routes[i],
                        },
                        IsExpanded = true,
                    };

                    last.Add(folder);
                    last = folder.Children;
                    prefix = folder.Backend + "/";
                }

                last.Add(new ViewModels.FileTreeNode
                {
                    Backend = objects[0]
                });
            }

            MakeRows(rows, _searchResult, 0);
        }

        _rows.AddRange(rows);
        GC.Collect();
    }

    public void ToggleNodeIsExpanded(ViewModels.FileTreeNode node)
    {
        _disableSelectionChangingEvent = true;
        node.IsExpanded = !node.IsExpanded;

        var depth = node.Depth;
        var idx = _rows.IndexOf(node);
        if (idx == -1)
            return;

        if (node.IsExpanded)
        {
            var subtree = GetChildrenOfTreeNode(node);
            if (subtree != null && subtree.Count > 0)
            {
                var subrows = new List<ViewModels.FileTreeNode>();
                MakeRows(subrows, subtree, depth + 1);
                _rows.InsertRange(idx + 1, subrows);
            }
        }
        else
        {
            var removeCount = 0;
            for (int i = idx + 1; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Depth <= depth)
                    break;

                removeCount++;
            }
            _rows.RemoveRange(idx + 1, removeCount);
        }

        _disableSelectionChangingEvent = false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _tree.Clear();
        _rows.Clear();
        _searchResult.Clear();

        if (DataContext is not ViewModels.FileTree vm)
        {
            GC.Collect();
            return;
        }

        var objects = vm.GetRevisionFilesUnderFolder(null);
        if (objects == null || objects.Count == 0)
        {
            GC.Collect();
            return;
        }

        foreach (var obj in objects)
            _tree.Add(new ViewModels.FileTreeNode { Backend = obj });

        _tree.Sort((l, r) =>
        {
            if (l.IsFolder == r.IsFolder)
                return string.Compare(l.Name, r.Name, StringComparison.Ordinal);
            return l.IsFolder ? -1 : 1;
        });

        var topTree = new List<ViewModels.FileTreeNode>();
        MakeRows(topTree, _tree, 0);
        _rows.AddRange(topTree);
        GC.Collect();
    }


    private void OnTreeNodeContextRequested(object sender, ContextRequestedEventArgs e)
    {
        if (DataContext is ViewModels.FileTree vm &&
            sender is Grid { DataContext: ViewModels.FileTreeNode { Backend: { } obj } } grid)
        {
            if (obj.Type != Models.FileSystemItemType.File)
            {
                var menu = vm.CreateFileContextMenu(obj);
                menu?.Open(grid);
            }
        }

        e.Handled = true;
    }

    private void OnTreeNodeDoubleTapped(object sender, TappedEventArgs e)
    {
        if (sender is Grid { DataContext: ViewModels.FileTreeNode { IsFolder: true } node })
        {
            var posX = e.GetPosition(this).X;
            if (posX < node.Depth * 16 + 16)
                return;

            ToggleNodeIsExpanded(node);
        }
    }

    private void OnRowsSelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        if (_disableSelectionChangingEvent)
            return;

        if (sender is ListBox { SelectedItem: ViewModels.FileTreeNode node } && DataContext is ViewModels.FileTree vm)
        {
            if (!node.IsFolder)
                vm.OpenFile(node.Backend);
            else
                vm.OpenFile(null);
        }
    }

    private List<ViewModels.FileTreeNode>? GetChildrenOfTreeNode(ViewModels.FileTreeNode node)
    {
        if (!node.IsFolder)
            return null;

        if (node.Children.Count > 0)
            return node.Children;

        if (DataContext is not ViewModels.FileTree vm)
            return null;

        var objects = vm.GetRevisionFilesUnderFolder(node.Backend?.Path);
        if (objects == null || objects.Count == 0)
            return null;

        foreach (var obj in objects)
            node.Children.Add(new ViewModels.FileTreeNode() { Backend = obj });

        node.Children.Sort((l, r) =>
        {
            if (l.IsFolder == r.IsFolder)
                return string.Compare(l.Name, r.Name, StringComparison.Ordinal);
            return l.IsFolder ? -1 : 1;
        });

        return node.Children;
    }

    private static void MakeRows(List<ViewModels.FileTreeNode> rows, List<ViewModels.FileTreeNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            node.Depth = depth;
            rows.Add(node);

            if (!node.IsExpanded || !node.IsFolder)
                continue;

            MakeRows(rows, node.Children, depth + 1);
        }
    }
}


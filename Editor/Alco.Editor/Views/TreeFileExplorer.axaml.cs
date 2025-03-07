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

public partial class TreeFileExplorer : UserControl
{

    private List<ViewModels.FileTreeNode> _tree = [];
    private AvaloniaList<ViewModels.FileTreeNode> _rows = [];
    private bool _disableSelectionChangingEvent = false;
    private List<ViewModels.FileTreeNode> _searchResult = [];


    public AvaloniaList<ViewModels.FileTreeNode> Rows
    {
        get => _rows;
    }

    public TreeFileExplorer()
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
            if (DataContext is not ViewModels.FileExplorer vm)
                return;

            var objects = vm.GetItemsInFolder(file);
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
                        Backend = new Models.ExplorerItem
                        {
                            Type = Models.ExplorerItemType.File,
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

        if (DataContext is not ViewModels.FileExplorer vm)
        {
            GC.Collect();
            return;
        }

        var objects = vm.GetItemsInFolder(null);
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
        if (DataContext is ViewModels.FileExplorer vm &&
            sender is Grid { DataContext: ViewModels.FileTreeNode { Backend: { } obj } } grid)
        {

            var menu = vm.CreateFileContextMenu(obj);
            menu?.Open(grid);
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

        if (sender is ListBox { SelectedItem: ViewModels.FileTreeNode node } && DataContext is ViewModels.FileExplorer vm)
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

        if (DataContext is not ViewModels.FileExplorer vm)
            return null;

        var objects = vm.GetItemsInFolder(node.Backend?.Path);
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


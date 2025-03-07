using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Threading;
using System.Threading.Tasks;

namespace Alco.Editor.Views;

public partial class FlatFileExplorer : UserControl
{

    private readonly List<ViewModels.FileTreeNode> _tree = [];
    private readonly AvaloniaList<ViewModels.FileTreeNode> _rows = [];
    private bool _disableSelectionChangingEvent = false;
    private readonly List<ViewModels.FileTreeNode> _searchResult = [];
    private readonly List<string> _subPaths = [];
    private string _currentPath = "";
    private CancellationTokenSource? _searchCancellationTokenSource;

    public AvaloniaList<ViewModels.FileTreeNode> Rows
    {
        get => _rows;
    }

    public FlatFileExplorer()
    {
        InitializeComponent();
    }

    public void Refresh()
    {
        EnterFolder(_currentPath);
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

        EnterFolder(null);
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
            // var posX = e.GetPosition(this).X;
            // if (posX < node.Depth * 16 + 16)
            //     return;

            // ToggleNodeIsExpanded(node);
            EnterFolder(node.Backend?.Path);
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

    private void EnterFolder(string? path)
    {

        if (DataContext is not ViewModels.FileExplorer vm)
            return;

        _currentPath = path ?? "";
        var objects = vm.GetItemsInFolder(path);
        if (objects == null || objects.Count == 0)
            return;

        _rows.Clear();
        _tree.Clear();
        _searchResult.Clear();

        HandlePath(path);

        foreach (var obj in objects)
            _tree.Add(new ViewModels.FileTreeNode { Backend = obj });

        _tree.Sort((l, r) =>
        {
            if (l.IsFolder == r.IsFolder)
                return string.Compare(l.Name, r.Name, StringComparison.Ordinal);
            return l.IsFolder ? -1 : 1;
        });

        var rows = new List<ViewModels.FileTreeNode>();
        MakeRows(rows, _tree, 0);
        _rows.AddRange(rows);
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

    private void HandlePath(string? path)
    {
        _subPaths.Clear();
        PathStack.Children.Clear();

        var button = new Button { Content = "/" };
        button.Click += (_, _) => EnterFolder(null);
        PathStack.Children.Add(button);

        if (path == null)
        {
            return;
        }

        path = path.Replace("\\", "/");
        _subPaths.AddRange(path.Split('/', StringSplitOptions.None));

        if (_subPaths.Count > 0 && _subPaths.Last() == "")
        {
            _subPaths.RemoveAt(_subPaths.Count - 1);
        }

        for (var i = 0; i < _subPaths.Count; i++)
        {
            string subPath = string.Join("/", _subPaths.Take(i + 1));
            button = new Button { Content = _subPaths[i] };
            button.Click += (_, _) => EnterFolder(subPath);
            PathStack.Children.Add(button);
        }
    }

    private void OnBtnBackClick(object sender, RoutedEventArgs e)
    {
        if (_subPaths.Count == 0)
            return;

        _subPaths.RemoveAt(_subPaths.Count - 1);
        string path = string.Join("/", _subPaths);
        EnterFolder(path);
    }

    private async void OnBtnSearchClick(object sender, RoutedEventArgs e)
    {
        await PerformSearch();
    }

    private async void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformSearch();
            e.Handled = true;
        }
    }

    private async Task PerformSearch()
    {
        if (DataContext is not ViewModels.FileExplorer vm)
            return;

        string? keyword = SearchBox.Text?.Trim();

        if (string.IsNullOrEmpty(keyword))
        {
            Refresh();
            return;
        }

        // Cancel previous search if any
        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource = new CancellationTokenSource();

        try
        {
            var searchResults = await vm.SearchItems(keyword, _searchCancellationTokenSource.Token);

            _rows.Clear();
            _tree.Clear();
            _searchResult.Clear();

            if (searchResults != null && searchResults.Count > 0)
            {
                foreach (var item in searchResults)
                {
                    _searchResult.Add(new ViewModels.FileTreeNode { Backend = item });
                }

                var rows = new List<ViewModels.FileTreeNode>();
                MakeRows(rows, _searchResult, 0);
                _rows.AddRange(rows);
            }
        }
        catch (TaskCanceledException)
        {
            // Search was cancelled, do nothing
        }
        catch (Exception ex)
        {
            // Handle any exceptions
            Console.WriteLine($"Search error: {ex.Message}");
        }
    }
}


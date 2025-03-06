using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.Collections.Generic;

using Alco.Editor.Models;
using Alco.Editor.Attributes;
using Alco.Editor.Utility;
using Avalonia;
using Avalonia.LogicalTree;


namespace Alco.Editor.Views;

public partial class ExplorerPage : UserControl
{
    private readonly ObservableCollection<TreeViewItem> _rootItems;
    
    private ContextMenu? _contextMenu;
    private TreeViewItem? _selectedItem;
    public ViewModels.ExplorerPage ViewModel => DataContext as ViewModels.ExplorerPage ?? throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");

    public ExplorerPage()
    {
        InitializeComponent();
        _rootItems = new ObservableCollection<TreeViewItem>();
        FileTreeView.ItemsSource = _rootItems;
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (!Design.IsDesignMode)
        {
            OnRefreshProjectFiles();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (!Design.IsDesignMode)
        {
            EditorEngine engine = App.Main.Engine;
            engine.OnFilesInProjectUpdated += OnRefreshProjectFiles;
            engine.OnProjectOpened += OnProjectOpened;
            engine.OnProjectClosed += OnProjectClosed;

            FileTreeView.IsVisible = engine.IsProjectOpen;
            NoFolderPanel.IsVisible = !engine.IsProjectOpen;
        }
        base.OnAttachedToVisualTree(e);
        InitializeContextMenu();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (!Design.IsDesignMode)
        {
            EditorEngine engine = App.Main.Engine;
            engine.OnFilesInProjectUpdated -= OnRefreshProjectFiles;
            engine.OnProjectOpened -= OnProjectOpened;
            engine.OnProjectClosed -= OnProjectClosed;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void InitializeContextMenu()
    {
        _contextMenu = new ContextMenu();

        var viewModel = DataContext as ViewModels.ExplorerPage;
        if (viewModel == null) return;

        foreach (var menuItem in viewModel.ContextMenuItemInfos)
        {
            var item = CreateMenuItem(menuItem);
            _contextMenu.Items.Add(item);
        }
        FileTreeView.ContextMenu = _contextMenu;

        _contextMenu.Opening += (sender, e) =>
        {
            var treeViewItem = FindTreeViewItem(FileTreeView.SelectedItem as Control);
            if (treeViewItem != null)
            {
                _selectedItem = treeViewItem;
            }
        };
    }

    private MenuItem CreateMenuItem(TreeItem<MethodInfo?> menuItem)
    {
        var item = new MenuItem { Header = menuItem.Header };
        foreach (var child in menuItem.Child)
        {
            item.Items.Add(CreateMenuItem(child.Value));
        }


        MethodInfo? method = menuItem.UserData;
        if (method != null)
        {
            item.Click += CreateContextMenuItemClickHandler(method);
        }
        return item;
    }

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        await App.Main.ShowOpenProjectDialog();
    }

    private void OnRefreshProjectFiles()
    {
        if (DataContext is not ViewModels.ExplorerPage viewModel)
        {
            throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");
        }

        EditorEngine engine = App.Main.Engine;
        if (!engine.IsProjectOpen)
        {
            _rootItems.Clear();
            return;
        }

        viewModel.RefreshFileNames(engine);
        _rootItems.Clear();
        foreach (var fileName in viewModel.FileNames)
        {
            _rootItems.Add(CreateFileTreeView(fileName));
        }
    }
    private void OnProjectOpened()
    {
        EditorEngine engine = App.Main.Engine;
        OnRefreshProjectFiles();
        FileTreeView.IsVisible = engine.IsProjectOpen;
        NoFolderPanel.IsVisible = !engine.IsProjectOpen;
    }

    private void OnProjectClosed()
    {
        FileTreeView.IsVisible = false;
        NoFolderPanel.IsVisible = true;
    }

    private TreeViewItem CreateFileTreeView(TreeItem<string> treeItem)
    {
        var item = new TreeViewItem
        {
            Header = treeItem.Header,
            Tag = treeItem
        };

        foreach (var child in treeItem.Child)
        {
            item.Items.Add(CreateFileTreeView(child.Value));
        }
        return item;
    }

    private async void OnFileTreeViewDoubleTapped(object? sender, RoutedEventArgs e)
    {
        var treeViewItem = FindTreeViewItem(e.Source as Control);
        if (treeViewItem?.Tag is not TreeItem<string> treeItem) return;

        EditorEngine engine = App.Main.Engine;

        if (treeItem.UserData is string filePath)
        {
            ViewModels.Inspector inspector = await ViewModel.OpenFile(engine, filePath);
            MainContentArea.Content = inspector.CreateControl();
        }
    }

    private static TreeViewItem? FindTreeViewItem(Control? element)
    {
        while (element != null)
        {
            if (element is TreeViewItem item)
            {
                return item;
            }
            element = element.Parent as Control;
        }
        return null;
    }

    private EventHandler<RoutedEventArgs> CreateContextMenuItemClickHandler(MethodInfo method)
    {
        return (s, e) =>
        {
            string localPath = string.Empty;


            if (_selectedItem != null && _selectedItem.Tag is TreeItem<string> treeItem)
            {
                localPath = treeItem.FullPath;
            }

            method.Invoke(null, [localPath]);
        };
    }

}

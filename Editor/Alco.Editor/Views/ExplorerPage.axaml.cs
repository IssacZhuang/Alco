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

namespace Alco.Editor.Views;

public partial class ExplorerPage : UserControl
{
    private readonly ObservableCollection<TreeViewItem> _rootItems;
    
    private ContextMenu? _contextMenu;
    public ViewModels.ExplorerPage ViewModel => DataContext as ViewModels.ExplorerPage ?? throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");

    public ExplorerPage()
    {
        InitializeComponent();
        _rootItems = new ObservableCollection<TreeViewItem>();
        FileTreeView.ItemsSource = _rootItems;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeContextMenu();
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
            item.Click += (s, e) => method.Invoke(null, [item]);
        }
        return item;
    }

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a folder",
            AllowMultiple = false
        });

        EditorEngine engine = this.GetEngine();

        if (folders.Count > 0)
        {
            var folder = folders[0];
            if (folder.TryGetLocalPath() is string path)
            {
                await ViewModel.OpenProject(engine, path);
                RefreshFileTreeView();
                FileTreeView.IsVisible = true;
                NoFolderPanel.IsVisible = false;
            }
        }
    }

    private void RefreshFileTreeView()
    {
        if (DataContext is not ViewModels.ExplorerPage viewModel)
        {
            throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");
        }   

        _rootItems.Clear();
        foreach (var fileName in viewModel.FileNames)
        {
            _rootItems.Add(CreateFileTreeView(fileName));
        }
    }

    private TreeViewItem CreateFileTreeView(TreeItem<string> fileNames)
    {
        var item = new TreeViewItem
        {
            Header = fileNames.Header,
            Tag = fileNames.UserData
        };

        foreach (var child in fileNames.Child)
        {
            item.Items.Add(CreateFileTreeView(child.Value));
        }
        return item;
    }

    private async void OnFileTreeViewDoubleTapped(object? sender, RoutedEventArgs e)
    {
        var treeViewItem = FindTreeViewItem(e.Source as Control);
        if (treeViewItem?.Tag is not string filePath) return;

        EditorEngine engine = this.GetEngine();

        ViewModels.Inspector inspector = await ViewModel.OpenFile(engine, filePath);
        MainContentArea.Content = inspector.CreateControl();
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

}

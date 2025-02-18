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

namespace Alco.Editor.Views;

public partial class ExplorerPage : UserControl
{
    private readonly ObservableCollection<TreeViewItem> _rootItems;
    private readonly List<FileEditorMeta> _fileEditorMetas = new();
    private ContextMenu? _contextMenu;

    public event EventHandler<FileEditor>? FileEditorCreated;

    public ExplorerPage()
    {
        InitializeComponent();
        _rootItems = new ObservableCollection<TreeViewItem>();
        FileTreeView.ItemsSource = _rootItems;
        InitializeContextMenu();
    }

    public ExplorerPage(params FileEditorMeta[] fileEditorMetas) : this()
    {
        _fileEditorMetas.AddRange(fileEditorMetas);
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

    private MenuItem CreateMenuItem(MenuItemInfo menuItem)
    {
        var item = new MenuItem { Header = menuItem.Header };
        foreach (var child in menuItem.Child)
        {
            item.Items.Add(CreateMenuItem(child.Value));
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

        if (folders.Count > 0)
        {
            var folder = folders[0];
            if (folder.TryGetLocalPath() is string path)
            {
                await LoadFolderContents(path);
            }
        }
    }

    private async Task LoadFolderContents(string folderPath)
    {
        // Clear existing items
        _rootItems.Clear();

        // Create root item
        var rootItem = new TreeViewItem
        {
            Header = new DirectoryInfo(folderPath).Name,
            Tag = folderPath
        };

        // Load folder contents
        await LoadDirectoryContents(rootItem, folderPath);

        // Show file tree and hide no folder panel
        _rootItems.Add(rootItem);
        FileTreeView.IsVisible = true;
        NoFolderPanel.IsVisible = false;
    }

    private async Task LoadDirectoryContents(TreeViewItem parentItem, string path)
    {
        try
        {
            var items = new ObservableCollection<TreeViewItem>();
            parentItem.ItemsSource = items;

            // Add directories
            foreach (var dir in Directory.GetDirectories(path))
            {
                var dirInfo = new DirectoryInfo(dir);
                var item = new TreeViewItem
                {
                    Header = dirInfo.Name,
                    Tag = dir
                };
                items.Add(item);

                // Load subdirectories
                await LoadDirectoryContents(item, dir);
            }

            // Add files
            foreach (var file in Directory.GetFiles(path))
            {
                var fileInfo = new FileInfo(file);
                var item = new TreeViewItem
                {
                    Header = fileInfo.Name,
                    Tag = file
                };
                items.Add(item);
            }
        }
        catch (Exception ex)
        {
            // Handle any errors (e.g., access denied)
            var errorItem = new TreeViewItem
            {
                Header = $"Error: {ex.Message}",
                Tag = null
            };
            var items = new ObservableCollection<TreeViewItem> { errorItem };
            parentItem.ItemsSource = items;
        }
    }

    private void OnFileTreeViewDoubleTapped(object? sender, RoutedEventArgs e)
    {
        var treeViewItem = FindTreeViewItem(e.Source as Control);
        if (treeViewItem?.Tag is not string filePath) return;

        OpenFileIfExists(filePath);
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

    private void OpenFileIfExists(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists) return;

        var extension = fileInfo.Extension;
        var supportedEditor = _fileEditorMetas.Find(meta => meta.IsSupported(extension));

        if (supportedEditor != null)
        {
            CleanupCurrentEditor();
            var editor = supportedEditor.CreateInstance();
            SetupEditor(editor);
            editor.OnOpenFile(fileInfo);
            FileEditorCreated?.Invoke(this, editor);
        }
    }

    private void CleanupCurrentEditor()
    {
        // if (_currentEditor != null)
        // {
        //     EditArea.Content = null;
        //     PreviewArea.Content = null;
        //     _currentEditor.OnCloseFile();
        //     _currentEditor = null;
        // }
    }

    private void SetupEditor(FileEditor editor)
    {
        //_currentEditor = editor;
        // EditArea.Content = editor.EditControl;
        // PreviewArea.Content = editor.PreviewControl;
    }
}

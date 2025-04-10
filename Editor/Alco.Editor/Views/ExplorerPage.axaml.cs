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
using Alco.Editor.ViewModels;


namespace Alco.Editor.Views;

/// <summary>
/// A user control that provides a file explorer interface with a file tree view and tabbed document interface.
/// Supports opening multiple files in tabs, file navigation, and project management.
/// </summary>
public partial class ExplorerPage : UserControl
{
    private ObservableCollection<ViewModels.InspectorTabItem> _tabItems = new();

    public ViewModels.ExplorerPage ViewModel => DataContext as ViewModels.ExplorerPage ?? throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");

    public ExplorerPage()
    {
        InitializeComponent();
        if (!Design.IsDesignMode && App.Main.Engine.IsProjectOpen)
        {
            OnProjectOpened();
        }

        DocumentTabs.ItemsSource = _tabItems;

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


    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        await App.Main.ShowOpenProjectDialog();
    }

    private void OnRefreshProjectFiles()
    {
        FileTreeView.Refresh();
    }
    private void OnProjectOpened()
    {
        EditorEngine engine = App.Main.Engine;
        FileTreeView.IsVisible = engine.IsProjectOpen;
        NoFolderPanel.IsVisible = !engine.IsProjectOpen;

        FileSystemExplorer vmFileTree = new FileSystemExplorer(App.Main.Engine.ProjectDirectory!);
        FileTreeView.DataContext = vmFileTree;
        vmFileTree.OnFileTapped += async (file) =>
        {
            if (file == null) return;

            foreach (var vmTabItem in _tabItems)
            {
                if (vmTabItem.Path == file.Path)
                {
                    DocumentTabs.SelectedItem = vmTabItem;
                    return;
                }
            }

            Inspector inspector = await ViewModel.OpenFile(engine, file.Path);

            // Add the new tab and select it
            var tabItem = new ViewModels.InspectorTabItem(inspector, file.Path);
            _tabItems.Add(tabItem);
            DocumentTabs.SelectedItem = tabItem;

            RemoveUnpinnedTabs(tabItem);
        };

        vmFileTree.OnFileDoubleTapped += async (file) =>
        {
            if (file == null) return;

            foreach (var vmTabItem in _tabItems)
            {
                if (vmTabItem.Path == file.Path)
                {
                    vmTabItem.IsPinned = true;
                    return;
                }
            }

            Inspector inspector = await ViewModel.OpenFile(engine, file.Path);

            // Add the new tab and select it
            var tabItem = new ViewModels.InspectorTabItem(inspector, file.Path);
            _tabItems.Add(tabItem);
            DocumentTabs.SelectedItem = tabItem;

            RemoveUnpinnedTabs(tabItem);
        };
    }

    private void OnProjectClosed()
    {
        FileTreeView.IsVisible = false;
        NoFolderPanel.IsVisible = true;
    }

    private void OnBtnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.FindLogicalAncestorOfType<TabItem>() is TabItem tabItem)
        {
            if (tabItem.DataContext is ViewModels.InspectorTabItem tabItemVM)
            {
                _tabItems.Remove(tabItemVM);
            }
        }
    }

    private void RemoveUnpinnedTabs(ViewModels.InspectorTabItem except)
    {
        for (int i = _tabItems.Count - 1; i >= 0; i--)
        {
            var tabItem = _tabItems[i];
            if (tabItem != except && !tabItem.IsPinned)
            {
                _tabItems.RemoveAt(i);
            }
        }
    }
}

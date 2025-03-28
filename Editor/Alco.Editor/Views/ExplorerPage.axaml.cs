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
    
    private ContextMenu? _contextMenu;
    public ViewModels.ExplorerPage ViewModel => DataContext as ViewModels.ExplorerPage ?? throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");

    public ExplorerPage()
    {
        InitializeComponent();
        if (!Design.IsDesignMode && App.Main.Engine.IsProjectOpen)
        {
            OnProjectOpened();
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

        ViewModels.FileSystemExplorer vmFileTree = new ViewModels.FileSystemExplorer(App.Main.Engine.ProjectDirectory!);
        FileTreeView.DataContext = vmFileTree;
        vmFileTree.OnFileOpened += async (file) =>
        {
            if (file == null) return;

            Inspector inspector = await ViewModel.OpenFile(engine, file.Path);

            // Create a new tab item
            var tabItem = new TabItem
            {
                Header = System.IO.Path.GetFileName(file.Path),
                Content = inspector.CreateControl()
            };

            // Add the new tab and select it
            DocumentTabs.Items.Add(tabItem);
            DocumentTabs.SelectedItem = tabItem;
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
            // Get the parent TabControl
            if (tabItem.FindLogicalAncestorOfType<TabControl>() is TabControl tabControl)
            {
                // Remove the tab
                tabControl.Items.Remove(tabItem);

                // You might also need to close the associated document/content
                // Implement additional logic as needed
                // For example, check if the document needs saving before closing
            }
        }
    }
}

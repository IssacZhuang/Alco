using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Alco.Editor.Views
{
    public partial class ExplorerPageView : UserControl
    {
        private readonly ObservableCollection<TreeViewItem> _rootItems;

        public ExplorerPageView()
        {
            InitializeComponent();
            _rootItems = new ObservableCollection<TreeViewItem>();
            FileTreeView.ItemsSource = _rootItems;
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
    }
} 
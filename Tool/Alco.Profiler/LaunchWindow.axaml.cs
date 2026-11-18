using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Alco.Profiler;

/// <summary>Selects a local application or a solution's startup project.</summary>
internal sealed partial class LaunchWindow : Window
{
    private static readonly FilePickerFileType Applications = new("Applications and .NET projects")
    {
        Patterns = ["*.exe", "*.csproj", "*.sln", "*.slnx"],
    };

    private readonly CancellationTokenSource _closing = new();
    private string? _selectedFile;
    private bool _busy;

    /// <summary>Creates the application picker.</summary>
    public LaunchWindow()
    {
        InitializeComponent();
        _selectedFile = LaunchTargets.FindDefaultPath();
        TargetPathBox.Text = _selectedFile;
        ProjectPicker.SelectionChanged += (_, _) => RefreshControls();
        RefreshControls();
    }

    /// <summary>Reports the running target and transfers ownership of its process handle.</summary>
    public event Action<Process, string>? ApplicationLaunched;

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _closing.Cancel();
        _closing.Dispose();
        base.OnClosed(e);
    }

    private async void BrowseClicked(object? sender, RoutedEventArgs e)
    {
        CancellationToken token = _closing.Token;
        SetBusy(true);
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select an executable, project, or solution",
                FileTypeFilter = OperatingSystem.IsWindows() ? [Applications, FilePickerFileTypes.All] : [FilePickerFileTypes.All, Applications],
            });
            if (token.IsCancellationRequested || files.Count == 0 || files[0].TryGetLocalPath() is not string path)
            {
                return;
            }

            _selectedFile = path;
            TargetPathBox.Text = path;
            ProjectPanel.IsVisible = LaunchTargets.IsSolution(path);
            ProjectPicker.ItemsSource = null;
            LaunchStatus.Text = null;
            if (!ProjectPanel.IsVisible)
            {
                return;
            }

            LaunchStatus.Text = "Reading solution projects…";
            var (projects, error) = await LaunchTargets.ReadSolutionAsync(path, token);
            token.ThrowIfCancellationRequested();
            string directory = Path.GetDirectoryName(path)!;
            ProjectPicker.ItemsSource = projects.Select(project => new ComboBoxItem
            {
                Content = Path.GetRelativePath(directory, project),
                Tag = project,
            }).ToArray();
            ProjectPicker.SelectedIndex = projects.Length == 1 ? 0 : -1;
            LaunchStatus.Text = error;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private async void LaunchClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || GetLaunchPath() is not string path)
        {
            return;
        }

        CancellationToken token = _closing.Token;
        SetBusy(true);
        LaunchStatus.Text = LaunchTargets.IsProject(path) ? "Building startup project…" : "Launching application…";
        try
        {
            var (process, error) = await LaunchTargets.StartAsync(path, ProjectPanel.IsVisible ? _selectedFile : null, token);
            if (token.IsCancellationRequested || ApplicationLaunched is not { } onLaunched)
            {
                process?.Dispose();
                return;
            }

            if (process == null)
            {
                LaunchStatus.Text = error;
                return;
            }

            onLaunched(process, Path.GetFileNameWithoutExtension(path));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private string? GetLaunchPath() => ProjectPanel.IsVisible ? (ProjectPicker.SelectedItem as ComboBoxItem)?.Tag as string : _selectedFile;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshControls();
    }

    private void RefreshControls()
    {
        BrowseButton.IsEnabled = !_busy;
        ProjectPicker.IsEnabled = !_busy;
        LaunchButton.IsEnabled = !_busy && GetLaunchPath() != null;
    }
}

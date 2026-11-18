using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Alco.Profiler;

internal sealed partial class MainWindow : Window
{
    private static readonly FilePickerFileType NetTrace = new(".NET traces") { Patterns = ["*.nettrace"] };

    private Process? _target;
    private TraceRecording? _recording;
    private TraceProfile? _profile;
    private ProfileAnalysis? _analysis;
    private TraceRange? _range;
    private HeatmapData? _heatmap;
    private (int? ThreadId, TraceScope Scope) _heatmapFilter;
    private CancellationTokenSource? _analysisCancellation;
    private bool _busy;

    /// <summary>Creates an independent process launcher and trace viewer.</summary>
    public MainWindow()
    {
        InitializeComponent();
        ExecutableBox.Text = FindGameExecutable();
        Heatmap.RangeSelected += RangeSelected;
        RefreshControls();
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_recording != null || _busy)
        {
            e.Cancel = true;
            StatusLabel.Text = _recording != null ? "Stop capture before closing the profiler." : "Wait for the current operation to finish.";
        }
        else
        {
            _analysisCancellation?.Cancel();
            _target?.Dispose();
            _target = null;
        }

        base.OnClosing(e);
    }

    private async void BrowseClicked(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a CoreCLR game executable",
            FileTypeFilter = OperatingSystem.IsWindows()
                ? [new FilePickerFileType("Executables") { Patterns = ["*.exe"] }, FilePickerFileTypes.All]
                : [FilePickerFileTypes.All],
        });
        if (files.Count != 0 && files[0].TryGetLocalPath() is string path)
        {
            ExecutableBox.Text = path;
        }
    }

    private void LaunchClicked(object? sender, RoutedEventArgs e)
    {
        if (_target != null)
        {
            return;
        }

        if (!TryLaunch(ExecutableBox.Text?.Trim(), out Process? process, out string error))
        {
            StatusLabel.Text = error;
            return;
        }

        _target = process;
        TargetLabel.Text = $"{Path.GetFileNameWithoutExtension(process.StartInfo.FileName)} · PID {process.Id}";
        StatusLabel.Text = "Game launched. Start capture once it is ready.";
        RefreshControls();
    }

    private bool TryLaunch(string? path, [NotNullWhen(true)] out Process? process, out string error)
    {
        process = null;
        if (!File.Exists(path))
        {
            error = "Select an existing game executable first.";
            return false;
        }

        string executable = Path.GetFullPath(path);
        var child = new Process
        {
            StartInfo = new ProcessStartInfo(executable, ArgumentsBox.Text ?? string.Empty)
            {
                UseShellExecute = false,
                WorkingDirectory = GetWorkingDirectory(executable),
            },
            EnableRaisingEvents = true,
        };
        child.Exited += TargetExited;
        try
        {
            if (child.Start())
            {
                process = child;
                error = string.Empty;
                return true;
            }

            error = "The executable did not start a process.";
        }
        catch (Win32Exception exception)
        {
            error = $"Could not launch the game: {exception.Message}";
        }

        child.Dispose();
        return false;
    }

    private void TargetExited(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (sender is not Process process || !ReferenceEquals(_target, process))
            {
                return;
            }

            _target = null;
            process.Dispose();
            TargetLabel.Text = "Target exited";
            RefreshControls();
        });
    }

    private async void StartClicked(object? sender, RoutedEventArgs e)
    {
        if (_target is not { HasExited: false } target || _busy || _recording != null)
        {
            return;
        }

        int pid = target.Id;
        _analysisCancellation?.Cancel();
        SetBusy(true);
        StatusLabel.Text = "Connecting to the .NET runtime…";
        try
        {
            var result = await Task.Run(() =>
            {
                TraceRecording.TryStart(pid, out TraceRecording? recording, out string error);
                return (Recording: recording, Error: error);
            });
            if (result.Recording is not TraceRecording recording)
            {
                StatusLabel.Text = $"Could not start capture. Check the target is a running CoreCLR build. {result.Error}";
                return;
            }

            _recording = recording;
            StatusLabel.Text = "Recording managed stacks. Stop capture to analyze the trace.";
            TracePathLabel.Text = recording.FilePath;
        }
        finally
        {
            SetBusy(false);
        }

        if (_recording is TraceRecording active)
        {
            _ = ObserveRecordingAsync(active);
        }
    }

    private async Task ObserveRecordingAsync(TraceRecording recording)
    {
        await recording.Completion.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        if (ReferenceEquals(_recording, recording))
        {
            await FinishRecordingAsync(requestStop: false);
        }
    }

    private async void StopClicked(object? sender, RoutedEventArgs e)
    {
        await FinishRecordingAsync(requestStop: true);
    }

    private async Task FinishRecordingAsync(bool requestStop)
    {
        if (_recording is not TraceRecording recording || _busy)
        {
            return;
        }

        _recording = null;
        SetBusy(true);
        StatusLabel.Text = "Finishing trace and resolving method names…";
        try
        {
            string? warning = await recording.FinishAsync(requestStop);
            if (warning == null && (!requestStop || _target is not { HasExited: false }))
            {
                warning = "The target disconnected. The trace may be incomplete.";
            }

            await LoadTraceAsync(recording.FilePath, warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OpenClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _recording != null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a managed sampling trace",
                FileTypeFilter = [NetTrace],
            });
            if (files.Count != 0 && files[0].TryGetLocalPath() is string path)
            {
                await LoadTraceAsync(path, null);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadTraceAsync(string path, string? warning)
    {
        _analysisCancellation?.Cancel();
        StatusLabel.Text = "Analyzing trace…";
        TracePathLabel.Text = path;
        var result = await Task.Run(() =>
        {
            TraceProfile.TryOpen(path, out TraceProfile? profile, out string error);
            return (Profile: profile, Error: error);
        });
        if (result.Profile is not TraceProfile profile)
        {
            StatusLabel.Text = $"{warning} Could not analyze this trace: {result.Error}".TrimStart();
            return;
        }

        _profile = profile;
        _analysis = null;
        _heatmap = null;
        _range = null;
        ThreadPicker.ItemsSource = new[] { new ThreadChoice(null, "All threads") }.Concat(profile.Threads).ToArray();
        ThreadPicker.SelectedIndex = profile.Threads[0].IsGameLoop ? 1 : 0;
        ScopePicker.SelectedIndex = 0;
        await UpdateAnalysisAsync();

        string note = $"Loaded {profile.SampleCount:N0} samples across {profile.DurationMs / 1000:F2} s.";
        if (profile.SampleIntervalMs is double interval)
        {
            note += $" Estimated {interval:F2} ms per sample.";
        }

        if (profile.LostEvents > 0)
        {
            note += $" Runtime reported {profile.LostEvents:N0} dropped events.";
        }

        StatusLabel.Text = warning == null ? note : $"{warning} {note}";
    }

    private async void FilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!CanFilter())
        {
            return;
        }

        await UpdateAnalysisAsync();
    }

    private async void RangeSelected(TraceRange? range)
    {
        if (!CanFilter())
        {
            return;
        }

        _range = range;
        RefreshControls();
        await UpdateAnalysisAsync();
    }

    private void ResetRangeClicked(object? sender, RoutedEventArgs e)
    {
        RangeSelected(null);
    }

    private async Task UpdateAnalysisAsync()
    {
        if (_profile is not TraceProfile profile)
        {
            return;
        }

        _analysisCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        var filter = (ThreadId: (ThreadPicker.SelectedItem as ThreadChoice)?.Id, Scope: (TraceScope)Math.Max(ScopePicker.SelectedIndex, 0));
        HeatmapData? cachedHeatmap = _heatmapFilter == filter ? _heatmap : null;
        TraceRange? range = _range;
        Heatmap.Show(cachedHeatmap ?? _heatmap, range);
        SelectionLabel.Text = "Updating selection…";
        try
        {
            var result = await Task.Run(() =>
            {
                HeatmapData heatmap = cachedHeatmap ?? profile.BuildHeatmap(filter.ThreadId, filter.Scope, token);
                ProfileAnalysis analysis = profile.Analyze(filter.ThreadId, filter.Scope, range, token);
                return (Heatmap: heatmap, Analysis: analysis);
            }, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _heatmap = result.Heatmap;
            _heatmapFilter = filter;
            _analysis = result.Analysis;
            Heatmap.Show(_heatmap, range);
            FlameGraph.Show(_analysis.Root);
            CallTree.ItemsSource = _analysis.Root?.Children;
            ApplyMethodFilter();

            string selection = range is TraceRange span ? $"{span.StartMs / 1000:F3}–{span.EndMs / 1000:F3} s" : "All time";
            SelectionLabel.Text = $"{selection} · {_analysis.SampleCount:N0} stack samples · {_analysis.Methods.Length:N0} methods";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_analysisCancellation, cancellation))
            {
                _analysisCancellation = null;
            }
        }
    }

    private void MethodFilterChanged(object? sender, RoutedEventArgs e)
    {
        ApplyMethodFilter();
    }

    private void ApplyMethodFilter()
    {
        if (_analysis is not ProfileAnalysis analysis)
        {
            return;
        }

        string? query = SearchBox.Text?.Trim();
        IEnumerable<MethodSummary> methods = analysis.Methods;
        if (!string.IsNullOrEmpty(query))
        {
            methods = methods.Where(method => method.Method.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<MethodSummary> ordered = SortPicker.SelectedIndex == 1
            ? methods.OrderByDescending(method => method.SelfSamples).ThenByDescending(method => method.TotalSamples)
            : methods.OrderByDescending(method => method.TotalSamples).ThenByDescending(method => method.SelfSamples);
        MethodsList.ItemsSource = ordered.ThenBy(method => method.Method, StringComparer.Ordinal).ToArray();
    }

    private bool CanFilter()
    {
        return _profile != null && !_busy && _recording == null;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshControls();
    }

    private void RefreshControls()
    {
        bool idle = !_busy && _recording == null;
        bool running = _target is { HasExited: false };
        LaunchButton.IsEnabled = idle && _target == null;
        BrowseButton.IsEnabled = LaunchButton.IsEnabled;
        ExecutableBox.IsEnabled = LaunchButton.IsEnabled;
        ArgumentsBox.IsEnabled = LaunchButton.IsEnabled;
        StartButton.IsEnabled = idle && running;
        StopButton.IsEnabled = !_busy && _recording != null;
        OpenButton.IsEnabled = idle;
        HeatmapPanel.IsEnabled = CanFilter();
        ResetRangeButton.IsEnabled = _range.HasValue;
    }

    private static string? FindGameExecutable()
    {
        string? root = FindGameRoot(Environment.CurrentDirectory) ?? FindGameRoot(AppContext.BaseDirectory);
        if (root == null)
        {
            return null;
        }

        string executable = OperatingSystem.IsWindows() ? "Application.exe" : "Application";
        string rid = RuntimeInformation.RuntimeIdentifier;
        string[] directories =
        [
            Path.Combine(root, "Bin", "Release", "Publish", rid),
            Path.Combine(root, "Bin", "Debug", "Application", rid),
            Path.Combine(root, "Bin", "Debug", "Application"),
            Path.Combine(root, "Bin", "Release", "Application", rid),
            Path.Combine(root, "Bin", "Release", "Application"),
        ];

        return directories.Select(directory => Path.Combine(directory, executable)).FirstOrDefault(File.Exists);
    }

    private static string GetWorkingDirectory(string executable)
    {
        string directory = Path.GetDirectoryName(executable)!;
        string? root = FindGameRoot(directory);
        if (root == null)
        {
            return directory;
        }

        string relative = Path.GetRelativePath(Path.Combine(root, "Bin"), directory);
        return relative.Split(Path.DirectorySeparatorChar) is ["Debug" or "Release", "Application", ..] ? root : directory;
    }

    private static string? FindGameRoot(string path)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Game.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}

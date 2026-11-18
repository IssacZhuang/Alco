using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Alco.Profiler;

/// <summary>Displays the capture session and analyzed traces for a launched application.</summary>
internal sealed partial class MainWindow : Window
{
    private static readonly FilePickerFileType NetTrace = new(".NET traces") { Patterns = ["*.nettrace"] };

    private readonly string _targetName;
    private readonly bool _overlaySupported = CaptureOverlay.IsSupported;
    private Process? _target;
    private CaptureOverlay? _overlay;
    private TraceRecording? _recording;
    private TraceProfile? _profile;
    private ProfileAnalysis? _analysis;
    private TraceRange? _range;
    private HeatmapData? _heatmap;
    private (int? ThreadId, TraceScope Scope) _heatmapFilter;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _runtimeWait;
    private string _overlayStatus = "Ready to capture";
    private bool _runtimeReady;
    private bool _busy;

    /// <summary>Opens an analysis session that owns the target's process handle.</summary>
    public MainWindow(Process target, string name)
    {
        _target = target;
        _targetName = name;
        InitializeComponent();
        Heatmap.RangeSelected += RangeSelected;
        TargetLabel.Text = $"{name} · PID {target.Id}";
        StatusLabel.Text = "Waiting for .NET diagnostics…";
        target.Exited += TargetExited;
        target.EnableRaisingEvents = true;
        RefreshControls();
    }

    /// <summary>Requests a return to the app picker without stopping the target.</summary>
    public event Action? ChooseApplicationRequested;

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_target is { HasExited: false } target)
        {
            _ = PrepareCaptureAsync(target);
        }
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_recording != null || _busy)
        {
            e.Cancel = true;
            StatusLabel.Text = _recording != null ? "Stop capture before closing the profiler." : "Wait for the current operation to finish.";
        }

        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _runtimeWait?.Cancel();
        _analysisCancellation?.Cancel();
        if (_target is { } target)
        {
            target.Exited -= TargetExited;
            target.Dispose();
            _target = null;
        }
        CloseOverlay();
        base.OnClosed(e);
    }

    private void ChooseAppClicked(object? sender, RoutedEventArgs e)
    {
        if (!_busy && _recording == null)
        {
            ChooseApplicationRequested?.Invoke();
        }
    }

    private async Task PrepareCaptureAsync(Process target)
    {
        using var cancellation = new CancellationTokenSource();
        _runtimeWait = cancellation;
        RefreshControls();
        string? error = await TraceRecording.WaitForRuntimeAsync(target.Id, cancellation.Token);
        _runtimeWait = null;
        if (cancellation.IsCancellationRequested || !ReferenceEquals(_target, target) || target.HasExited)
        {
            return;
        }

        _runtimeReady = error == null;
        StatusLabel.Text = error ?? (_overlaySupported ? "Ready to capture. Use the overlay while playing." : "Ready to capture using the analysis controls.");
        RefreshControls();
        if (_runtimeReady)
        {
            ShowOverlay();
        }
    }

    private void TargetExited(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (sender is not Process process || !ReferenceEquals(_target, process))
            {
                return;
            }

            _runtimeWait?.Cancel();
            _target = null;
            _runtimeReady = false;
            process.Dispose();
            CloseOverlay();
            TargetLabel.Text = $"{_targetName} exited";
            if (_recording == null && !_busy && _profile == null)
            {
                StatusLabel.Text = "The application exited before a capture was loaded.";
            }
            RefreshControls();
        });
    }

    private async void StartClicked(object? sender, RoutedEventArgs e) => await StartCaptureAsync();

    private async Task StartCaptureAsync()
    {
        if (_target is not { HasExited: false } target || _busy || _recording != null || _runtimeWait != null)
        {
            return;
        }

        int pid = target.Id;
        bool showOverlay = !_runtimeReady;
        _analysisCancellation?.Cancel();
        _overlayStatus = "Connecting…";
        StatusLabel.Text = "Connecting to the .NET runtime…";
        SetBusy(true);
        try
        {
            var result = await Task.Run(() =>
            {
                TraceRecording.TryStart(pid, out TraceRecording? recording, out string error);
                return (Recording: recording, Error: error);
            });
            if (result.Recording is not TraceRecording recording)
            {
                _overlayStatus = "Could not start capture";
                StatusLabel.Text = $"Could not start capture. Check the target is a running CoreCLR build. {result.Error}";
                return;
            }

            _runtimeReady = true;
            _recording = recording;
            _overlayStatus = "Recording";
            StatusLabel.Text = "Recording managed stacks. Stop capture to analyze the trace.";
            TracePathLabel.Text = recording.FilePath;
        }
        finally
        {
            SetBusy(false);
        }

        if (showOverlay)
        {
            ShowOverlay();
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
        _overlayStatus = "Finalizing trace…";
        StatusLabel.Text = "Finishing trace and resolving method names…";
        SetBusy(true);
        try
        {
            string? warning = await recording.FinishAsync(requestStop);
            if (warning == null && (!requestStop || _target is not { HasExited: false }))
            {
                warning = "The target disconnected. The trace may be incomplete.";
            }

            bool loaded = await TryLoadTraceAsync(recording.FilePath, warning);
            _overlayStatus = !loaded ? "Could not analyze capture" : warning == null ? "Capture complete" : "Capture may be incomplete";
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
                await TryLoadTraceAsync(path, null);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> TryLoadTraceAsync(string path, string? warning)
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
            return false;
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
        return true;
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
        ChooseAppButton.IsEnabled = idle;
        bool canStart = idle && running && _runtimeWait == null;
        bool canStop = !_busy && _recording != null;
        StartButton.IsEnabled = canStart;
        StopButton.IsEnabled = canStop;
        ShowOverlayButton.IsEnabled = _overlaySupported && running && _runtimeReady && _overlay == null;
        OpenButton.IsEnabled = idle;
        HeatmapPanel.IsEnabled = CanFilter();
        ResetRangeButton.IsEnabled = _range.HasValue;
        _overlay?.UpdateCaptureControls(canStart, canStop, _overlayStatus, StatusLabel.Text);
    }

    private void ShowOverlayClicked(object? sender, RoutedEventArgs e) => ShowOverlay();

    private void ShowOverlay()
    {
        if (!_overlaySupported || _overlay != null || !_runtimeReady || _target is not { HasExited: false })
        {
            return;
        }

        var overlay = new CaptureOverlay(_targetName, StartCaptureAsync, () => FinishRecordingAsync(requestStop: true));
        _overlay = overlay;
        overlay.Closed += (_, _) =>
        {
            _overlay = null;
            RefreshControls();
        };
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
        {
            overlay.PlaceOn(screen);
        }
        RefreshControls();
        overlay.Show();
    }

    private void CloseOverlay() => _overlay?.Close();
}

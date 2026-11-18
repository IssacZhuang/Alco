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
    private string? _profilePath;
    private ProfileAnalysis? _analysis;
    private HeatmapData? _heatmap;
    private (int First, int Last)? _selectedColumns;
    private int _slicePage;
    private (int? ThreadId, TraceScope Scope, TraceSliceMode Mode, int Page) _heatmapFilter;
    private (int? ThreadId, TraceScope Scope, TraceRange? Range) _analysisFilter;
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
        Heatmap.ColumnsSelected += ColumnsSelected;
        TargetLabel.Text = $"{name} · PID {target.Id}";
        StatusLabel.Text = "Waiting for .NET diagnostics…";
        target.Exited += TargetExited;
        target.EnableRaisingEvents = true;
        RefreshControls();
    }

    /// <summary>Requests a return to the app picker without stopping the target.</summary>
    public event Action? ChooseApplicationRequested;

    /// <summary>Gets the most recently loaded trace, which can be analyzed off the UI thread.</summary>
    public TraceProfile? LoadedProfile => _profile;

    /// <summary>Reports the current target, recording and loaded trace.</summary>
    public object GetAgentStatus() => new
    {
        stage = "analysis",
        targetName = _targetName,
        pid = _target is { HasExited: false } target ? (int?)target.Id : null,
        diagnosticsReady = _runtimeReady && _target is { HasExited: false },
        waitingForDiagnostics = _runtimeWait != null,
        isRecording = _recording != null,
        busy = _busy,
        message = StatusLabel.Text,
        capturePath = _recording?.FilePath,
        trace = _profile is not { } profile ? null : new
        {
            filePath = _profilePath,
            profile.DurationMs,
            profile.SampleCount,
            profile.SampleIntervalMs,
            profile.LostEvents,
            tickCount = profile.GetSliceCount(TraceSliceMode.Tick),
            updateCount = profile.GetSliceCount(TraceSliceMode.Update),
            profile.Threads,
        },
    };

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

    /// <summary>Starts recording and returns the trace path or an explanation of why capture could not start.</summary>
    public async Task<(string? FilePath, string? Error)> StartCaptureAsync()
    {
        if (_target is not { HasExited: false } target)
        {
            return (null, "The target is no longer running.");
        }

        if (_recording != null || _busy || _runtimeWait != null)
        {
            return (null, "Wait for diagnostics or the current capture operation to finish.");
        }

        int pid = target.Id;
        bool showOverlay = !_runtimeReady;
        _analysisCancellation?.Cancel();
        _overlayStatus = "Connecting…";
        StatusLabel.Text = "Connecting to the .NET runtime…";
        SetBusy(true);
        TraceRecording active;
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
                string error = $"Could not start capture. Check the target is a running CoreCLR build. {result.Error}";
                StatusLabel.Text = error;
                return (null, error);
            }

            _runtimeReady = true;
            active = _recording = recording;
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

        _ = ObserveRecordingAsync(active);
        return (active.FilePath, null);
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

    /// <summary>Finalizes the active capture and reports its path, import outcome and any warning.</summary>
    public async Task<(string? FilePath, bool Loaded, string? Message)> FinishRecordingAsync(bool requestStop)
    {
        if (_recording is not TraceRecording recording || _busy)
        {
            return (null, false, "There is no active recording to stop.");
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
            return (recording.FilePath, loaded, loaded ? warning : StatusLabel.Text);
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
        _profilePath = path;
        _analysis = null;
        _heatmap = null;
        _selectedColumns = null;
        _slicePage = 0;
        TickSlicesItem.IsEnabled = profile.GetSliceCount(TraceSliceMode.Tick) > 0;
        UpdateSlicesItem.IsEnabled = profile.GetSliceCount(TraceSliceMode.Update) > 0;
        if (profile.GetSliceCount(SliceMode) == 0)
        {
            SlicePicker.SelectedIndex = 0;
        }

        ThreadPicker.ItemsSource = new[] { new ThreadChoice(null, "All threads") }.Concat(profile.Threads).ToArray();
        ThreadPicker.SelectedIndex = profile.Threads.FirstOrDefault()?.IsGameLoop == true ? 1 : 0;
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

    private TraceSliceMode SliceMode => (TraceSliceMode)Math.Max(SlicePicker.SelectedIndex, 0);

    private async void SliceModeChanged(object? sender, SelectionChangedEventArgs e) => await ShowSlicePageAsync(0);

    private async void PreviousSlicePageClicked(object? sender, RoutedEventArgs e) => await ShowSlicePageAsync(_slicePage - 1);

    private async void NextSlicePageClicked(object? sender, RoutedEventArgs e) => await ShowSlicePageAsync(_slicePage + 1);

    private async Task ShowSlicePageAsync(int page)
    {
        if (!CanFilter() || _profile is not { } profile || page < 0 || page >= profile.GetPageCount(SliceMode))
        {
            return;
        }

        _slicePage = page;
        _selectedColumns = null;
        RefreshControls();
        await UpdateAnalysisAsync();
    }

    private async void ColumnsSelected((int First, int Last)? selection)
    {
        if (!CanFilter())
        {
            return;
        }

        _selectedColumns = selection;
        RefreshControls();
        await UpdateAnalysisAsync();
    }

    private void ResetRangeClicked(object? sender, RoutedEventArgs e) => ColumnsSelected(null);

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
        var filter = (ThreadId: (ThreadPicker.SelectedItem as ThreadChoice)?.Id,
            Scope: (TraceScope)Math.Max(ScopePicker.SelectedIndex, 0), Mode: SliceMode, Page: _slicePage);
        HeatmapData? cachedHeatmap = _heatmapFilter == filter ? _heatmap : null;
        ProfileAnalysis? cachedAnalysis = _analysis;
        var cachedAnalysisFilter = _analysisFilter;
        var columns = _selectedColumns;
        bool samePage = _heatmapFilter.Mode == filter.Mode && _heatmapFilter.Page == filter.Page;
        Heatmap.Show(samePage ? _heatmap : null, columns);
        SelectionLabel.Text = "Updating selection…";
        try
        {
            var result = await Task.Run(() =>
            {
                HeatmapData heatmap = cachedHeatmap ?? profile.BuildHeatmap(filter.ThreadId, filter.Scope, filter.Mode, filter.Page, token);
                TraceRange? range = columns is { } selected
                    ? new TraceRange(heatmap.Slices[selected.First].Range.StartMs, heatmap.Slices[selected.Last].Range.EndMs) : null;
                var analysisFilter = (filter.ThreadId, filter.Scope, Range: range);
                ProfileAnalysis analysis = cachedAnalysis != null && cachedAnalysisFilter == analysisFilter
                    ? cachedAnalysis : profile.Analyze(filter.ThreadId, filter.Scope, range, token);
                return (Heatmap: heatmap, Analysis: analysis, Filter: analysisFilter);
            }, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _heatmap = result.Heatmap;
            _heatmapFilter = filter;
            _analysisFilter = result.Filter;
            Heatmap.Show(_heatmap, columns);
            if (!ReferenceEquals(_analysis, result.Analysis))
            {
                _analysis = result.Analysis;
                FlameGraph.Show(_analysis.Root);
                CallTree.ItemsSource = _analysis.Root?.Children;
                ApplyMethodFilter();
            }

            string selection = DescribeSelection(result.Heatmap, columns, result.Filter.Range);
            SelectionLabel.Text = $"{selection} · {result.Analysis.SampleCount:N0} stack samples · {result.Analysis.Methods.Length:N0} methods";
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

    private static string DescribeSelection(HeatmapData heatmap, (int First, int Last)? columns, TraceRange? range)
    {
        if (range is not { } span)
        {
            return "All time";
        }

        if (heatmap.IsPerInvocation && columns is { } selected)
        {
            string kind = heatmap.Mode == TraceSliceMode.Tick ? "Tick" : "Update";
            long sequence = heatmap.Slices[selected.First].Sequence;
            return $"{kind} #{sequence:N0} · {span.StartMs:0.######}–{span.EndMs:0.######} ms · {span.EndMs - span.StartMs:0.######} ms elapsed";
        }

        return $"{span.StartMs / 1000:F3}–{span.EndMs / 1000:F3} s";
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

        MethodsList.ItemsSource = MethodSummary.OrderByCost(methods, sortBySelf: SortPicker.SelectedIndex == 1).ToArray();
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
        ResetRangeButton.IsEnabled = _selectedColumns.HasValue;
        TraceSliceMode mode = SliceMode;
        int pages = _profile?.GetPageCount(mode) ?? 0;
        SlicePagePanel.IsVisible = pages > 0 && mode != TraceSliceMode.Overview;
        PreviousSlicePageButton.IsEnabled = _slicePage > 0;
        NextSlicePageButton.IsEnabled = _slicePage + 1 < pages;
        SlicePageLabel.Text = $"Page {_slicePage + 1:N0} / {pages:N0} · {_profile?.GetSliceCount(mode) ?? 0:N0} slices";
        SliceHint.Text = mode is TraceSliceMode.Tick or TraceSliceMode.Update
            ? "One invocation per column · hover for elapsed time"
            : _profile != null && (!TickSlicesItem.IsEnabled || !UpdateSlicesItem.IsEnabled)
                ? "Tick / Update slices require complete timing markers in the trace."
                : null;
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

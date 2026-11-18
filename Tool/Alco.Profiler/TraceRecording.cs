using System.Diagnostics.Tracing;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Diagnostics.NETCore.Client;

namespace Alco.Profiler;

internal sealed class TraceRecording
{
    private const int CircularBufferMb = 64;
    private const int FileBufferBytes = 64 * 1024;

    private static readonly EventPipeProvider[] Providers =
    [
        new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
        new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x100003801DL),
        new(TraceProfile.LoopTimingProviderName, EventLevel.Informational),
    ];

    private readonly EventPipeSession _session;

    private TraceRecording(EventPipeSession session, FileStream file)
    {
        _session = session;
        FilePath = file.Name;
        Completion = WriteTraceAsync(session.EventStream, file);
    }

    /// <summary>Gets the path of the raw trace file.</summary>
    public string FilePath { get; }

    /// <summary>Gets the task that drains the runtime stream to disk.</summary>
    public Task Completion { get; }

    /// <summary>Waits for the target's diagnostic endpoint and returns a message if it is unavailable.</summary>
    public static async Task<string?> WaitForRuntimeAsync(int pid, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            while (!DiagnosticsClient.GetPublishedProcesses().Contains(pid))
            {
                await Task.Delay(200, timeout.Token).ConfigureAwait(false);
            }
            return null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return "No .NET diagnostic endpoint responded. Use a CoreCLR app with diagnostics enabled.";
        }
        catch (Exception exception) when (IsRecordingFailure(exception))
        {
            return $"Could not connect to the .NET runtime: {exception.Message}";
        }
    }

    /// <summary>Starts a recording on a running CoreCLR process.</summary>
    public static bool TryStart(int pid, [NotNullWhen(true)] out TraceRecording? recording, out string error)
    {
        FileStream? file = null;
        EventPipeSession? session = null;
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Alco", "Profiler", "Captures");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{pid}-{Guid.NewGuid():N}.nettrace");
            file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, FileBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var client = new DiagnosticsClient(pid);
            session = client.StartEventPipeSessionAsync(Providers, requestRundown: true, CircularBufferMb, timeout.Token)
                .GetAwaiter().GetResult();
            recording = new TraceRecording(session, file);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsRecordingFailure(exception))
        {
            session?.Dispose();
            if (file != null)
            {
                file.Dispose();
                try
                {
                    File.Delete(file.Name);
                }
                catch (Exception cleanupError) when (IsRecordingFailure(cleanupError))
                {
                }
            }

            recording = null;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Finishes the trace without stopping the target and returns any recording warning.</summary>
    public async Task<string?> FinishAsync(bool requestStop)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string? warning = null;
        try
        {
            if (requestStop && !Completion.IsCompleted)
            {
                await _session.StopAsync(timeout.Token).ConfigureAwait(false);
            }

            await Completion.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecordingFailure(exception))
        {
            warning = exception is OperationCanceledException
                ? "Timed out finishing the trace. The file may be incomplete."
                : $"The trace may be incomplete: {exception.Message}";
        }
        finally
        {
            _session.Dispose();
        }

        if (warning != null)
        {
            try
            {
                await Completion.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsRecordingFailure(exception))
            {
            }
        }

        return warning;
    }

    private static async Task WriteTraceAsync(Stream source, FileStream file)
    {
        await using (file.ConfigureAwait(false))
        {
            await source.CopyToAsync(file, FileBufferBytes).ConfigureAwait(false);
        }
    }

    private static bool IsRecordingFailure(Exception exception)
    {
        return exception is DiagnosticsClientException or IOException or UnauthorizedAccessException
            or ObjectDisposedException or OperationCanceledException or TimeoutException;
    }
}

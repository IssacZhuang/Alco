using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Alco.AgentControlProtocol;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Alco.Profiler;

/// <summary>Hosts the profiler's tools on an opt-in local Agent Control endpoint.</summary>
internal sealed class ProfilerAgentControl : IDisposable
{
    private readonly WebApplication _server;

    private ProfilerAgentControl(WebApplication server) => _server = server;

    /// <summary>Binds the local endpoint or reports why it could not start.</summary>
    public static bool TryStart(IClassicDesktopStyleApplicationLifetime desktop, int port, string token,
        [NotNullWhen(true)] out ProfilerAgentControl? control, out string error)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
            options.Limits.MaxRequestBodySize = 16 * 1024;
        });
        var server = builder.Build();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var registry = new ToolRegistry([], [new ProfilerTools(desktop, server.Lifetime.ApplicationStopping)], json);
        byte[] credential = Encoding.UTF8.GetBytes(token);
        server.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            string host = context.Request.Host.Host;
            if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)
                || (host != "127.0.0.1" && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            const string scheme = "Bearer ";
            string authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
                || !CryptographicOperations.FixedTimeEquals(credential, Encoding.UTF8.GetBytes(authorization[scheme.Length..])))
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        server.MapToolApi(registry, "Alco Profiler API", allowGetInvocation: false);
        try
        {
            Task.Run(() => server.StartAsync()).GetAwaiter().GetResult();
            control = new ProfilerAgentControl(server);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or SocketException)
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            control = null;
            error = $"Could not start Profiler Agent Control on 127.0.0.1:{port}: {exception.Message}";
            return false;
        }
    }

    /// <summary>Stops serving tools when the profiler closes.</summary>
    public void Dispose()
    {
        _server.Lifetime.StopApplication();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            Task.Run(() => _server.StopAsync(timeout.Token)).GetAwaiter().GetResult();
        }
        finally
        {
            _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class ProfilerTools(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken stopping)
    {
        /// <summary>Reports the profiler stage, diagnostic readiness and loaded trace.</summary>
        [AgentFunction(IsOnAgentThread = true)]
        [Description("Read the launcher or analysis state, target PID, capture state and trace metadata.")]
        public Task<object> GetProfilerStatus() => ReadOnUiAsync<object>(() => desktop.MainWindow switch
        {
            LaunchWindow launcher => launcher.GetAgentStatus(),
            MainWindow analysis => analysis.GetAgentStatus(),
            _ => new { stage = "closed" },
        });

        /// <summary>Starts an application while the profiler is on the launcher.</summary>
        [AgentFunction(IsOnAgentThread = true)]
        [Description("Launch an executable, C# project or solution from the profiler's application picker.")]
        public Task<object> LaunchApplication(
            [Description("Executable, .csproj, .sln or .slnx path.")] string path,
            [Description("Startup .csproj, absolute or relative to the solution directory.")] string? startupProject = null)
            => RunOnUiAsync<object>(async () =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Failure("Specify a target path.");
                }

                if (desktop.MainWindow is not LaunchWindow launcher)
                {
                    return Failure("Use Choose app… to return to the application picker before launching another target.");
                }

                string? error = await launcher.LaunchAsync(path, startupProject);
                return error != null ? Failure(error) : new { ok = true, session = ((MainWindow)desktop.MainWindow!).GetAgentStatus() };
            });

        /// <summary>Starts the shared EventPipe recording.</summary>
        [AgentFunction(IsOnAgentThread = true)]
        [Description("Start collecting managed stacks for the launched application. Returns the raw trace path.")]
        public Task<object> StartCapture() => RunOnUiAsync<object>(async () =>
        {
            if (desktop.MainWindow is not MainWindow analysis)
            {
                return Failure("Launch an application first.");
            }

            var (filePath, error) = await analysis.StartCaptureAsync();
            return error != null ? Failure(error) : new { ok = true, filePath };
        });

        /// <summary>Stops the recording and loads it into the analysis window.</summary>
        [AgentFunction(IsOnAgentThread = true)]
        [Description("Stop collecting without stopping the target, then import the resulting trace.")]
        public Task<object> StopCapture() => RunOnUiAsync<object>(async () =>
        {
            if (desktop.MainWindow is not MainWindow analysis)
            {
                return Failure("Launch an application first.");
            }

            var (filePath, loaded, message) = await analysis.FinishRecordingAsync(requestStop: true);
            return loaded ? new { ok = true, filePath, warning = message } : new { ok = false, filePath, error = message };
        });

        /// <summary>Returns sampled method costs for the requested subset of the loaded trace.</summary>
        [AgentFunction(IsOnAgentThread = true)]
        [Description("Rank sampled methods in the loaded trace. Times are sampling estimates, not per-call timings.")]
        public async Task<object> GetTopMethods(
            [Description("Stack ancestry filter: all, tick, update, or gameLoop.")] string scope = "all",
            [Description("Maximum methods to return (1–100).")] int limit = 20,
            [Description("Rank by self cost instead of inclusive cost.")] bool sortBySelf = false,
            [Description("Optional thread ID from GetProfilerStatus.")] int? threadId = null,
            [Description("Optional inclusive start in relative milliseconds.")] double? startMs = null,
            [Description("Optional exclusive end in relative milliseconds.")] double? endMs = null)
        {
            TraceScope? filter = scope?.ToLowerInvariant() switch
            {
                "all" => TraceScope.All,
                "tick" => TraceScope.Tick,
                "update" => TraceScope.Update,
                "gameloop" => TraceScope.GameLoop,
                _ => null,
            };
            if (filter == null || limit is < 1 or > 100)
            {
                return Failure("Use scope all, tick, update, or gameLoop and a method limit from 1 to 100.");
            }

            TraceProfile? profile = await ReadOnUiAsync(() => (desktop.MainWindow as MainWindow)?.LoadedProfile).ConfigureAwait(false);
            if (profile == null)
            {
                return Failure("There is no loaded trace.");
            }

            if (threadId.HasValue && !profile.Threads.Any(thread => thread.Id == threadId))
            {
                return Failure("Use a thread ID from the loaded trace.");
            }

            double start = startMs ?? 0;
            double end = endMs ?? profile.DurationMs;
            if (!(start >= 0 && end > start && end <= profile.DurationMs))
            {
                return Failure("The requested range must have positive length within the loaded trace.");
            }

            TraceRange? range = startMs.HasValue || endMs.HasValue ? new TraceRange(start, end) : null;
            ProfileAnalysis analysis = profile.Analyze(threadId, filter.Value, range, stopping);
            var methods = MethodSummary.OrderByCost(analysis.Methods, sortBySelf).Take(limit)
                .Select(method => new { method.Method, method.TotalSamples, method.SelfSamples, method.TotalMs, method.SelfMs }).ToArray();
            return new { ok = true, scope, threadId, startMs = start, endMs = end, analysis.SampleCount, methods };
        }

        private async Task<T> ReadOnUiAsync<T>(Func<T> read)
            => await Dispatcher.UIThread.InvokeAsync(read, DispatcherPriority.Normal, stopping);

        private async Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
        {
            Task<T> operation = await ReadOnUiAsync(action).ConfigureAwait(false);
            return await operation.WaitAsync(stopping).ConfigureAwait(false);
        }

        private static object Failure(string error) => new { ok = false, error };
    }
}

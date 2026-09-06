using System;
using System.Text.Json;
using Alco.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Alco.AgentControlProtocol;

/// <summary>
/// Manages the Kestrel HTTP API server lifecycle.
/// Embeds an ASP.NET Minimal API server within the game process
/// to expose tool functions to external AI agents.
/// </summary>
public sealed class GameApiServer : IDisposable
{
    private WebApplication? _app;
    private readonly ToolRegistry _registry;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _port;

    /// <summary>
    /// Gets whether the server is currently running.
    /// </summary>
    public bool IsRunning => _app != null;

    /// <summary>
    /// Gets the port the server is listening on.
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameApiServer"/> class.
    /// </summary>
    /// <param name="registry">The tool registry to expose via HTTP.</param>
    /// <param name="jsonOptions">The JSON serializer options for HTTP response serialization.</param>
    /// <param name="port">The port to listen on.</param>
    public GameApiServer(ToolRegistry registry, JsonSerializerOptions jsonOptions, int port = 52100)
    {
        _registry = registry;
        _jsonOptions = jsonOptions;
        _port = port;
    }

    /// <summary>
    /// Starts the HTTP API server.
    /// </summary>
    /// <param name="configureEndpoints">
    /// Optional callback invoked after the tool API is mapped and before the server starts,
    /// allowing the host to register additional endpoints on the same application.
    /// </param>
    public void Start(Action<WebApplication>? configureEndpoints = null)
    {
        if (_app != null) return;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{_port}");

        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = _jsonOptions.PropertyNamingPolicy;
            foreach (var converter in _jsonOptions.Converters)
            {
                options.SerializerOptions.Converters.Add(converter);
            }
        });

        var app = builder.Build();
        app.MapToolApi(_registry);
        configureEndpoints?.Invoke(app);

        _app = app;
        _ = app.RunAsync();

        Log.Info($"Game API server started on http://localhost:{_port}");
    }

    /// <summary>
    /// Stops the HTTP API server.
    /// </summary>
    public void Stop()
    {
        if (_app == null) return;

        try
        {
            _app.DisposeAsync().GetAwaiter().GetResult();
            Log.Info("Game API server stopped.");
        }
        catch (Exception ex)
        {
            Log.Error($"Error stopping API server: {ex.Message}");
        }
        finally
        {
            _app = null;
        }
    }

    /// <summary>
    /// Disposes the server, stopping it if running.
    /// </summary>
    public void Dispose()
    {
        Stop();
    }
}

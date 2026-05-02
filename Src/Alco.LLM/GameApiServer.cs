using System;
using Alco.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Alco.LLM;

/// <summary>
/// Manages the Kestrel HTTP API server lifecycle.
/// Embeds an ASP.NET Minimal API server within the game process
/// to expose tool functions to external AI agents.
/// </summary>
public sealed class GameApiServer : IDisposable
{
    private WebApplication? _app;
    private readonly ToolRegistry _registry;
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
    /// <param name="port">The port to listen on.</param>
    public GameApiServer(ToolRegistry registry, int port = 52100)
    {
        _registry = registry;
        _port = port;
    }

    /// <summary>
    /// Starts the HTTP API server.
    /// </summary>
    public void Start()
    {
        if (_app != null) return;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{_port}");

        var app = builder.Build();
        app.MapToolApi(_registry);

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

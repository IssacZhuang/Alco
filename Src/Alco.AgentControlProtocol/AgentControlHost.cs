using System;
using System.Text.Json;
using Alco.Engine;

namespace Alco.AgentControlProtocol;

/// <summary>
/// Hosts the engine's agent control protocol: a <see cref="ToolRegistry"/> of agent
/// tools — discovered from the configured tool types plus the built-in tools — whose
/// main-thread queue is drained on each tick, optionally exposed over localhost HTTP
/// through <see cref="GameApiServer"/>.
/// <br/>Adding this system to an engine gives external AI agents full control of a
/// running game: tool discovery and invocation, script execution and frame
/// screenshots. The HTTP server lifecycle is explicit (<see cref="StartServer"/>);
/// referencing this project alone starts nothing.
/// </summary>
public sealed class AgentControlHost : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private GameApiServer? _server;

    /// <summary>
    /// Creates the host and builds the tool registry from the options: the configured
    /// tool types and instances first, then the enabled built-in tools (which replace
    /// same-named tools).
    /// </summary>
    /// <param name="engine">The engine the tools operate on.</param>
    /// <param name="options">The host configuration.</param>
    public AgentControlHost(GameEngine engine, AgentControlOptions options)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);

        _engine = engine;

        JsonOptions = engine.CreateAgentJsonOptions();
        Registry = new ToolRegistry(options.ToolTypes, options.ToolInstances, JsonOptions);

        // Built-in tools are always constructed so hosts can toggle them at runtime via
        // the registry; they are only registered when enabled.
        ScreenshotTool = new ScreenshotTool(engine);
        ScriptTool = new ScriptTool(engine, options.ScriptGlobalsFactory, options.ScriptCompilationTimeoutMs);
        if (options.EnableScreenshotTool)
        {
            Registry.RegisterInstance(ScreenshotTool);
        }

        if (options.EnableScriptExecution)
        {
            Registry.RegisterInstance(ScriptTool);
        }
    }

    /// <summary>
    /// The tool registry backing the HTTP API and any in-process agent.
    /// </summary>
    public ToolRegistry Registry { get; }

    /// <summary>
    /// The JSON serializer options (engine converters, camelCase) shared by tool
    /// invocation and HTTP serialization.
    /// </summary>
    public JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    /// The built-in screenshot tool instance. Registered with the registry only while
    /// enabled; hosts can unregister/re-register it at runtime to toggle the tool
    /// without rebuilding the registry.
    /// </summary>
    public ScreenshotTool ScreenshotTool { get; }

    /// <summary>
    /// The built-in script execution tool instance. Registered with the registry only
    /// while enabled; hosts can unregister/re-register it at runtime to toggle the
    /// tool without rebuilding the registry.
    /// </summary>
    public ScriptTool ScriptTool { get; }

    /// <summary>
    /// The HTTP API server, or <c>null</c> before <see cref="StartServer"/> is called.
    /// </summary>
    public GameApiServer? Server => _server;

    /// <summary>
    /// Starts the HTTP API server on the given localhost port, stopping any existing
    /// server first.
    /// </summary>
    /// <param name="port">The localhost port to listen on.</param>
    /// <param name="configureEndpoints">
    /// Optional callback invoked after the tool API is mapped and before the server
    /// starts, allowing the host to register additional endpoints on the same application.
    /// </param>
    public void StartServer(
        int port = AgentControlOptions.DefaultPort,
        Action<Microsoft.AspNetCore.Builder.WebApplication>? configureEndpoints = null)
    {
        StopServer();

        _server = new GameApiServer(Registry, JsonOptions, port);
        _server.Start(configureEndpoints);
    }

    /// <summary>
    /// Stops the HTTP API server if running.
    /// </summary>
    public void StopServer()
    {
        _server?.Dispose();
        _server = null;
    }

    /// <inheritdoc/>
    public override void OnTick(float delta)
    {
        Registry.DrainMainThreadQueue();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        StopServer();
    }
}

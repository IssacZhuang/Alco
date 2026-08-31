using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.Engine;
using Alco.LLM;

namespace Alco.Editor;

/// <summary>
/// Hosts the editor's agent API: a <see cref="GameApiServer"/> (HTTP/JSON on localhost)
/// exposing the base editor tools (screenshot, open/close/save, project switching,
/// asset listing, layout)
/// plus whatever tools the open asset documents contribute. Document tools are
/// discovered from <see cref="AssetDocument.CreateAgentTools"/> and registered while
/// the document is open — text-format assets usually contribute none, since agents
/// edit those files directly and hot reload updates the preview.
/// <br/>The registry's main-thread queue is drained from <see cref="OnTick"/>; tool
/// invocations therefore complete while the editor loop keeps running.
/// </summary>
public sealed class EditorApiHost : BaseEngineSystem
{
    private readonly DocumentManager _documents;
    private readonly ToolRegistry _registry;
    private readonly GameApiServer _server;
    private readonly Dictionary<AssetDocument, object[]> _documentTools = new();

    /// <summary>
    /// Creates and starts the agent API host.
    /// </summary>
    /// <param name="engine">The editor engine (JSON converters, main-thread pump).</param>
    /// <param name="editorSystem">The editor shell the tools operate on.</param>
    /// <param name="capture">The swapchain capture backing the screenshot tool.</param>
    /// <param name="port">The localhost port to listen on.</param>
    public EditorApiHost(GameEngine engine, EditorSystem editorSystem, SwapchainCaptureSystem capture, int port)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(editorSystem);
        ArgumentNullException.ThrowIfNull(capture);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        foreach (JsonConverter converter in engine.CreateDefaultJsonConverters())
        {
            jsonOptions.Converters.Add(converter);
        }

        _registry = new ToolRegistry(
            Array.Empty<Type>(),
            new object[] { new EditorBaseTools(engine, editorSystem, capture) },
            jsonOptions);
        _server = new GameApiServer(_registry, jsonOptions, port);

        _documents = editorSystem.Documents;
        _documents.DocumentOpened += OnDocumentOpened;
        _documents.DocumentClosed += OnDocumentClosed;
        foreach (AssetDocument document in _documents.Documents)
        {
            OnDocumentOpened(document);
        }

        _server.Start();
    }

    /// <summary>The port the API listens on.</summary>
    public int Port => _server.Port;

    /// <summary>The tool registry behind the API (base tools + open documents' tools).</summary>
    public ToolRegistry Registry => _registry;

    private void OnDocumentOpened(AssetDocument document)
    {
        object[] tools = document.CreateAgentTools().ToArray();
        if (tools.Length == 0)
        {
            return;
        }

        foreach (object tool in tools)
        {
            _registry.RegisterInstance(tool);
        }
        _documentTools[document] = tools;
    }

    private void OnDocumentClosed(AssetDocument document)
    {
        if (!_documentTools.Remove(document, out object[]? tools))
        {
            return;
        }

        foreach (object tool in tools)
        {
            _registry.UnregisterInstance(tool);
        }
    }

    /// <inheritdoc/>
    public override void OnTick(float delta)
    {
        _registry.DrainMainThreadQueue();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _documents.DocumentOpened -= OnDocumentOpened;
        _documents.DocumentClosed -= OnDocumentClosed;
        _server.Dispose();
    }
}

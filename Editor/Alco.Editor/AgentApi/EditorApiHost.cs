using Alco.AgentControlProtocol;
using Alco.Engine;

namespace Alco.Editor;

/// <summary>
/// Hosts the editor's agent API on top of an <see cref="AgentControlHost"/> (HTTP/JSON
/// on localhost): the engine's built-in tools (script execution, frame screenshots)
/// plus the base editor tools (open/close/save, project switching, asset listing,
/// layout) and whatever tools the open asset documents contribute. Document tools are
/// discovered from <see cref="AssetDocument.CreateAgentTools"/> and registered while
/// the document is open — text-format assets usually contribute none, since agents
/// edit those files directly and hot reload updates the preview.
/// <br/>The host's main-thread queue is drained from <see cref="OnTick"/>; tool
/// invocations therefore complete while the editor loop keeps running.
/// </summary>
public sealed class EditorApiHost : BaseEngineSystem
{
    private readonly DocumentManager _documents;
    private readonly AgentControlHost _host;
    private readonly Dictionary<AssetDocument, object[]> _documentTools = new();

    /// <summary>
    /// Creates and starts the agent API host.
    /// </summary>
    /// <param name="engine">The editor engine.</param>
    /// <param name="editorSystem">The editor shell the tools operate on.</param>
    /// <param name="port">The localhost port to listen on.</param>
    public EditorApiHost(GameEngine engine, EditorSystem editorSystem, int port)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(editorSystem);

        _host = new AgentControlHost(engine, new AgentControlOptions
        {
            ToolInstances = new object[] { new EditorBaseTools(engine, editorSystem) },
        });

        _documents = editorSystem.Documents;
        _documents.DocumentOpened += OnDocumentOpened;
        _documents.DocumentClosed += OnDocumentClosed;
        foreach (AssetDocument document in _documents.Documents)
        {
            OnDocumentOpened(document);
        }

        _host.StartServer(port);
    }

    /// <summary>The port the API listens on.</summary>
    public int Port => _host.Server!.Port;

    /// <summary>The tool registry behind the API (built-ins + editor tools + open documents' tools).</summary>
    public ToolRegistry Registry => _host.Registry;

    private void OnDocumentOpened(AssetDocument document)
    {
        object[] tools = document.CreateAgentTools().ToArray();
        if (tools.Length == 0)
        {
            return;
        }

        foreach (object tool in tools)
        {
            _host.Registry.RegisterInstance(tool);
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
            _host.Registry.UnregisterInstance(tool);
        }
    }

    /// <inheritdoc/>
    public override void OnTick(float delta)
    {
        _host.Registry.DrainMainThreadQueue();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _documents.DocumentOpened -= OnDocumentOpened;
        _documents.DocumentClosed -= OnDocumentClosed;
        _host.Dispose();
    }
}

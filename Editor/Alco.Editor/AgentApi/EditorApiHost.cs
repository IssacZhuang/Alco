using Alco.AgentControlProtocol;
using Alco.Engine;

namespace Alco.Editor;

/// <summary>Hosts the editor's localhost HTTP agent API with the built-in editor tools plus tools contributed by open asset documents.</summary>
/// <remarks>Tool invocations are drained from <see cref="OnTick"/> on the main thread.</remarks>
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

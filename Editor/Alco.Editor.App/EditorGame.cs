using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Particles;
using Alco.Rendering;

namespace Alco.Editor.App;

/// <summary>
/// Editor host: drives the engine with the ImGui and editor systems and clears the
/// frame every tick so the editor UI is drawn on a black background.
/// </summary>
public sealed class EditorGame : GameEngine
{
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly EditorSystem _editorSystem;

    /// <summary>
    /// Creates the editor game.
    /// </summary>
    /// <param name="setting">Engine settings.</param>
    /// <param name="project">The project to open in the editor.</param>
    /// <param name="apiPort">The localhost port of the agent API (screenshot, open/close/save, ...).</param>
    /// <param name="enableApi">Whether to host the agent API server.</param>
    public EditorGame(GameEngineSetting setting, AlcoProject project, int apiPort = 52200, bool enableApi = true)
        : base(setting)
    {
        AddSystem(new ImGUISystem(this));

        // The editor system mounts the project's owned asset roots (hot-reloaded) and
        // referenced (read-only) entries through its ProjectOpener, so asset editors
        // can load project and referenced assets by name and switch projects later.
        _editorSystem = new EditorSystem(this, project);
        AddSystem(_editorSystem);

        if (enableApi)
        {
            // The capture system must come after ImGUISystem: screenshots arm in the
            // post-ImGui / pre-present window. The API host drains its tool queue on tick.
            SwapchainCaptureSystem capture = new(this);
            AddSystem(capture);
            AddSystem(new EditorApiHost(this, _editorSystem, capture, apiPort));
        }

        _commandBuffer = GraphicsDevice.CreateCommandBuffer();

        if (AssetSystem.TryLoadRaw(BuiltInAssetsPath.Font_Default, out SafeMemoryHandle data))
        {
            var span = data.AsSpan();
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Basic);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Chinese);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Japanese);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Korean);
            ImGUIRenderer.Instance!.AddFontForLanguage(span, FontLanguage.Cyrillic);
        }
    }

    /// <summary>
    /// Exposes the editor system so the host can print the loaded project.
    /// </summary>
    public EditorSystem EditorSystem => _editorSystem;

    /// <inheritdoc />
    public override IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
    {
        foreach (IAssetLoader loader in base.CreateDefaultAssetLoaders())
        {
            yield return loader;
        }

        // Particle effect assets (.afx) open in the particle effect document; the
        // loader also serves games that load effects through the editor's asset system.
        yield return new AssetLoaderParticleEffect(AssetSystem, RenderingSystem.ShaderSystem);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (MainPresenter.FrameBuffer is { } frameBuffer)
        {
            _commandBuffer.Begin();
            using (_commandBuffer.BeginRender(frameBuffer, ColorFloat.Black))
            {
            }
            _commandBuffer.End();
            GraphicsDevice.Submit(_commandBuffer);
        }

        // ImGui content goes between ImGUISystem's frame begin and render: the game
        // update phase is exactly that window (systems update first).
        _editorSystem.DoUI(delta);
    }
}

using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
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
    public EditorGame(GameEngineSetting setting, AlcoProject project)
        : base(setting)
    {
        // Serve the project's owned asset roots (hot-reloaded) and referenced
        // (read-only) entries to the runtime asset system, so asset editors can load
        // project and referenced assets by name.
        ProjectAssetMount.Mount(project, AssetSystem);

        AddSystem(new ImGUISystem(this));

        _editorSystem = new EditorSystem(this, project);
        AddSystem(_editorSystem);

        _commandBuffer = GraphicsDevice.CreateCommandBuffer();

        if (AssetSystem.TryLoadRaw(BuiltInAssetsPath.Font_Default, out SafeMemoryHandle data))
        {
            var span = data.AsSpan();
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

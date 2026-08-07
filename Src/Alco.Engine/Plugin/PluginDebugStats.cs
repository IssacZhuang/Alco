using System.Numerics;
using Alco.Graphics;
using Alco.GUI;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

public class PluginDebugStats : BaseEnginePlugin
{
    private class DebugStatsSystem : BaseEngineSystem
    {
        private readonly DebugStatsRenderer _renderer;
        private readonly ViewPresenter _presenter;

        public override int Order => 2000;

        public DebugStatsSystem(DebugStatsRenderer renderer, ViewPresenter presenter)
        {
            _renderer = renderer;
            _presenter = presenter;

            _presenter.OnResize += OnViewResize;
        }

        /// <summary>
        /// Submits the accumulated stats into the scene texture, before the pipeline's
        /// post-process chain resolves it into the swapchain.
        /// </summary>
        public override void OnPostUpdate(float deltaTime)
        {
            DebugStats.CheckAndSubmit();
        }

        private void OnViewResize(uint2 size)
        {
            _renderer.SetResolution(size.X, size.Y);
        }

        public override void Dispose()
        {
            _presenter.OnResize -= OnViewResize;
            _renderer.Dispose();
            DebugStats.Reset();
        }
    }

    public override int Order => 0;

    public override void OnPostInitialize(GameEngine engine)
    {
        BuiltInAssets builtInAssets = engine.BuiltInAssets;

        Shader shaderText = builtInAssets.Shader_Text;
        Shader shaderSprite = builtInAssets.Shader_Sprite;
        Font font = builtInAssets.Font_Default;

        DebugStatsRenderer renderer = new(engine.Input, engine.MainView, engine.MainView.Size.X, engine.MainView.Size.Y, engine.MainPipeline, engine.RenderingSystem, shaderText, shaderSprite);
        DebugStatsStyle style = new DebugStatsStyle
        {
            Font = font,
            FontSize = 16,
            SliderWidth = 140,
            SliderThumbWidth = 16,
            SliderColor = 0x2a2a2a,
            SliderThumbColor = 0x373737,
            SliderThumbHoverColor = 0x525252,
            SliderThumbDragColor = 0x234A6C,
            TextColor = 0xf1f1f1,
            ButtonColor = 0x2a2a2a,
            ButtonHoverColor = 0x3a3a3a,
            ButtonPressedColor = 0x234A6C,
            CheckBoxColor = 0x2a2a2a,
            CheckBoxHoverColor = 0x3a3a3a,
            CheckBoxCheckColor = 0x007ACC,
            Margin = new Vector4(2, 2, 2, 2),
            Padding = new Vector2(10, 4)
        };


        DebugStats.Initialize(renderer, style);
        engine.AddSystem(new DebugStatsSystem(renderer, engine.MainPresenter));
    }
}

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
        private readonly ViewRenderTarget _renderTarget;

        public DebugStatsSystem(DebugStatsRenderer renderer, ViewRenderTarget renderTarget)
        {
            _renderer = renderer;
            _renderTarget = renderTarget;

            renderTarget.OnResize += OnRenderTargetResize;
        }

        public override void OnEndFrame(float deltaTime)
        {
            _renderer.Blit(_renderTarget.RenderTexture.FrameBuffer);
        }

        public override void OnPostUpdate(float delta)
        {
            DebugStats.CheckAndSubmit();
        }


        private void OnRenderTargetResize(uint2 size)
        {
            _renderer.SetResolution(size.X, size.Y);
        }

        public override void Dispose()
        {
            _renderTarget.OnResize -= OnRenderTargetResize;
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
        Shader ShaderBlit = builtInAssets.Shader_Blit;
        Font font = builtInAssets.Font_Default;

        DebugStatsRenderer renderer = new(engine.Input, engine.MainView, engine.MainView.Size.X, engine.MainView.Size.Y, engine.RenderingSystem, shaderText, shaderSprite, ShaderBlit);
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
        engine.AddSystem(new DebugStatsSystem(renderer, engine.MainRenderTarget));
    }
}
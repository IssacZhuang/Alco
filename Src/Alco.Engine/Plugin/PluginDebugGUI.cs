using System.Numerics;
using Alco.Graphics;
using Alco.GUI;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

public class PluginDebugGUI : BaseEnginePlugin
{
    private class DebugGUISystem : BaseEngineSystem
    {
        private readonly DebugGUIRenderer _renderer;
        private readonly WindowRenderTarget _renderTarget;

        public DebugGUISystem(DebugGUIRenderer renderer, WindowRenderTarget renderTarget)
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
            DebugGUI.CheckAndSubmit();
        }


        private void OnRenderTargetResize(uint2 size)
        {
            _renderer.SetResolution(size.X, size.Y);
        }

        public override void Dispose()
        {
            _renderTarget.OnResize -= OnRenderTargetResize;
            _renderer.Dispose();
            DebugGUI.Reset();
        }
    }

    public override int Order => 0;

    public override void OnPostInitialize(GameEngine engine)
    {
        BuiltInAssets builtInAssets = engine.BuiltInAssets;

        Shader shaderText = builtInAssets.Shader_TextMasked;
        Shader shaderSprite = builtInAssets.Shader_SpriteMasked;
        Shader ShaderBlit = builtInAssets.Shader_Blit;
        Font font = builtInAssets.Font_Default;

        DebugGUIRenderer renderer = new(engine.Input, engine.MainWindow, engine.MainWindow.Size.X, engine.MainWindow.Size.Y, engine.Rendering, shaderText, shaderSprite, ShaderBlit);
        DebugGUIStyle style = new DebugGUIStyle
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


        DebugGUI.Initialize(renderer, style);
        engine.AddSystem(new DebugGUISystem(renderer, engine.MainRenderTarget));
    }
}
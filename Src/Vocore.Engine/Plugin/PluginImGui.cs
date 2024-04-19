using System.Numerics;
using Vocore.Graphics;
using Vocore.GUI;
using Vocore.Rendering;

namespace Vocore.Engine;

public class PluginImGui : IEnginePlugin
{
    public class ImGuiSystem : BaseEngineSystem
    {
        private readonly ImGuiRenderer _renderer;
        public ImGuiSystem(ImGuiRenderer renderer)
        {
            _renderer = renderer;
        }

        public override void OnUpdate(float delta)
        {
            
        }

        public override void OnPostUpdate(float delta)
        {
            ImGui.CheckAndSubmit();
        }

        public override void OnResize(int2 size)
        {
            _renderer.SetResolution((float)size.x, (float)size.y);
        }

        public override void Dispose()
        {
            _renderer.Dispose();
        }
    }

    public int Order => 0;

    public void Dispose()
    {

    }

    public void OnInitilize(GameEngine engine)
    {
        AssetSystem assets = engine.Assets;

        Shader shaderText = assets.Load<Shader>("Rendering/Shader/2D/Text-AlphaClip.hlsl");
        Shader shaderSprite = assets.Load<Shader>("Rendering/Shader/2D/Sprite-AlphaClip.hlsl");

        Font font = assets.Load<Font>("Font/Default.ttf");

        ImGuiRenderer imGuiRenderer = new(engine.Input, engine.Window.Size.x, engine.Window.Size.y, engine.Rendering, shaderText, shaderSprite);
        ImGuiStyle style = new ImGuiStyle
        {
            Font = font,
            FontSize = 16,
            TextColor = 0xffffff,
            ButtonColor = 0x323233,
            ButtonHoverColor = 0x424243,
            Margin = new Vector4(2, 2, 2, 2),
            Padding = new Vector2(4,4)
        };


        ImGui.Initialize(imGuiRenderer, style);
        engine.AddSystem(new ImGuiSystem(imGuiRenderer));
    }
}
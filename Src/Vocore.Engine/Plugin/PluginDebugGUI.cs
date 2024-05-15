using System.Numerics;
using Vocore.Graphics;
using Vocore.GUI;
using Vocore.Rendering;

namespace Vocore.Engine;

public class PluginDebugGUI : BaseEnginePlugin
{
    public class DebugGUISystem : BaseEngineSystem
    {
        private readonly DebugGUIRenderer _renderer;
        public DebugGUISystem(DebugGUIRenderer renderer)
        {
            _renderer = renderer;
        }

        public override void OnUpdate(float delta)
        {
            
        }

        public override void OnPostUpdate(float delta)
        {
            DebugGUI.CheckAndSubmit();
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

    public override int Order => 0;

    public override void OnPostInitialize(GameEngine engine)
    {
        AssetSystem assets = engine.Assets;

        Shader shaderText = assets.Load<Shader>("Rendering/Shader/2D/Text-Masked.hlsl");
        Shader shaderSprite = assets.Load<Shader>("Rendering/Shader/2D/Sprite-Masked.hlsl");

        Font font = assets.Load<Font>("Font/Default.ttf");

        DebugGUIRenderer renderer = new(engine.Input, engine.Window.Size.x, engine.Window.Size.y, engine.Rendering, shaderText, shaderSprite);
        DebugGUIStyle style = new DebugGUIStyle
        {
            Font = font,
            FontSize = 16,
            SliderWidth = 140,
            SliderThumbWidth = 16,
            SliderColor = 0x2A2D2E,
            SliderThumbColor = 0x37373D,
            SliderThumbHoverColor = 0x525354,
            SliderThumbDragColor = 0x234A6C,
            TextColor = 0xf1f1f1,
            ButtonColor = 0x2A2D2E,
            ButtonHoverColor = 0x37373D,
            CheckBoxColor = 0x2A2D2E,
            CheckBoxHoverColor = 0x37373D,
            CheckBoxCheckColor = 0x007ACC,
            Margin = new Vector4(2, 2, 2, 2),
            Padding = new Vector2(10, 4)
        };


        DebugGUI.Initialize(renderer, style);
        engine.AddSystem(new DebugGUISystem(renderer));
    }
}
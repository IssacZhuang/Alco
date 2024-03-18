using System;
using Vocore.Graphics;

namespace Vocore.Engine
{
    public struct GameEngineSetting
    {
        public bool HasGraphics
        {
            get => Graphics.Backend != GraphicsBackend.None;
        }

        public int GametTickRate;
        public bool StopWhenError;
        public WindowSetting Window;
        public GraphicsSetting Graphics;
        public RenderingSetting Rendering;

        public readonly static GameEngineSetting Default = new GameEngineSetting
        {
            GametTickRate = 60,
            Window = WindowSetting.Default,
            Graphics = GraphicsSetting.Default,
            Rendering = RenderingSetting.Forward
        };


        public readonly static GameEngineSetting WithGraphics = new GameEngineSetting
        {
            GametTickRate = 60,
            Window = WindowSetting.Default,
            Graphics = GraphicsSetting.Default,
            Rendering = RenderingSetting.Forward
        };

        public readonly static GameEngineSetting NoGraphics = new GameEngineSetting
        {
            GametTickRate = 60,
            Window = WindowSetting.Default,
            Graphics = GraphicsSetting.NoGPU,
        };
    }
}
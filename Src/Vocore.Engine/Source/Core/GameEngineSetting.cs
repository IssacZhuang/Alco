using System;
using Vocore.Graphics;

namespace Vocore.Engine
{
    public struct GameEngineSetting
    {
        public GraphicsBackend GraphicsAPI;
        public bool HasGraphics;
        public string WindowName;
        public int Width;
        public int Height;
        public int GametTickRate;
        public bool StopWhenError;
        public RenderingSetting RenderingSetting;

        public readonly static GameEngineSetting Default = new GameEngineSetting
        {
            HasGraphics = true,
            GraphicsAPI = GraphicsBackend.Auto,
            WindowName = "Vocore",
            Width = 640,
            Height = 360,
            GametTickRate = 30,
            RenderingSetting = RenderingSetting.Forward
        };


        public readonly static GameEngineSetting WithGraphics = new GameEngineSetting
        {
            HasGraphics = true,
            GraphicsAPI = GraphicsBackend.Auto,
            WindowName = "Vocore",
            Width = 640,
            Height = 360,
            GametTickRate = 30,
            RenderingSetting = RenderingSetting.Forward
        };

        public readonly static GameEngineSetting NoGraphics = new GameEngineSetting
        {
            HasGraphics = false,
            GametTickRate = 30,
        };
    }
}
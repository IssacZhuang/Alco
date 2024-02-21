using System;
using Vocore.Graphics;

namespace Vocore.Engine
{
    public struct GameEngineSetting
    {
        public bool HasGraphics
        {
            get => GraphicsAPI != GraphicsBackend.None;
        }
        public GraphicsBackend GraphicsAPI;
        public string WindowName;
        public int Width;
        public int Height;
        public int GametTickRate;
        public bool StopWhenError;
        public RenderingSetting RenderingSetting;

        public readonly static GameEngineSetting Default = new GameEngineSetting
        {
            GraphicsAPI = GraphicsBackend.Auto,
            WindowName = "Vocore",
            Width = 640,
            Height = 360,
            GametTickRate = 30,
            RenderingSetting = RenderingSetting.Forward
        };


        public readonly static GameEngineSetting WithGraphics = new GameEngineSetting
        {
            GraphicsAPI = GraphicsBackend.Auto,
            WindowName = "Vocore",
            Width = 640,
            Height = 360,
            GametTickRate = 30,
            RenderingSetting = RenderingSetting.Forward
        };

        public readonly static GameEngineSetting NoGraphics = new GameEngineSetting
        {
            GraphicsAPI = GraphicsBackend.None,
            GametTickRate = 30,
        };
    }
}
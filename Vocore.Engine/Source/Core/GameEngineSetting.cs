using System;
using Veldrid;
using Veldrid.StartupUtilities;

namespace Vocore.Engine
{
    public struct GameEngineSetting
    {
        public GraphicsBackend backend;
        public bool hasGraphics;
        public string windowName;
        public int width;
        public int height;
        public int gametTickRate;

        public readonly static GameEngineSetting Default = new GameEngineSetting
        {
            hasGraphics = true,
            backend = VeldridStartup.GetPlatformDefaultBackend(),
            windowName = "Vocore",
            width = 1280,
            height = 720,
            gametTickRate = 30,
        };


        public readonly static GameEngineSetting HasGraphics = new GameEngineSetting
        {
            hasGraphics = true,
            backend = VeldridStartup.GetPlatformDefaultBackend(),
            windowName = "Vocore",
            width = 1280,
            height = 720,
            gametTickRate = 30,
        };

        public readonly static GameEngineSetting NoGraphics = new GameEngineSetting
        {
            hasGraphics = false,
            gametTickRate = 30,
        };
    }
}
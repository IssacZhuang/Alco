using System;
using Veldrid;

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
        public bool stopWhenError;

        public readonly static GameEngineSetting Default = new GameEngineSetting
        {
            hasGraphics = true,
            backend = CompatibilityHelper.GetPlatformDefaultBackend(),
            windowName = "Vocore",
            width = 640,
            height = 360,
            gametTickRate = 30,
        };


        public readonly static GameEngineSetting HasGraphics = new GameEngineSetting
        {
            hasGraphics = true,
            backend = CompatibilityHelper.GetPlatformDefaultBackend(),
            windowName = "Vocore",
            width = 640,
            height = 360,
            gametTickRate = 30,
        };

        public readonly static GameEngineSetting NoGraphics = new GameEngineSetting
        {
            hasGraphics = false,
            gametTickRate = 30,
        };
    }
}
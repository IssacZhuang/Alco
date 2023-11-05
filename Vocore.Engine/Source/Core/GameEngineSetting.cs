using System;
using Veldrid;
using Veldrid.StartupUtilities;

namespace Vocore
{
    public struct GameEngineSetting
    {
        public GraphicsBackend backend;
        public string windowName;
        public int width;
        public int height;
        public int gametTickRate;

        public static GameEngineSetting Default
        {
            get
            {
                return new GameEngineSetting
                {
                    backend = VeldridStartup.GetPlatformDefaultBackend(),
                    windowName = "Vocore",
                    width = 1280,
                    height = 720,
                    gametTickRate = 30,
                };
            }
        }
    }
}
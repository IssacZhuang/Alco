using Alco.Engine;

GameEngineSetting setting = new()
{
    StopWhenError = true,
    View = new ViewSetting(800, 600, "Basic Window - Vulkan Triangle")
    {
        VSync = true,
    },
};

using Game game = new(setting);
game.Run();

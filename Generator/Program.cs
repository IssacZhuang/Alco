using Vocore;
using Vocore.Engine;
using Vocore.Graphics;



GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Graphics = GraphicsSetting.NoGPU
};

using (Generator game = new Generator(setting))
{
    game.Run();
}
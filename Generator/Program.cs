using Vocore;
using Vocore.Engine;
using Vocore.Graphics;



GameEngineSetting setting = GameEngineSetting.Default with
{
    StopWhenError = true,
    Graphics = GraphicsSetting.NoGPU
};

using (Generator game = new Generator(setting))
{
    game.Run();
}
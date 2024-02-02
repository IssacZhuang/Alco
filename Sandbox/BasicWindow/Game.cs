using Silk.NET.Input;
using Vocore.Engine;
using Vocore.Graphics;

public class Game : GameEngine
{
    private GPUCommandBuffer _commandBuffer;
    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
    }
    
    protected override void OnUpdate(float delta)
    {
        if(Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }
    }

    protected override void OnDraw(float delta)
    {
        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }
}
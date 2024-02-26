using System.Numerics;
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
        _commandBuffer.ClearColor(new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
        //duplicate clear test
        _commandBuffer.ClearColor(new Vector4(0.5f, 0.2f, 0.2f, 1.0f));
        _commandBuffer.ClearColor(new Vector4(0.2f, 0.2f, 0.4f, 1.0f)); // the last color will be used, and only one clear will be recorded
        _commandBuffer.ClearDepthStencil(1.0f, 0);
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }
}
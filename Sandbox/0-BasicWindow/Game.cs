using System.Numerics;
using Alco.Engine;
using Alco.Graphics;
public class Game : GameEngine
{
    private GPUCommandBuffer _commandBuffer;
    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
    }
    
    protected override void OnUpdate(float delta)
    {
        if(Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }


        _commandBuffer.Begin();

        using (var renderScope = _commandBuffer.BeginRender(MainFrameBuffer, new Vector4(0.2f, 0.2f, 0.4f, 1.0f), 1, 0))
        {
        }

        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }
}
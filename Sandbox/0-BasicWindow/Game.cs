using System.Numerics;
using Alco.Engine;
using Alco.Graphics;
/// <summary>Minimal sandbox that opens a window and clears it to a solid color.</summary>
public class Game : GameEngine
{
    private GPUCommandBuffer _commandBuffer;
    /// <summary>Initializes the sandbox with a command buffer.</summary>
    /// <param name="setting">Engine settings for the game instance.</param>
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


        if (MainPresenter.FrameBuffer is not { } frameBuffer) return;
        _commandBuffer.Begin();

        using (var renderPass = _commandBuffer.BeginRender(frameBuffer, new Vector4(0.2f, 0.2f, 0.4f, 1.0f), 1, 0))
        {
        }

        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }
}
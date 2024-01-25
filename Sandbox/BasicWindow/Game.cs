using System;
using Silk.NET.Input;
using Silk.NET.Windowing;
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

        // if (Input.IsKeyDown(Key.F11))
        // {
        //     Window.WindowState = Window.WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
        // }

        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }
}
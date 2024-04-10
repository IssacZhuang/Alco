using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine
{
    // the engine internal context for graphics
    internal struct EngineGraphics
    {
        private static readonly float TimeLimit = math.pow(2, 24);
        private readonly GPUDevice _device;
        private readonly RenderingSystem _renderingSystem;
        private readonly GPUCommandBuffer _commandBuffer;

        public EngineGraphics(GameEngine engine)
        {
            // TODO : implement with new graphics module
            _device = engine.GraphicsDevice;
            _renderingSystem = engine.Rendering;
            _commandBuffer = _device.CreateCommandBuffer();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrameUpdate(float delta)
        {
            _commandBuffer.Begin();
            _commandBuffer.SetFrameBuffer(_renderingSystem.DefaultFrameBuffer);
            _commandBuffer.ClearColor(new Vector4(0, 0, 0, 1));
            _commandBuffer.ClearDepthStencil(1.0f, 0);
            _commandBuffer.End();
            _device.Submit(_commandBuffer);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndFrame()
        {
            _device.SwapBuffers();
        }
    }
}
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
        private readonly GPUSwapchain? _windowSwapchain;

        public EngineGraphics(GameEngine engine)
        {
            _device = engine.GraphicsDevice;
            _renderingSystem = engine.Rendering;
            _windowSwapchain = engine.WindowSwapchain;
            _commandBuffer = _device.CreateCommandBuffer();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrameUpdate(GPUSwapchain? swapchain)
        {
            if (swapchain == null)
            {
                return;
            }

            // _commandBuffer.Begin();
            // _commandBuffer.SetFrameBuffer(_renderingSystem.DefaultFrameBuffer);
            // _commandBuffer.ClearColor(new Vector4(0, 0, 0, 1));
            // _commandBuffer.ClearDepthStencil(1.0f, 0);
            // _commandBuffer.End();
            // _device.Submit(_commandBuffer);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndFrame()
        {
            // _windowSwapchain?.Present();
            _device.ProcessDestroy();
        }
    }
}
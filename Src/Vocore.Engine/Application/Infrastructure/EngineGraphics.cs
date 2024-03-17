using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine
{
    // the engine internal context for graphics
    internal struct EngineGraphics
    {
        private static readonly float TimeLimit = math.pow(2, 24);
        public Vector2 ScreenSize { get; set; }
        private readonly GPUDevice _device;
        private readonly GPUCommandBuffer _commandBuffer;

        public EngineGraphics(GameEngine engine, Vector2 screenSize)
        {
            // TODO : implement with new graphics module
            _device = engine.GraphicsDevice;
            _commandBuffer = _device.CreateCommandBuffer();
            ScreenSize = screenSize;

           // _renderTarget = OffscreenBuffer.CreateBySwapchainFramebuffer(_device);
            // _globalShaderData = new UniformBuffer<GlobalShaderData>(_device);
            // _transformBuffer = new UniformBuffer<Matrix4x4>(_device);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrameUpdate(float delta)
        {
            // TODO : implement with new graphics module
            // _shaderTimer += delta;
            // if (_shaderTimer >= TimeLimit)
            // {
            //     _shaderTimer -= TimeLimit;
            // }

            // GlobalShaderData data = _globalShaderData.Value;
            // data.deltaTime = delta;
            // data.time = _shaderTimer;
            // data.sinTime = math.sin(_shaderTimer);
            // data.cosTime = math.cos(_shaderTimer);
            // if (Camera != null)
            // {
            //     data.viewProjMatrix = Camera.ViewProjectionMatrix;
            // }
            // data.screenSize = ScreenSize;

            // _globalShaderData.Value = data;

            _commandBuffer.Begin();
            _commandBuffer.SetFrameBuffer(_device.SwapChainFrameBuffer);
            _commandBuffer.ClearColor(new Vector4(0, 0, 0, 1));
            _commandBuffer.ClearDepthStencil(1.0f, 0);
            _commandBuffer.End();
            _device.Submit(_commandBuffer);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndFrame()
        {
            // TODO : implement with new graphics module
            // _device.WaitForIdle();
            _device.SwapBuffers();
        }
    }
}
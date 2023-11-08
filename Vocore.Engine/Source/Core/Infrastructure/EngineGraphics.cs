using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    /// <summary>
    /// The graphics module <br/>
    /// Have only one instance in the Class Engine
    /// </summary>
    internal struct EngineGraphics
    {
        private static readonly float TimeLimit = math.pow(2, 24);
        public ICamera? Camera { get; set; }
        public Vector2 ScreenSize { get; set; }
        private CommandList _commandList;
        private GraphicsDevice _device;
        private GraphicsBuffer<GlobalShaderData> _globalShaderData;
        private float _shaderTimer;

        public EngineGraphics(GameEngine engine, Vector2 screenSize)
        {
            _device = engine.GraphicsDevice;
            _commandList = _device.ResourceFactory.CreateCommandList();
            _globalShaderData = new GraphicsBuffer<GlobalShaderData>(_device, BufferUsage.UniformBuffer);
            _shaderTimer = 0f;
            Camera = null;
            ScreenSize = screenSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrameUpdate(float delta)
        {
            _shaderTimer += delta;
            if (_shaderTimer >= TimeLimit)
            {
                _shaderTimer -= TimeLimit;
            }
            
            ref GlobalShaderData data = ref _globalShaderData.ValueRef;
            data.deltaTime = delta;
            data.time = _shaderTimer;
            data.sinTime = math.sin(_shaderTimer);
            data.cosTime = math.cos(_shaderTimer);
            if (Camera != null)
            {
                data.viewProjMatrix = Camera.ViewProjectionMatrix;
            }
            data.screenSize = ScreenSize;

            _commandList.Begin();
            _commandList.SetFramebuffer(_device.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.ClearDepthStencil(1f);
            _commandList.UpdateBuffer(_globalShaderData);
            _commandList.End();
            _device.SubmitCommands(_commandList);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndFrame()
        {
            _device.SwapBuffers();
            _device.WaitForIdle();
        }
    }
}
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
    public class EngineGraphics
    {
        private static readonly uint VertexBufferSize = 1024 * 1024 * 4;
        private static readonly uint IndexBufferSize = 1024 * 1024 * 4;
        private static readonly float TimeLimit = math.pow(2, 24);
        public ICamera? Camera { get; set; }
        public Vector2 ScreenSize { get; set; }
        private readonly CommandList _commandList;
        private readonly GraphicsDevice _device;
        private readonly UniformBuffer<Matrix4x4> _transformBuffer;
        private readonly UniformBuffer<GlobalShaderData> _globalShaderData;
        private readonly OffscreenBuffer _renderTarget;
        private float _shaderTimer;
        public UniformBuffer<GlobalShaderData> GlobalShaderData
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _globalShaderData;
            }
        }

        public OffscreenBuffer RenderTarget
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _renderTarget;
            }
        }

        public EngineGraphics(GameEngine engine, Vector2 screenSize)
        {
            // TODO : implement with new graphics module
            //_device = engine.GraphicsDevice;
            //_commandList = _device.ResourceFactory.CreateCommandList();
            _shaderTimer = 0f;
            Camera = null;
            ScreenSize = screenSize;

           // _renderTarget = OffscreenBuffer.CreateBySwapchainFramebuffer(_device);
            // _globalShaderData = new UniformBuffer<GlobalShaderData>(_device);
            // _transformBuffer = new UniformBuffer<Matrix4x4>(_device);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrameUpdate(float delta)
        {
            // TODO : implement with new graphics module
            return;
            _shaderTimer += delta;
            if (_shaderTimer >= TimeLimit)
            {
                _shaderTimer -= TimeLimit;
            }
            
            GlobalShaderData data = _globalShaderData.Value;
            data.deltaTime = delta;
            data.time = _shaderTimer;
            data.sinTime = math.sin(_shaderTimer);
            data.cosTime = math.cos(_shaderTimer);
            if (Camera != null)
            {
                data.viewProjMatrix = Camera.ViewProjectionMatrix;
            }
            data.screenSize = ScreenSize;

            _globalShaderData.Value = data;

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
            // TODO : implement with new graphics module
            // _device.SwapBuffers();
            // _device.WaitForIdle();
        }
    }
}
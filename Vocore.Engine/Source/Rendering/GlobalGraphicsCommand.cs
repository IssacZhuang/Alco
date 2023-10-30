using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using Veldrid.SPIRV;
using Vocore.Unsafe;

namespace Vocore.Engine
{

    internal class GlobalGraphicsCommand : IDisposable
    {
        private static readonly float TimeLimit = math.pow(2, 24);
        private readonly GraphicsDevice _device;
        private readonly ResourceFactory _factory;
        private readonly CommandList _commandList;
        private readonly ResourceSet _resourceSetGlobalData;
        private readonly DeviceBuffer _globalBuffer;
        private GlobalShaderData _globalData;
        private float _timer = 0;
        public ResourceSet ResourceGlobalData
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _resourceSetGlobalData;
            }
        }

        public GraphicsDevice Device
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _device;
            }
        }

        public ResourceFactory Factory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _factory;
            }
        }

        public CommandList CommandList
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _commandList;
            }
        }



        public GlobalGraphicsCommand(GraphicsDevice _graphicsDevice)
        {
            _device = _graphicsDevice;
            _factory = _device.ResourceFactory;
            _commandList = _factory.CreateCommandList();
            _globalBuffer = _factory.CreateBuffer(new BufferDescription(GlobalShaderData.SizeInBytes, BufferUsage.UniformBuffer));
            var bufferLayoutGlobalData = _factory.CreateResourceLayout(GlobalShaderData.Layout);
            _resourceSetGlobalData = _factory.CreateResourceSet(new ResourceSetDescription(bufferLayoutGlobalData, _globalBuffer));
        }

        public void Update(float delta)
        {
            
           
        }

        public Framebuffer GetFrameBuffer()
        {
            return _device.SwapchainFramebuffer;
        }

        public void BeginFrame(float delta)
        {
            _timer += delta;
            if (Current.Camera != null)
            {
                _globalData.viewProjMatrix = Current.Camera.ViewProjectionMatrix;
            }

            if (_timer > TimeLimit)
            {
                _timer -= TimeLimit;
            }

            _globalData.time = _timer;
            _globalData.deltaTime = delta;
            _globalData.sinTime = math.sin(_timer);
            _globalData.cosTime = math.cos(_timer);
            _globalData.screenSize.X = Screen.Height;
            _globalData.screenSize.Y = Screen.Width;

            _commandList.Begin();
            _commandList.SetFramebuffer(_device.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.ClearDepthStencil(1f);
            _commandList.UpdateBuffer(_globalBuffer, 0, _globalData);
            _commandList.End();
            _device.SubmitCommands(_commandList);

        }

        public void EndFrame()
        {
            _device.SwapBuffers();
        }

        public void Dispose()
        {
            _globalBuffer.Dispose();
            _commandList.Dispose();
        }
    }
}
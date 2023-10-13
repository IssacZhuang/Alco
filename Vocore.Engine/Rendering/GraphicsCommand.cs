using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using Veldrid.SPIRV;
using Vocore.Unsafe;

namespace Vocore.Engine
{

    public class GraphicsCommand:IDisposable
    {
        private GraphicsDevice _device;
        private ResourceFactory _factory;
        private CommandList _commandList;
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private DeviceBuffer _cameraBuffer;
        private DeviceBuffer _transformBuffer;
        private ResourceSet _resourceSetCamera;
        private ResourceSet _resourceSetTransform;
        private static readonly uint VertexBufferSize = 1024*1024*4;
        private static readonly uint IndexBufferSize = 1024*1024*4;

        public GraphicsDevice Device
        {
            get
            {
                return _device;
            }
        }

        public ResourceFactory Factory
        {
            get
            {
                return _factory;
            }
        }

        public CommandList CommandList
        {
            get
            {
                return _commandList;
            }
        }

        public GraphicsCommand(GraphicsDevice _graphicsDevice)
        {
            _device = _graphicsDevice;
            _factory = _device.ResourceFactory;
            _commandList = _factory.CreateCommandList();
            _vertexBuffer = _factory.CreateBuffer(new BufferDescription(VertexBufferSize, BufferUsage.VertexBuffer));
            _indexBuffer = _factory.CreateBuffer(new BufferDescription(IndexBufferSize, BufferUsage.IndexBuffer));
            _cameraBuffer = _factory.CreateBuffer(new BufferDescription(CameraPerspective.BufferSize, BufferUsage.UniformBuffer));
            _transformBuffer = _factory.CreateBuffer(new BufferDescription((uint)UtilsMemory.SizeOf<Matrix4x4>(), BufferUsage.UniformBuffer));
            var bufferLayoutCamera = _factory.CreateResourceLayout(BufferLayout.Camera);
            _resourceSetCamera = _factory.CreateResourceSet(new ResourceSetDescription(bufferLayoutCamera, _cameraBuffer));
            var bufferLayoutTransform = _factory.CreateResourceLayout(BufferLayout.Transform);
            _resourceSetTransform = _factory.CreateResourceSet(new ResourceSetDescription(bufferLayoutTransform, _transformBuffer));
        }

        public void UpdateCameraBuffer()
        {
            if (Current.Camera == null)
            {
                return;
            }

            _device.UpdateBuffer(_cameraBuffer, 0, Current.Camera.ViewProjectionMatrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(Mesh mesh, Pipeline shaderPipeline, Transform transform)
        {
            DrawMesh(mesh, shaderPipeline, transform.Matrix);
        }

        public void DrawMesh(IMesh mesh, Pipeline shaderPipeline, Matrix4x4 transform)
        {
            _device.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            _device.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
            _device.UpdateBuffer(_transformBuffer, 0, transform);

            _commandList.Begin();
            _commandList.SetPipeline(shaderPipeline);
            _commandList.SetFramebuffer(_device.SwapchainFramebuffer);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, mesh.IndexFormat);

            _commandList.SetGraphicsResourceSet(0, _resourceSetCamera);
            _commandList.SetGraphicsResourceSet(1, _resourceSetTransform);
            _commandList.DrawIndexed(
                indexCount: mesh.IndexCount,
                instanceCount: 1,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);
            _commandList.End();
            _device.SubmitCommands(_commandList);
        }

        public Framebuffer GetFrameBuffer()
        {
            return _device.SwapchainFramebuffer;
        }

        public void BeginFrame()
        {
            _commandList.Begin();
            _commandList.SetFramebuffer(_device.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.ClearDepthStencil(1f);
            _commandList.End();
            _device.SubmitCommands(_commandList);

        }

        public void EndFrame()
        {
            _device.SwapBuffers();
        }

        public void Dispose()
        {
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _commandList.Dispose();
        }
    }
}
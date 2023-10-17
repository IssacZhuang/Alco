using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using Vocore.Unsafe;

namespace Vocore.Engine
{
    // command list and buffer
    public class CommandBuffer
    {
        private static readonly uint VertexBufferSize = 1024 * 1024 * 4;
        private static readonly uint IndexBufferSize = 1024 * 1024 * 4;
        private readonly GraphicsDevice _device;
        private ResourceFactory _factory;
        private readonly CommandList _commandList;
        private readonly DeviceBuffer _vertexBuffer;
        private readonly DeviceBuffer _indexBuffer;
        private ResourceSet _resourceSetTransform;
        private DeviceBuffer _transformBuffer;
        // from global
        private ResourceSet _resourceSetGlobalData;
        public unsafe CommandList CommandList
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _commandList;
            }
        }

        public CommandBuffer(GraphicsDevice _graphicsDevice, ResourceSet globalResource)
        {
            _device = _graphicsDevice;
            _resourceSetGlobalData = globalResource;
            _factory = _device.ResourceFactory;
            _commandList = _factory.CreateCommandList();
            _vertexBuffer = _factory.CreateBuffer(new BufferDescription(VertexBufferSize, BufferUsage.VertexBuffer));
            _indexBuffer = _factory.CreateBuffer(new BufferDescription(IndexBufferSize, BufferUsage.IndexBuffer));
            _transformBuffer = _factory.CreateBuffer(new BufferDescription((uint)UtilsMemory.SizeOf<Matrix4x4>(), BufferUsage.UniformBuffer));
            var bufferLayoutTransform = _factory.CreateResourceLayout(BufferLayout.Transform);
            _resourceSetTransform = _factory.CreateResourceSet(new ResourceSetDescription(bufferLayoutTransform, _transformBuffer));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMesh mesh, Shader shader, Transform transform)
        {
            DrawMesh(mesh, shader.Pipeline, transform.Matrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMesh mesh, Shader shader, Matrix4x4 transform)
        {
            DrawMesh(mesh, shader.Pipeline, transform);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMesh mesh, Pipeline shaderPipeline, Transform transform)
        {
            DrawMesh(mesh, shaderPipeline, transform.Matrix);
        }

        public void UpdateMesh(IMesh mesh)
        {
            _device.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            _device.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
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

            _commandList.SetGraphicsResourceSet(0, _resourceSetGlobalData);
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

    }
}


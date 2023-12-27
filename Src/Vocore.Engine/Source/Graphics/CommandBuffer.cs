using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using Vocore.Unsafe;

namespace Vocore.Engine
{
    // command list and buffer
    public class CommandBuffer : IDisposable
    {
        private static readonly uint VertexBufferSize = 1024 * 1024 * 4;
        private static readonly uint IndexBufferSize = 1024 * 1024 * 4;
        private static readonly uint InstanceIdBufferSize = 1024 * 1024 * 4;
        public static readonly int MaxInstanceCount = 40000;
        private readonly GraphicsDevice _device;
        private ResourceFactory _factory;
        private readonly CommandList _commandList;
        private readonly DeviceBuffer _vertexBuffer;
        private readonly DeviceBuffer _indexBuffer;
        private ResourceSet _resourceSetTransform;
        private DeviceBuffer _transformBuffer;
        private DeviceBuffer _instanceIdBuffer;
        private NativeBuffer<uint> _instanceIds;
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

        public unsafe CommandBuffer(GraphicsDevice _graphicsDevice, ResourceSet globalResource)
        {
            _device = _graphicsDevice;
            _resourceSetGlobalData = globalResource;
            _factory = _device.ResourceFactory;
            _commandList = _factory.CreateCommandList();
            _vertexBuffer = _factory.CreateBuffer(new BufferDescription(VertexBufferSize, BufferUsage.VertexBuffer));
            _indexBuffer = _factory.CreateBuffer(new BufferDescription(IndexBufferSize, BufferUsage.IndexBuffer));
            _transformBuffer = _factory.CreateBuffer(new BufferDescription((uint)UtilsMemory.SizeOf<Matrix4x4>(), BufferUsage.UniformBuffer));
            var bufferLayoutTransform = _factory.CreateResourceLayout(BufferLayout.Uniform);
            _resourceSetTransform = _factory.CreateResourceSet(new ResourceSetDescription(bufferLayoutTransform, _transformBuffer));
            _instanceIdBuffer = _factory.CreateBuffer(new BufferDescription(InstanceIdBufferSize, BufferUsage.VertexBuffer));
            _instanceIds = new NativeBuffer<uint>(MaxInstanceCount);
            for (int i = 0; i < MaxInstanceCount; i++)
            {
                _instanceIds[i] = (uint)i;
            }
            _device.UpdateBuffer(_instanceIdBuffer, 0, _instanceIds.IntPtr, (uint)_instanceIds.Length * sizeof(uint));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMeshData mesh, Shader shader, Transform3D transform)
        {
            DrawMesh(mesh, shader.Pipeline, transform.Matrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMeshData mesh, Shader shader, Matrix4x4 transform)
        {
            DrawMesh(mesh, shader.Pipeline, transform);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMeshData mesh, Pipeline shaderPipeline, Transform3D transform)
        {
            DrawMesh(mesh, shaderPipeline, transform.Matrix);
        }

        public void DrawMesh(IMeshData mesh, Pipeline shaderPipeline, Matrix4x4 transform)
        {
            // _device.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            // _device.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
            // _device.UpdateBuffer(_transformBuffer, 0, transform);

            _commandList.Begin();
            _commandList.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            _commandList.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
            _commandList.UpdateBuffer(_transformBuffer, 0, transform);
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

        public void DrawMeshWithTexture(IMeshData mesh, Shader shader, Transform3D transform, ResourceSet texture)
        {
            DrawMeshWithTexture(mesh, shader.Pipeline, transform.Matrix, texture);
        }

        public void DrawMeshWithTexture(IMeshData mesh, Shader shader, Matrix4x4 transform, ResourceSet texture)
        {
            DrawMeshWithTexture(mesh, shader.Pipeline, transform, texture);
        }

        public void DrawMeshWithTexture(IMeshData mesh, Pipeline shaderPipeline, Matrix4x4 transform, ResourceSet texture)
        {
            // _device.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            // _device.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
            // _device.UpdateBuffer(_transformBuffer, 0, transform);

            _commandList.Begin();
            _commandList.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            _commandList.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
            _commandList.UpdateBuffer(_transformBuffer, 0, transform);
            _commandList.SetPipeline(shaderPipeline);
            _commandList.SetFramebuffer(_device.SwapchainFramebuffer);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, mesh.IndexFormat);

            _commandList.SetGraphicsResourceSet(0, _resourceSetGlobalData);
            _commandList.SetGraphicsResourceSet(1, _resourceSetTransform);
            _commandList.SetGraphicsResourceSet(2, texture);
            _commandList.DrawIndexed(
                indexCount: mesh.IndexCount,
                instanceCount: 1,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);
            _commandList.End();
            _device.SubmitCommands(_commandList);
        }

        public void DrawMeshIntanced(IMeshData mesh, Shader shader, Transform3D transform, uint instanceCount)
        {
            DrawMeshIntanced(mesh, shader.Pipeline, transform.Matrix, instanceCount);
        }

        public void DrawMeshIntanced(IMeshData mesh, Shader shader, Matrix4x4 transform, uint instanceCount)
        {
            DrawMeshIntanced(mesh, shader.Pipeline, transform, instanceCount);
        }

        public void DrawMeshIntanced(IMeshData mesh, Pipeline shaderPipeline, Matrix4x4 transform, uint instanceCount)
        {
            // _device.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            // _device.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
            // _device.UpdateBuffer(_transformBuffer, 0, transform);

            _commandList.Begin();
            _commandList.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            _commandList.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
            _commandList.UpdateBuffer(_transformBuffer, 0, transform);


            _commandList.SetPipeline(shaderPipeline);
            _commandList.SetFramebuffer(_device.SwapchainFramebuffer);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, mesh.IndexFormat);
            _commandList.SetVertexBuffer(1, _instanceIdBuffer);

            _commandList.SetGraphicsResourceSet(0, _resourceSetGlobalData);
            _commandList.SetGraphicsResourceSet(1, _resourceSetTransform);
            _commandList.DrawIndexed(
                indexCount: mesh.IndexCount,
                instanceCount: instanceCount,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);
            _commandList.End();
            _device.SubmitCommands(_commandList);
        }

        public void Dispose()
        {
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _transformBuffer.Dispose();
            _instanceIdBuffer.Dispose();
            _instanceIds.Dispose();
            _commandList.Dispose();
        }
    }
}


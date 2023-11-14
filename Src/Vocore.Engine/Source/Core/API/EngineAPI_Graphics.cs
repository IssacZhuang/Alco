using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    /// <summary>
    /// The graphics API to draw object <br/>
    /// !!not thread safe!!
    /// </summary>
    public class EngineAPI_Graphics
    {
        private static readonly uint VertexBufferSize = 1024 * 1024 * 4;
        private static readonly uint IndexBufferSize = 1024 * 1024 * 4;
        public const int MaxInstanceCount = 40000;
        private readonly GraphicsDevice _device;
        private readonly ResourceFactory _factory;
        private readonly CommandList _command;
        private readonly GraphicsBuffer<Matrix4x4> _transformBuffer;
        private readonly GraphicsArrayBuffer<uint> _instanceIdBuffer;
        private readonly DeviceBuffer _vertexBuffer;
        private readonly DeviceBuffer _indexBuffer;
        private readonly ResourceSet _resourceTransformData;
        private readonly ResourceSet _resourceGlobalShaderData;

        public EngineAPI_Graphics(GraphicsDevice device, ResourceSet globalShaderDataSet)
        {
            _device = device;
            _factory = device.ResourceFactory;
            _command = device.ResourceFactory.CreateCommandList();
            _resourceGlobalShaderData = globalShaderDataSet;

            //init matrix buffer for the transform
            _transformBuffer = new GraphicsBuffer<Matrix4x4>(device, BufferUsage.UniformBuffer);
            _resourceTransformData = _transformBuffer.CreateResourceSet(BufferLayout.Matrix4x4);
            
            //prepare instance id 
            _instanceIdBuffer = new GraphicsArrayBuffer<uint>(device, MaxInstanceCount, BufferUsage.VertexBuffer);
            for (int i = 0; i < MaxInstanceCount; i++)
            {
                _instanceIdBuffer[i] = (uint)i;
            }
            _instanceIdBuffer.UpdateToGPUImmediately();

            //prepare shared vertex and index buffer
            _vertexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(VertexBufferSize, BufferUsage.VertexBuffer));
            _indexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(IndexBufferSize, BufferUsage.IndexBuffer));
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

        public void DrawMesh(IMesh mesh, Pipeline shaderPipeline, Matrix4x4 transform)
        {
            //set transform data to memory, later will be updated to GPU by the command list
            _transformBuffer.Value = transform;

            _command.Begin();

            _command.UpdateBuffer(_vertexBuffer, 0, mesh.VertexPtr, mesh.VertexBufferSize);
            _command.UpdateBuffer(_indexBuffer, 0, mesh.IndexPtr, mesh.IndexBufferSize);
            _command.UpdateBuffer(_transformBuffer);

            _command.SetPipeline(shaderPipeline);

            _command.SetFramebuffer(_device.SwapchainFramebuffer);
            _command.SetVertexBuffer(0, _vertexBuffer);
            _command.SetIndexBuffer(_indexBuffer, mesh.IndexFormat);

            _command.SetGraphicsResourceSet(0, _resourceGlobalShaderData);
            _command.SetGraphicsResourceSet(1, _resourceTransformData);

            _command.DrawIndexed(
                indexCount: mesh.IndexCount,
                instanceCount: 1,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);
            _command.End();
            _device.SubmitCommands(_command);
        }

        internal void Dispose()
        {
            _transformBuffer.Dispose();
            _instanceIdBuffer.Dispose();
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _command.Dispose();
        }


    }
}
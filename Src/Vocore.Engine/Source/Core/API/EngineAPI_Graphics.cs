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
        private readonly UniformBuffer<Matrix4x4> _transformBuffer;
        private readonly UniformBuffer<GlobalShaderData> _globalShaderBuffer;
        private readonly DeviceBuffer _vertexBuffer;
        private readonly DeviceBuffer _indexBuffer;

        public EngineAPI_Graphics(GraphicsDevice device, UniformBuffer<GlobalShaderData> globalShaderBuffer)
        {
            _device = device;
            _factory = device.ResourceFactory;
            _command = device.ResourceFactory.CreateCommandList();

            //init matrix buffer for the transform
            _transformBuffer = new UniformBuffer<Matrix4x4>(device);
            _globalShaderBuffer = globalShaderBuffer;

            //prepare shared vertex and index buffer
            _vertexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(VertexBufferSize, BufferUsage.VertexBuffer));
            _indexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(IndexBufferSize, BufferUsage.IndexBuffer));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMesh mesh, Shader shader, Transform3D transform)
        {
            DrawMesh(mesh, shader.Pipeline, transform.Matrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMesh mesh, Shader shader, Matrix4x4 transform)
        {
            DrawMesh(mesh, shader.Pipeline, transform);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMesh mesh, Pipeline shaderPipeline, Transform3D transform)
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

            _command.SetBuffer(0, _globalShaderBuffer);
            _command.SetBuffer(1, _transformBuffer);


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
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _command.Dispose();
        }


    }
}
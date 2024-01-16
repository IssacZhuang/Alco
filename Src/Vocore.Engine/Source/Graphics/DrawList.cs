using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    public class DrawList
    {
        private Framebuffer _target;
        private readonly CommandList _commands;
        private readonly GraphicsDevice _device;
        private readonly UniformBuffer<GlobalShaderData> _globalShaderData;
        private readonly UniformBuffer<Matrix4x4> _transformBuffer;

        public DrawList(GameEngine engine, CommandList commandList, Framebuffer framebuffer)
        {
            _target = framebuffer;
            _commands = commandList;
            //_device = engine.GraphicsDevice;
            _globalShaderData = engine.Graphics.GlobalShaderData;
            _transformBuffer = new UniformBuffer<Matrix4x4>(_device);
        }

        public DrawList(GameEngine engine, OffscreenBuffer framebuffer, CommandList commandList) : this(engine, commandList, framebuffer.Framebuffer)
        {
        }

        // public DrawList(GameEngine engine, CommandList commandList) : this(engine, commandList, engine.GraphicsDevice.SwapchainFramebuffer)
        // {
        // }

        // public DrawList(GameEngine engine, Framebuffer framebuffer) : this(engine, engine.GraphicsDevice.ResourceFactory.CreateCommandList(), framebuffer)
        // {
        // }

        // public DrawList(GameEngine engine, OffscreenBuffer framebuffer) : this(engine, engine.GraphicsDevice.ResourceFactory.CreateCommandList(), framebuffer.Framebuffer)
        // {
        // }

        // public DrawList(GameEngine engine) : this(engine, engine.GraphicsDevice.ResourceFactory.CreateCommandList(), engine.GraphicsDevice.SwapchainFramebuffer)
        // {
        // }

        public void Begin()
        {
            _commands.Begin();
            _commands.SetFramebuffer(_target);
            _commands.SetFullViewport(0);
            _commands.ClearColorTarget(0, RgbaFloat.Black);
            _commands.ClearDepthStencil(1f);
            
        }

        /// <summary>
        /// Draw a mesh with a shader and a buffer group, no preset buffers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMeshResource mesh, Shader shader, GpuResourceGroup? bufferGroup = null)
        {
            DrawMesh(mesh, shader.Pipeline, bufferGroup);
        }

        /// <summary>
        /// Draw a mesh with a shader and a buffer group, no preset buffers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMeshResource mesh, Pipeline pipeline, GpuResourceGroup? bufferGroup = null)
        {
            _commands.SetPipeline(pipeline);
            _commands.SetVertexBuffer(0, mesh.VertexBuffer);
            _commands.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
            if (bufferGroup != null)
            {
                _commands.SetResourceGroup(bufferGroup);
            }
            _commands.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        }

        /// <summary>
        /// Draw a mesh with a shader and a buffer group with the global shader data(slot 0) and the transform buffer(slot 1).
        /// </summary>
        public void DrawMeshTranformed(IMeshResource mesh, Shader shader, Transform3D transform, GpuResourceGroup? bufferGroup = null)
        {
            DrawMeshTranformed(mesh, shader.Pipeline, transform, bufferGroup);
        }

        /// <summary>
        /// Draw a mesh with a shader and a buffer group with the global shader data(slot 0) and the transform buffer(slot 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMeshTranformed(IMeshResource mesh, Pipeline pipeline, Transform3D transform, GpuResourceGroup? bufferGroup = null)
        {
            _transformBuffer.Value = transform.Matrix;
            _commands.SetPipeline(pipeline);
            _commands.SetVertexBuffer(0, mesh.VertexBuffer);
            _commands.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
            _commands.SetBuffer(0, _globalShaderData);
            _commands.SetBuffer(1, _transformBuffer);
            if (bufferGroup != null)
            {
                _commands.SetResourceGroup(bufferGroup);
            }
            _commands.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        }

        public void End()
        {
            _commands.End();
            _device.SubmitCommands(_commands);
        }

        public void PushToScreen()
        {
            _commands.Begin();
            _commands.CopyTexture(_target.ColorTargets[0].Target, _device.SwapchainFramebuffer.ColorTargets[0].Target);
            _commands.End();
            _device.SubmitCommands(_commands);
        }


    }
}
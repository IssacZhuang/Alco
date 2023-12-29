using System;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    public class DrawList
    {
        private Framebuffer _target;
        private CommandList _commands;
        private GraphicsDevice _device;

        public DrawList(GraphicsDevice device, CommandList commandList, Framebuffer framebuffer)
        {
            _target = framebuffer;
            _commands = commandList;
            _device = device;
        }

        public DrawList(GraphicsDevice device, OffscreenBuffer framebuffer, CommandList commandList) : this(device, commandList, framebuffer.Framebuffer)
        {
        }

        public DrawList(GraphicsDevice device, CommandList commandList) : this(device, commandList, device.SwapchainFramebuffer)
        {
        }

        public DrawList(GraphicsDevice device, Framebuffer framebuffer) : this(device, device.ResourceFactory.CreateCommandList(), framebuffer)
        {
        }

        public DrawList(GraphicsDevice device, OffscreenBuffer framebuffer) : this(device, device.ResourceFactory.CreateCommandList(), framebuffer.Framebuffer)
        {
        }



        public DrawList(GraphicsDevice device) : this(device, device.ResourceFactory.CreateCommandList(), device.SwapchainFramebuffer)
        {
        }

        public void Begin()
        {
            _commands.Begin();
            _commands.SetFramebuffer(_target);
            _commands.SetFullViewport(0);
            _commands.ClearColorTarget(0, RgbaFloat.Black);
            _commands.ClearDepthStencil(1f);
            
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMeshResource mesh, Shader shader, GpuResourceGroup bufferGroup){
            DrawMesh(mesh, shader.Pipeline, bufferGroup);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawMesh(IMeshResource mesh, Pipeline pipeline, GpuResourceGroup bufferGroup)
        {
            _commands.SetPipeline(pipeline);
            _commands.SetVertexBuffer(0, mesh.VertexBuffer);
            _commands.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
            _commands.SetResourceGroup(bufferGroup);
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
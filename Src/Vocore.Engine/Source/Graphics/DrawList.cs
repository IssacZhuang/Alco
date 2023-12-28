using System;
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

        public DrawList(GraphicsDevice device, CommandList commandList)
        {
            _commands = commandList;
            _device = device;
            _target = device.SwapchainFramebuffer;
        }

        public DrawList(GraphicsDevice device, Framebuffer framebuffer)
        {
            _target = framebuffer;
            _commands = device.ResourceFactory.CreateCommandList();
            _device = device;
        }

        public DrawList(GraphicsDevice device)
        {
            _device = device;
            _commands = device.ResourceFactory.CreateCommandList();
            _target = device.SwapchainFramebuffer;
        }

        public void Begin()
        {
            _commands.Begin();
        }



        public void DrawMesh(IMeshResource mesh, Pipeline pipeline, GpuResourceGroup bufferGroup)
        {
            _commands.SetPipeline(pipeline);
            _commands.SetFramebuffer(_target);
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


    }
}
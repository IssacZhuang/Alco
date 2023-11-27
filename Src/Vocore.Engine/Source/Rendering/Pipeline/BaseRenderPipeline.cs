using System;
using System.Collections.Generic;
using Veldrid;

#pragma warning disable CS8618

namespace Vocore.Engine
{
    public abstract class BaseRenderPipeline : IRenderPipline
    {
        
        public virtual bool IsEnable => true;

        public virtual int Order => 1000;

        protected GraphicsDevice _device;
        protected ResourceFactory _factory;

        public virtual void OnCreate(GraphicsDevice device)
        {
            _device = device;
            _factory = device.ResourceFactory;
        }

        public virtual void OnDraw(CommandList commandList)
        {
            
        }

        public virtual void OnDestroy()
        {

        }

        public void OnPostProcess(Framebuffer framebuffer)
        {
            
        }
    }
}


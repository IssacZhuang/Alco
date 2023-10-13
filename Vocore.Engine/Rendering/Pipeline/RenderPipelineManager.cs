using System;
using System.Collections.Generic;
using System.Reflection;
using Veldrid;

namespace Vocore.Engine
{
    public class RenderPipelineManager
    {
        private List<IRenderPipline> _renderPiplines = new List<IRenderPipline>();
        private GraphicsDevice _device;
        public RenderPipelineManager(GraphicsDevice device)
        {
            _device = device;
        }

        public void AddRenderPipeline(IRenderPipline renderPipline)
        {
            _renderPiplines.Add(renderPipline);
            renderPipline.OnCreate(_device);
        }

        public void RemoveRenderPipeline(IRenderPipline renderPipline)
        {
            _renderPiplines.Remove(renderPipline);
            renderPipline.OnDestroy();
        }

        public void OnDraw(CommandList commandList)
        {
            IRenderPipline? renderPipline = null;
            for (int i = 0; i < _renderPiplines.Count; i++)
            {
                renderPipline = _renderPiplines[i];
                if (renderPipline.IsEnable)
                {
                    renderPipline.OnDraw(commandList);
                }
            }
        }

        public void OnDestroy()
        {
            for (int i = 0; i < _renderPiplines.Count; i++)
            {
                _renderPiplines[i].OnDestroy();
            }
        }
    }
}


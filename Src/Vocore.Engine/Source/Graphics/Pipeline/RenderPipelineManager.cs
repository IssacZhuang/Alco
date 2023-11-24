using System;
using System.Collections.Generic;
using System.Reflection;
using Veldrid;

namespace Vocore.Engine
{
    public class RenderPipelineManager
    {
        private readonly PriorityList<IRenderPipline> _renderPiplines = new PriorityList<IRenderPipline>((x, y) => x.Order.CompareTo(y.Order));
        private readonly GraphicsDevice _device;
        public RenderPipelineManager(GraphicsDevice device)
        {
            _device = device;
        }

        public void AddRenderPipeline(IRenderPipline renderPipline)
        {
            try
            {
                renderPipline.OnCreate(_device);
                _renderPiplines.Add(renderPipline);
            }
            catch (Exception e)
            {
                Log.Error($"RenderPipline {renderPipline.GetType().Name} failed to create");
                Log.Error(e.ToString());
                return;
            }
        }

        public void RemoveRenderPipeline(IRenderPipline renderPipline)
        {
            _renderPiplines.Remove(renderPipline);
            renderPipline.OnDestroy();
        }

        public void OnDraw(CommandList commandList)
        {
            IRenderPipline? renderPipline;
            for (int i = 0; i < _renderPiplines.Count; i++)
            {
                renderPipline = _renderPiplines[i];
                try
                {
                    if (renderPipline.IsEnable)
                    {
                        renderPipline.OnDraw(commandList);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"RenderPipline {renderPipline.GetType().Name} failed to draw");
                    Log.Error(e.ToString());
                }

            }
        }

        public void OnDestroy()
        {
            for (int i = 0; i < _renderPiplines.Count; i++)
            {
                try
                {
                    _renderPiplines[i].OnDestroy();
                }
                catch (Exception e)
                {
                    Log.Error($"RenderPipline {_renderPiplines[i].GetType().Name} failed to destroy");
                    Log.Error(e.ToString());
                }
            }
        }
    }
}


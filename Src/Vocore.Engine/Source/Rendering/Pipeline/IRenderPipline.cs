using System;
using Veldrid;

namespace Vocore.Engine
{
    public interface IRenderPipline
    {
        /// <summary>
        /// Is the pipeline enable
        /// </summary>
        bool IsEnable { get;}
        /// <summary>
        /// Is the pipeline async
        /// </summary>
        bool IsAsync { get; }
        /// <summary>
        /// The order of the pipeline, only available if IsAsync is false
        /// </summary>
        int Order { get; }
        void OnCreate(GraphicsDevice device);
        void OnDraw();
        void OnDestroy();
    }
}


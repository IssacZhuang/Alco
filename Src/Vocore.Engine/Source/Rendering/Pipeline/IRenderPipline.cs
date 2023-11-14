using System;
using Veldrid;

namespace Vocore.Engine
{
    public interface IRenderPipline
    {
        bool IsEnable { get;}
        int Order { get; }
        void OnCreate(GraphicsDevice device);
        void OnDraw(CommandList commandList);
        void OnDestroy();
    }
}


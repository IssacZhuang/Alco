using System;
using Veldrid;

namespace Vocore.Engine
{
    public interface IRenderPipline
    {
        void OnCreate(ResourceFactory factory);
        void OnDraw(CommandList commandList);
        void OnDestroy();
    }
}


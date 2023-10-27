using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    public struct RenderContext
    {
        private readonly ResourceSet _globalBuffer;
        private readonly GlobalShaderData _globalData;
        public GlobalShaderData GlobalData
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _globalData;
        }
        public void PushGlobalData(CommandList commandList)
        {
            commandList.SetGraphicsResourceSet(0, _globalBuffer);
        }
    }
}


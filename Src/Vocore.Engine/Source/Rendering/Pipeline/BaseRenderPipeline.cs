using System;
using Veldrid;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    public abstract class BaseRenderPipeline : IRenderPipline
    {
        private CommandList _commandList = null!;
        public CommandList CommandList
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _commandList;
            }
        }
        public bool IsEnable
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return true;
            }
        }

        public bool IsAsync
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return false;
            }
        }

        public int Order => 0;

        public virtual void OnCreate(GraphicsDevice device)
        {
            _commandList = device.ResourceFactory.CreateCommandList();
        }

        public virtual void OnDestroy()
        {
            _commandList.Dispose();
        }

        public virtual void OnDraw()
        {
            //to be implemented
        }
    }
}
using System;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    /// <summary>
    /// The basic GPU controller for the engine.
    /// </summary>
    internal struct EngineGPU
    {
        private CommandList _commandList;
        private GraphicsDevice _device;

        public EngineGPU(GameEngine engine)
        {
            _device = engine.GraphicsDevice;
            _commandList = _device.ResourceFactory.CreateCommandList();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrame()
        {
            _commandList.Begin();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndFrame()
        {
            _commandList.End();
            _device.SubmitCommands(_commandList);
            _device.SwapBuffers();
        }
    }
}
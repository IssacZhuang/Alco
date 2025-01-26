using System;
using Alco.Rendering;

namespace Alco.Engine
{
    public interface IEnginePlugin : IDisposable
    {
        /// <summary>
        /// The execution order of the plugin. The lower the number, the earlier it will be executed.
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Called when the engine is in the initialization stage.
        /// Used for modifying the engine state/settings and adding components.
        /// </summary>
        void OnPostInitialize(GameEngine engine);
    }
}
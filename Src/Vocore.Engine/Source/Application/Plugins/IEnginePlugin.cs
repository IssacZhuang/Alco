using System;

namespace Vocore.Engine
{
    public interface IEnginePlugin
    {
        /// <summary>
        /// The priority of the plugin, 
        /// the higher the priority, the later the plugin will be called to override the previous plugin.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Called when the engine is in the initialization stage.
        /// Used for modifying the engine state/settings and adding components.
        /// </summary>
        void OnInitilize(GameEngine engine, ref GameEngineSetting setting);

        /// <summary>
        /// Called when the engine starts running.
        /// Used for initializing resources.
        /// </summary>
        void OnStart();

        /// <summary>
        /// Called when the engine stops running.
        /// Used for cleaning up resources.
        /// </summary>
        void OnExit();
    }
}
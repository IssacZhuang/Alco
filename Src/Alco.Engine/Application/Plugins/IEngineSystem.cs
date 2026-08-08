using System;

namespace Alco.Engine
{
    /// <summary>
    /// A self-contained unit of engine logic driven by the engine lifecycle.
    /// Systems are registered via <see cref="GameEngine.AddSystem"/> and updated
    /// each frame in priority order.
    /// </summary>
    public interface IEngineSystem : IDisposable
    {
        /// <summary>
        /// The execution order. Lower values run first.
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Called once before the main loop starts.
        /// </summary>
        void OnStart();

        /// <summary>
        /// Called each fixed-rate tick.
        /// </summary>
        void OnTick(float delta);

        /// <summary>
        /// Called each frame.
        /// </summary>
        void OnUpdate(float delta);

        /// <summary>
        /// Called at the end of each frame, after scene rendering and before present.
        /// </summary>
        void OnEndFrame(float deltaTime);

        /// <summary>
        /// Called once after the main loop ends, before dispose.
        /// </summary>
        void OnStop();
    }
}

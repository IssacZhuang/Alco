using System.Collections.Concurrent;
using System.Threading;

namespace Alco.Engine
{
    /// <summary>
    /// A custom SynchronizationContext for executing callbacks on the game's main thread.
    /// This allows async/await operations to properly synchronize with the game loop.
    /// </summary>
    public class GameSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbackQueue =
            new ConcurrentQueue<(SendOrPostCallback, object?)>();

        private int _mainThreadId;

        /// <summary>
        /// Creates a new instance of the GameSynchronizationContext.
        /// </summary>
        public GameSynchronizationContext()
        {
            _mainThreadId = Environment.CurrentManagedThreadId;
        }

        /// <summary>
        /// Queues a callback to be executed on the game's main thread.
        /// </summary>
        public override void Post(SendOrPostCallback callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);    

            _callbackQueue.Enqueue((callback, state));
        }

        /// <summary>
        /// Executes a callback on the game's main thread synchronously.
        /// If called from the main thread, executes immediately.
        /// If called from another thread, throws NotSupportedException.
        /// </summary>
        public override void Send(SendOrPostCallback callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);

            // If we're already on the main thread, execute immediately
            if (Environment.CurrentManagedThreadId == _mainThreadId)
            {
                callback(state);
                return;
            }

            // We don't support synchronous execution from other threads
            throw new NotSupportedException("Synchronous execution from non-main thread is not supported. Use Post instead.");
        }

        /// <summary>
        /// Processes all queued callbacks.
        /// This should be called from the game's main thread during the update cycle.
        /// </summary>
        public void ProcessCallbacks()
        {
            // Process all callbacks in the queue
            while (_callbackQueue.TryDequeue(out var item))
            {
                try
                {
                    item.Callback(item.State);
                }
                catch (Exception ex)
                {
                    // Log the exception but continue processing other callbacks
                    Log.Error($"Exception in GameSynchronizationContext callback: {ex}");
                }
            }
        }

        /// <summary>
        /// Installs this SynchronizationContext as the current context for the calling thread.
        /// This should be called from the game's main thread during initialization.
        /// </summary>
        public void Install()
        {
            _mainThreadId = Environment.CurrentManagedThreadId;
            //it is actually a thread-static method, not global static method
            SetSynchronizationContext(this);
            Log.Success($"GameSynchronizationContext installed, main thread id: {_mainThreadId}");
        }
    }
}

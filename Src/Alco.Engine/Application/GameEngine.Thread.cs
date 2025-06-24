using System.Threading;

namespace Alco.Engine;


public partial class GameEngine
{
    /// <summary>
    /// Posts an action to be executed on the main thread synchronously.
    /// </summary>
    /// <param name="action">The action to execute on the main thread.</param>
    /// <exception cref="ArgumentNullException">Thrown when action is null.</exception>
    public void PostToMainThread(Action action)
    {
        PostToMainThread((state) => action(), null);
    }

    /// <summary>
    /// Posts a callback to be executed on the main thread using the synchronization context.
    /// </summary>
    /// <param name="action">The callback to execute on the main thread.</param>
    /// <param name="state">Optional state object to pass to the callback.</param>
    /// <exception cref="ArgumentNullException">Thrown when action is null.</exception>
    public virtual void PostToMainThread(SendOrPostCallback action, object? state)
    {
        ArgumentNullException.ThrowIfNull(action);
        _synchronizationContext.Post(action, state);
    }

    /// <summary>
    /// Posts an action to be executed on the main thread asynchronously.
    /// <br/><strong>Warning:</strong> Do not call this method from the main thread as it may cause deadlocks.
    /// Use <see cref="IsMainThread"/> to check if you're on the main thread.
    /// </summary>
    /// <param name="action">The action to execute on the main thread.</param>
    /// <returns>A task that completes when the action has been executed on the main thread.</returns>
    /// <exception cref="ArgumentNullException">Thrown when action is null.</exception>
    /// <remarks>
    /// This method is designed to be called from background threads to execute main-thread-only operations
    /// and wait for completion. The main thread ID is <see cref="MainThreadId"/>.
    /// </remarks>
    public Task PostToMainThreadAsync(Action action)
    {
        if (IsMainThread)
        {
            throw new InvalidOperationException("Cannot call PostToMainThreadAsync from a main thread");
        }

        TaskCompletionSource tcs = new TaskCompletionSource();
        PostToMainThread((state) =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    /// <summary>
    /// Posts a function to be executed on the main thread asynchronously and returns its result.
    /// <br/><strong>Warning:</strong> Do not call this method from the main thread as it may cause deadlocks.
    /// Use <see cref="IsMainThread"/> to check if you're on the main thread.
    /// </summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="action">The function to execute on the main thread.</param>
    /// <returns>A task that completes with the result when the function has been executed on the main thread.</returns>
    /// <exception cref="ArgumentNullException">Thrown when action is null.</exception>
    /// <remarks>
    /// This method is designed to be called from background threads to access main-thread-only resources
    /// and return their values. The main thread ID is <see cref="MainThreadId"/>.
    /// </remarks>
    public Task<T> PostToMainThreadAsync<T>(Func<T> action)
    {
        if (IsMainThread)
        {
            throw new InvalidOperationException("Cannot call PostToMainThreadAsync from a main thread");
        }

        TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
        PostToMainThread((state) =>
        {
            try
            {
                T result = action();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);

        return tcs.Task;
    }
}
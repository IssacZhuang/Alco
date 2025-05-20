using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alco;

/// <summary>
/// Provides a managed way to execute and control background tasks
/// </summary>
public class BackgroundTask
{
    private readonly Task _task;
    private readonly CancellationTokenSource _cancellationTokenSource;

    internal BackgroundTask(Task task, CancellationTokenSource cancellationTokenSource)
    {
        _task = task;
        _cancellationTokenSource = cancellationTokenSource;
    }

    /// <summary>
    /// Gets whether the background task has completed
    /// </summary>
    public bool IsCompleted => _task.IsCompleted;

    /// <summary>
    /// Gets the task that the background task is running
    /// </summary>
    public Task Task => _task;

    /// <summary>
    /// Waits for the background task to complete
    /// </summary>
    public void Wait()
    {
        _task.Wait();
    }

    /// <summary>
    /// Waits for the background task to complete
    /// </summary>
    /// <returns>True if the task completed, false otherwise</returns>
    public bool TryWait()
    {
        if (!_task.IsCompleted)
        {
            _task.Wait();
        }
        return _task.IsCompleted;
    }

    /// <summary>
    /// Cancels the background task
    /// </summary>
    public void Cancel()
    {
        if (_cancellationTokenSource.IsCancellationRequested) return;
        _cancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Runs a background task
    /// </summary>
    /// <param name="callback">The callback to execute</param>
    /// <returns>The background task</returns>
    public static BackgroundTask Run(Action<CancellationToken> callback)
    {
        CancellationTokenSource cancellationTokenSource = new();
        Task task = Task.Run(() => callback(cancellationTokenSource.Token), cancellationTokenSource.Token);
        return new BackgroundTask(task, cancellationTokenSource);

    }
}


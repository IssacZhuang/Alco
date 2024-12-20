using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Vocore;

/// <summary>
/// Represents a reusable task that can be executed on a thread pool.
/// <br/>Note: The methods of this class are not thread-safe and should not be used across multiple threads.
/// </summary>
/// <typeparam name="T">The type of the result produced by the task.</typeparam>
public abstract class ReuseableTask<T> : AutoDisposable, IThreadPoolWorkItem
{
    private static readonly Exception _exceptionTaskNotRunning = new InvalidOperationException("Task is not running");
    private readonly ManualResetEventSlim _event = new ManualResetEventSlim(false);
    private T? _result;
    private Exception? _exception;

    /// <summary>
    /// Gets the result of the task. Waits for the task to complete if it is still running.
    /// </summary>
    /// <exception cref="Exception">Thrown if the task execution resulted in an exception.</exception>
    public T Result
    {
        get
        {
            Wait();
            if (_exception != null)
            {
                throw _exception;
            }
            return _result!;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the task has completed.
    /// </summary>
    public bool IsCompleted => _event.IsSet;

    /// <summary>
    /// Executes the task on a thread pool.
    /// </summary>
    void IThreadPoolWorkItem.Execute()
    {
        try
        {
            _result = ExecuteCore();
        }
        catch (Exception e)
        {
            _exception = e;
        }
        finally
        {
            _event.Set();
        }
    }

    /// <summary>
    /// Executes the core logic of the task. Must be implemented by derived classes.
    /// </summary>
    /// <returns>The result of the task execution.</returns>
    protected abstract T ExecuteCore();

    /// <summary>
    /// Waits for the task to complete if it is currently executing.
    /// </summary>
    public void Wait()
    {
        if (!_event.IsSet)
        {
            _event.Wait();
        }
    }

    /// <summary>
    /// Attempts to get the result of the task. Returns false if the task is not running or if it resulted in an exception.
    /// </summary>
    /// <param name="result">The result of the task if successful.</param>
    /// <param name="exception">The exception if the task failed.</param>
    /// <returns>True if the result was successfully retrieved; otherwise, false.</returns>
    public bool TryGetResult([NotNullWhen(true)] out T? result, [NotNullWhen(false)] out Exception? exception)
    {
        if (!_event.IsSet)
        {
            result = default;
            exception = _exceptionTaskNotRunning;
            return false;
        }
        Wait();
        if (_exception != null)
        {
            exception = _exception;
            result = default;
            return false;
        }
        result = _result!;
        exception = null;
        return true;
    }

    /// <summary>
    /// Starts the execution of the task. Throws an exception if the task is already executing.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the task is already in executing.</exception>
    public void Run()
    {
        Wait();
        _event.Reset();
        ThreadPool.UnsafeQueueUserWorkItem(this, false);
    }

    protected override void Dispose(bool disposing)
    {
        Wait();
        _event.Dispose();
    }
}

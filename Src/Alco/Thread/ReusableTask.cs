using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Alco;

/// <summary>
/// Represents a reusable task that can be executed on a thread pool.
/// <br/>Note: The methods of this class are not thread-safe and should not be used across multiple threads.
/// </summary>
/// <typeparam name="T">The type of the result produced by the task.</typeparam>
public abstract class ReusableTask<T> : AutoDisposable, IThreadPoolWorkItem
{
    private readonly ManualResetEventSlim _event = new ManualResetEventSlim(true);
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
        if (_exception != null)
        {
            throw _exception;
        }
    }


    /// <summary>
    /// Starts the execution of the task. Throws an exception if the task is already executing.
    /// </summary>
    protected void RunCore()
    {
        Wait();
        _event.Reset();
        ThreadPool.UnsafeQueueUserWorkItem(this, false);
    }

    protected override void Dispose(bool disposing)
    {
        _event.Dispose();
    }
}

/// <summary>
/// Represents a reusable task that can be executed on a thread pool.
/// <br/>Note: The methods of this class are not thread-safe and should not be used across multiple threads.
/// </summary>
public abstract class ReusableTask : AutoDisposable, IThreadPoolWorkItem
{
    private readonly ManualResetEventSlim _event = new ManualResetEventSlim(true);

    private Exception? _exception;

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
            ExecuteCore();
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
    protected abstract void ExecuteCore();

    /// <summary>
    /// Starts the execution of the task. Throws an exception if the task is already executing.
    /// </summary>
    protected void RunCore()
    {
        Wait();
        _event.Reset();
        ThreadPool.UnsafeQueueUserWorkItem(this, false);
    }

    /// <summary>
    /// Waits for the task to complete if it is currently executing.
    /// </summary>
    public void Wait()
    {
        if (!_event.IsSet)
        {
            _event.Wait();
        }

        if (_exception != null)
        {
            throw _exception;
        }
    }

    protected override void Dispose(bool disposing)
    {
        _event.Dispose();
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Vocore.Engine
{
    public class ThreadManager
    {
        private class AsyncTask
        {
            //on thread
            private Action task;
            //on main thread
            private Action success;
            private bool isDone;
            private Exception? exception;
            public bool IsDone
            {
                get
                {
                    return Volatile.Read(ref isDone);
                }
            }
            public AsyncTask(Action task, Action success)
            {
                this.task = task;
                this.success = success;
                isDone = false;
                exception = null;
            }

            //for reuse
            public void Reset(Action task, Action success)
            {
                this.task = task;
                this.success = success;
                isDone = false;
                exception = null;
            }

            public void SetException(Exception exception)
            {
                this.exception = exception;
            }

            public bool TryGetException(out Exception? exception)
            {
                exception = this.exception;
                return exception != null;
            }

            public void SetDone()
            {
                Volatile.Write(ref isDone, true);
            }

            //on thread
            public void DoTask()
            {
                try
                {
                    task();
                }
                catch (Exception e)
                {
                    SetException(e);
                }
                finally
                {
                    SetDone();
                }
            }

            //on main thread
            public void DoSuccess()
            {
                try
                {
                    success();
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }
        }
        private List<AsyncTask> _tasks = new List<AsyncTask>();
        private List<AsyncTask> _pendingRemove = new List<AsyncTask>();
        private ArrayPool<AsyncTask> _pool = new ArrayPool<AsyncTask>(100);
        //allways run in main thread
        internal void Update()
        {
            foreach (var task in _tasks)
            {
                if (task.IsDone)
                {
                    if (task.TryGetException(out var exception))
                    {
                        Log.Error(exception);
                    }
                    else
                    {
                        task.DoSuccess();
                    }
                    _pendingRemove.Add(task);
                }
            }

            foreach (var task in _pendingRemove)
            {
                _tasks.Remove(task);
                _pool.Return(task);
            }
            _pendingRemove.Clear();
        }

        public void AddAsyncTask(Action task, Action success)
        {
            if (!IsCallFromMainThread())
            {
                Log.Error("AddAsyncTask should be called out of main thread");
                return;
            }

            if (task == null)
            {
                Log.Error("function task should not be null");
                return;
            }

            if (success == null)
            {
                Log.Error("function success should not be null");
                return;
            }

            AsyncTask asyncTask = _pool.Get();
            if (asyncTask == null)
            {
                asyncTask = new AsyncTask(task, success);
            }
            else
            {
                asyncTask.Reset(task, success);
            }
            _tasks.Add(asyncTask);
            ThreadPool.QueueUserWorkItem((state) =>
            {
                asyncTask.DoTask();
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsCallFromMainThread()
        {
            return Environment.CurrentManagedThreadId == Application.MainThread;
        }
    }
}